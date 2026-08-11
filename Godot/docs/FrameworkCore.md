# 框架核心 (Framework Core)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Base/`、`Framework/GodotGameFrameworkCore/Base/`、`SingletonSystem/`、`Variable/`、`Utility/`、`addons/TopMenu/`
> 本文档描述 GGF 的核心骨架：模块/组件双层驱动、启动序列、生命周期、GF 门面、引用池、日志系统与通用工具（PhysicsCheck2D、GTween、LayerMask、NodeExtension）。

---

## 1. 概述

GGF 是 [Game Framework](https://gameframework.cn/)（Jiang Yin）的 Godot 4 + C# 移植，核心遵循严格的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Base/` | 模块入口（GameFrameworkEntry）、模块基类、引用池、事件池、任务池、日志抽象、Variable、Utility | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Base/` 等 | GF 静态门面、GameEntry 驱动、组件基类（Node）、日志桥接、单例系统、物理检测等工具 | ✅ |

**核心规则**：`GameFramework/` 对 Godot 一无所知；每个 Godot 侧组件（`XxxComponent`）只是把调用**委托**给纯 C# 层对应的 `XxxManager`（经 `GameFrameworkEntry.GetModule<IXxxManager>()` 获取）。

### 两套并行的"实体"概念

| 概念 | 基类 | 驱动方式 |
|------|------|----------|
| 框架模块 (Module) | `GameFrameworkModule`（internal，纯 C#） | `GameFrameworkEntry.Update()` 每帧轮询，按 `Priority` 降序 |
| 框架组件 (Component) | `GameFrameworkComponent : GodotComponent : Node` | Godot 场景树回调（`_Process` 等） |

---

## 2. 架构与数据流

### 2.1 启动序列

主场景 `Framework/GameFramework.tscn`（`run/main_scene`）：

```
GameFramework (GameEntry : GodotComponent)          ← 根节点
├── Base / Event / Resource
├── Procedure / Scene / Fsm
├── DataNode / ObjectPool / Setting
├── Entity / UI / Sound / Localization
└── WebRequest / Download
```

```
Godot 加载 GameFramework.tscn
    │ _EnterTree（自底向上依次进入）
    ▼
GameFrameworkComponent.OnInit()
    └── GameEntry.RegisterComponent(this)     ← 每种组件类型只允许注册一个
         └── BaseComponent 额外被记为 m_BaseComponent（负责最终 Shutdown）
    │
    ▼
GameEntry.OnUpdate(delta)  （即 _Process，每帧）
    ├── CheckProcedure()                       ← 首帧检测到 ProcedureComponent
    │       └── procedureComponent.StartProcedure()  → 进入入口流程（见 ProcedureSystem.md）
    └── GameFrameworkEntry.Update(elapseSeconds, realElapseSeconds)
            └── 按 Priority 降序轮询所有 GameFrameworkModule
```

时间参数换算（`GameEntry.OnUpdate`）：

```csharp
float elapseSeconds = (float)delta;                      // 逻辑时间（已受 Engine.TimeScale 缩放）
float realElapseSeconds = (float)Engine.TimeScale > 0f
    ? elapseSeconds / (float)Engine.TimeScale            // 反除回真实时间
    : 0f;
```

### 2.2 关闭序列

```
BaseComponent.OnPreDestroy()（节点销毁通知）
    └── BaseComponent.Shutdown()
         └── GameFrameworkEntry.Shutdown()
              ├── 模块按注册链表逆序 Shutdown()（优先级高的最后关闭）
              ├── ReferencePool.ClearAll()
              ├── Utility.Marshal.FreeCachedHGlobal()
              └── GameFrameworkLog.SetLogHelper(null)
```

主动关闭走 `GameEntry.Shutdown(ShutdownType)`：`None`（仅清框架）/ `Restart`（`ReloadCurrentScene`）/ `Quit`（`SceneTree.Quit`）。

### 2.3 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Base/GameFrameworkEntry.cs` | 模块注册表：`Update` / `Shutdown` / `GetModule<T>`（按接口自动创建实现） |
| `GameFramework/Base/GameFrameworkModule.cs` | 模块抽象基类：`Priority` / `Update` / `Shutdown` |
| `GameFramework/Base/ReferencePool/*` | 引用池（`IReference` + 按类型的 `ReferenceCollection` 队列） |
| `GameFramework/Base/EventPool/*` | 事件池（详见 EventSystem.md） |
| `GameFramework/Base/TaskPool/*` | 任务池（资源/下载异步任务复用） |
| `GameFramework/Base/Log/GameFrameworkLog.cs` | 日志抽象（`ILogHelper` 注入） |
| `GameFramework/Base/Variable/Variable.cs`、`GenericVariable.cs` | 池化变量抽象 `Variable` / `Variable<T>` |
| `GameFramework/Base/GameFrameworkEventArgs.cs` | 事件参数基类（`EventArgs` + `IReference`） |
| `GameFramework/Base/GameFrameworkLinkedList(Range).cs`、`GameFrameworkMultiDictionary.cs` | 缓存节点的链表/多值字典容器 |
| `GodotGameFrameworkCore/Base/GF.cs` | 静态门面，惰性缓存所有组件 |
| `GodotGameFrameworkCore/Base/GameEntry.cs` | 组件注册表 + 每帧驱动 + Shutdown |
| `GodotGameFrameworkCore/Base/GodotComponent.cs` | Node 基类：Godot 回调 → 虚方法生命周期 |
| `GodotGameFrameworkCore/Base/GameFrameworkComponent.cs` | 组件基类：`OnInit` 自动注册到 GameEntry |
| `GodotGameFrameworkCore/Base/BaseComponent.cs` | 帧率/游戏速度/暂停、Text/Log/Json 辅助器初始化、框架关闭 |
| `GodotGameFrameworkCore/Base/Log.cs` | `Log.Debug/Info/Warning/Error/Fatal`（`[Conditional]` 编译期裁剪） |
| `GodotGameFrameworkCore/Base/ShutdownType.cs` | `None / Restart / Quit` |
| `GodotGameFrameworkCore/SingletonSystem/*` | `SingletonNode<T>`、`SingletonSystem` 注册表、`UpdateDriver` |
| `GodotGameFrameworkCore/Variable/VarInt32 / VarSingle / VarString / VarBoolean` | 池化变量的隐式转换封装 |
| `GodotGameFrameworkCore/Utility/DefaultLogHelper.cs` | `ILogHelper` 实现：桥接 `GD.Print / PushWarning / PushError` |
| `GodotGameFrameworkCore/Utility/DefaultTextHelper.cs`、`NodeExtension.cs`、`NodeUtility.cs`、`PhysicsCheck2D.cs`、`GTween.cs`、`LayerMask.cs` | 文本格式化、Node 扩展、2D 物理检测、DOTween 风格 Tween 扩展、物理层名↔索引↔位掩码映射 |
| `addons/TopMenu/GameFrameworkTopMenu.cs` + `GameFrameworkTopMenu.Generate.cs` | 编辑器菜单：日志级别切换（改写 csproj）、打开 res:// / user:// / Configs 目录、Generate File 子菜单（本地化导出 / GameConfig Luban / 资源收集） |

---

## 3. 核心机制

### 3.1 模块系统（纯 C# 层）

- `GameFrameworkEntry.GetModule<T>()` **只接受接口**，且接口全名必须以 `GameFramework.` 开头；实现类名 = 接口名去掉 `I` 前缀（如 `IEventManager` → `GameFramework.Event.EventManager`），经 `Activator.CreateInstance` 按需创建。
- 模块按 `Priority` **降序**插入链表：优先级高的先轮询、后关闭。实测优先级：`EventManager = 7`、`FsmManager = 1`、默认 `0`、`ProcedureManager = -2`。
- 模块类是 `internal sealed`，业务代码不可直接 new，必须经组件层访问。

### 3.2 组件生命周期（GodotComponent）

`GodotComponent : Node` 把 Godot 原生回调统一转发为虚方法：

| Godot 回调 | 虚方法 | 说明 |
|------------|--------|------|
| `_EnterTree` | `OnInit()` | 进入场景树（可多次触发）；`GameFrameworkComponent` 在此自动注册 |
| `_Ready` | `OnEnter()` | 就绪（仅一次，子节点已进树） |
| `_Process` | `OnUpdate(delta)` | 每帧 |
| `_PhysicsProcess` | `OnFixedUpdate(delta)` | 物理帧 |
| `_ExitTree` | `OnExitTree()` | 退出场景树 |
| `Notification(Predelete)` | `OnPreDestroy()` | 销毁前（BaseComponent 在此触发框架 Shutdown） |

另有输入回调（`OnInput / OnUnhandledInput / OnUnhandledKeyInput / OnShortcutInput`）、场景树通知（`OnPostEnterTree / OnParented / OnUnparented / OnRenamed / OnChildOrderChanged / OnPaused / OnUnpaused`）与编辑器属性系统虚方法（`OnGetProperty / OnSetProperty / OnGetPropertyList` 等）。

> ⚠️ 生命周期实名是 `OnInit → OnEnter → OnUpdate/OnFixedUpdate → OnExitTree → OnPreDestroy`。旧文档中的 "OnExit / OnShutdown" 命名并不存在于代码。

静态工具：`GodotComponent.Create<T>(parent)` / `Create(Type)` / `Create(string typeName)`（经 `Utility.Assembly.GetType` 反射）与 `Destroy(node)`（`QueueFree`）。

### 3.3 组件注册（GameEntry）

- `GameFrameworkComponent.OnInit()` → `GameEntry.RegisterComponent(this)`；同类型重复注册打印错误并忽略。
- `GameEntry.GetComponent<T>() / GetComponent(Type) / GetComponent(string typeName)` 线性遍历链表查找。
- `GameEntry` 自身不注册（它是 `GodotComponent` 而非 `GameFrameworkComponent`）。

### 3.4 GF 静态门面

`GF` 为每个组件提供惰性缓存属性（首次访问时 `GameEntry.GetComponent<T>()` 并缓存）：

```csharp
GF.Base  GF.Event  GF.Fsm  GF.Procedure  GF.ObjectPool  GF.DataNode
GF.Resource  GF.Entity  GF.UI  GF.Sound  GF.Localization
GF.Setting  GF.Scene  GF.WebRequest  GF.Download  GF.Debugger
GF.Archive   // 共 17 个（Archive 为 new() 惰性单例，不来自 GameEntry 组件注册表）
```

> ✅（2026-07）`ShutdownType.Restart` 时 `GameEntry.OnInit` 自动调用 `GF.ClearCache()` 清除所有静态缓存，不再存在指向旧实例的问题。

### 3.5 ReferencePool 引用池

任何实现 `IReference`（唯一成员 `void Clear()`）的类都可池化：

```csharp
// 获取（池空则 new）
MyArgs e = ReferencePool.Acquire<MyArgs>();
// 归还（内部先调用 e.Clear() 再入队）
ReferencePool.Release(e);

// 预热 / 收缩 / 诊断
ReferencePool.Add<MyArgs>(16);
ReferencePool.RemoveAll<MyArgs>();
ReferencePoolInfo[] infos = ReferencePool.GetAllReferencePoolInfos();

// 严格检查（检测重复 Release、非法类型）——由场景 ReferencePool 节点按策略统一设置
ReferencePool.EnableStrictCheck = true;
```

- 内部按类型维护 `ReferenceCollection`（`Queue<IReference>` + lock），**线程安全**。
- `Release` 会先调 `Clear()` 再入队 —— 归还后严禁再持有/访问该引用。
- ⚠️ `ReferencePool.Release(null)` 会抛出 `GameFrameworkException("Reference is invalid.")` —— 调用方必须在 Release 前做空检查（如 `if (m_Check != null) ReferencePool.Release(m_Check)`）。
- 严格检查关闭时，重复 Release 不会抛异常，会导致同一实例被入队两次，后果是两处调用方拿到同一对象。✅（2026-07）场景新增 `ReferencePool` 节点（`ReferencePoolComponent`，命名空间 `GodotGameFramework.Reference`），按 `ReferenceStrictCheckType` 策略在 `OnEnter` 统一设置开关：`AlwaysEnable`（当前默认）/ `OnlyEnableInEditor`（`OS.HasFeature("editor")`）/ `OnlyOpenWhenDevelopment`（`OS.IsDebugBuild()`）/ `AlwaysDisable`。调试器 `Profiler/Reference Pool` 页签可查看各池计数并运行时切换严格检查（见 `DebuggerSystem.md`）。

### 3.6 日志系统

三层结构：

```
Log（Godot 桥接层，[Conditional] 编译期裁剪，1~16 泛型参数重载避免装箱）
  └── GameFrameworkLog（纯 C# 层，未设置 Helper 时静默丢弃）
        └── ILogHelper 实现：DefaultLogHelper → GD.Print / GD.PushWarning / GD.PushError
```

- `BaseComponent.OnInit()` 按 Inspector 中的类型名（默认 `GodotGameFramework.DefaultLogHelper`）反射创建并 `GameFrameworkLog.SetLogHelper()`。TextHelper（`DefaultTextHelper`，StringBuilder 缓存的 `Utility.Text.Format`）与 JsonHelper 同理。
- ✅（2026-07）`DefaultLogHelper` 在写 GD 输出前触发静态事件 `LogMessageReceived(level, message, stackTrace)`（堆栈仅 Error/Fatal 级别捕获），调试器控制台由此捕获全部框架日志（见 `DebuggerSystem.md`）；无订阅者时零额外开销。
- ✅（2026-07）`DefaultLogHelper` 另将 Warning/Error/Fatal 级别日志**持久化写入** `user://session.log`（512KB 滚动、线程安全），便于崩溃后排查。
- 各级别生效条件（`GodotProject.csproj` 的 `<DefineConstants>`）：

| 方法 | 生效符号（任一命中即编译保留） |
|------|------|
| `Log.Debug` | `ENABLE_LOG` / `ENABLE_DEBUG_LOG` / `ENABLE_DEBUG_AND_ABOVE_LOG` |
| `Log.Info` | `ENABLE_LOG` / `ENABLE_INFO_LOG` / `ENABLE_DEBUG_AND_ABOVE_LOG` / `ENABLE_INFO_AND_ABOVE_LOG` |
| `Log.Warning` | 上述 + `ENABLE_WARNING_LOG` / `ENABLE_WARNING_AND_ABOVE_LOG` |
| `Log.Error` / `Log.Fatal` | 依此类推（`ENABLE_ERROR_*` / `ENABLE_FATAL_*`） |

- `[Conditional]` 意味着符号未定义时**调用点整体被编译器删除**（连参数求值都不发生），零运行时开销。发布版删除整行 `<DefineConstants>` 即全部裁掉。

**TopMenu 插件**（`addons/TopMenu/`，`GameFrameworkTopMenu.cs` + 分部类 `GameFrameworkTopMenu.Generate.cs`）在编辑器 Tools 菜单提供三个子菜单：

- `GameFrameworkLog` —— 一键切换 7 档日志级别，实现方式是正则改写 `GodotProject.csproj` 中的 `<DefineConstants>`（如 "Enable Info And Above Logs" → `ENABLE_LOG;ENABLE_INFO_AND_ABOVE_LOG`；"Disable All Logs" → 删除整行）。改完需重新 `dotnet build` 生效。
- `OpenFolder` —— 直接打开 `res://` / `user://` / `Configs/GameConfig` / `Configs/Localization` 目录。
- `Generate File` —— 整合原 LocalizationEditor / Resources 插件：
  - `Localization File`：`Configs/Localization/*.xlsx` → `.txt` 字典导出（原 `LocalizationEditorPlugin`）；
  - `GameConfig File`：启动 Luban `gen_code_bin_to_project.bat/.sh` 生成配置代码；
  - `Collection Res`：扫描 `res://TheGame/` 重生成 `ResourcesCollectionConstant.cs`（原 `ResourcesCollectionEditor`）。

### 3.7 SingletonNode / SingletonSystem

框架组件之外的全局节点单例（命名空间 `GodotGameFrameworkCore.SingletonSystem`）：

```csharp
public partial class MyManager : SingletonNode<MyManager>
{
    public override void Active() { }        // 首次经 Instance 创建时
    protected override void OnLoad() { }     // _Ready 且确认非重复实例时
    protected override void OnRelease() { }  // Release 时
}

MyManager.Instance.DoSomething();            // 首次访问自动 new + 注册
```

- `Instance` 按 `typeof(T).Name` 查 `SingletonSystem` 的节点注册表，不存在则 `new T()` 并 `Retain`，随后通过 `tree.Root.CallDeferred(Node.MethodName.AddChild, node)` **自动将节点加入场景树根节点**。使用 `CallDeferred` 避免 `Instance` 在 `_EnterTree` 回调期间首次访问时触发 "Parent node is busy setting up children" 错误。`Active()` 方法在入树前同步执行，用于立即初始化。场景里预置的实例在 `_Ready` 时经 `CheckInstance()` 去重（重复者 `QueueFree`）。
- `SingletonSystem` 额外维护 `IUpdate` / `IFixedUpdate` 生命周期列表，由 `IUpdateDriver` 驱动（`UpdateDriver : GameFrameworkComponent` 把 `OnUpdate/OnFixedUpdate` 广播给监听者；也可 `SingletonSystem.SetUpdateDriver()` 手动注入）。
- 节点 `_ExitTree` 时自动 `Release()`（从注册表移除并 `QueueFree`）。

### 3.8 Variable 池化变量

`Variable`（抽象，`IReference`）→ `Variable<T>`（持有 `T Value`）→ 具体封装 `VarInt32 / VarSingle / VarString / VarBoolean`，用于 FSM 数据、DataNode 等需要以统一基类存值的场合：

```csharp
VarInt32 v = 100;            // 隐式转换内部 ReferencePool.Acquire<VarInt32>()
int raw = v;                 // 隐式转回
fsm.SetData("hp", v);        // SetData 覆盖旧值时旧 Variable 自动 Release
```

> 隐式转换每次都会从引用池取新实例 —— 存入 Fsm/DataNode 后由容器负责 Release；自行使用时需手动 `ReferencePool.Release(v)`。

### 3.9 PhysicsCheck2D

池化的 2D 形状检测封装（全局命名空间，文件在 `GodotGameFrameworkCore/Utility/`）：

```csharp
var check = PhysicsCheck2D.Create(
    targetNode: this,                 // 检测中心，自动从结果排除
    shape: new CircleShape2D { Radius = 200f },
    collisionMask: 0,                 // 0 = 不限层
    collideWithAreas: true,
    maxResults: 32,                   // 最大结果数
    collideWithBodies: true,          // 是否检测刚体
    margin: 0.0f);                    // 形状扩展边距

if (check.IsColliding())              // IntersectShape，过滤不在树上/不可见节点
{
    int count = check.CollidingCount;              // 碰撞数量
    Node2D nearest = check.GetCollidingNodesSorted()[0];   // 按距离升序
}
check.DrawDebugLines();               // 仅可在 _Draw() 内调用
ReferencePool.Release(check);         // 用完归还
```

`IsColliding()` 每次都会用 `TargetNode.GlobalTransform` 刷新查询位置，因此可作为长期字段持有、逐帧调用。

### 3.10 NodeExtension

`Node` 扩展方法：`GetOrAddChild<T>(name)`、`GetChild<T>()`、`GetChildren<T>()`、`GetChildByName<T>(name)`、`FindChildOfType<T>()`（递归）、`FindChildrenOfType<T>()`（递归）、`RemoveAllChildren()`、`GetParent<T>()`。

### 3.11 GTween（DOTween 风格 Tween 扩展）

`Utility/GTween.cs` — `Node2D` / `Control` 的扩展方法，封装 `Godot.Tween`，提供类似 DOTween 的链式动画 API。命名空间 `GodotGameFramework.DoTween`。

| 方法 | 适用类型 | 说明 |
|------|----------|------|
| `DoScale(target, duration)` | `Node2D`, `Control` | 缩放到目标值，默认 Expo.Out 缓动 |
| `DOPunchScale(punch, duration)` | `Node2D`, `Control` | 脉冲缩放：放大再缩回原值，Cubic.InOut |
| `DOLocalMove(target, duration)` | `Node2D` | 局部坐标移动，Expo.Out |
| `DOMove(target, duration)` | `Node2D` | 全局坐标移动，Expo.Out |
| `DORotate(angle, duration)` | `Node2D` | 旋转到目标角度，Back.Out |
| `DOColor(color, duration)` | `Node2D` | 调制颜色过渡，InOut |
| `Delay(delay, callback?)` | `Node` | 延迟执行回调（`TweenInterval` + `TweenCallback`） |

```csharp
// 使用示例
this.DoScale(1.2f, 0.5f);           // 缩放到 1.2 倍
this.DOPunchScale(1.5f, 0.3f);      // 脉冲放大
this.DOMove(new Vector2(100, 200));  // 移动到全局坐标
```

所有方法返回 `Tween` 对象，支持 `.Finished` 事件和 `.Kill()` 中断。`DropItem` 实体中使用 `DOMove` + `Finished` 回调实现拾取动画。

### 3.12 LayerMask（物理层工具）

`Utility/LayerMask : SingletonNode<LayerMask>` — 静态工具类，将 Godot 项目设置中的 `layer_names/2d_physics/layer_{i}` 和 `layer_names/3d_physics/layer_{i}` 映射为运行时可用字典，提供层名 ↔ 索引 ↔ 位掩码双向转换。

```csharp
// 层名 → 索引
int layer = LayerMask.NameToLayer2D("Player");     // e.g. 1
string name = LayerMask.LayerToName2D(1);           // "Player"

// 索引 → 位掩码（1-32 范围检查，越界返回 0）
uint mask = LayerMask.LayerToMask2D(1);             // 0b0001

// 层名 → 位掩码
uint mask = LayerMask.LayerToMask2D("Player");      // 0b0001

// 组合掩码（最常用）
uint mask = LayerMask.LayerToMask2D("Player", "Enemy", "Wall");
// 或 3D 版本
uint mask3D = LayerMask.LayerToMask3D("Player", "Ground");
```

| 方法 | 参数 | 说明 |
|------|------|------|
| `NameToLayer2D(string)` / `NameToLayer3D(string)` | 层名 | 返回索引（1-32），找不到返回 0 |
| `LayerToName2D(int)` / `LayerToName3D(int)` | 索引 | 返回层名，找不到返回 `""` |
| `LayerToMask2D(int/string)` / `LayerToMask3D(int/string)` | 索引或层名 | 返回 `uint` 位掩码，越界/找不到返回 0 |
| `LayerToMask2D(params string[])` / `LayerToMask3D(params string[])` | 多个层名 | 组合位掩码 |

设计要点：
- 继承 `SingletonNode<LayerMask>` 保证全局唯一实例，`OnLoad` 中初始化
- `m__Initialized` 标志防止重复遍历
- 2D 和 3D 层在同一循环中并行读取（`layer_names/2d_physics` 和 `layer_names/3d_physics`）
- 索引范围 [1, 32]，越界安全返回 0（避免 C# 移位计数截断导致静默错误）

---

## 4. BaseComponent

场景节点 `Base`，经 `GF.Base` 访问。

### 4.1 Inspector 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `EditorLanguage` | — | 编辑器语言（本地化系统用） |
| `EnableEditorResLoad` | `false` | 为 `true` 时框架直接从 `res://TheGame/` 加载资源而非 .pck 包；`OnInit` 中自动 `&= OS.HasFeature("editor")`，**仅在编辑器中生效** |
| `m_TextHelper` | `GodotGameFramework.DefaultTextHelper` | 文本格式化辅助器类型名 |
| `m_LogHelper` | `GodotGameFramework.DefaultLogHelper` | 日志辅助器类型名 |
| `m_JsonHelper` | `GodotGameFramework.DefaultJsonHelper` | JSON 辅助器类型名 |
| `m_FrameRate` | 60（30~120） | 映射 `Engine.MaxFps` |
| `m_GameSpeed` | 1（0~8） | 映射 `Engine.TimeScale` |

### 4.2 API

```csharp
GF.Base.FrameRate = 120;          // 直接改 Engine.MaxFps
GF.Base.GameSpeed = 0.5f;         // 直接改 Engine.TimeScale
GF.Base.PauseGame();              // GameSpeed = 0（记住暂停前速度）
GF.Base.ResumeGame();             // 恢复暂停前速度
GF.Base.ResetNormalGameSpeed();   // GameSpeed = 1
GF.Base.IsGamePaused / IsNormalGameSpeed
```

---

## 5. 注意事项 / FAQ

**Q: 新增一个框架系统要做什么？**
纯 C# 层：`GameFramework/Xxx/` 下建 `IXxxManager`（接口，`GameFramework.` 命名空间）+ `XxxManager : GameFrameworkModule, IXxxManager`（internal sealed，类名必须 = 接口名去 `I`，否则 `GetModule` 反射失败）。桥接层：`XxxComponent : GameFrameworkComponent`，`OnInit` 里 `GameFrameworkEntry.GetModule<IXxxManager>()`；再把节点挂进 `GameFramework.tscn` 并在 `GF.cs` 加惰性属性。

**Q: 为什么我的模块 Update 没被调？**
模块只有被 `GetModule` 过才存在（惰性创建）。确认对应组件已挂到主场景且 `OnInit` 已执行。

**Q: 组件里能用构造函数吗？**
不建议。Godot Node 由引擎实例化，初始化逻辑放 `OnInit`（进树）或 `OnEnter`（就绪）。注意 `OnInit` 时**子节点尚未 Ready**。

**Q: `Engine.TimeScale = 0` 后框架还跑吗？**
`_Process` 的 `delta` 会变 0 但仍每帧回调，`GameFrameworkEntry.Update(0, 0)` 照常轮询（此时 realElapseSeconds 被计为 0，与原版 GF 的"真实时间照走"语义不同，定时类逻辑注意）。

**Q: 池化对象被"重复 Release"怎么排查？**
开发期设 `ReferencePool.EnableStrictCheck = true`，重复归还会立刻抛 `GameFrameworkException`。

**Q: 日志切了级别没生效？**
TopMenu 只改 csproj，需要重新 `dotnet build`；编辑器内已加载的程序集要等重载。

---

## 6. 已知边界

- ✅（2026-07 已修复）GF 门面静态缓存问题 — `GameEntry.OnInit` 中 `GF.ClearCache()` 自动清空缓存，`ShutdownType.Restart` 后新场景重新获取。
- `GameEntry.OnUpdate` 的 realElapseSeconds 在 `TimeScale = 0` 时为 0（原版 Unity GF 使用 `Time.unscaledDeltaTime`，此处语义有差异）。
- `SingletonNode<T>.Instance` 通过 `CallDeferred` 将节点加入根场景树，`OnLoad` 在 `_Ready` 确认非重复实例后触发。
