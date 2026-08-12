# 状态机系统 (FSM Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Fsm/`、`Framework/GodotGameFrameworkCore/Fsm/FsmComponent.cs`
> 本文档描述 GGF 的有限状态机系统：泛型 IFsm/FsmState 设计、创建与销毁、状态切换、Fsm 数据存取，以及与 Procedure（流程）系统的关系。

---

## 1. 概述

FSM 模块是 Game Framework Fsm 的 Godot 移植，为任意"持有者"对象提供类型安全的有限状态机，遵循框架的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Fsm/` | 状态机实现、状态基类、管理器、轮询与数据存储 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Fsm/FsmComponent.cs` | 组件封装（纯透传 `IFsmManager`） | ✅ |

### 能力清单

- ✅ 泛型持有者：`IFsm<T> where T : class`，状态为 `FsmState<T>` 派生类，编译期保证状态与持有者匹配
- ✅ 同一持有者类型可创建多个命名状态机（`TypeNamePair` 区分）
- ✅ 状态生命周期：`OnInit → OnEnter → OnUpdate → OnLeave → OnDestroy`
- ✅ 状态切换只能由状态内部发起（`ChangeState` 是 protected）
- ✅ 键值数据存取（`SetData/GetData`，值为池化 `Variable`）
- ✅ 状态机实例本身池化（`ReferencePool`），销毁自动回收
- ✅ Procedure（流程）系统即建立在本模块之上的一个特化 FSM

---

## 2. 架构与数据流

```
业务代码
    │  GF.Fsm.CreateFsm / GetFsm / DestroyFsm / HasFsm
    ▼
FsmComponent (Godot 桥接层，场景节点 "Fsm")
    │  纯透传
    ▼
FsmManager : GameFrameworkModule, IFsmManager (纯 C# 层, Priority = 1)
    │  Dictionary<TypeNamePair, FsmBase> m_Fsms      ← key = (持有者类型, 名称)
    │  每帧 Update：快照到 m_TempFsms 后逐个轮询（跳过已销毁）
    ▼
Fsm<T> : FsmBase, IReference, IFsm<T>
    ├── m_States : Dictionary<Type, FsmState<T>>     ← 状态集合（按状态类型索引）
    ├── m_CurrentState / m_CurrentStateTime          ← 当前状态与其持续时间
    └── m_Datas : Dictionary<string, Variable>       ← 键值数据
```

状态切换时序：

```
当前状态.OnLeave(fsm, isShutdown: false)
    → m_CurrentStateTime = 0
    → m_CurrentState = 新状态
    → 新状态.OnEnter(fsm)
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Fsm/FsmBase.cs` | 非泛型基类：Name/FullName/OwnerType/IsRunning/IsDestroyed/CurrentStateName 等 |
| `GameFramework/Fsm/IFsm.cs` | 泛型状态机接口（Start/HasState/GetState/SetData/GetData…） |
| `GameFramework/Fsm/Fsm.cs` | 状态机实现（池化，internal） |
| `GameFramework/Fsm/FsmState.cs` | 状态基类：5 个生命周期虚方法 + protected `ChangeState` |
| `GameFramework/Fsm/IFsmManager.cs`、`FsmManager.cs` | 管理器：注册表 + 轮询 + 创建/销毁 |
| `GodotGameFrameworkCore/Fsm/FsmComponent.cs` | 组件封装（`GF.Fsm`） |

---

## 3. 核心机制

### 3.1 泛型设计

```csharp
FsmState<T>  where T : class      // 状态基类，T = 持有者类型
IFsm<T>      where T : class      // 状态机接口，暴露给状态与外部
Fsm<T>       : FsmBase, IReference, IFsm<T>   // internal 实现
```

- 持有者 `T` 是任意 class（不要求继承框架类型），状态经 `fsm.Owner` 访问持有者。
- 一个状态机内**每种状态类型只能有一个实例**（按 `Type` 索引，重复注册抛异常）。
- 状态实例在 `CreateFsm` 时全部传入并逐个 `OnInit(fsm)` —— 状态集合创建后不可增删。

### 3.2 状态生命周期（FsmState&lt;T&gt;）

| 回调 | 时机 |
|------|------|
| `OnInit(fsm)` | 状态机创建时（每个状态一次） |
| `OnEnter(fsm)` | 进入该状态 |
| `OnUpdate(fsm, elapse, realElapse)` | 该状态为当前状态时每帧（FsmManager 轮询驱动） |
| `OnLeave(fsm, isShutdown)` | 离开该状态；`isShutdown = true` 表示因状态机销毁被动离开 |
| `OnDestroy(fsm)` | 状态机销毁时（每个状态一次） |

