# NodePool 通用节点池系统

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`TheGame/MainPack/Scripts/ObjectPool/`、`TheGame/MainPack/Scripts/Resources/`、`addons/ComponentInsoector/NodePoolInspectorPlugin.cs`
> 本文档描述 GGF 的通用 Node 对象池：IPoolable 接口、配置驱动注册、懒加载 Instantiate、孤儿节点设计、归还流程与编辑器扫描工具。

---

## 1. 概述

NodePool 是一套**基于 `GF.ObjectPool` 的通用节点池系统**，用于复用实现了 `IPoolable` 接口的 Godot 场景实例（如 DamagePop 伤害数字、特效粒子等）。

### 能力清单

- ✅ **配置驱动**：`NodePoolConfig.tres` 管理所有池化场景，编辑器一键扫描生成
- ✅ **懒加载**：不预实例化，`Get` 时池中有闲置则取，无闲置则 Instantiate 新实例
- ✅ **GF.ObjectPool 托管**：容量、过期、自动释放由底层对象池统一管理
- ✅ **孤儿节点设计**：空闲节点不在场景树中，避免 "already has a parent" 冲突
- ✅ **类型安全**：泛型 `Get<T>` 带类型校验，不匹配自动归还
- ✅ **编辑器工具**：`NodePoolInspectorPlugin` 反射扫描所有 `IPoolable` 实现类

---

## 2. 架构

```
NodePoolConfig (.tres)                NodePool (SingletonNode)
  ├── DefaultCapacity=300              ├── OnLoad: 读取 Config
  ├── DefaultExpireTime=60s            │   └── foreach entry:
  ├── DefaultAutoReleaseInterval=30s   │       创建 GF.ObjectPool(poolName, capacity, expireTime)
  └── Entries[]                        │       存储 PackedScene 引用（懒加载用）
       └── PoolEntry                   
            ├── Scene (string path)    Get<T>(scenePath, parent)
            ├── Capacity (0=默认)       ├── pool.Spawn(scenePath) → 有闲置 → 返回
            ├── ExpireTime              └── 无闲置 → Instantiate → Register(spawned:true) → 返回
            └── AutoReleaseInterval
                                       Release(node) / Release(poolItem)
PoolContainer (Node)                    ├── OnRelease()
  └── PoolName                          ├── Visible = false
                                        ├── GetParent()?.RemoveChild() → 孤儿
                                        └── pool.Unspawn() → 回到池
```

---

## 3. 文件清单

| 文件 | 职责 |
|------|------|
| `TheGame/MainPack/Scripts/ObjectPool/NodePool.cs` | 主池管理器：`OnLoad` 注册池、`Get<T>` 获取、`Release` 归还 |
| `TheGame/MainPack/Scripts/ObjectPool/NodeObject.cs` | `ObjectBase` 子类，包装池中的 Node 实例 |
| `TheGame/MainPack/Scripts/ObjectPool/PoolContainer.cs` | 场景树容器（轻量 Node，仅存 PoolName 元数据） |
| `TheGame/MainPack/Scripts/Resources/NodePoolConfig.cs` | 配置资源：全局默认参数 + `Array<PoolEntry>` |
| `TheGame/MainPack/Scripts/Resources/PoolEntry.cs` | 单个池条目：Scene 路径 + 容量/过期/释放间隔 |
| `TheGame/MainPack/Resources/NodePoolConfigRes.tres` | 配置文件实例（编辑器选中后显示扫描按钮） |
| `addons/ComponentInsoector/NodePoolInspectorPlugin.cs` | 编辑器 Inspector 插件：扫描按钮 + 反射匹配 |

---

## 4. 核心机制

### 4.1 IPoolable 接口

```csharp
namespace GodotGameFramework.NodePool;

public interface IPoolable
{
    void OnGet();      // 从池中取出时调用
    void OnRelease();  // 归还到池中时调用
}
```

实现类需要在 `.tscn` 场景的根节点脚本中实现此接口。编辑器扫描时通过 `GetAssignableFormTypes(typeof(IPoolable))` 发现所有实现类，再与 `.tscn` 的脚本类名交叉匹配。

### 4.2 懒加载流程

```
Get<T>(scenePath, parent)
  │
  ├── pool.Spawn(scenePath)
  │     ├── 有闲置对象 → 返回，跳到"激活"
  │     └── 返回 null → 走懒加载分支
  │           ├── s_PoolScenes[poolName] 取 PackedScene
  │           ├── packedScene.Instantiate()
  │           ├── NodeObject.Create(scenePath, newNode)
  │           ├── pool.Register(obj, spawned: true)  ← GF.ObjectPool 自动管理容量
  │           └── 返回新对象
  │
  └── 激活: OnGet() → Visible=true → Reparent(parent) → 记录 s_NodeToContainer
```

