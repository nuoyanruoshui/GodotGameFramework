# 对象池系统 (Object Pool Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/ObjectPool/`、`Framework/GodotGameFrameworkCore/ObjectPool/`
> 本文档描述 GGF 的对象池系统：ObjectBase/IObjectPool 设计、Spawn/Unspawn 流程、释放策略、与 ReferencePool 的区别，以及框架内的实际使用点。

---

## 1. 概述

对象池系统是 [Game Framework](https://gameframework.cn/) ObjectPool 模块的移植（**逻辑几乎原封不动**——本模块本身不含任何 Godot 类型），用于重用"创建/销毁代价高"的对象（典型：Godot 节点实例）。遵循框架**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/ObjectPool/` | `ObjectPoolManager` 模块：池的创建/查询/销毁/轮询、内部 `ObjectPool<T>` 与 `Object<T>` 实现、释放筛选策略 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/ObjectPool/` | `ObjectPoolComponent`：纯透传封装（每个管理器方法一一对应），经 `GF.ObjectPool` 访问 | ✅ |

### 两种池模式

| 模式 | 创建方法 | 语义 |
|------|----------|------|
| SingleSpawn | `CreateSingleSpawnObjectPool<T>` | 同一对象同一时刻只能被获取一次（Entity/UIForm 实例即此模式） |
| MultiSpawn | `CreateMultiSpawnObjectPool<T>` | 同一对象可被同时获取多次（内部 `SpawnCount` 引用计数） |

池以 **`(对象类型 T, 池名称 name)`** 二元组唯一标识（`TypeNamePair`）——同一 `T` 可以建多个不同名的池（如每个实体组一个 `EntityInstanceObject` 池）。

### 与 ReferencePool 的区别

| | **ReferencePool**（引用池） | **ObjectPool**（对象池） |
|---|---|---|
| 定位 | 轻量 C# 对象复用（`IReference`），消 GC | 重量级资源实例复用（Godot 节点等） |
| 粒度 | 按类型全局一个池 | 按 `(类型, 池名)` 任意多个池，每池独立参数 |
| 获取/归还 | `ReferencePool.Acquire<T>()` / `Release(obj)` | `pool.Spawn(name)` / `pool.Unspawn(target)` |
| 生命周期回调 | 仅 `Clear()`（归还时清理） | `OnSpawn / OnUnspawn / Release(isShutdown)` |
| 淘汰策略 | 无（永不释放，只增不减） | 容量 + 过期时间 + 优先级 + 自动释放轮询 |
| 命名/查找 | 无 | 对象按 `Name` 键入池，可 `Spawn(name)` 定向取 |
| 典型使用 | 事件参数、任务对象、`PhysicsCheck2D`、**ObjectBase 包装壳本身** | 实体场景实例、UI 界面实例 |

两者是**嵌套关系**：`ObjectBase` 派生类实现 `IReference`，其包装壳照例从 `ReferencePool.Acquire` 创建、释放时归还——对象池管理"贵重货物"（`Target`），引用池管理"包装纸"。

> ✅（2026-07）场景新增 `ReferencePool` 节点（`ReferencePoolComponent`）：按 `ReferenceStrictCheckType` 策略（`AlwaysEnable`〈当前默认〉/ `OnlyEnableInEditor` / `OnlyOpenWhenDevelopment` / `AlwaysDisable`）统一设置 `ReferencePool.EnableStrictCheck`，双重 `Release` 直接抛异常而非静默污染池。运行时可在调试器 `Profiler/Object Pool`（逐池参数 + Release 按钮）与 `Profiler/Reference Pool`（7 列计数表 + 严格检查开关）页签观察两种池（见 `DebuggerSystem.md`）。

---

## 2. 架构与数据流

```
调用方（EntityManager.EntityGroup / UIManager / 业务代码）
    │  GF.ObjectPool.CreateSingleSpawnObjectPool<T>(name, autoReleaseInterval, capacity, expireTime, priority)
    ▼
ObjectPoolComponent (Godot 桥接层，场景节点 "ObjectPool")   ← 纯透传
    ▼
ObjectPoolManager : GameFrameworkModule (Priority=6，纯 C# 层)
    │  Dictionary<TypeNamePair, ObjectPoolBase>
    │  每帧 Update() 轮询每个池
    ▼
ObjectPool<T>（内部类）
    ├── m_Objects   : MultiDictionary<string, Object<T>>   ← 按对象 Name 索引（Spawn(name) 查找用）
    ├── m_ObjectMap : Dictionary<object, Object<T>>        ← 按 Target 反查（Unspawn/SetLocked 用）
    └── Object<T>（内部包装，ReferencePool 池化）
            ├── SpawnCount / IsInUse / Locked / Priority / LastUseTime
            └── Spawn() → obj.OnSpawn()   Unspawn() → obj.OnUnspawn()   Release() → obj.Release(isShutdown)
```

对象状态流转：

```
Register(obj, spawned) ──┬─ spawned=true  → 入池即"使用中"（实体新实例的典型路径）
                         └─ spawned=false → 入池即空闲
Spawn(name)  → SpawnCount++ → OnSpawn()  → 使用中（刷新 LastUseTime）
Unspawn(target) → OnUnspawn() → SpawnCount-- → 空闲（刷新 LastUseTime）
    │
    ├─ 空闲 + 未锁 + CustomCanReleaseFlag → "可释放"候选
    ▼
Release()（容量超限 / 自动间隔 / 手动） → 筛选 → obj.Release(false) → 移出池 → 包装壳还给 ReferencePool
Shutdown → 所有对象 obj.Release(true)
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/ObjectPool/ObjectBase.cs` | 池化对象基类：Name/Target/Locked/Priority/LastUseTime + `OnSpawn`/`OnUnspawn`/`Release(isShutdown)`/`CustomCanReleaseFlag` |
| `GameFramework/ObjectPool/IObjectPool.cs` | 单池接口：Register/CanSpawn/Spawn/Unspawn/UnspawnAll/SetLocked/SetPriority/Release 系列 |
| `GameFramework/ObjectPool/IObjectPoolManager.cs` | 管理器接口：Has/Get/Create/Destroy/Release/ReleaseAllUnused |
| `GameFramework/ObjectPool/ObjectPoolManager.cs` | 管理器实现 + 30 余个 Create 重载（参数组合） |
| `GameFramework/ObjectPool/ObjectPoolManager.ObjectPool.cs` | 单池实现：双索引、释放筛选、自动释放计时 |
| `GameFramework/ObjectPool/ObjectPoolManager.Object.cs` | 内部对象包装 `Object<T>`（SpawnCount 计数） |
| `GameFramework/ObjectPool/ObjectPoolBase.cs` / `ObjectInfo.cs` | 非泛型池基类（调试面板用）/ 对象信息快照 |
| `GameFramework/ObjectPool/ReleaseObjectFilterCallback.cs` | 自定义释放筛选委托 |
| `GodotGameFrameworkCore/ObjectPool/ObjectPoolComponent.cs` | `GF.ObjectPool` 组件（透传） |

---

## 3. 核心机制

### 3.1 池参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `Capacity` | `int.MaxValue` | 池容量。`Register`/`Unspawn` 后 `Count > Capacity` 即触发一次 `Release()`（尝试释放超额部分） |
| `ExpireTime` | `float.MaxValue` | 对象过期秒数。空闲对象 `LastUseTime` 距今超过此值即成为优先释放对象；**运行时改小会立即触发 Release** |
| `AutoReleaseInterval` | 未显式指定时 = `ExpireTime` | 自动释放轮询间隔：每帧累计真实时间，达到间隔即 `Release()`。默认双 MaxValue 等于**关闭自动释放** |
| `Priority` | 0 | 对象/池优先级。释放时**优先级低的先被释放**；同优先级按 `LastUseTime` 旧者先释放 |

释放筛选（`DefaultReleaseObjectFilterCallback`）两轮：先释放**全部已过期**对象；仍需释放时再按 `Priority` 升序 + `LastUseTime` 升序补足 `Count - Capacity` 个。

### 3.2 可释放条件

对象同时满足以下三条才进入候选：

1. `!IsInUse`（SpawnCount == 0，未被获取）
2. `!Locked`（`SetLocked(target, true)` 可钉住常驻对象，如常用 UI）
3. `CustomCanReleaseFlag`（`ObjectBase` 虚属性，默认 true；子类可按业务否决，如"动画未播完不许销毁"）

`ReleaseObject(target)` 强制释放单个对象也受同样条件约束（不满足返回 false）。

### 3.3 Register 的 `spawned` 语义

`Register(obj, spawned: true)` 表示"入池即已被使用"——实体/UI 加载出新实例后**直接投入使用**，同时登记进池，避免"先入池再 Spawn"的一来一回。`spawned: true` 时 `Object<T>.Create` 会补调一次 `OnSpawn()`。

### 3.4 Name 定向获取

`Spawn(name)` 只在同名对象中找空闲实例——实体组池以**资源路径**为 Name，同组内 `CatEntity.tscn` 与 `AngerEntity.tscn` 的实例互不串用。`Spawn()` 等价于 `Spawn(string.Empty)`，只匹配注册时未命名的对象。

### 3.5 线程模型

无锁设计，所有 API 必须在主线程调用（框架 `Update` 驱动亦在主线程）。

---

## 4. 组件与 API

场景节点：`Framework/GameFramework.tscn` 中的 `ObjectPool` 节点，经 `GF.ObjectPool` 访问。组件无 Inspector 参数（池全部代码创建）。

### 4.1 API 总览

```csharp
// 池管理
IObjectPool<T> pool = GF.ObjectPool.CreateSingleSpawnObjectPool<T>(name, capacity, expireTime, priority);
IObjectPool<T> pool = GF.ObjectPool.CreateMultiSpawnObjectPool<T>(name);
// Create 重载覆盖 name/capacity/expireTime/priority/autoReleaseInterval 的各种组合（各 30+ 个）
bool has  = GF.ObjectPool.HasObjectPool<T>(name);
pool      = GF.ObjectPool.GetObjectPool<T>(name);          // 不存在返回 null
var pools = GF.ObjectPool.GetAllObjectPools(sort: true);   // 调试面板用（ObjectPoolBase）
GF.ObjectPool.DestroyObjectPool<T>(name);                  // 池内所有对象 Release(true)
GF.ObjectPool.Release();                                   // 全部池立即执行一次释放检查
GF.ObjectPool.ReleaseAllUnused();                          // 释放全部池的全部空闲对象（切场景后调用）

// 单池操作（IObjectPool<T>）
pool.Register(obj, spawned);        // 新对象入池
bool ok = pool.CanSpawn(name);      // 是否有可用实例（不获取）
T obj   = pool.Spawn(name);         // 无可用实例返回 null（不自动创建！）
pool.Unspawn(target);               // 按 Target 归还（找不到抛异常）
pool.UnspawnAll();                  // 归还全部（GGF 扩展，原版无）
pool.SetLocked(target, true);       // 钉住不释放
pool.SetPriority(target, 10);       // 调整单个对象释放优先级
pool.ReleaseObject(target);         // 强制释放单个（使用中/加锁/Custom 否决时返回 false）
pool.Release();                     // 手动触发一次释放检查
pool.ReleaseAllUnused();

// 运行时调参
pool.Capacity = 32;  pool.ExpireTime = 120f;  pool.AutoReleaseInterval = 60f;  pool.Priority = 1;
```

### 4.2 自定义池化对象

参考 `TheGame/MainPack/Scripts/ObjectPool/NodeObject.cs`（NodePool 的 `ObjectBase` 子类）：

```csharp
public class MyPoolObject : ObjectBase
{
    public MyPoolObject() { }                       // 无参构造必须有（ReferencePool 要求）

    public static MyPoolObject Create(string name, MyItem item)
    {
        var obj = ReferencePool.Acquire<MyPoolObject>();   // 包装壳来自引用池
        obj.Initialize(name, item);                        // name = 池内键；item = Target
        return obj;
    }

    public MyItem Item => (MyItem)Target;

    protected internal override void OnSpawn()   { /* 取出：重置/显示 */ }
    protected internal override void OnUnspawn() { /* 归还：隐藏/断开 */ }
    protected internal override void Release(bool isShutdown)
    {
        // 从池中永久移除：销毁 Target（Godot 节点应 QueueFree）
    }
}

// 使用
var pool = GF.ObjectPool.CreateSingleSpawnObjectPool<MyPoolObject>("MyPool", capacity: 16);
pool.Register(MyPoolObject.Create("item1", new MyItem()), spawned: false);
var obj = pool.Spawn("item1");     // → OnSpawn
pool.Unspawn(obj.Target);          // → OnUnspawn
```

---

## 5. 框架内实际使用点

| 使用方 | 池 | 模式 | 说明 |
|--------|-----|------|------|
| `EntityManager.EntityGroup`（纯 C# 层） | `Entity Instance Pool ({组名})`，对象 `EntityInstanceObject` | SingleSpawn | **每个实体组一个池**，参数来自 `EntityGroupRes`（ReleaseInterval/Capacity/ExpireTime/Priority）。对象 Name = 场景路径；`Release` 时经 `IEntityHelper.ReleaseEntity` → `QueueFree()`。详见 `EntitySystem.md` §3.2 |
| `UIManager`（纯 C# 层） | `UI Instance Pool`，对象为 UIManager 私有嵌套 `UIFormInstanceObject` | SingleSpawn | 全局一个 UI 实例池，`CloseUIForm` 后界面实例回池，`OpenUIForm` 复用。详见 `UISystem.md` |
| `GodotGameFrameworkCore/UI/UIFormInstanceObject.cs` `UIItemInstanceObject.cs` | — | — | Godot 层公开版包装（`UIItemInstanceObject.OnSpawn/OnUnspawn` 自带节点显隐与位置重置）。`UIItemInstanceObject` 面向 UIItem（列表项等）复用场景，**当前尚无运行时调用方**，作为模板保留 |

> 注意与 **TaskPool**（`DownloadManager`/资源加载中的任务调度器）区分：TaskPool 是"任务队列 + 代理"，其任务对象走 ReferencePool，与本模块无关。

---

## 6. 注意事项 / FAQ

**Q: `Spawn` 返回 null 怎么办？**
对象池**不负责创建对象**——池空/无空闲实例时返回 null，由调用方自行创建实例并 `Register(obj, spawned: true)`（实体/UI 系统均为此模式："先问池，无则加载"）。

**Q: `Unspawn` 传什么？**
传 **Target**（被包装的真实对象，如 Node），不是 `ObjectBase` 壳（传壳的重载内部也是取 `obj.Target`）。目标不在池中会抛 `GameFrameworkException`。

**Q: 默认参数下对象会被释放吗？**
不会。Capacity/ExpireTime/AutoReleaseInterval 默认均为 MaxValue，池只进不出。要有淘汰行为必须显式给出容量或过期时间（实体组经 `EntityGroupRes` 配置）。

**Q: `Capacity` 是硬上限吗？**
不是。它是**释放水位线**：超过容量只是触发释放检查，若空闲对象不足（都在使用中/被锁），Count 可以持续大于 Capacity。

**Q: 切场景后如何一次性清掉缓存节点？**
`GF.ObjectPool.ReleaseAllUnused()`——释放所有池的全部空闲对象（使用中与加锁对象不受影响）。

**Q: 想让某个对象常驻（如主界面）？**
`pool.SetLocked(target, true)`。或重写 `CustomCanReleaseFlag` 按运行时条件动态否决释放。

**Q: MultiSpawn 池的对象什么时候算"空闲"？**
`SpawnCount` 归零时。每次 `Spawn` +1、`Unspawn` -1，必须严格配对；多归还会使计数为负并抛 `GameFrameworkException`。

**Q: 池本身的 `Priority` 影响什么？**
仅用于 `GetAllObjectPools(sort: true)` 的排序展示（调试面板）；释放顺序由**对象**的 Priority 决定，两者不要混淆。

---

## 7. NodePool 系统

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`TheGame/MainPack/Scripts/ObjectPool/`、`TheGame/MainPack/Scripts/Resources/NodePoolConfig.cs`、`addons/ComponentInsoector/NodePoolInspectorPlugin.cs`
> NodePool 是基于 `GF.ObjectPool` 构建的**场景节点级对象池封装**，提供开箱即用的 Godot Node 复用能力：懒加载实例化、自动回收、编辑器扫描发现。

---

### 7.1 概述

NodePool 解决一个具体问题：`GF.ObjectPool` 是通用对象池基础设施，但直接操作需要手写 `ObjectBase` 子类、管理 PackedScene 缓存、处理场景树的挂载/摘除逻辑。NodePool 将这一切封装为类型安全的 API，游戏业务代码只需两行调用：

```csharp
// 获取
var pop = NodePool.Get<DamagePop>(ResourcesCollectionConstant.UIs_DamagePop, parentNode);

// 归还（由对象自身调用）
NodePool.Release(this);
```

**与原始的 `GF.ObjectPool` 的关系：**

NodePool 是**上层便利封装**，不是替代品。它在底层使用 `GF.ObjectPool.CreateSingleSpawnObjectPool<NodeObject>(...)`，每场景类型自动建一个池。Entity/UI 系统仍直接使用 `GF.ObjectPool` 原始 API（它们的入池/释放逻辑更复杂，与 `EntityManager`/`UIManager` 深度耦合）。

| 维度 | Entity/UIManager 直接使用 ObjectPool | NodePool 封装 |
|------|------|------|
| 池对象类型 | 各自专用 `ObjectBase` 子类（`EntityInstanceObject` 等） | 统一 `NodeObject` |
| 配置方式 | 代码参数（`EntityGroupRes`） | `NodePoolConfigRes.tres` 资源文件 |
| 编辑器支持 | 无 | `NodePoolInspectorPlugin` 一键扫描 |
| 场景树管理 | 各自处理 | 自动容器节点 + 挂载/摘除 |
| 适用场景 | 实体、UI 窗体（框架级） | 弹跳文字、临时特效、复用 UI 组件（项目级） |

---

### 7.2 架构与数据流

```
调用方（业务代码）
    │  NodePool.Get<T>(scenePath, parent)  /  NodePool.Release(poolItem)
    ▼
NodePool : SingletonNode<NodePool>          ← 单例，ProcedureLaunch 中 Active()
    │
    ├── Config (NodePoolConfigRes.tres)     ← NodePoolConfig，含 Entries: PoolEntry[]
    │       └── 每个 PoolEntry: Scene路径 / Capacity / ExpireTime / AutoReleaseInterval
    │
    ├── s_Containers : List<PoolContainer>  ← 每池一个容器节点（挂在 NodePool 下）
    ├── s_NodeToContainer : Dict<ulong, PoolContainer>  ← Node.GetInstanceId() → 所属容器
    ├── s_PoolScenes : Dict<string, PackedScene>        ← poolName → PackedScene（懒加载用）
    │
    └── GF.ObjectPool（每场景一个 SingleSpawn 池）
            └── ObjectPool<NodeObject>
                    └── NodeObject : ObjectBase
                            └── Target → 池化的 Node 实例（如 DamagePop Label）
```

启动流程：

```
ProcedureLaunch → NodePool.Instance.Active()
    → SingletonNode<T>.OnLoad()
        → 加载 NodePoolConfigRes.tres
        → 遍历 Entries
            → 加载 PackedScene
            → 创建 PoolContainer 子节点
            → GF.ObjectPool.CreateSingleSpawnObjectPool<NodeObject>(poolName, ...)
            → 缓存 PackedScene 到 s_PoolScenes（不预实例化！）
```

### 7.3 核心类型

#### 7.3.1 `IPoolable`（接口）

```csharp
namespace GodotGameFramework.NodePool;

public interface IPoolable
{
    void OnGet();      // 从池中取出时调用（恢复状态/播放入场动画）
    void OnRelease();  // 归还池时调用（重置状态/播放离场动画）
}
```

实现此接口的 Node 子类即可被 NodePool 管理。与 `ObjectBase.OnSpawn`/`OnUnspawn` 的差异：
- `IPoolable` 回调在**业务层**（实际 Node 实例上），用于游戏逻辑重置（如重置文本、清空动画状态）
- `ObjectBase.OnSpawn`/`OnUnspawn` 在**池基础设施层**（`NodeObject` 包装壳上），框架内部用于刷新 `LastUseTime`

#### 7.3.2 `NodePool : SingletonNode<NodePool>`（池管理器）

```csharp
public partial class NodePool : SingletonNode<NodePool>
```

核心属性：
- `Config`：运行时加载的 `NodePoolConfig` 资源（`OnLoad` 中通过 `GF.Resource.LoadAsset` 加载 `NodePoolConfigRes.tres`）

核心公共 API：

| 方法 | 说明 |
|------|------|
| `Get<T>(scenePath, parent)` | 泛型获取，返回 `T` 类型节点；类型不匹配时自动归还并返回 null |
| `Get(scenePath, parent)` | 非泛型获取，返回 `NodeObject` 包装 |
| `Release(NodeObject)` | 归还 `NodeObject` 包装到池 |
| `Release(IPoolable)` | 归还 `IPoolable` 节点到池（从追踪字典反查所属池） |

内部静态字典：
- `s_Containers`：`List<PoolContainer>`，每场景一个容器，回收时节点重新挂回容器下
- `s_NodeToContainer`：`Dictionary<ulong, PoolContainer>`，`Get` 时记录节点 `InstanceId` → 所属容器，`Release` 时查询并清理
- `s_PoolScenes`：`Dictionary<string, PackedScene>`，缓存已加载的 PackedScene，`Get` 池空时懒加载实例化

#### 7.3.3 `PoolEntry : Resource`（单场景池配置）

```csharp
[GlobalClass]
public partial class PoolEntry : Resource
{
    [Export] public string Scene { get; set; }            // 场景资源路径
    [Export(PropertyHint.Range, "0,1000,1")]
    public int Capacity { get; set; } = 0;                // 池容量（0 = 使用全局默认）
    [Export(PropertyHint.Range, "0,3600,1")]
    public float ExpireTime { get; set; } = 0f;           // 对象过期秒数（0 = 使用全局默认）
    [Export(PropertyHint.Range, "0,3600,1")]
    public float AutoReleaseInterval { get; set; } = 0f;  // 自动释放间隔（0 = 使用全局默认）
}
```

`0` 值表示继承 `NodePoolConfig` 中的全局默认值。每个属性都可通过 `Export` 属性在 Inspector 中独立覆盖。

#### 7.3.4 `NodePoolConfig : Resource`（全局配置）

```csharp
[GlobalClass]
public partial class NodePoolConfig : Resource
{
    [Export] public Array<PoolEntry> Entries { get; set; }           // 池条目列表
    [Export(PropertyHint.Range, "1,5000,1")]
    public int DefaultCapacity { get; set; } = 300;                  // 全局默认容量
    [Export(PropertyHint.Range, "0,3600,1")]
    public float DefaultExpireTime { get; set; } = 60f;              // 全局默认过期时间
    [Export(PropertyHint.Range, "0,3600,1")]
    public float DefaultAutoReleaseInterval { get; set; } = 30f;     // 全局默认自动释放间隔
}
```

资源路径：`res://TheGame/MainPack/Resources/NodePoolConfigRes.tres`，通过 `ResourcesCollectionConstant.Resources_NodePoolConfigRes` 常量引用。

#### 7.3.5 `PoolContainer : Node`（容器节点）

```csharp
public partial class PoolContainer : Node
{
    public string PoolName { get; set; }
}
```

每个池在 `NodePool` 节点下挂一个 `PoolContainer` 子节点作为"空闲区"。归还的节点会从当前父节点摘除后重新挂回对应 `PoolContainer` 下——节点空闲时处于**孤儿状态**（无业务父节点），只有容器持有引用。

#### 7.3.6 `NodeObject : ObjectBase`（池包装壳）

```csharp
public partial class NodeObject : ObjectBase
{
    public static NodeObject Create(string name, Node node);

    protected internal override void Release(bool isShutdown)
    {
        if (Target is Node node) node.QueueFree();
    }
}
```

`NodeObject` 继承 `ObjectBase`，用途：
- `Create` 使用 `ReferencePool.Acquire<NodeObject>()` 获取包装壳（遵循框架的对像池套引用池模式）
- `Release(bool)` 在池淘汰对象时调用 `QueueFree()` 销毁 Godot 节点——这是 NodePool 中**唯一的节点销毁路径**

### 7.4 生命周期详解

#### 7.4.1 获取流程：`NodePool.Get<T>(scenePath, parent)`

```
1. GF.ObjectPool.GetObjectPool<NodeObject>(scenePath)  → 查找池
2. pool.Spawn(scenePath)  → 尝试从池中获取空闲实例
       ├── 命中 → 跳到步骤 5（复用已有实例）
       └── 未命中 → 懒加载新实例（步骤 3-4）
3. s_PoolScenes[scenePath].Instantiate()  → 创建新 Node
4. 验证 IPoolable → 挂到容器下 → NodeObject.Create + Register(spawned:true)  → 入池
5. poolable.OnGet()  → 通知业务层"被取出"
6. CanvasItem → Visible = true / Node3D → Visible = true
7. node.GetParent().RemoveChild(node); parent.AddChild(node)  → 挂到请求父节点
8. 记录 s_NodeToContainer[node.InstanceId] = container
```

**关键设计：懒加载。** `OnLoad` 中只创建池和缓存 PackedScene，不预实例化任何节点。第一次 `Get` 时才 `Instantiate`。这意味着启动时零开销、运行时按需创建。

#### 7.4.2 归还流程：`NodePool.Release(IPoolable)`

```
1. s_NodeToContainer.TryGetValue(node.InstanceId)  → 查找所属容器
2. GF.ObjectPool.GetObjectPool<NodeObject>(container.PoolName)  → 找到池
3. pool.Unspawn(poolItem)  → 刷 LastUseTime、SpawnCount--、触发 ObjectBase.OnUnspawn
4. poolable.OnRelease()  → 通知业务层"被归还"
5. CanvasItem → Visible = false / Node3D → Visible = false  → 隐藏
6. node.GetParent().RemoveChild + container.AddChild  → 挂回容器（成为孤儿）
7. s_NodeToContainer.Remove(node.InstanceId)  → 清理追踪
```

**关键设计：节点空闲时是孤儿。** 归还的节点不保留在任何业务父节点下——它被重新挂到 `PoolContainer` 下等待下次 `Get`。这避免了 "节点在场景树有父节点但仍然闲置" 的混乱状态。

### 7.5 编辑器工具：NodePoolInspectorPlugin

`addons/ComponentInsoector/NodePoolInspectorPlugin.cs` 是一个 `EditorInspectorPlugin`（`[Tool]`，`#if TOOLS` 条件编译），在选中 `NodePoolConfigRes.tres` 时激活。注册于 `ComponentInsoector.cs`：

```csharp
m_NodePoolInspector = new NodePoolInspectorPlugin();
AddInspectorPlugin(m_NodePoolInspector);
```

**功能：**

1. **显示池数量信息**：绿色标签 "池化场景: N 个"
2. **Scan IPoolable Scenes 按钮**：扫描 `res://TheGame/` 下所有 `.tscn` 文件
   - 通过 `Utility.Assembly.GetAssignableFormTypes(typeof(IPoolable))` 反射程序集中所有实现了 `IPoolable` 且是 `Node` 子类的 C# 类型，构建 `类名 → 全名` 映射（缓存至 `s_PoolableTypeMap`，每次 IDE 重启后重建）
   - 遍历 `.tscn` 文件，用 `NodeUtility.GetSceneScriptClassName` 提取每个 `.tscn` 的根节点脚本类名
   - 类名命中映射 → 判定为 IPoolable 场景，加入 Entries
   - **增量更新**：保留旧 Entries 中的参数（Capacity/ExpireTime 等），新增条目使用默认值（0）
3. **Clear 按钮**：弹出确认对话框后清空所有条目

**扫描原理：** 不实例化场景（避免触发 `_Ready`），而是通过 `PackedScene.GetState()` 读取场景元数据中的脚本类型名。速度快且安全。

### 7.6 设计决策

| 决策 | 理由 |
|------|------|
| **懒加载，不预实例化** | 避免启动时创建所有池节点——尤其是许多场景可能在整个游戏过程中根本不会被用到。与原始 GF.ObjectPool 的"先 Register 再 Spawn"模式不同。 |
| **节点空闲时是孤儿** | 归还后节点从父节点摘除、挂回 PoolContainer。没有业务父节点持有引用，避免"节点在树中但不可见"的状态混乱。 |
| **GF.ObjectPool 管理容量和过期** | 不重新发明轮子——容量超限、过期时间淘汰、自动释放轮询全部由 GF.ObjectPool 的 `Release()` 机制处理。NodePool 只做场景树管理 + 便利封装。 |
| **每场景一个池，池名 = 场景路径** | 不同场景类型独立池，避免类型混用。路径作为池名自然唯一。 |
| **s_NodeToContainer 追踪 InstanceId** | 归还时无需调用方提供池名——系统自动反查。容错性强，防止误还到错误的池。 |
| **NodePool 自己作为 SingletonNode** | 复用框架的 `SingletonNode<T>` 模式（自动创建、`_Ready` 去重、`OnLoad` 回调）。在 `ProcedureLaunch` 中显式调用 `Active()` 触发池初始化。 |

### 7.7 实际使用点

| 使用方 | 获取 | 归还 | 说明 |
|--------|------|------|------|
| `GanTanEntity.Logic.cs` | `NodePool.Get<DamagePop>(UIs_DamagePop, actor)` | `NodePool.Release(this)`（在 `DamagePop.SetText` 内延迟 500ms 后自动归还） | 弹跳伤害数字：子弹击中时从池取 Label 实例、设置位置和文字、500ms 后自动回池 |
| `DamagePop.cs` | 被 `Get` 创建/复用 | `NodePool.Release(this)` | 实现 `IPoolable`，`OnGet` 和 `OnRelease` 暂为空（预留扩展点）。当前 `SetText` 中用 `await Task.Delay(500)` 定时归还（注意：此处 `Task.Delay` 在 Godot 中非标准用法，应改用 `ToSignal(GetTree().CreateTimer(...), Timer.SignalName.Timeout)` 以保证帧同步） |
| `DropItem.cs` | `NodePool.Get<DropItem>(...)`（未在共享代码中直接调用，由外部系统触发） | `NodePool.Release(this)`（在 `MoveTo` 的 GTween `Finished` 回调中归还） | `Node2D : IPoolable`，拾取物实体：`MoveTo(targetPos, callback)` 使用 `GTween.DOMove` 播放飞向目标的动画，动画结束后自动归还池。`OnRelease` 中 `Kill` Tween 防止干扰 |

### 7.8 文件清单

| 文件 | 职责 |
|------|------|
| `TheGame/MainPack/Scripts/ObjectPool/NodePool.cs` | 池管理器（SingletonNode）：Config 加载、池创建、Get/Release API、容器/追踪字典 |
| `TheGame/MainPack/Scripts/ObjectPool/NodeObject.cs` | `ObjectBase` 子类：包装 `Node` 实例，`Release` 时 `QueueFree` |
| `TheGame/MainPack/Scripts/ObjectPool/PoolContainer.cs` | 池容器节点：保管空闲 Node，按 `PoolName` 索引 |
| `TheGame/MainPack/Scripts/Resources/NodePoolConfig.cs` | `[GlobalClass]` 全局配置资源：Entries 列表 + 全局默认值 |
| `TheGame/MainPack/Scripts/Resources/PoolEntry.cs` | `[GlobalClass]` 单条目配置：Scene 路径 + 可选的 Capacity/ExpireTime/AutoReleaseInterval |
| `TheGame/MainPack/Resources/NodePoolConfigRes.tres` | 配置资源实例，由编辑器扫描生成 |
| `addons/ComponentInsoector/NodePoolInspectorPlugin.cs` | 编辑器 Inspector 插件：扫描 IPoolable 场景、UI 按钮（Scan/Clear） |
| `TheGame/GameScripts/UI/DamagePop.cs` | 使用示例：`Label : IPoolable`，500ms 后自动回池 |
| `TheGame/GameScripts/Entity/DropItem.cs` | 使用示例：`Node2D : IPoolable`，GTween DOMove 动画结束后自动回池 |
| `TheGame/GameScripts/Entity/GanTanEntity.Logic.cs` | 使用示例：子弹命中时 `NodePool.Get<DamagePop>(...)` |
| `TheGame/MainPack/Scripts/Procedure/ProcedureLaunch.cs` | 启动入口：`NodePool.Instance.Active()` 初始化池 |

---

## 8. 已知边界与后续计划

- [ ] `UIItemInstanceObject` 的 UIItem 复用链路接入实际调用方（当前为预留模板）
- [ ] 调试面板：`GetAllObjectInfos()` / `ObjectInfo` 已具备数据能力，缺编辑器/运行时可视化
- [x] `LoadBinaryTask` / `LoadBinaryAgent` 异步二进制加载通道（✅ 2026-07 完成，见 `ResourceSystem.md` §3.2）
- [ ] `GF.ObjectPool.Release()` 挂接内存告警（Godot 无 Unity `lowMemory` 回调，需自行监控）
