using GameFramework;
using GameFramework.ObjectPool;
using Godot;
using GodotGameFramework;
using GodotGameFrameworkCore.SingletonSystem;
using System;
using System.Collections.Generic;
using GameConfig.Constant;
namespace GodotGameFramework.NodePool;
/// <summary>
/// 池化对象接口
/// </summary>
public interface IPoolable
{
    void OnGet();
    void OnRelease();
}

/// <summary>
/// 通用 Node 对象池。基于GF.ObjectPool。
/// </summary>
public partial class NodePool : SingletonNode<NodePool>
{
    public NodePoolConfig Config { get; private set; }

    /// <summary>
    /// 每种场景对应的容器节点（挂在 NodePool 下），归还时对象放回此处。
    /// </summary>
    private static readonly List<PoolContainer> s_Containers = new();

    /// <summary>
    /// Node.GetInstanceId() → 所属池容器。Get 时记录，Release 时查询并清理。
    /// </summary>
    private static readonly Dictionary<ulong, PoolContainer> s_NodeToContainer = new();

    /// <summary>
    /// 池名 → PackedScene，Get 时懒加载实例化用。
    /// </summary>
    private static readonly Dictionary<string, PackedScene> s_PoolScenes = new();

    protected override void OnLoad()
    {
        base.OnLoad();

        Config = GF.Resource.LoadAsset<NodePoolConfig>(ResourcesCollectionConstant.Resources_NodePoolConfigRes);
        if (Config == null)
        {
            Log.Warning("[NodePool] 未找到 NodePoolConfig: {0}", ResourcesCollectionConstant.Resources_NodePoolConfigRes);
            return;
        }

        if (Config.Entries == null || Config.Entries.Count == 0)
        {
            Log.Warning("[NodePool] NodePoolConfig.Entries 为空，请在编辑器中点击 Scan 按钮生成。");
            return;
        }

        var seenPools = new HashSet<string>();

        foreach (var entry in Config.Entries)
        {
            if (entry?.Scene == null)
            {
                Log.Warning("[NodePool] 跳过空条目");
                continue;
            }

            string scenePath = entry.Scene;
            string poolName = scenePath;

            // 去重
            if (!seenPools.Add(poolName))
            {
                Log.Warning("[NodePool] 跳过重复条目: {0}", poolName);
                continue;
            }

            // 池已存在则跳过
            if (GF.ObjectPool.HasObjectPool<NodeObject>(poolName))
            {
                Log.Info("[NodePool] 池已存在，跳过: {0}", poolName);
                continue;
            }

            var packedScene = ResourceLoader.Load<PackedScene>(scenePath);
            if (packedScene == null)
            {
                Log.Warning("[NodePool] 无法加载场景: {0}", scenePath);
                continue;
            }

            // 创建容器节点
            var container = new PoolContainer(poolName);
            AddChild(container);
            s_Containers.Add(container);

            int capacity = entry.Capacity > 0 ? entry.Capacity : Config.DefaultCapacity;
            float expireTime = entry.ExpireTime > 0 ? entry.ExpireTime : Config.DefaultExpireTime;
            float autoRelease = entry.AutoReleaseInterval > 0 ? entry.AutoReleaseInterval : Config.DefaultAutoReleaseInterval;

            try
            {
                // 创建池（懒加载，不预实例化）
                GF.ObjectPool.CreateSingleSpawnObjectPool<NodeObject>(
                    poolName, autoRelease, capacity, expireTime, 0);

                // 保存 PackedScene 引用，Get 时按需 Instantiate
                s_PoolScenes[poolName] = packedScene;

                Log.Info("[NodePool] 已注册: {0} (容量={1}, 过期={2}s, 懒加载)", scenePath, capacity, expireTime);
            }
            catch (Exception ex)
            {
                Log.Error("[NodePool] 注册池失败: {0} — {1}", poolName, ex.Message);
            }
        }
    }

    // ── 获取 ──

    /// <summary>
    /// 从池中获取指定类型的节点。
    /// </summary>
    /// <param name="scenePath">场景资源路径。</param>
    /// <param name="parent">可选父节点，获取后自动 AddChild。</param>
    public static T Get<T>(string scenePath, Node parent = null) where T : class, IPoolable
    {
        var obj = GetInternal(scenePath, parent);
        if (obj == null) return null;

        if (obj.Target is T target)
            return target;

        Log.Error("[NodePool] 类型不匹配: 期望 {0}，实际 {1}", typeof(T).Name, obj.Target?.GetType().Name);
        Release(obj); // 类型不对，归还
        return null;
    }

    /// <summary>
    /// 从池中获取节点（非泛型版本，返回 NodeObject 包装）。
    /// </summary>
    public static NodeObject Get(string scenePath, Node parent = null)
    {
        return GetInternal(scenePath, parent);
    }

