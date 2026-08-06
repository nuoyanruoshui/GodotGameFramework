# 事件系统 (Event Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Event/`、`Framework/GameFramework/Base/EventPool/`、`Framework/GodotGameFrameworkCore/Event/EventComponent.cs`
> 本文档描述 GGF 的全局事件系统：EventPool 机制、EventId 约定、Fire/FireNow 差异、池化回收规则与自定义事件完整示例。

---

## 1. 概述

事件系统是 Game Framework Event 模块的 Godot 移植，用于模块间/游戏逻辑间的解耦通信，遵循框架的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Base/EventPool/`、`GameFramework/Event/` | 事件队列、订阅表、分发、池化回收 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Event/EventComponent.cs` | 组件封装（纯透传 `IEventManager`） | ✅ |

### 能力清单

- ✅ 按 `int` 事件编号订阅/退订，一个编号可挂多个处理函数（`AllowMultiHandler`）
- ✅ 允许无人订阅（`AllowNoHandler`，Fire 不报错）；**不允许**同一编号重复挂同一个处理函数
- ✅ `Fire`：线程安全、下一次轮询（下一帧）在主线程分发
- ✅ `FireNow`：立即同步分发（非线程安全）
- ✅ 事件参数经 `ReferencePool` 池化，分发完自动回收
- ✅ 分发过程中退订自身/其他处理函数是安全的（链表节点缓存机制）
- ✅ `SetDefaultHandler`：兜底处理无人订阅的事件

---

## 2. 架构与数据流

```
业务代码
    │  GF.Event.Fire(this, e) / FireNow / Subscribe / Unsubscribe
    ▼
EventComponent (Godot 桥接层，场景节点 "Event")
    │  纯透传
    ▼
EventManager : GameFrameworkModule, IEventManager (纯 C# 层, Priority = 7)
    └── EventPool<GameEventArgs>  (mode = AllowNoHandler | AllowMultiHandler)
          ├── m_EventHandlers : GameFrameworkMultiDictionary<int, EventHandler<T>>   ← 订阅表
          ├── m_Events : Queue<Event>          ← Fire 的待分发队列（lock 保护）
          └── m_CachedNodes / m_TempNodes      ← 分发期间安全退订的游标缓存
```

`Fire` 的一帧流转：

```
任意线程 Fire(sender, e)
    → Event.Create(sender, e)（池化结点） → lock 入队
        ...下一次 EventManager.Update（主线程，GameEntry._Process 驱动）...
    → while 队列非空: 出队 → HandleEvent → 依次调用该 Id 的所有处理函数
    → ReferencePool.Release(e)（事件参数回收）
    → ReferencePool.Release(eventNode)（结点回收）
```