### 3.3 创建与启动

```csharp
// 示例：CatEntity 的状态机（见 TheGame/GameScripts/Entity/CatEntity.cs）
public class IdleState : FsmState<CatEntity>
{
    protected internal override void OnUpdate(IFsm<CatEntity> fsm, float e, float r)
    {
        if (fsm.Owner.m_IsMoving) ChangeState<MoveState>(fsm);     // 切换只能在状态内部
    }
}
public class MoveState : FsmState<CatEntity>
{
    protected internal override void OnUpdate(IFsm<CatEntity> fsm, float e, float r)
    {
        if (!fsm.Owner.m_IsMoving) ChangeState<IdleState>(fsm);
    }
}

// 创建（未启动，IsRunning = false）
IFsm<CatEntity> fsm = GF.Fsm.CreateFsm(Name, this, new IdleState(), new MoveState());
// 或简单版本（不命名；同一持有者类型多个状态机时必须命名）
IFsm<CatEntity> fsm2 = GF.Fsm.CreateFsm(this, statesList);

// 启动：指定初始状态（重复 Start 抛异常）
fsm.Start<IdleState>();
// 或 fsm.Start(typeof(IdleState));
```

- `CreateFsm` 重复创建同 `(类型, 名称)` 的状态机会抛 `GameFrameworkException("Already exist FSM ...")`。
- `Fsm<T>` 实例来自 `ReferencePool.Acquire<Fsm<T>>()`，销毁时回收复用。

### 3.4 状态切换（ChangeState）

`ChangeState` 定义在 `FsmState<T>` 上且为 **protected** —— 只有状态自身能发起切换，外部代码做不到（这是有意设计：切换逻辑内聚在状态内）：

```csharp
protected void ChangeState<TState>(IFsm<T> fsm) where TState : FsmState<T>;
protected void ChangeState(IFsm<T> fsm, Type stateType);
```

- 目标状态必须已在创建时注册，否则抛异常。
- 切换会重置 `CurrentStateTime`（当前状态持续时间，秒，按 elapseSeconds 累计）。
- `Fsm<T>` 上的 `ChangeState` 是 internal，仅供 `FsmState` 调用。

### 3.5 Fsm 数据（SetData / GetData）

状态机自带一个 `Dictionary<string, Variable>`，用于跨状态传参（值必须是 `Variable` 派生，配合 `VarInt32 / VarString / VarBoolean / VarSingle` 的隐式转换）：

```csharp
fsm.SetData<VarInt32>("level", 3);          // int 隐式转 VarInt32（内部从引用池取）
VarInt32 lv = fsm.GetData<VarInt32>("level");
int raw = lv;                                // 隐式转回 int

fsm.HasData("level");
fsm.RemoveData("level");                     // 旧 Variable 自动 ReferencePool.Release
```

- `SetData` 覆盖同名旧值时会自动 `Release` 旧 `Variable`；`RemoveData`、状态机销毁（`Clear`）同理 —— 调用方**不要**手动 Release 已交给 Fsm 的 Variable。
- `GetData` 不存在时返回 null（不抛异常）。

### 3.6 销毁

```csharp
GF.Fsm.DestroyFsm<Hero>();          // 按持有者类型（无名状态机）
GF.Fsm.DestroyFsm<Hero>("battle");  // 按类型 + 名称
GF.Fsm.DestroyFsm(fsm);             // 按实例
```

销毁时 `Fsm<T>.Clear()`：当前状态 `OnLeave(isShutdown: true)` → 所有状态 `OnDestroy` → 释放全部 Data → 实例回收进引用池，`IsDestroyed = true`。框架整体 Shutdown 时 FsmManager 会销毁所有状态机。

> 销毁后不要继续持有 `IFsm<T>` 引用 —— 实例会被引用池复用给下一个 `CreateFsm`。

### 3.7 轮询驱动

`FsmManager.Update` 每帧把注册表快照进临时列表再轮询（允许回调中创建/销毁状态机），对每个未销毁状态机调 `fsm.Update` → 累计 `CurrentStateTime` 并调当前状态 `OnUpdate`。未 `Start` 的状态机（`m_CurrentState == null`）跳过。

---

## 4. FsmComponent API

场景节点：`Framework/GameFramework.tscn` 中的 `Fsm` 节点，经 `GF.Fsm` 访问。无 Inspector 参数。

