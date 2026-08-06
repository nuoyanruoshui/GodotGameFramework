# 流程系统 (Procedure Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Procedure/`、`Framework/GodotGameFrameworkCore/Procedure/ProcedureComponent.cs`、`TheGame/MainPack/Scripts/Procedure/`
> 本文档描述 GGF 的流程系统：Procedure 即顶层 FSM 的设计、启动入口、状态切换、TheGame 现有流程链（Launch → Update → Prelode → Game）与新增流程的完整步骤。

---

## 1. 概述

Procedure（流程）是 Game Framework Procedure 模块的 Godot 移植，管理游戏的**顶层状态**（启动、热更、预载、玩法……），遵循框架的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Procedure/` | ProcedureBase（= FSM 状态）、ProcedureManager（持有流程 FSM） | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Procedure/ProcedureComponent.cs` | Inspector 配置流程列表、反射实例化、启动入口 | ✅ |

**核心事实：Procedure 就是一台持有者为 `IProcedureManager` 的全局 FSM**（见 `FsmSystem.md`）：

```csharp
public abstract class ProcedureBase : FsmState<IProcedureManager> { ... }
// 常用别名（各流程文件顶部）：
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
```

整个游戏**同时只有一个当前流程**；流程内如需子状态，再嵌套创建自己的 FSM。

---

## 2. 架构与数据流

### 2.1 启动链路

```
GameEntry.OnUpdate()  （每帧，_Process）
    │ CheckProcedure()：首帧发现 ProcedureComponent 已注册
    ▼
ProcedureComponent.StartProcedure()
    │（此前 OnInit → LoadProcedures 已完成：）
    │   ① 读 Inspector 的 Procedures（string[] 类型名）
    │   ② Type.GetType + Activator.CreateInstance 逐个实例化 ProcedureBase
    │   ③ FullName == EnterProcedure 的实例记为入口流程
    │   ④ m_ProcedureManager.Initialize(fsmManager, procedureList)
    │        └── m_ProcedureFsm = fsmManager.CreateFsm(this, procedures)   ← 创建流程 FSM
    ▼
ProcedureManager.StartProcedure(入口流程类型)
    └── m_ProcedureFsm.Start(type) → 入口流程.OnEnter(procedureOwner)
```

之后每帧 `FsmManager.Update` 驱动流程 FSM → 当前流程的 `OnUpdate`。`ProcedureManager` 自身 `Priority = -2`（最后轮询），其 `Update` 为空 —— 流程轮询完全由 FsmManager 完成。

### 2.2 TheGame 流程链

场景配置（`Framework/GameFramework.tscn` 的 `Procedure` 节点）：

```
EnterProcedure = "ProcedureLaunch"
Procedures = ["ProcedureGame", "ProcedureLaunch", "ProcedurePrelode", "ProcedureUpdate"]
```

```
ProcedureLaunch ──► ProcedureUpdate ──► ProcedurePrelode ──► ProcedureGame
   组件自检             热更检测/下载          资源组预载             玩法入口
```

### 2.3 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Procedure/ProcedureBase.cs` | 流程基类 = `FsmState<IProcedureManager>`，5 个生命周期均带日志 |
| `GameFramework/Procedure/IProcedureManager.cs` | 管理器接口 |
| `GameFramework/Procedure/ProcedureManager.cs` | 持有流程 FSM；Initialize/StartProcedure/HasProcedure/GetProcedure |
| `GodotGameFrameworkCore/Procedure/ProcedureComponent.cs` | Inspector 配置 + 反射装配 + `StartProcedure()` |
| `TheGame/MainPack/Scripts/Procedure/ProcedureLaunch.cs` | 入口：框架组件自检 |
| `TheGame/MainPack/Scripts/Procedure/ProcedureUpdate.cs` | 热更：版本比对、并发下载、子包加载（详见 DownloadSystem.md §5） |
| `TheGame/MainPack/Scripts/Procedure/ProcedurePrelode.cs` | 预载：本地化 + UI/Entity/Sound 组注册 |
| `TheGame/MainPack/Scripts/Procedure/ProcedureGame.cs` | 玩法：标记启动成功、打开主菜单 |

---

## 3. 核心机制

### 3.1 流程生命周期