    private static NodeObject GetInternal(string scenePath, Node parent)
    {
        var pool = GF.ObjectPool.GetObjectPool<NodeObject>(scenePath);
        if (pool == null)
        {
            Log.Error("[NodePool] 对象池不存在: {0}", scenePath);
            return null;
        }

        var obj = pool.Spawn(scenePath);
        // 池中无闲置对象 → 懒加载实例化新的
        if (obj == null)
        {
            if (!s_PoolScenes.TryGetValue(scenePath, out var packedScene) || packedScene == null)
            {
                Log.Error("[NodePool] 无法实例化：未找到 PackedScene: {0}", scenePath);
                return null;
            }

            Node newNode;
            try { newNode = packedScene.Instantiate(); }
            catch (Exception ex)
            {
                Log.Error("[NodePool] Instantiate 失败: {0} — {1}", scenePath, ex.Message);
                return null;
            }

            if (newNode is not IPoolable)
            {
                Log.Error("[NodePool] 实例化的节点未实现 IPoolable: {0}", scenePath);
                newNode.QueueFree();
                return null;
            }

            // 挂在容器下
            if (TryGetContainer(scenePath, out var container))
                container.AddChild(newNode);

            // 注册到池（spawned: true = 已获取状态），GF.ObjectPool 自动管理容量
            obj = NodeObject.Create(scenePath, newNode);
            pool.Register(obj, spawned: true);
        }

        if (obj.Target is not Node node)
        {
            Log.Error("[NodePool] Target 不是 Node: {0}", scenePath);
            pool.Unspawn(obj);
            return null;
        }

        if (obj.Target is IPoolable poolable)
            poolable.OnGet();

        // 设置可见
        if (node is CanvasItem ci) ci.Visible = true;
        else if (node is Node3D n3d) n3d.Visible = true;

        // 挂到请求的父节点下（会从容器下摘走）
        if (parent != null)
        {
            if (node.GetParent() != null)
                node.GetParent().RemoveChild(node);
            parent.AddChild(node);
        }

        // 记录归属容器（Release 时用）
        if (TryGetContainer(scenePath, out var retContainer))
            s_NodeToContainer[node.GetInstanceId()] = retContainer;

        return obj;
    }

    // ── 归还 ──

    /// <summary>
    /// 归还 NodeObject 到池中。
    /// </summary>
    public static void Release(NodeObject nodeObj)
    {
        if (nodeObj == null) return;

        var target = nodeObj.Target as Node;
        if (target == null) return;

        ulong id = target.GetInstanceId();

        // 从追踪字典取容器并清理
        if (!s_NodeToContainer.Remove(id, out var container))
        {
            Log.Warning("[NodePool] 无法归还：未找到节点 {0} 的归属池", target.Name);
            return;
        }

        // 通知 IPoolable
        if (nodeObj.Target is IPoolable poolable)
            poolable.OnRelease();

        // 隐藏
        if (target is CanvasItem ci) ci.Visible = false;
        else if (target is Node3D n3d) n3d.Visible = false;

        if (target.GetParent() != container)
        {
            // 归还到容器下
            container.AddChild(target);
        }


        // Unspawn
        var pool = GF.ObjectPool.GetObjectPool<NodeObject>(container.PoolName);
        if (pool != null)
        {
            pool.Unspawn(nodeObj);
        }
        else
        {
            Log.Warning("[NodePool] 无法归还：对象池 '{0}' 不存在", container.PoolName);
        }
    }

    /// <summary>
    /// 归还 IPoolable 节点到池中
    /// </summary>
    public static void Release(IPoolable poolItem)
    {
        if (poolItem == null) return;
        if (poolItem is not Node node) return;

        ulong id = node.GetInstanceId();

        if (!s_NodeToContainer.TryGetValue(id, out var container))
        {
            Log.Warning("[NodePool] 无法归还 IPoolable：未找到节点 {0} 的归属池", node.Name);
            return;
        }

        // 从池中反向找到 NodeObject 包装
        var pool = GF.ObjectPool.GetObjectPool<NodeObject>(container.PoolName);
        if (pool == null)
        {
            Log.Warning("[NodePool] 无法归还：对象池 '{0}' 不存在", container.PoolName);
            return;
        }

        pool.Unspawn((object)poolItem);

        poolItem.OnRelease();

        if (node is CanvasItem ci) ci.Visible = false;
        else if (node is Node3D n3d) n3d.Visible = false;
        if (node.GetParent() != null)
        {
            node.GetParent().RemoveChild(node);
        }

        if (node.GetParent() != container)
            container.AddChild(node);
        s_NodeToContainer.Remove(id);
    }
    /// <summary>
    /// 回收所有已获取的节点到池中。
    /// </summary>
    public static void ReleaseAll()
    {
        // 先收集所有 ID，避免迭代时修改字典
        var ids = new List<ulong>(s_NodeToContainer.Keys);
        foreach (var id in ids)
        {
            // 可能已被前一轮 Release 清理
            if (!s_NodeToContainer.ContainsKey(id))
                continue;

            var node = GodotObject.InstanceFromId(id) as Node;
            if (node is IPoolable poolable)
            {
                Release(poolable);
            }
            else if (node == null)
            {
                // 节点已被外部释放，清理残留的追踪记录
                s_NodeToContainer.Remove(id);
            }
        }
    }
    /// <summary>
    /// 回收指定场景的所有已获取节点到池中。
    /// </summary>
    /// <param name="scenePath">场景资源路径（同时也是池名称）。</param>
    public static void ReleaseAll(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
            return;

        var ids = new List<ulong>(s_NodeToContainer.Keys);
        foreach (var id in ids)
        {
            if (!s_NodeToContainer.TryGetValue(id, out var container) || container.PoolName != scenePath)
                continue;

            var node = GodotObject.InstanceFromId(id) as Node;
            if (node is IPoolable poolable)
            {
                Release(poolable);
            }
            else if (node == null)
            {
                s_NodeToContainer.Remove(id);
            }
        }
    }

    // ── 容器查找 ──

    private static bool TryGetContainer(string scenePath, out PoolContainer container)
    {
        foreach (var c in s_Containers)
        {
            if (c.PoolName == scenePath)
            {
                container = c;
                return true;
            }
        }
        container = null;
        return false;
    }
}