> 注意：`EventPool.Update` 一次把队列**全部清空**（while 循环），不是每帧一条；处理函数中再 Fire 的事件会在同一次 Update 里继续被处理。

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Base/EventPool/EventPool.cs` | 事件池：订阅/退订/Fire/FireNow/分发 |
| `GameFramework/Base/EventPool/EventPool.Event.cs` | 池化的事件结点（sender + args） |
| `GameFramework/Base/EventPool/EventPoolMode.cs` | `Default / AllowNoHandler / AllowMultiHandler / AllowDuplicateHandler` |
| `GameFramework/Base/EventPool/BaseEventArgs.cs` | 事件基类，抽象属性 `int Id` |
| `GameFramework/Base/GameFrameworkEventArgs.cs` | `EventArgs + IReference`，抽象 `Clear()` |
| `GameFramework/Event/GameEventArgs.cs` | 游戏逻辑事件基类（空派生，作类型隔离） |
| `GameFramework/Event/IEventManager.cs`、`EventManager.cs` | 管理器接口与实现 |
| `GodotGameFrameworkCore/Event/EventComponent.cs` | 组件封装（`GF.Event`） |
| `GodotGameFrameworkCore/Event/OnLanagueChangeEventArgs.cs` | Godot 层事件：语言切换时由 LocalizationComponent 经 `GF.Event.Fire` 发出 |

> `EventPool<T>` 是 internal 泛型；除全局 EventManager 外，UIManager/EntityManager 等模块内部也各自持有 EventPool 实例分发自己的事件。

---

## 3. 核心机制

### 3.1 EventId 约定

`BaseEventArgs.Id`（abstract）是分发路由键。项目内存在两种合法写法：

**框架层约定 —— `typeof().GetHashCode()`**（Godot 桥接层全部事件如此）：

```csharp
public static readonly int EventId = typeof(DownloadSuccessEventArgs).GetHashCode();
public override int Id => EventId;
```

**游戏侧约定 —— 手工分配常量**（`TheGame/GameScripts/Event/` 示例如此）：

```csharp
public const int EventId = 10010;    // ScoreChanged=10010, BlockClicked=10011, TestPhaseChanged=10001
public override int Id => EventId;
```

两者可共存（都是 int），但手工分配需要自行保证全局唯一；`GetHashCode` 方式理论上存在哈希碰撞可能（实践中极少），且**跨进程/版本不稳定，不可持久化**。

### 3.2 Fire vs FireNow

| | `Fire` | `FireNow` |
|--|--------|-----------|
| 线程安全 | ✅（入队 lock，可从任意线程调用） | ❌（直接分发，必须主线程） |
| 分发时机 | 下一次 `EventManager.Update`（≈下一帧，主线程） | 立即同步 |
| 适用 | 绝大多数场景、异步/多线程回调 | 需要同帧拿到结果的时序敏感逻辑 |

两者最终都走 `HandleEvent`：按 Id 查订阅链 → 逐个调用 → 无订阅者时调 DefaultHandler（若设置）→ **`ReferencePool.Release(e)` 回收参数** → 若模式不允许无处理函数才抛异常（全局 EventManager 允许，不抛）。

### 3.3 订阅 / 退订

```csharp
GF.Event.Subscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);
GF.Event.Unsubscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);
GF.Event.Check(id, handler);   // 是否已订阅
GF.Event.Count(id);            // 某事件的处理函数数量
```

- 同一 `(id, handler)` 重复 Subscribe 会抛 `GameFrameworkException`（全局池未开 `AllowDuplicateHandler`）。
- Unsubscribe 不存在的 handler 也抛异常 —— 订阅/退订必须严格配对（常见做法：UI `OnOpen` 订阅、`OnClose` 退订；流程 `OnEnter` 订阅、`OnLeave` 退订）。
- **分发中退订是安全的**：`HandleEvent` 通过 `m_CachedNodes` 缓存"下一个节点"游标，`Unsubscribe` 会同步修正被删节点的游标，不会漏调或访问已删节点。

### 3.4 池化回收（最重要的坑）

事件参数在**所有处理函数返回后立即被 `ReferencePool.Release` 回收**（`Clear()` 后入池，随后可能被别的 Fire 复用）。因此：

- ❌ 不可把 `e` 存到字段、闭包、集合里跨帧使用
- ❌ 不可在 `async` 处理函数的 `await` 之后再读 `e` 的属性
- ✅ 需要延后使用的字段，在回调内**先拷贝到局部/自有对象**

```csharp
private async void OnDownloadSuccess(object sender, GameEventArgs e)
{
    var args = (DownloadSuccessEventArgs)e;
    string path = args.DownloadPath;      // ✅ 先拷贝
    await Task.Delay(100);
    GD.Print(path);                       // ✅ 用拷贝
    // GD.Print(args.DownloadPath);       // ❌ args 已被回收/复用
}
```

同理，`Fire` 之后调用方也**不得再持有/复用** `e` —— 每次 Fire 都必须用 `Create()` 新取一个实例。

**组件层转发管理器事件必须复制**（2026-07 因此修复过 EntityComponent 的双重归还崩溃）：纯层管理器（Entity/Sound/UI/Download 等）的 C# 事件回调返回后，管理器会**立即 `ReferencePool.Release` 事件参数**；Godot 组件若把同一实例直接 `Fire` 入事件池，事件池分发完会再次 Release —— 严格检查下抛 `The reference has been released.`，关闭严格检查则订阅者读到已被 `Clear()` 的脏数据。

```csharp
// ❌ 双重归还：管理器回调结束会回收 e，事件池下一帧又回收一次
private void OnShowEntitySuccess(object sender, ShowEntitySuccessEventArgs e)
    => m_EventComponent.Fire(this, e);