`ProcedureBase` 完整继承 FSM 状态生命周期，且每个阶段都会打 `Log.Info`（`Procedure 'Xxx' init/enter/leave/destroy.`）：

| 回调 | 时机 |
|------|------|
| `OnInit(procedureOwner)` | 流程 FSM 创建时（每个流程一次，游戏启动早期） |
| `OnEnter(procedureOwner)` | 进入该流程 |
| `OnUpdate(procedureOwner, elapse, realElapse)` | 该流程为当前流程时每帧 |
| `OnLeave(procedureOwner, isShutdown)` | 离开该流程（`isShutdown = true` 表示框架关闭） |
| `OnDestroy(procedureOwner)` | 流程 FSM 销毁时 |

### 3.2 流程切换（ChangeState）

继承自 `FsmState` 的 protected 方法 —— **只能在流程内部**发起：

```csharp
protected internal override void OnEnter(ProcedureOwner procedureOwner)
{
    base.OnEnter(procedureOwner);
    ChangeState<ProcedureUpdate>(procedureOwner);   // 当前流程 OnLeave → 目标流程 OnEnter
}
```

目标流程必须在 Inspector 的 `Procedures` 列表里（即已注册进流程 FSM），否则抛异常。

### 3.3 流程间传参

`procedureOwner` 即流程 FSM（`IFsm<IProcedureManager>`），可用 FSM 数据在流程间传值：

```csharp
procedureOwner.SetData<VarInt32>("NextSceneId", 2);      // A 流程写
int sceneId = procedureOwner.GetData<VarInt32>("NextSceneId");  // B 流程读
procedureOwner.RemoveData("NextSceneId");                 // 用完移除（自动 Release）
```

### 3.4 反射装配细节（ProcedureComponent.LoadProcedures）

- `Type.GetType(Procedures[i])`：TheGame 的流程类都在**全局命名空间**，所以配置里写短名即可；如果流程类带命名空间，配置必须写完整限定名（必要时含程序集）。
- 入口匹配用 `procedureType.FullName == EnterProcedure`，同样受命名空间影响。
- 类型无效 / 不是 `ProcedureBase` 派生 / 入口没匹配到，都会 `Log.Error/Fatal` 并**中止装配**（游戏停在空流程，仅有日志提示）。
- 也支持代码手动装配：`GF.Procedure.Initialize(fsmManager, procedures)` + `StartProcedure<T>()`（勿与 Inspector 配置混用，会重复初始化）。

### 3.5 与异步的配合

`ProcedureUpdate` / `ProcedureGame` 的 `OnEnter` 是 `async void`：进入流程后启动异步任务（热更下载、`OpenUIFormAsync`），完成后再 `ChangeState`。注意：

- 框架不会等待异步完成，流程期间的每帧逻辑仍走 `OnUpdate`；
- 异步续体经 Godot 同步上下文回到主线程，可直接调 `ChangeState`；
- 异常要自行 try/catch（`async void` 的异常无法被框架捕获），`ProcedureUpdate` 的做法是 catch 后 `SkipToNext`（降级进入下一流程）。

---

## 4. TheGame 现有流程职责

### 4.1 ProcedureLaunch（入口）

`OnEnter` 检查 14 个框架组件（`Base / Event / Fsm / Setting / DataNode / Resource / Entity / UI / Sound / Localization / WebRequest / Download / Scene / ObjectPool`）是否都已注册（`GF.Xxx != null`）。全部通过后 `ChangeState<ProcedureUpdate>`；否则 `Log.Fatal` 列出缺失组件并停留。

### 4.2 ProcedureUpdate（热更）

完整逻辑见 `DownloadSystem.md` §5，摘要：