**关键设计决策**：不在 `OnLoad` 时预实例化 `capacity` 份副本。GF.ObjectPool 的 `Register(obj, spawned:true)` 会将新对象加入池，当 `Count > Capacity` 时自动触发 `Release()` 回收最老/最低优先级对象。

### 4.3 孤儿节点设计

池中空闲的节点**不挂在任何父节点下**（孤儿状态），只在被取出时才 `Reparent(parent)` 挂到使用方。归还时 `GetParent()?.RemoveChild()` 恢复孤儿状态。

这是为了解决 Godot `AddChild` 的 "already has a parent" 错误——如果空闲节点挂在 `PoolContainer` 下，下次 `Get` 时 `parent.AddChild(node)` 会因节点已有父节点而失败。

### 4.4 归还流程

```
Release(NodeObject nodeObj)
  ├── s_NodeToContainer.Remove(id, out container)  ← 查归属池
  ├── poolable.OnRelease()
  ├── Visible = false
  ├── GetParent()?.RemoveChild(target)              ← 摘除恢复孤儿
  └── pool.Unspawn(nodeObj)                         ← 回到池（spawn count 0）

Release(IPoolable poolItem)
  └── 同上，额外通过 (object)poolItem 查找 NodeObject 包装
```

---

## 5. 编辑器扫描工具

```
NodePoolInspectorPlugin (EditorInspectorPlugin)
  │
  ├── _CanHandle: 检查 script 是否为 NodePoolConfig.cs
  │
  ├── _ParseBegin: 绘制 UI
  │     ├── 池化场景数量标签
  │     ├── [Scan IPoolable Scenes] 按钮
  │     └── [Clear] 按钮
  │
  └── OnScanPressed:
        ├── BuildPoolableTypeMap()    ← 反射 Assembly 找所有 IPoolable 类型
        ├── CollectTscnFiles()         ← 递归扫描 res://TheGame/ 下 .tscn
        ├── GetSceneScriptClassName()  ← PackedScene.GetState() 读 script 属性
        ├── 类名交叉比对
        ├── 生成 PoolEntry 数组
        └── SaveConfig()              ← 写 .tmp → 原子替换 .tres
```

**扫描原理**：不 `Instantiate` 场景（编辑器里拿不到 C# 脚本类型），而是反射程序集获取所有 `IPoolable` 实现类的**类名列表**，再读取每个 `.tscn` 的 `PackedScene.GetState()` 获取根节点 script 属性，从中提取 C# 类名进行交叉匹配。

---

## 6. 使用示例

> 当前 `NodePoolConfigRes.tres` 注册了 **3 个池化场景**：`DamagePop`（伤害数字）、`QuestionTips`（确认对话框）、`DropItem`（拾取物）。

### DamagePop（伤害数字）

```csharp
// 获取 DamagePop 并挂到 actor 下
var d = NodePool.Get<DamagePop>(
    ResourcesCollectionConstant.UIs_DamagePop,
    parent: actor);
d?.SetText(actor.GlobalPosition, 20);

// 归还（通常在 DamagePop 动画结束后调用）
NodePool.Release(d);  // 或 NodePool.Release(nodeObject)
```

### DropItem（拾取物）

```csharp
// 从池中获取 DropItem，移动到目标位置后自动归还
var drop = NodePool.Get<DropItem>(scenePath, parent: GetTree().CurrentScene);
drop?.MoveTo(targetPosition, onFinish: () => {
    // 拾取物到达后业务回调（如加积分）
});

// DropItem 内部：GTween DOMove 动画结束后自动 NodePool.Release(this)
public void MoveTo(Vector2 position, Action finish)
{
    m_Tween = this.DOMove(position, 0.5f);       // GTween 扩展方法
    m_Tween.Finished += () =>
    {
        finish?.Invoke();
        NodePool.Release(this);                   // 动画结束自动回池
    };
}

// OnRelease 中 Kill Tween、清空引用，防止下次复用残留状态
public void OnRelease()
{
    m_Tween?.Kill();
    m_Tween = null;
}
```

---

## 7. 已知边界与后续计划

- [x] 懒加载 Instantiate（不预实例化）✅ 2026-07
- [x] 孤儿节点设计（解决 AddChild 冲突）✅ 2026-07
- [x] Get<T> 类型安全 + 自动归还 ✅ 2026-07
- [x] 编辑器一键扫描 IPoolable 场景 ✅ 2026-07
- [x] .tres 原子保存（tmp + rename）✅ 2026-07
- [ ] `Release(IPoolable)` 通过 `object` 重载查找 NodeObject，语义不够直接，建议后续统一为 `Release(NodeObject)`
- [ ] 池容量耗尽时 `Get<T>` 返回 null，可增加"等待可用"的异步版本
- [ ] 预实例化 warm-up：可选在 `OnLoad` 时预创建少量实例（如 5 个）减少首次 Get 的 Instantiate 开销