// ✅ 复制后入队（Godot 层包装 Create(e)，或纯层全参 Create(...)）
private void OnShowEntitySuccess(object sender, ShowEntitySuccessEventArgs e)
    => m_EventComponent.Fire(this, ShowEntitySuccessEventArgs.Create(e.Entity, e.Duration, e.UserData));
```

---

## 4. EventComponent API

场景节点：`Framework/GameFramework.tscn` 中的 `Event` 节点，经 `GF.Event` 访问。无 Inspector 参数。

```csharp
// 状态
GF.Event.EventHandlerCount   // 已注册处理函数总数
GF.Event.EventCount          // 当前队列中待分发事件数

// 订阅管理
GF.Event.Subscribe(int id, EventHandler<GameEventArgs> handler);
GF.Event.Unsubscribe(int id, EventHandler<GameEventArgs> handler);
GF.Event.Check(int id, EventHandler<GameEventArgs> handler);
GF.Event.Count(int id);
GF.Event.SetDefaultHandler(EventHandler<GameEventArgs> handler);

// 触发
GF.Event.Fire(object sender, GameEventArgs e);      // 线程安全，下帧分发
GF.Event.FireNow(object sender, GameEventArgs e);   // 立即分发，仅主线程
```

---

## 5. 自定义事件完整示例

**1) 定义事件参数**（参考 `TheGame/GameScripts/Event/ScoreChangedEventArgs.cs`）：

```csharp
using GameFramework;
using GameFramework.Event;

public class ScoreChangedEventArgs : GameEventArgs
{
    public const int EventId = 10010;              // 或 typeof(...).GetHashCode()
    public override int Id => EventId;

    public int ScoreDelta { get; private set; }

    // 静态工厂 + 引用池，框架标准模式
    public static ScoreChangedEventArgs Create(int scoreDelta)
    {
        ScoreChangedEventArgs e = ReferencePool.Acquire<ScoreChangedEventArgs>();
        e.ScoreDelta = scoreDelta;
        return e;
    }

    public override void Clear()                   // 回收时框架自动调用
    {
        ScoreDelta = 0;
    }
}
```

**2) 触发**：

```csharp
GF.Event.Fire(this, ScoreChangedEventArgs.Create(+10));
```

**3) 订阅与处理**：

```csharp
// 进入时（流程 OnEnter / UI OnOpen）
GF.Event.Subscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);

private void OnScoreChanged(object sender, GameEventArgs e)
{
    var args = (ScoreChangedEventArgs)e;
    m_TotalScore += args.ScoreDelta;               // 回调内消费完毕，不持有 args
}

// 离开时（流程 OnLeave / UI OnClose）—— 必须配对，否则重复订阅抛异常
GF.Event.Unsubscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);
```

`Clear()` 要重置**所有**字段 —— 池化实例会被复用，漏清的字段会把上一次的值带给下一个事件。

---

## 6. 注意事项 / FAQ

**Q: Fire 后事件没触发？**
① 检查订阅时机是否晚于 Fire（Fire 的事件下帧才分发，但订阅必须在分发前完成）；② 确认 `Id` 双方一致（一个用常量一个用 GetHashCode 是对不上的）；③ 全局池允许无订阅者，不会报错提示。

**Q: 可以在子线程 Fire 吗？**
可以，`Fire` 入队有 lock，回调保证在主线程。`FireNow` 不行。

**Q: 处理函数里能 Unsubscribe 自己吗？**
能，游标缓存机制保证安全（典型的一次性事件监听写法）。

**Q: 为什么重复 Subscribe 抛异常？**
全局 EventPool 模式是 `AllowNoHandler | AllowMultiHandler`，未开 `AllowDuplicateHandler`。检查是否有"订阅了但没退订又再次订阅"的生命周期泄漏。

**Q: 事件参数能复用吗？**
不能。每次 Fire 都要 `Create()`（内部从池里取，本身就是复用机制）；Fire 后该引用视为失效。

**Q: EventId 能存档/入配置表吗？**
手工常量可以；`GetHashCode` 方式不行（运行时才确定，跨进程不稳定）。