```csharp
// 状态
GF.Fsm.Count                                    // 状态机总数

// 查询
GF.Fsm.HasFsm<T>() / HasFsm<T>(name) / HasFsm(ownerType) / HasFsm(ownerType, name)
GF.Fsm.GetFsm<T>() / GetFsm<T>(name)            // → IFsm<T>
GF.Fsm.GetFsm(ownerType) / GetFsm(ownerType, name)  // → FsmBase（非泛型）
GF.Fsm.GetAllFsms() / GetAllFsms(List<FsmBase>)     // → FsmBase[] / 填充已有列表

// 创建（4 重载：±name × params[]/List）
IFsm<T> fsm = GF.Fsm.CreateFsm<T>(owner, params FsmState<T>[] states);
IFsm<T> fsm = GF.Fsm.CreateFsm<T>(name, owner, List<FsmState<T>> states);

// 销毁（6 重载）
GF.Fsm.DestroyFsm<T>() / DestroyFsm<T>(name) / DestroyFsm(ownerType[, name])
GF.Fsm.DestroyFsm<T>(IFsm<T> fsm) / DestroyFsm(FsmBase fsm)
```

---

## 5. 与 Procedure 的关系

Procedure（流程）系统就是**一个持有者为 `IProcedureManager` 的全局 FSM**：

```csharp
// ProcedureBase 即流程状态：
public abstract class ProcedureBase : FsmState<IProcedureManager> { ... }

// ProcedureManager.Initialize 内部：
m_ProcedureFsm = m_FsmManager.CreateFsm(this, procedures);   // this = IProcedureManager
```

因此：

- 流程的 `ChangeState<T>(procedureOwner)` 就是 FSM 的状态切换；`procedureOwner` 类型 `IFsm<IProcedureManager>` 就是这台流程状态机。
- 流程间传参用的 `procedureOwner.SetData / GetData` 就是 §3.5 的 Fsm 数据。
- `GF.Fsm.GetAllFsms()` 能看到流程状态机（持有者类型 `ProcedureManager`）。
- 复杂流程可在自己内部再 `GF.Fsm.CreateFsm` 创建嵌套子状态机（如战斗内的回合状态），在 `OnLeave` 时销毁。

详见 `ProcedureSystem.md`。

---

## 6. 游戏侧使用示例

CatEntity（`TheGame/GameScripts/Entity/CatEntity.cs`）是当前项目中最完整的 FSM 使用案例：

- **持有者**：`CatEntity : ActorEntity`（`partial class`，`CharacterBody2D` 派生，实现 `IEntity` 和 `IActor`），详见 `EntitySystem.md`
- **状态**：`IdleState` / `MoveState` 两个 `FsmState<CatEntity>` 类，定义在 `CatEntity.cs` 文件中
- **创建**：`OnInit(isNewInstance)` 中 `m_Fsm = (Fsm<CatEntity>)GF.Fsm.CreateFsm(Name, this, new IdleState(), new MoveState())`
- **启动**：`OnShow()` 中 `m_Fsm.Start<IdleState>()`
- **切换**：`IdleState.OnUpdate` 检测 `fsm.Owner.m_IsMoving` → `ChangeState<MoveState>`；`MoveState.OnUpdate` 反之切回 `IdleState`
- `m_IsMoving` 由 `CatEntity.KeybordMove()`（在 `OnUpdate` 中调用）读取键盘输入后设置

---

## 7. 注意事项 / FAQ

**Q: 为什么不能从外部切换状态？**
`ChangeState` 是 `FsmState<T>` 的 protected 方法，原版 GF 设计如此 —— 外部只能经 `fsm.Start`（首次）或让持有者置数据/发事件，由状态在 `OnUpdate` 里自行决定切换。

**Q: 同一持有者能有多个状态机吗？**
能，用命名版本 `CreateFsm(name, owner, ...)`；查询/销毁时带同样的 name。无名与命名互不冲突（key 是 `(OwnerType, name)`，无名即空串）。

**Q: 状态里能拿到持有者吗？**
`fsm.Owner`（强类型 `T`）。

**Q: OnUpdate 的两个时间参数区别？**
`elapseSeconds` 是逻辑时间（受 `Engine.TimeScale` 缩放），`realElapseSeconds` 为换算的真实时间（见 FrameworkCore.md §2.1；TimeScale = 0 时为 0）。`CurrentStateTime` 按 `elapseSeconds` 累计。

**Q: 状态实例能复用/共享吗？**
不能跨状态机共享 —— 每台状态机应持有自己的状态实例（状态实例保存了对 fsm 的隐式约定，且 `OnInit` 只在所属状态机创建时调用一次）。

**Q: `GetData` 拿到的 Variable 要 Release 吗？**
不要。生命周期归 Fsm 管（覆盖/移除/销毁时自动释放）。只有从未交给 Fsm 的临时 Variable 才需要自己 Release。