- Package 模式 + `EnableEditorResLoad` → 直接 `ChangeState<ProcedurePrelode>`（跳过子包加载）；
- Package 模式（非 EditorResLoad）→ 调用 `TryLoadLocalSubpackagesAsync()` 读取 `SubpackDir` 下的 `GameFrameworkVersion.dat` 清单加载本地子包，然后进入下一流程；
- 上次启动崩溃（`HotUpdateSafetyGuard` 安全模式）→ 直接 `ChangeState<ProcedurePrelode>`；
- 未配置 `RemoteUrl` → 校验并加载本地已下载子包后进入下一流程；
- 正常路径：拉取远端 `GameFrameworkVersion.dat` → `MinAppVersion` 检查 → 本地完整性自检 → 差量比对 → 磁盘空间预检 → `GF.Download` 并发下载（每包重试 3 次、指数退避）→ `LoadResourcePack` 加载子包 → 保存新清单；
- 热更目录 `SubpackDir` 优先级：`UpdateSettingRes.HotUpdatePath` → 安装目录 `subpackages/`（可写时）→ `user://subpackages/`；
- 字段 `LoadingForm m_loadingForm`（打开后经 `SetLogState(message, progress)` 更新进度条与状态文本）；异步加载流程在 `finally` 块中关闭 LoadingForm；
- 任何异常都降级 `SkipToNext` → `ProcedurePrelode`（保证能进游戏）。

### 4.3 ProcedurePrelode（预载）

`OnEnter` 按顺序完成 8 步（`async void`，前 4 个 Load 方法各自包在 try/catch 中，单个失败不中断流程）：

| 顺序 | 项 | 动作 |
|------|----|------|
| 1 | EntityGroup | 遍历 `GF.Entity.EntityGroupRes.EntityGroups` → `AddEntityGroup(...)` |
| 2 | Localization | `EnableEditorResLoad` 分支：非编辑器路径 `GF.Localization.Language = (Language)GF.Setting.GetInt("Language", ...)`（从 Setting 持久化读取）；编辑器路径使用 `GF.Base.EditorLanguage` 或 `SystemLanguage` |
| 3 | UIGroup | 遍历 `GF.UI.UIGroupRes.Groups` → `AddUIGroup(name, depth)` |
| 4 | SoundGroup | 遍历 `GF.Sound.SoundGroupRes.SoundGroups` → `AddSoundGroup(...)`，并从 Setting 恢复音量（`SetVolume(DefaultMusicGroup/SfxGroup/UiGroup, GetFloat(..., 1))`） |
| 5 | NodePool | `NodePool.Instance.Active()` 启动节点池 |
| 6 | LayerMask | `LayerMask.Instance.Active()` 启动层级工具 |
| 7 | LoadingForm | `await GF.UI.OpenLoadingUIFormAsync()` 打开加载界面 |
| 8 | Archive | `await GF.Archive.LoadAsync()` 加载存档数据 |

错误语义分层：① 单个组 `Add*Group` 返回 false 时，Load 方法内 `Log.Warning`（如 `"Add UI group '{0}' failure."`）并 return，对应 `m_LoadFlagDic` 项保持 false；② 四个 Load 方法抛异常时被各自 try/catch 捕获 → `Log.Fatal`；③ 最后 `IsLoadAll()` 检查 `m_LoadFlagDic` 四项 —— 任一为 false 则 `Log.Warning("部分模块加载失败，继续进入游戏。")`，但**无论成败都 `ChangeState<ProcedureGame>` 继续进入游戏**。

### 4.4 ProcedureGame（玩法）

`OnEnter`：`HotUpdateSafetyGuard.MarkStartupSuccess()`（标记本次启动成功，后续崩溃不再归因热更）→ `await GF.UI.OpenUIFormAsync<MenuForm>(UIFormId.MenuForm)` 打开主菜单。玩法内的具体状态（菜单/对局等）由 UI 与实体逻辑驱动。

> **LoadingForm 复用**：`LevelManager.StartLevel(level)`（`TheGame/GameScripts/Manager/LevelManager.cs`）也会调用 `await GF.UI.OpenLoadingUIFormAsync()` 打开 LoadingForm 展示关卡加载进度，随后异步加载场景、生成实体、打开 MainForm。

---

## 5. ProcedureComponent API

场景节点：`Framework/GameFramework.tscn` 中的 `Procedure` 节点，经 `GF.Procedure` 访问。

### 5.1 Inspector 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `Procedures` | `string[]` | 全部流程的类型名（顺序无关；全局命名空间类写短名） |
| `EnterProcedure` | `string` | 入口流程类型名（须与某项的 `Type.FullName` 相等） |

### 5.2 方法 / 属性

