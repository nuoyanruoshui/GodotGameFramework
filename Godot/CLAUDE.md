# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**GGF** (Godot Game Framework) — **Godot 4.7 + C# (.NET 8)** port of [Game Framework](https://gameframework.cn/) (Jiang Yin). Modular architecture: Event, FSM, Procedure, Resource, Entity, UI, Audio, Localization, ObjectPool, DataTable, DataNode, Setting, WebRequest, Download, Debugger, Archive.

> 📚 **Per-system deep-dive docs live in `docs/`** (FrameworkCore / Event / Fsm / Procedure / Debugger / Resource / Entity / ObjectPool / UI / Sound / Scene / DataTable / DataNode / Setting / Localization / WebRequest / Download / Archive + 资源热更审计; C# 程序集热更方案已搁置等待华佗团队适配). See `docs/README.md` for the index. Prefer those docs over this file for system details.

- **Godot .NET SDK**: `Godot.NET.Sdk/4.7.0` (NuGet)
- **Build**: `cd GodotProject && dotnet build`
- **Add .cs files**: `"<godot_exe>" --build-solutions --path GodotProject --no-window -q`
- **Open editor**: `"<godot_exe>" --path GodotProject --editor`
- **Godot path**: `D:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe` (note: the exe is nested two directory levels deep — `Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64/`). Bash on Windows here needs forward slashes.
- **Active game project**: `TheGame/`
- **No test framework detected** — game is runtime-only (no test files found)
- **Rendering**: D3D12 (Forward Plus), **Physics**: Jolt Physics (3D), **Stretch**: canvas_items / expand

## Dual-Layer Architecture

The framework has a strict **two-layer separation** mirroring the original Game Framework design. **Key rule:** `GameFramework/` knows nothing about Godot. `GodotGameFrameworkCore/` depends on both `GameFramework/` and Godot — new systems put interface/logic in `GameFramework/` and Godot bridge in `GodotGameFrameworkCore/`.

```
GodotProject/
  Framework/
    GameFramework/                  ← Pure C# modules (zero Godot dependency)
      Base/                         ← GameFrameworkEntry, GameFrameworkModule, ReferencePool, EventPool
      Fsm/                          ← State machine system
      Procedure/                    ← Procedure (game state) manager
      Entity/ UI/ Sound/ Scene/     ← Manager interfaces + logic (no Godot types)
      DataNode/ ObjectPool/
      Resource/                     ← IResourceManager + 加载回调委托/状态枚举/Constant
      Debugger/ Download/           ← Debugger windows, download manager
      Event/ Localization/          ← Event manager, localization system
      WebRequest/                   ← IWebRequestManager + WebRequestManager (TaskPool 调度、serialId、超时)
      Properties/ Utility/          ← Assembly info, text/compression utilities
    GodotGameFrameworkCore/         ← Godot runtime components
      Base/                         ← GF.cs facade, GameEntry, GameFrameworkComponent, GodotComponent
      Entity/ UI/ Sound/ Scene/    ← Godot bridge components (each delegates to the corresponding Manager)
      Resource/                     ← ResourceComponent, ResourceManager, PackVersionList, load tasks, IResourceLoadHelper/DefaultResourceLoadHelper
      Download/ WebRequest/         ← DownloadComponent (queue+resume+verify), WebRequestComponent (wraps IWebRequestManager + N HttpRequest agent helpers)
      HotUpdate/                    ← HotUpdateSafetyGuard (crash-safe hot update)
      DataNode/ Setting/ Localization/
      Event/ Fsm/ Procedure/ ObjectPool/          ← ObjectPool 含 ReferencePoolComponent（引用池严格检查策略）
      Debugger/                     ← UGF 风格运行时调试器（FPS 图标 + Console/Information/Profiler/Other 页签）
      Archive/                      ← ArchiveSystem<T,U> 通用存档系统（Catalogue + Data 分离）+ Rijindael（AES-256 加密，由 ArchiveSetting 配置）
      Config/ Variable/             ← GameFolderConstant, VarInt32/VarString/VarBoolean/VarSingle
      Json/                         ← Newtonsoft.Json helper (local .dll reference) + EasySave
      Lib/LubanLib/                 ← Luban runtime (ByteBuf, BeanBase, StringUtil)
      SingletonSystem/              ← SingletonNode<T> pattern
      Templet/                      ← Script generation templates (UIForm/Entity, Ge/Logic)
      Utility/                      ← PhysicsCheck2D, NodeExtension, DefaultLogHelper, GTween, LayerMask
  TheGame/                          ← Active game project
    MainPack/                       ← Main package (shared core infrastructure)
      Scripts/
        ObjectPool/                 ← NodePool, NodeObject, PoolContainer
        Procedure/                  ← ProcedureLaunch, ProcedureUpdate, ProcedurePrelode, ProcedureGame
        Resources/                  ← EntityGroup, SoundGroup, UIGroup, NodePoolConfig, ScriptGenerateRes, UpdateSettingRes
        UI/                         ← LoadingForm, QuestionTips (shared UI)
      Fonts/                        ← simhei.ttf
      Resources/                    ← .tres config resources (EntityGroupRes, UIGroupRes, etc.)
      Themes/                       ← MainThemes.tres
      UI/                           ← .tscn scene files (LoadingForm, QuestionTips)
    GameScripts/
      Entity/                       ← ActorEntity, CatEntity, AngerEntity, GanTanEntity (Logic halves)
      UI/                           ← MenuForm, MainForm, GameOverForm, PauseMenuForm, TestOverlayForm
      Event/                        ← BlockClickedEventArgs, ScoreChangedEventArgs, TestPhaseChangedEventArgs
      Archive/                      ← GameCatalogue, GameData (ArchiveCatalogue/ArchiveData extensions)
      Manager/                      ← LevelManager (SingletonNode, wave system)
      GameProto/GameConfig/         ← Luban-generated C# (EntityConfig, TbEntityConfig, EntityId, etc.)
      GameProto/EntityGe/           ← Generated entity Ge halves
      GameProto/UIGe/               ← Generated UI form Ge halves (MenuForm, MainForm, GameOver, SettingForm)
  addons/                           ← Editor plugins
    ComponentInsoector/             ← Custom Godot Inspector for framework components
    ExportInspector/                ← AssetBundle visual export management panel (C# EditorPlugin)
    asset_bundle/                   ← AssetBundle resource marker + export plugin + pack utils (GDScript)
    TopMenu/                        ← GameFramework top menu: log level, open folders, Generate File (localization / GameConfig Luban / resource collection)
```

### Newtonsoft.Json

Referenced from a local .dll (not NuGet):
```xml
<Reference Include="Newtonsoft.Json">
  <HintPath>.\Framework\GodotGameFrameworkCore\Lib\Json\Newtonsoft.Json.dll</HintPath>
</Reference>
```

### Unsafe Code

`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` is enabled for pointer operations in `Utility.Converter.cs`.

## Scene Tree & Startup

Main scene: `Framework/GameFramework.tscn` (uid `bggentry001`), set as `run/main_scene`.

```
GameFramework (GameEntry : GodotComponent)
├── Base / Event / Resource / Debugger
├── Procedure / Scene / Fsm
├── DataNode / ObjectPool / ReferencePool / Setting
├── Entity / UI / Sound / Localization
├── WebRequest / Download
```

Each component type can only register one instance. Component list mirrors the `GF.cs` static facade (16 `GameFrameworkComponent` + `GF.Archive` as a `new()`-lazy singleton).

### Startup Sequence

1. Godot loads `Framework/GameFramework.tscn`
2. `GameFrameworkComponent.OnInit()` auto-calls `GameEntry.RegisterComponent(this)`
3. `GameEntry._Process()` drives `GameFrameworkEntry.Update()` each frame, polling all `GameFrameworkModule`s
4. `GameEntry.CheckProcedure()` detects `ProcedureComponent` registration, then auto-calls `StartProcedure()` → enters `ProcedureLaunch`

### Component Lifecycle (GodotComponent)

```
OnInit()       → OnEnter()  → OnUpdate(delta) / OnFixedUpdate(delta) → OnExitTree()  → OnPreDestroy()
 (_EnterTree)    (_Ready)     (_Process / _PhysicsProcess)             (_ExitTree)     (Predelete)
```

`GameFrameworkComponent : GodotComponent` overrides `OnInit()` to self-register with `GameEntry`.

## GF Static Facade

`GodotGameFrameworkCore/Base/GF.cs` provides lazy-cached static access to all components:

```csharp
GF.Base / GF.Event / GF.Fsm / GF.Procedure / GF.ObjectPool / GF.DataNode
GF.Resource / GF.Entity / GF.UI / GF.Sound / GF.Localization
GF.Setting / GF.Scene / GF.WebRequest / GF.Download / GF.Debugger
GF.Archive   // 共 16 个组件 + 1 个存档管理器
```

Each property calls `GameEntry.GetComponent<T>()` and caches the result. `GF.Archive` is an exception: it's an `ArchiveSystem<GameCatalogue, GameData>` lazily created via `new()` rather than looked up from the component registry. 存档目录名/加密开关/密钥盐由 `ArchiveSetting.tres`（`TheGame/MainPack/Resources/`）驱动，可经 `Rijindael` 做 AES-256 加密（详见 `docs/ArchiveSystem.md`）。

## Key Patterns

### Entity System

Entities directly inherit Godot node types and implement `IEntity` (no abstract base-class layer). Entity scripts are **generated** by `ScriptGenerateInspector` as split partial classes (Ge half in `GameProto/EntityGe/`, Logic half in `GameScripts/Entity/`). Lifecycle: `OnInit/OnRecycle/OnShow/OnHide/OnUpdate` (see `docs/EntitySystem.md`).

**TheGame project entity hierarchy:**
```
CharacterBody2D (Godot)                  Area2D (Godot)                    Node2D (Godot)
  └── ActorEntity : IEntity, IActor        ├── GanTanEntity : IEntity        └── DropItem : IPoolable
       ├── CatEntity   ← Player cat        └── LightningBall : IEntity            ← Collectible, GTween DOMove
       └── AngerEntity ← Enemy
  ActorData (Hp/MaxHp), EntityTeam, PhysicsCheck2D, Die()
```

Entity spawning via `GF.Entity.ShowEntity<T>(EntityId.Xxx)` or `ShowEntityAsync<T>(EntityId.Xxx, userData)` — config-driven from `TbEntityConfig`. `DefaultEntityHelper` 会在 AddChild 后对 `CanvasItem` 实体调用 `MoveToFront()`（同样 `DefaultUIFormHelper` 生成 UIForm 后 `MoveToFront()`，保证新开窗口/实体置顶）。

**CatEntity FSM example:** `CatEntity` creates a nested `Fsm<CatEntity>` in `OnInit` with `IdleState`/`MoveState : FsmState<CatEntity>` classes. `m_Fsm.Start<IdleState>()` in `OnShow`. States switch based on the public `m_IsMoving` flag. `Anim` (`AnimatedSprite2D`) is declared in the `ActorEntity` base class, shared by `CatEntity` and `AngerEntity`.

### UI System

UI forms are **generated** partial classes: `partial class XxxForm : Control, IUIForm` (no shared `ControlUIForm` base class). Localization text nodes implement `IStringKey` and are auto-collected from descendants (see `docs/UISystem.md`).

UI lifecycle: `OnInit` → `OnOpen` → `OnCover`/`OnReveal` → `OnUpdate` → `OnClose`.

Opening: `GF.UI.OpenUIForm(UIFormId.MenuForm)` or `await GF.UI.OpenUIFormAsync<T>(UIFormId.MenuForm)`.

TheGame UIs: `LoadingForm`, `MenuForm`, `MainForm`, `GameOverForm`, `PauseMenuForm`, `TestOverlayForm`, `SettingForm`, `QuestionTips`. `LoadingForm` subscribes to `OpenUIFormUpdateEventArgs`/`LoadSceneUpdateEventArgs` (progress bar + Tween smoothing) and `OpenUIFormSuccessEventArgs`/`LoadSceneSuccessEventArgs` (auto-close with re-entry guard `m_IsCloseRequested`). `QuestionTips` implements `ITips` for confirmation dialogs.

`UIItemBase : Control` for reusable UI widgets. Pooled via `UIItemInstanceObject` (infrastructure present; the sample `ScorePopupItem` is currently commented out).

### Procedure (FSM) System

Procedures manage top-level game states. TheGame procedure chain:
- `ProcedureLaunch` — validates all framework components, then → `ProcedureUpdate`
- `ProcedureUpdate` — resource hot-update (active): version check, concurrent pack download via `GF.Download`, integrity verify, subpackage loading (see `docs/DownloadSystem.md`), then → `ProcedurePrelode`；C# 程序集热更已搁置等待华佗团队 Godot 适配
- `ProcedurePrelode` — loads entity/UI/sound groups and localization, then → `ProcedureGame`
- `ProcedureGame` — gameplay loop, opens `MenuForm` on entry

Change state: `ChangeState<T>(procedureOwner)`. Each procedure can have its own nested FSM for sub-states.

### Component Delegate Pattern

```
Godot Component (e.g., EntityComponent)
  → GameFrameworkEntry.GetModule<IEntityManager>()
  → Pure C# Manager (e.g., EntityManager)
  → Delegates all real work to the Manager
```

### SingletonNode<T>

`SingletonSystem/SingletonNode<T> : Node` — a generic singleton pattern for Godot nodes:
- `SingletonNode<T>.Instance` creates the node on first access if none exists in the scene tree
- `_Ready()` ensures only one instance survives (duplicates `QueueFree()`)

### PhysicsCheck2D

`Utility/PhysicsCheck2D : IReference` — wraps `PhysicsDirectSpaceState2D.IntersectShape` with object pooling (`ReferencePool`). Auto-excludes the target node, supports sorted results by distance, and debug drawing. Usage: `PhysicsCheck2D.Create(targetNode, shape, ...)`.

### GTween (DOTween-style Extensions)

`Utility/GTween.cs` — `Node2D` / `Control` extension methods for `Godot.Tween`, mimicking DOTween API:

| Method | Target | Description |
|--------|--------|-------------|
| `DoScale(Vector2/float, duration)` | `Node2D`, `Control` | Scale to target with Expo.Out easing |
| `DOPunchScale(Vector2/float, duration)` | `Node2D`, `Control` | Punch scale: grow → shrink back with Cubic.InOut |
| `DOLocalMove(Vector2, duration)` | `Node2D` | Move local position with Expo.Out |
| `DOMove(Vector2, duration)` | `Node2D` | Move global position with Expo.Out |
| `DORotate(float, duration)` | `Node2D` | Rotate with Back.Out easing |
| `DOColor(Color, duration)` | `Node2D` | Modulate color with InOut easing |
| `Delay(float, callback?)` | `Node` | Delayed callback via `TweenInterval` + `TweenCallback` |

Namespace: `GodotGameFramework.DoTween`. Use via `this.DoScale(...)` / `this.DOMove(...)` extension calls.

### LayerMask

`Utility/LayerMask : SingletonNode<LayerMask>` — static utility mapping Godot physics layer names ↔ indices ↔ bit masks. Reads `layer_names/2d_physics/layer_{i}` and `layer_names/3d_physics/layer_{i}` from `ProjectSettings`.

| Method | Description |
|--------|-------------|
| `NameToLayer2D/3D(string)` | Layer name → index (0 if not found) |
| `LayerToName2D/3D(int)` | Index → layer name (empty if not found) |
| `LayerToMask2D/3D(int)` | Index → uint bit mask (0 if out of range [1,32]) |
| `LayerToMask2D/3D(string)` | Name → uint bit mask (0 if not found) |
| `LayerToMask2D/3D(params string[])` | Combine multiple names into single mask |

```csharp
uint mask = LayerMask.LayerToMask2D("Player", "Enemy", "Wall");
```

### Event System

Custom event args inherit `GameFrameworkEventArgs`. TheGame examples: `BlockClickedEventArgs`, `ScoreChangedEventArgs`, `TestPhaseChangedEventArgs`. Fire via `GF.Event.Fire(this, e)`.

**Critical rule — copy on forward:** pure-layer managers release their event args **immediately after the callback returns**. A Godot component forwarding a manager event into `GF.Event.Fire` must fire a **copy** (`XxxEventArgs.Create(e)` or full-parameter `Create(...)`), never the manager's instance — otherwise EventPool releases it a second time (strict check throws `The reference has been released.`) and subscribers read cleared data.

### ReferencePool / Object Pool

`IReference` interface + `ReferencePool.Acquire<T>()`/`Release()` for lightweight object reuse. `ObjectPoolComponent` wraps `ObjectPoolManager` for pooled Godot objects. `ReferencePoolComponent` (scene node `ReferencePool`) applies a strict-check policy (`ReferenceStrictCheckType`, default `AlwaysEnable`) — double-`Release` throws instead of silently corrupting the pool.

### NodePool

`TheGame/MainPack/Scripts/ObjectPool/NodePool.cs` — `SingletonNode<NodePool>`, a general-purpose node instance pool built on `GF.ObjectPool`. Objects implement `IPoolable` (`OnGet`/`OnRelease`), config-driven via `NodePoolConfigRes.tres`, lazy-instantiate on first `Get`, and auto-recycle orphan nodes. Details: `docs/ObjectPoolSystem.md` §7, `docs/NodePoolSystem.md`.

```csharp
// 获取 → 使用 → 自动归还
var d = NodePool.Get<DamagePop>(ScenePath, parent);
d?.SetText(pos, 20);
// DamagePop 内部延迟后调用 NodePool.Release(this)
```

Pooled types: `DamagePop` (floating damage numbers), `DropItem` (collectibles with GTween DOMove animation), `QuestionTips` (confirmation dialog).

### Debugger

UGF-style runtime debugger (`GF.Debugger`): draggable FPS icon (click to expand) + full window with `Console | Information | Profiler | Other` tab groups, rendered as BBCode into a RichTextLabel (IMGUI-style, `[url]` links route interactions). Profiler 页签含 Resource/WebRequest/Download 代理计数窗口。Console captures framework logs via `DefaultLogHelper.LogMessageReceived`. Custom windows: `GF.Debugger.RegisterDebuggerWindow("Game/Cheat", window)`. Details: `docs/DebuggerSystem.md`.

### Scene System

`SceneComponent` wraps `ISceneManager`. New `enum LoadSceneMode { Single, Additive }`. `LoadScene`/`LoadSceneAsync` now have overloads taking `LoadSceneMode`. `Single` mode calls `UnloadAllScenes()` first; `Additive` stacks scenes. `LoadSceneUpdateEventArgs` provides real-time progress (0.0--1.0), fired via the `m_EnableLoadSceneUpdate` toggle. `SceneEventArgs` (Godot layer) exposes `SceneAssetName`, `Duration`, `Progress`, `UserData`.

`LoadAssetAgent` now reports REAL progress via the `IResourceLoadHelper` (`GetLoadStatus(path, progress)`, default impl 走 `ResourceLoader.LoadThreadedGetStatus`) (0.0--1.0), consumed by the loading UI and scene update events.

### Localization

`LocalizationComponent.Language` setter now (on change): sets manager language, sets `TranslationServer.Locale`, calls `RemoveAllRawStrings()`, reloads data, fires `OnLanagueChangeEventArgs`, persists via `GF.Setting.SetInt("Language", ...)` + `GF.Setting.Save()`. New `GetLocalizationFileNames()` returns `.txt` file names from `GameFolderConstant.LocalizationPath`（经 `DirAccess` 扫描，兼容打包后的 `.pck` 虚拟文件系统；结果按名称排序）.

`LabelTr`/`ButtonTr` implement `IStringKey` and auto-subscribe to `OnLanagueChangeEventArgs` in `_Ready` / `_ExitTree` for live text updates on language switch.

## Component Inspector Addon

`addons/ComponentInsoector/` provides custom Godot Inspector plugins for the framework's component hierarchy. It registers `BaseComponentInspectorPlugin`, `ProcedureComponentInspectorPlugin`, `SceneComponentInspectorPlugin`, `SettingComponentInspectorPlugin`, `EntityComponentInspectorPlugin`, `UIComponentInspectorPlugin`, `SoundComponentInspectorPlugin`, `LocalizationComponentInspectorPlugin`, `DownloadComponentInspectorPlugin`, `WebRequestComponentInspectorPlugin`, `ResourceComponentInspectorPlugin`, `NodePoolInspectorPlugin`, `ArchiveSettingInspectorPlugin`, and `ScriptGenerateInspector` — each providing custom property editors, dropdowns, and debug info in the Godot editor inspector panel. `ArchiveSettingInspectorPlugin` 提供存档配置编辑：`EnableAesEncryption` 开关 + 密钥/盐字段显隐 + 随机盐生成按钮。

### UIForm / Entity Script Generation

`ScriptGenerateInspector` (an `EditorInspectorPlugin`) — shows **"Generate Script"** / **"Delete Gen"** / **"Delete Logic"** buttons in the inspector for **`CanvasItem` or `Node3D` nodes** (`Control` → UIForm templates, other 2D/3D nodes → Entity templates). Delete buttons are styled red via `.Modulate = Colors.Red`. `WriteText` auto-creates the target directory if missing. It scaffolds a **split partial class** across two files:

- Ge half `<ClassName>.cs` (output: `UIOutPutPathGe` / `EntityOutPutPathGe`) — regenerated boilerplate: `[Export]` child-node fields, interface properties, localization collector. **Always overwritten.**
- Logic half `<ClassName>.Logic.cs` (output: `UIOutPutPathLogic` / `EntityOutPutPathLogic`) — user lifecycle code (`OnInit`/`OnOpen`/`OnClose`/…). **Only created if absent** (never clobbers edits).

**Template placeholders:** `_NAMESPACE_` / `_PARENT_` / `_CLASSNAME_` / `_CHILDNODES_`

Templates: `Framework/GodotGameFrameworkCore/Templet/` (`UIFormTemplet.txt` / `UIFormLogicTemplet.txt` + entity equivalents).

**Config:** `TheGame/MainPack/Resources/ScriptGenerateRes.tres` (`ScriptGenerateRes : Resource`):
| Field | Purpose | Default |
|-------|---------|---------|
| `NameSpace` | 生成的命名空间 | `"GameLogic"` |
| `UIOutPutPathGe` / `EntityOutPutPathGe` | Ge 脚本输出目录 | `"res://TheGame/"` |
| `UIOutPutPathLogic` / `EntityOutPutPathLogic` | Logic 脚本输出目录 | `"res://TheGame/"` |
| `NodePrefix` | 子节点名称前缀（用于自动收集） | `"m_"` |

The plugin reads config **by property name** off the base `Resource` (not a typed cast) so it works even before the C# type is registered in the editor.

**子节点自动收集与赋值 (`ReadChildNodes`):**
- 递归遍历节点树，收集名称以 `NodePrefix`（默认 `m_`）开头的子节点
- 生成 `[Export] public Type NodeName;` 字段，替换模板中的 `_CHILDNODES_` 占位符
- `SetScript` 之后自动调用 `node.Set(child.Name, child)` 为每个 `[Export]` 字段赋值子节点引用
- 调用 `MarkSceneAsUnsaved()` 标记场景已修改

**生成流程:**
1. 写入 `.cs` 文件 → 刷新文件系统 → `fs.Scan()`
2. `GD.Load<CSharpScript>(gePath)` → `node.SetScript(script)` → 自动赋值子节点
3. 新生成的脚本需要一次构建（`dotnet build`）后才能编译并通过 Inspector 显示 `[Export]` 字段

## Luban Config Pipeline

Excel configs in `Configs/GameConfig/Datas/` → Luban code generation:

```
Configs/                            ← Repo root (sibling to Godot/)
  GameConfig/
    Datas/
      __beans__.xlsx                ← Shared type definitions
      __enums__.xlsx                ← Enum definitions
      __tables__.xlsx               ← Table/index definitions
      实体.xlsx                     ← Entity configs (scenes, paths, groups)
      界面UI.xlsx                   ← UI form configs
      角色.xlsx                     ← Character/actor configs
    Defines/                        ← Luban type definitions (XML)
    luban.conf                      ← Luban configuration
    gen_code_bin_to_project.bat/sh  ← 生成 C# 代码 + 二进制数据（Godot client）
    gen_code_bin_to_project_lazyload.bat/sh  ← 同上 + 懒加载模板
    gen_code_bin_to_server.bat/sh  ← 服务端代码生成
```

Generated code: `TheGame/GameScripts/GameProto/GameConfig/` (e.g., `EntityConfig.cs`, `EntityId.cs`, `TbEntityConfig.cs`). Auto-generated `ResourcesCollectionConstant.cs` via the Resources editor plugin.

Config-driven usage: `GF.Entity.ShowEntity(EntityId.Cat)` → `TbEntityConfig` resolves the scene path.

> 💡 **配表操作请使用 `/luban-dev` 技能** — 提供 Excel 表/枚举/Bean 的 CRUD 工具（`luban_helper.py`）、导表命令（`gen_code_bin_to_project_lazyload.bat/sh`）、Schema/校验器参考，并内置本项目的 GGF 集成约定（`ConfigSystem.Instance.Tables` 懒加载、`GameConfig` 命名空间、输出路径）。详见 `.claude/skills/luban-dev/`。

## Source Generators (Tools/) — Removed

The `Tools/GameEventSourceGenerator/` Unity/TEngine Roslyn Source Generator project has been **removed** (2026-07). It was never referenced by `GodotProject.csproj` and was unrelated to GGF's event system.

## Editor Plugins (`addons/`)

| Plugin | Function |
|--------|----------|
| **ComponentInsoector** | Custom inspector plugins for framework components (Base, Procedure, Scene, Setting, Entity, UI, Sound, Localization, Download, WebRequest, Resource) + NodePool + UIForm script generator (`ScriptGenerateInspector`) with auto child-node collection and assignment |
| **ExportInspector** | AssetBundle visual export management panel — scan `.tres` bundle markers, expand to view per-resource details (type, size, import status), one-click export `.pck` subpackages + `GameFrameworkVersion.dat` manifest. Supports **full mode** (source files + imported) and **imported-only mode** (only `.ctex`/`.fontdata`/`.sample`, 80%+ smaller) |
| **TopMenu** | `GameFrameworkLog` / `OpenFolder` / `Generate File` submenus — toggle log level (rewrites csproj `DefineConstants`), open `res://` / `user://` folders, and generate: **Localization File** (`Configs/Localization/*.xlsx` → `.txt`), **GameConfig File** (run Luban `gen_code_bin_to_project.bat/.sh`), **Collection Res** (scan `res://TheGame/`, regenerate `ResourcesCollectionConstant.cs`). Merged from the former `LocalizationEditor` + `Resources` plugins |

Enabled in `project.godot`:
```
editor_plugins/enabled = [
  "res://addons/ComponentInsoector/plugin.cfg",
  "res://addons/ExportInspector/plugin.cfg",
  "res://addons/TopMenu/plugin.cfg",
  "res://addons/asset_bundle/plugin.cfg",
  "res://addons/ezpz_inspector/plugin.cfg"
]
```

## Logging System

Compile-time conditional via `DefineConstants` in `GodotProject.csproj`:
```xml
<DefineConstants>ENABLE_LOG;ENABLE_INFO_AND_ABOVE_LOG</DefineConstants>
```

Level granularity: `ENABLE_DEBUG_LOG / INFO / WARNING / ERROR / FATAL_LOG` and composite `ENABLE_DEBUG_AND_ABOVE_LOG` etc.

`Log.Debug/Info/Warning/Error/Fatal` are `[Conditional]` — zero runtime overhead when the symbol is undefined. Release builds can remove the entire `DefineConstants` line.

`DefaultLogHelper` bridges framework logs to `GD.Print/PushWarning/PushError` and raises the static event `DefaultLogHelper.LogMessageReceived(level, message, stackTrace)` (stack trace captured for Error/Fatal) — consumed by the debugger's Console window.

## Resource System

`IResourceManager` with 18 members (10 core operations + 8 asset/binary agent counts; reduced from ~97 Unity-era members). Details: `docs/ResourceSystem.md`.

| Mode | Status |
|------|--------|
| `ResourceMode.Package` | ✅ Active (Godot.ResourceLoader; no subpackage loading in this mode. When `BaseComponent.EnableEditorResLoad` is true, this mode also skips local subpackage detection — resources load directly from `res://TheGame/`) |
| `Updatable` | ✅ Hot-update pipeline live (`ProcedureUpdate` downloads + loads `.pck` subpackages) |
| `UpdatableWhilePlaying` | 📅 Not implemented |

### Loading

`TaskPool<LoadAssetTask>` drives async asset loading (`IResourceLoadHelper.LoadAssetAsync` → `ResourceLoader.LoadThreadedRequest` + per-frame status polling) with a configurable number of load agents (`ResourceComponent.AgentCount` export, default 10, via `SetLoadAssetAgentCount`). The waiting queue is ordered by `priority` (descending). `LoadBinaryAsync` 走独立的 `TaskPool<LoadBinaryTask>`（`LoadBinaryAgent` 经 helper 后台 `File.ReadAllBytes`，主线程每帧轮询回调）；`LoadBinary()`/`LoadText()` on `ResourceComponent` 为同步主线程实现。Async API: `LoadAsset`/`LoadAssetAsync<T>` (no `LoadSceneAsync` on ResourceComponent; scene loading goes through `SceneComponent`).

### Subpackage System (Updatable mode)

`ProcedureUpdate` downloads `.pck` subpackages via `GF.Download` (concurrent, resumable, SHA256-verified — see `docs/DownloadSystem.md`), then loads them via `ProjectSettings.LoadResourcePack()` and persists the manifest (`GameFrameworkVersion.dat`, 旧版先备份为 `.bak`)。**版本文件保存条件已放宽**：只要发生了下载就刷新本地清单（不再仅在版本号变化时保存），避免服务端版本号未变但 `.pck` 哈希已变（重新导出）时，重启后完整性校验拿旧哈希比对新文件 → 判定损坏 → 反复重下 + 反复弹"是否重启"的死循环。`HotUpdateSafetyGuard.MarkStartupSuccess()` 在 `finally` 中统一标记（不再只依赖用户点击重启/退出回调）。Patch files are stored in `SubpackDir` (game-exe `subpackages/` folder or `user://subpackages/` fallback; formerly always `user://`). Crash-safety via `HotUpdateSafetyGuard` (skip patches after a crashed launch). Package mode skips local subpackage detection when `BaseComponent.EnableEditorResLoad` is true. Audit trail: `docs/ResourceHotUpdateAudit.md`. C# 程序集热更（`docs/CodeHotUpdateDesign.md`）已搁置等待华佗团队 Godot 适配。

`PackVersionList` structure (`PackVersionList.cs`):
```csharp
public class PackVersionList {
    public string Version;        // e.g. "1.0.0"
    public Pack[] Packs;          // Name, Size, Hash(SHA256), Url, Type(Resource/Config/Script)
    public string MinAppVersion;  // below this → must update the app
    public bool   ForceUpdate;    // non-skippable update flag
}
```

### ExportInspector (addon)

C# `EditorPlugin` (`addons/ExportInspector/`) for visual AssetBundle management:
- Scans project for `AssetBundle.gd` marker resources (`.tres` files)
- Tree view with expandable bundles showing each file: type label, size, import status (✅/⚠️/—)
- Toggle: enabled, export enabled, **export-only-imported** (skip source files, only pack `.ctex`/`.fontdata`/`.sample`)
- One-click export to timestamped directory, generates `GameFrameworkVersion.dat` with configurable version string
- Uses `PckPacker` for `.pck` generation, supports imported resource packing (reads `.import` `dest_files`)

### Hot-Patch (Future)

Planned: runtime `user://patch.pck` detection — `ProjectSettings.LoadResourcePack("user://patch.pck")` inserts the patch at the head of Godot's resource resolution chain. Any path found in the patch overrides the main `.pck`; unchanged paths fall through. Enables incremental updates without full re-export.

## Build & Development Commands

```bash
# From the Godot/ directory:
cd GodotProject

dotnet build                              # Daily development build

# After adding new .cs files (regenerate solution):
"<godot_exe>" --build-solutions --path GodotProject --no-window -q

# Open Godot editor:
"<godot_exe>" --path GodotProject --editor
```

## MCP & Claude Code Config

- **MCP**: CodeGraph (`@colbymchenry/codegraph`) in `.mcp.json` — provides code intelligence via SQLite knowledge graph of all symbols/edges/files
- **Hooks**: SessionStart, PreToolUse (Bash validation), PostToolUse (Write/Edit validation), Notification, PreCompact/PostCompact, Stop, SubagentStart/SubagentStop
- **Agent definitions**: `.claude/agents/` — specialized agents (godot-csharp-specialist, godot-specialist, gameplay-programmer, etc.) for targeted sub-tasks. Engine-specific agents (unity-*, ue-*, unreal-*) have been removed (2026-07).
- **Skills** (`.claude/skills/`): **17 个活跃技能**（2026-08 精简）——**GGF 开发**（`/ggf-dev`：红线 + 模块文档路由 + API 速查）、思考/审查（`/caveman`、`/grill-me`、`/grill-with-docs`、`/improve-codebase-architecture`、`/security-audit`、`/perf-profile`、`/tech-debt`、`/reverse-document`）、配置管线（`/luban-dev`、`/localize`）、导航/元（`/start`、`/help`、`/project-stage-detect`、`/setup-engine`、`/skill-test`、`/skill-improve`）。游戏生产流水线技能（GDD/故事/冲刺/QA/发布/团队编排，61 个）已归档到 `.claude/skills-archived/`。完整索引见 `.claude/docs/skills-reference.md`。