```csharp
GF.Procedure.CurrentProcedure        // 当前流程实例（ProcedureBase）
GF.Procedure.CurrentProcedureTime    // 当前流程已持续秒数

GF.Procedure.HasProcedure<T>() / HasProcedure(Type)
GF.Procedure.GetProcedure<T>() / GetProcedure(Type)

GF.Procedure.StartProcedure()        // 启动 Inspector 配置的入口流程（GameEntry 自动调用）
GF.Procedure.StartProcedure<T>()     // 启动指定流程（仅初始化时用一次；已运行则忽略）
GF.Procedure.Initialize(fsmManager, procedures)   // 手动装配（替代 Inspector）
GF.Procedure.LoadProcedures()        // 按 Inspector 配置装配（OnInit 自动调用）
```

> `StartProcedure` 只在流程 FSM 未运行时生效（`IsRunning` 则直接 return）；运行期切换一律走流程内部 `ChangeState`。

---

## 6. 如何新增一个 Procedure

以新增 `ProcedureSettlement`（结算流程）为例：

**1) 建类**（`TheGame/MainPack/Scripts/Procedure/ProcedureSettlement.cs`，与现有流程一致放全局命名空间）：

```csharp
using GameFramework.Procedure;
using GodotGameFramework;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

public class ProcedureSettlement : ProcedureBase
{
    protected internal override void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);
        int score = procedureOwner.GetData<VarInt32>("FinalScore");   // 读上游传参
        // ... 展示结算 UI ...
    }

    protected internal override void OnUpdate(ProcedureOwner procedureOwner, float e, float r)
    {
        base.OnUpdate(procedureOwner, e, r);
        if (/* 玩家确认 */) ChangeState<ProcedureGame>(procedureOwner);
    }

    protected internal override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        procedureOwner.RemoveData("FinalScore");
        base.OnLeave(procedureOwner, isShutdown);
    }
}
```

**2) 注册到场景**：Godot 编辑器打开 `Framework/GameFramework.tscn`，选中 `Procedure` 节点，在 Inspector 的 `Procedures` 数组里追加 `"ProcedureSettlement"`（新增 .cs 后需先 `dotnet build`，必要时 `--build-solutions` 刷新解决方案）。

**3) 接入切换**：在来源流程（如 `ProcedureGame`）里 `procedureOwner.SetData<VarInt32>("FinalScore", score); ChangeState<ProcedureSettlement>(procedureOwner);`

若要更换游戏入口流程，改 `EnterProcedure` 字段即可（如指到调试用流程，跳过热更链）。

---

## 7. 注意事项 / FAQ

**Q: 流程没启动/黑屏，日志说 "Entrance procedure is invalid"？**
`EnterProcedure` 与流程类 `FullName` 不一致（注意命名空间），或该类型不在 `Procedures` 列表里。

**Q: `ChangeState` 抛 "can not change state to ... which is not exist"？**
目标流程没加进 Inspector 的 `Procedures` 数组 —— 流程集合在启动时一次性装配，运行期不可增补。

**Q: 能从流程外部强制切流程吗？**
不能（`ChangeState` 是 protected，`StartProcedure` 在运行中无效）。正确做法：Fire 事件或 SetData，由当前流程在 `OnUpdate` 响应后自行切换。

**Q: 流程类为什么建议放全局命名空间？**
装配用 `Type.GetType(短名)`，全局命名空间最省事；带命名空间也支持，但 Inspector 里必须写完整限定名，且 `EnterProcedure` 同步修改。

**Q: OnInit 里能用 GF.Xxx 吗？**
慎用。流程 FSM 在 `ProcedureComponent.OnInit`（组件进树阶段）创建，此时**其余组件可能尚未注册**。组件依赖统一放 `OnEnter`（`ProcedureLaunch` 的组件自检就是为此存在）。

**Q: Prelode 某个组注册失败卡住怎么排查？**
看 `Add XX group ... failure.` 警告；该流程无重试，修复资源配置（`TheGame/MainPack/Resources/` 下的 Group 定义）后重启。

**Q: 与 CLAUDE.md 描述的差异？**
CLAUDE.md 写 "ProcedureLaunch 加载实体/UI/声音组与本地化后切到 ProcedureGame"——实际该职责在 `ProcedurePrelode`；现行流程链为 Launch → Update → Prelode → Game（四段）。
