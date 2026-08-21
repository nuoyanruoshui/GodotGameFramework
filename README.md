<div align="center">

**Godot 4.7 + C# (.NET 8) Game Framework**

[![Godot Version](https://img.shields.io/badge/Godot-4.7-blue?style=flat-square)](https://godotengine.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/NuoYan/GGF?style=flat-square)](LICENSE)
[![GameFramework](https://img.shields.io/badge/GameFramework-2025.07.10-green?style=flat-square)](https://gameframework.cn/)

</div>

---

## 📖 简介

**GGF** (Godot Game Framework) 是 [Game Framework](https://gameframework.cn/)（Jiang Yin）的 **Godot 4.7 + .NET 8 C# 移植版**。提供一套完整的模块化游戏开发框架，包含事件、FSM、流程、资源、实体、UI、音频、本地化、对象池、数据表、数据结点、设置、Web 请求、下载、调试器、存档等子系统。
交流Q群：1098113249
### ✨ 核心特性

- 🧩 **模块化架构** — 16 个独立组件 + 1 个存档管理器，高内聚低耦合，可按需替换
- 🔄 **双层架构** — 纯 C# 核心层（零 Godot 依赖）+ Godot 运行时组件层，新系统严格遵循此分层
- 🎯 **直接继承模式** — Entity/UI 脚本直接继承 Godot 原生类型 + 框架接口，无中间基类
- 📊 **Luban 数据管线** — Excel 配置 → C# 强类型代码 + 二进制数据，运行时经 `ConfigSystem.Instance.Tables` 访问
- ♻️ **对象池体系** — 实体、UI、音频等资源经 `GF.ObjectPool` 池化管理；`NodePool` 基于 ObjectPool 的通用节点池
- 🔊 **音频系统** — 声音组 + 优先级抢占 + 扩展方法 `PlayBGM()`/`PlaySFX()`
- 📝 **条件日志** — `[Conditional]` 编译时零开销移除，TopMenu 编辑器插件可切换级别
- 🔧 **编辑器插件** — 组件监视、UIForm/Entity 脚本生成器、子包可视化导出管理、资源路径常量生成、日志级别切换、本地化导出
- 🧬 **单例模式** — 泛型 `SingletonNode<T>` 提供类型安全的 Godot 节点单例
- 📦 **子包系统** — 基于 `.pck` 的资源子包管理，支持可视化导出、构建时自动打包、热更新下载 + SHA256 校验
- 💾 **存档系统** — 通用 `ArchiveSystem<T,U>`（Catalogue + Data 分离），基于 EasySave JSON 持久化，支持 AES-256 加密（`Rijindael` + `ArchiveSetting` 配置）

---

## 📚 目录

- [快速开始](#-快速开始)
- [架构概览](#-架构概览)
- [核心模块](#-核心模块)
- [项目结构](#-项目结构)
- [使用示例](#-使用示例)
- [数据管线](#-数据管线)
- [编辑器插件](#-编辑器插件)
- [系统要求](#-系统要求)
- [开源项目推荐](#-开源项目推荐)
- 📚 **[系统文档索引 (Godot/docs)](Godot/docs/README.md)** — 22 篇子系统深度文档 + 热更设计/审计

---

## 🚀 快速开始

> 💡 **只需要框架？请自行切换到 `Empty` 分支** — `main` 分支是包含示例游戏（TheGame）与完整 AI 开发工作流（`.claude/` 技能/代理、`docs/` 系统文档、钩子、MCP 等）的开发主分支。若你只想要**纯框架**进行二次开发，请克隆后执行 `git checkout Empty`，该分支聚焦框架项目本身。

### 环境要求

- **Godot**: 4.7+（.NET 版本，Godot .NET SDK 4.7.0）
- **.NET SDK**: 8.0+
- **渲染器**: D3D12（Forward Plus，默认）
- **物理引擎**: Jolt Physics（3D 默认）
- **NuGet**: Newtonsoft.Json 13.0.4（本地 .dll 引用）

### 快速上手

1. **克隆项目**
   ```bash
   git clone <repo-url>
   cd Godot
   ```

2. **快速编译**
   ```bash
   cd GodotProject
   dotnet build
   ```

3. **打开编辑器**
   ```bash
   "<godot_exe>" --path GodotProject --editor
   ```

4. **添加新 .cs 文件后需执行完整构建**
   ```bash
   "<godot_exe>" --build-solutions --path GodotProject --no-window -q
   ```

---

## 🏗️ 架构概览

### 双层架构

```
┌──────────────────────────────────────────────────────┐
│               Godot Runtime Layer                     │
│  GodotGameFrameworkCore/                              │
│  ├── GodotComponent (Node)        生命周期虚方法       │
│  ├── GameFrameworkComponent       自动注册到 GameEntry  │
│  ├── GameEntry                    根节点，驱动 Update   │
│  ├── GF                           静态门面（16 组件）   │
│  ├── EntityComponent / UIComponent / SoundComponent    │
│  ├── SingletonNode<T>             泛型节点单例          │
│  └── PhysicsCheck2D / GTween / LayerMask  工具类        │
├──────────────────────────────────────────────────────┤
│                Pure C# Core Layer                      │
│  GameFramework/                                        │
│  ├── GameFrameworkEntry          模块入口               │
│  ├── GameFrameworkModule         模块基类               │
│  ├── ReferencePool / EventPool   引用池 / 事件调度       │
│  └── EntityManager / UIManager / ...  核心 Manager      │
└──────────────────────────────────────────────────────┘
```

**核心规则：** `GameFramework/` 不引用任何 Godot 类型。`GodotGameFrameworkCore/` 依赖 `GameFramework/` 和 Godot。新增系统应保持此分层。

### 直接继承模式

脚本直接继承 Godot 原生类型 + 框架接口，无需中间基类。脚本生成器产生的 Ge partial 提供框架属性（`IEntity`/`IUIForm` 实现），Logic partial 由用户编写生命周期逻辑：

```
ActorEntity : CharacterBody2D, IEntity, IActor    ← 用户直接继承 Godot 类型 + 框架接口
MainForm (Ge) : Control, IUIForm                  ← 生成器产生的框架样板（每次覆盖）
MainForm (Logic) : partial                        ← 用户编写的生命周期逻辑（仅首次创建）
```

---

## 🧩 核心模块

> 📚 每个模块都有对应的深度系统文档（架构 / 数据流 / 核心机制 / API / FAQ），完整索引见 **[Godot/docs/README.md](Godot/docs/README.md)**。下方仅为速览。

### 实体模块 (EntityComponent)

> 📖 详细文档：[EntitySystem.md](Godot/docs/EntitySystem.md)

- ✅ 基于 `IEntityManager` 的实体生命周期管理
- ✅ `ShowEntity(EntityId)` Luban 配置驱动，支持对象池复用
- ✅ `ShowEntityAsync<T>()` 异步加载，返回类型安全的 `T : Node, IEntity`
- ✅ 实体组管理（容量、过期时间、优先级可配）
- ✅ 父子实体挂载 + Godot 场景树 Node 关系同步
- ✅ 脚本直接继承 Godot 类型 + `IEntity`，Ge partial 提供框架属性，Logic partial 编写生命周期

**当前 TheGame 项目实体层级：**
```
CharacterBody2D + IEntity + IActor
  └── ActorEntity               ← 阵营 (EntityTeam)、血量、PhysicsCheck2D 检测、Die()
       ├── CatEntity            ← 玩家猫：键盘移动、自动瞄准、发射 GanTan
       ├── AngerEntity          ← 敌人
       └── GanTanEntity         ← 弹射物（Ge+Logic partial, BulletData: 方向/速度/归属）
```

### UI 模块 (UIComponent)

> 📖 详细文档：[UISystem.md](Godot/docs/UISystem.md)

- ✅ 基于 `IUIManager` 的窗体管理
- ✅ 默认 UI 层级： Normal
- ✅ 界面组管理，支持深度排序 + 遮挡算法
- ✅ `OpenUIForm(UIFormId)` Luban 配置驱动
- ✅ Ge partial 提供 `Control, IUIForm` 框架样板，Logic partial 编写生命周期逻辑
- ✅ 自动收集 `IStringKey` 子节点并刷新本地化文本

当前 TheGame UI：`LoadingForm`、`MenuForm`、`MainForm`、`GameOverForm`、`PauseMenuForm`、`TestOverlayForm`、`SettingForm`、`QuestionTips`、`DamagePop`（`IPoolable` 池化控件）。`LoadingForm` 显示加载进度（Tween 平滑过渡），监听 `OpenUIFormSuccessEventArgs`/`LoadSceneSuccessEventArgs` 自动关闭。`QuestionTips` 实现 `ITips` 接口，用于确认对话框。

### 音频模块 (SoundComponent)

> 📖 详细文档：[SoundSystem.md](Godot/docs/SoundSystem.md)

- ✅ 基于 `ISoundManager` 的音频管理
- ✅ 默认声音组：Music / SFX / UI（通过 `SoundGroupRes` 配置）
- ✅ 优先级抢占算法
- ✅ 淡入/淡出控制
- ✅ 组级静音/音量级联
- ✅ 扩展方法：`GF.Sound.PlayBGM(assetName)` / `GF.Sound.PlaySFX(assetName)`

### 资源模块 (ResourceComponent)

> 📖 详细文档：[ResourceSystem.md](Godot/docs/ResourceSystem.md) ｜ 热更审计：[ResourceHotUpdateAudit.md](Godot/docs/ResourceHotUpdateAudit.md)

- ✅ **精简 IResourceManager** — 从 Unity 原版 97 个成员精简为 18 个（10 核心 + 8 代理计数）
- ✅ **异步加载** — `IResourceLoadHelper`（默认 `ResourceLoader.LoadThreadedRequest`）+ `TaskPool<LoadAssetTask>` 多代理调度
- ✅ **同步读写** — `LoadBinary()` / `LoadText()` 主线程同步返回
- ✅ **子包系统** — `Updatable` 模式下热更下载 `.pck` + `ProjectSettings.LoadResourcePack()` 加载
- ✅ **版本清单** — `PackVersionList`（JSON）记录子包名称、大小、SHA256、MinAppVersion、ForceUpdate
- ✅ **多模式** — `ResourceMode.Package`（仅主包）/ `Updatable`（热更管线，已实现）/ `UpdatableWhilePlaying`（未实现）
- ✅ **EasySave** — JSON 持久化工具（`SaveInUserAsync<T>()` / `LoadFromUserAsync<T>()` + 加密重载），用于版本文件 + 存档

### Web 请求模块 (WebRequestComponent)

> 📖 详细文档：[WebRequestSystem.md](Godot/docs/WebRequestSystem.md)

- ✅ **异步 API** — `SendRequestAsync(url)` 返回 `Task<WebRequestCompleteEventArgs>`，支持 GET / POST
- ✅ **事件驱动** — `SendRequest(url)` 通过 `EventComponent` 推送结果
- ✅ **超时控制** — 默认 30s，可配置，超时自动取消底层请求
- ✅ **三层架构** — Component / 纯 C# Manager / HttpRequest Helper（TaskPool 驱动），并发可配（默认 4）

### 下载模块 (DownloadComponent)

> 📖 详细文档：[DownloadSystem.md](Godot/docs/DownloadSystem.md)

- ✅ 任务队列 + 多代理并发（默认 3 agent），优先级 / 标签 / 暂停 / 速度统计
- ✅ 流式下载（64KB 缓冲）+ `.download` 断点续传（HTTP Range）
- ✅ 无进度超时（默认 30s）
- ✅ `DownloadFileAsync()` 可 await API：大小 + SHA256 校验
- ✅ `user://`、`res://` 虚拟路径自动转换
- ✅ 热更流程 `ProcedureUpdate` 的多包并发下载基于本模块

### 事件模块 (EventComponent)

> 📖 详细文档：[EventSystem.md](Godot/docs/EventSystem.md)

- ✅ 基于 `IEventManager` 的线程安全事件系统
- ✅ 延迟分发（`Fire`，线程安全，下帧主线程回调）和立即分发（`FireNow`）
- ✅ 事件参数经 `ReferencePool` 池化，分发完自动回收
- ✅ 自定义事件继承 `GameEventArgs`，通过 `Create()` 工厂从引用池取实例

### 流程模块 (ProcedureComponent)

> 📖 详细文档：[ProcedureSystem.md](Godot/docs/ProcedureSystem.md) ｜ 状态机基础：[FsmSystem.md](Godot/docs/FsmSystem.md)

- ✅ 基于 `IFsmManager` 的流程状态机
- ✅ 启动流程链：`ProcedureLaunch`（组件验证）→ `ProcedureUpdate`（热更新检测与下载）→ `ProcedurePrelode`（子包加载、配置、实体组初始化）→ `ProcedureGame`（游戏主循环）
- ✅ 通过 `ChangeState<T>(procedureOwner)` 切换流程

### 调试器 (DebuggerComponent)

> 📖 详细文档：[DebuggerSystem.md](Godot/docs/DebuggerSystem.md)

- ✅ UGF 风格运行时调试器：可拖拽 FPS 图标（点击展开）+ 完整窗口
- ✅ BBCode + RichTextLabel 模拟 IMGUI（Console / Information / Profiler / Other 多级页签）
- ✅ 框架日志 + Godot 原生日志双源捕获
- ✅ 自定义调试窗口扩展 API：`GF.Debugger.RegisterDebuggerWindow("Name", window)`

### 数据表系统（Luban 管线）

> 📖 详细文档：[DataTableSystem.md](Godot/docs/DataTableSystem.md)

- ✅ Luban 生成的二进制数据反序列化（`ByteBuf` + `BeanBase`）
- ✅ `ConfigSystem.Instance.Tables` 返回类型安全的 `Tables` 实例，懒加载
- ✅ Excel 配置 → C# 强类型代码 + `.bytes` 二进制数据

### 其他模块

| 模块 | 速览 | 详细文档 |
|------|------|----------|
| 框架核心 (Base/GF/GameEntry) | 启动序列、组件生命周期、ReferencePool、条件日志、GTween、LayerMask | [FrameworkCore.md](Godot/docs/FrameworkCore.md) |
| 状态机 (FsmComponent) | 泛型 IFsm/FsmState、SetData/GetData、池化销毁 | [FsmSystem.md](Godot/docs/FsmSystem.md) |
| 对象池 (ObjectPoolComponent) | Spawn/Unspawn、容量/过期/优先级、与 ReferencePool 对照 | [ObjectPoolSystem.md](Godot/docs/ObjectPoolSystem.md) |
| 节点池 (NodePool) | IPoolable 接口、配置驱动注册、懒加载 Instantiate、孤儿节点设计 | [NodePoolSystem.md](Godot/docs/NodePoolSystem.md) |
| 数据结点 (DataNodeComponent) | 树形数据、路径访问、Variable 池化类型 | [DataNodeSystem.md](Godot/docs/DataNodeSystem.md) |
| 设置 (SettingComponent) | ConfigFile → `user://settings.cfg`、Save/Load | [SettingSystem.md](Godot/docs/SettingSystem.md) |
| 本地化 (LocalizationComponent) | TSV 字典、语言切换、IStringKey 自动刷新 | [LocalizationSystem.md](Godot/docs/LocalizationSystem.md) |
| 场景 (SceneComponent) | 场景加载/卸载、LoadSceneAsync、单实例约束 | [SceneSystem.md](Godot/docs/SceneSystem.md) |
| 存档 (ArchiveSystem) | ArchiveSystem\<T,U\> 泛型设计、Catalogue/Data 分离、EasySave JSON 持久化 | [ArchiveSystem.md](Godot/docs/ArchiveSystem.md) |

### GF 静态门面

```csharp
GF.Base  GF.Event  GF.Fsm  GF.Procedure  GF.ObjectPool  GF.DataNode
GF.Resource  GF.Entity  GF.UI  GF.Sound  GF.Localization
GF.Setting  GF.Scene  GF.WebRequest  GF.Download  GF.Debugger
GF.Archive   // 共 16 个组件 + 1 个存档管理器
```

`GF.Archive` 是泛型 `ArchiveSystem<GameCatalogue, GameData>` 的惰性单例（`new()` 创建，非组件注册）。其余属性经 `GameEntry.GetComponent<T>()` 懒缓存。

### NodeExtension

`GodotGameFrameworkCore/Utility/NodeExtension.cs` 提供常用 Node 查询扩展方法：
- `FindChildOfType<T>()` — 递归查找子节点
- `FindChildrenOfType<T>()` — 递归查找所有匹配子孙节点
- `GetChild<T>()` / `GetChildren<T>()` / `GetParent<T>()`
- `GetOrAddChild<T>()` — 获取或创建指定类型子节点
- `RemoveAllChildren()` — 移除所有子节点

### GTween（DOTween 风格动画）

`Utility/GTween.cs` — `Node2D` / `Control` 的 Tween 扩展方法：`DoScale`、`DOPunchScale`、`DOLocalMove`、`DOMove`、`DORotate`、`DOColor`、`Delay`。命名空间 `GodotGameFramework.DoTween`。

### PhysicsCheck2D

`Utility/PhysicsCheck2D : IReference` — 封装 `PhysicsDirectSpaceState2D.IntersectShape`：
- 通过 `ReferencePool` 池化复用（`PhysicsCheck2D.Create()` / `ReferencePool.Release()`）
- 自动排除自身节点，支持按距离排序、Debug 绘制

### SingletonNode&lt;T&gt;

`SingletonSystem/SingletonNode<T> : Node` — 泛型单例模式。首次访问 `Instance` 自动创建并加入场景树根节点，`_Ready()` 检测并销毁重复实例。

---

## 📁 项目结构

```
Configs/                         ← Excel 配置源数据（Luban 管线输入）
  GameConfig/                    ← 表定义 + 业务 Excel
  Localization/                  ← 多语言 Excel 源
GodotProject/                    ← Godot 项目根
├── Framework/
│   ├── GameFramework/           ← 纯 C# 框架（零 Godot 依赖）
│   │   ├── Base/                ← GameFrameworkEntry, GameFrameworkModule, ReferencePool, EventPool
│   │   ├── Entity/ UI/ Sound/   ← Manager 接口 + 逻辑（无 Godot 类型）
│   │   ├── Resource/ Scene/     ← IResourceManager, ISceneManager
│   │   ├── Event/ Fsm/ Procedure/
│   │   ├── DataNode/ ObjectPool/ Setting/
│   │   ├── Localization/ Debugger/
│   │   ├── Download/ Network/ WebRequest/
│   │   ├── Utility/             ← 压缩、加密、WebGL 持久化等纯 C# 工具
│   │   └── Properties/          ← AssemblyInfo
│   └── GodotGameFrameworkCore/  ← Godot 运行时组件
│       ├── Base/                ← GF.cs 门面, GameEntry, GodotComponent, GameFrameworkComponent, Log
│       ├── Entity/ UI/ Sound/   ← Godot 桥接组件 + DefaultHelper
│       ├── Resource/            ← ResourceComponent, ResourceManager, PackVersionList, 加载任务
│       ├── Archive/             ← ArchiveSystem<T,U> 通用存档系统 + Rijindael（AES-256）
│       ├── HotUpdate/           ← HotUpdateSafetyGuard（崩溃安全热更）
│       ├── Download/ WebRequest/
│       ├── Event/ Fsm/ Procedure/ ObjectPool/
│       ├── DataNode/ Setting/ Localization/
│       ├── Scene/ Debugger/
│       ├── SingletonSystem/     ← SingletonNode<T> 泛型单例
│       ├── Templet/             ← UIForm / Entity 脚本生成模板
│       ├── Variable/            ← VarInt32, VarString, VarBoolean, VarSingle
│       ├── Config/ Json/ Lib/   ← GameFolderConstant, EasySave, LubanLib(ByteBuf/BeanBase)
│       └── Utility/             ← DefaultLogHelper, NodeExtension, PhysicsCheck2D, GTween, LayerMask
│   └── GameFramework.tscn       ← 主场景
├── TheGame/                     ← 活跃游戏项目
│   ├── MainPack/                 ← 主包（共享核心，随应用打包，不参与热更）
│   │   ├── Scripts/
│   │   │   ├── ObjectPool/       ← NodePool, NodeObject, PoolContainer
│   │   │   ├── Procedure/        ← ProcedureLaunch, ProcedureUpdate, ProcedurePrelode, ProcedureGame
│   │   │   ├── Resources/        ← EntityGroup, SoundGroup, UIGroup, NodePoolConfig, ScriptGenerateRes, UpdateSettingRes
│   │   │   └── UI/               ← LoadingForm, QuestionTips（共享 UI）
│   │   ├── Fonts/                ← simhei.ttf
│   │   ├── Resources/            ← .tres 配置资源（EntityGroupRes, UIGroupRes, SoundGroupRes 等）
│   │   ├── Themes/               ← MainThemes.tres
│   │   └── UI/                   ← .tscn 场景文件（LoadingForm, QuestionTips）
│   ├── GameScripts/
│   │   ├── Entity/               ← ActorEntity, CatEntity, AngerEntity, GanTanEntity.Logic, DropItem
│   │   ├── UI/                   ← MenuForm.Logic, MainForm.Logic, GameOver.Logic, DamagePop 等
│   │   ├── Event/                ← BlockClickedEventArgs, ScoreChangedEventArgs 等
│   │   ├── Archive/              ← GameCatalogue, GameData
│   │   ├── Manager/              ← LevelManager (SingletonNode, 波次系统)
│   │   └── GameProto/
│   │       ├── GameConfig/       ← Luban 生成的 C# 数据类（EntityConfig, TbEntityConfig 等）
│   │       ├── EntityGe/         ← 实体脚本 Ge（自动覆盖）
│   │       └── UIGe/             ← UI 脚本 Ge（自动覆盖，MenuForm/MainForm/GameOver/SettingForm）
│   ├── DataTables/
│   │   ├── GameConfigs/         ← Luban 生成的二进制配置 (.bytes)
│   │   └── Localizations/      ← 本地化文本 (.txt)
│   └── Resources/               ← Godot 资源配置（ScriptGenerateRes, UpdateSettingRes 等 .tres）
└── addons/                      ← 编辑器插件
    ├── ComponentInsoector/      ← 框架组件监视（含各组件 helper 下拉）+ UIForm/Entity 脚本生成器 + NodePool 扫描
    ├── ExportInspector/         ← 子包可视化导出管理面板（C# EditorPlugin）
    ├── asset_bundle/            ← 资源包标记 + 导出插件 + 打包工具（GDScript）
    ├── ezpz_inspector/          ← C# Inspector 增强（Ezpz Inspector）
    └── TopMenu/                 ← 日志级别切换 + 本地化导出 + 资源路径常量生成（合并自 LocalizationEditor/Resources）
```

---

## 📋 场景树

主场景 `Framework/GameFramework.tscn`（`run/main_scene`）：

```
GameFramework (GameEntry : GodotComponent)
├── Base / Event / Resource / Debugger
├── Procedure / Scene / Fsm
├── DataNode / ObjectPool / ReferencePool / Setting
├── Entity / UI / Sound / Localization
├── WebRequest / Download
```

每种组件类型只允许注册一个实例，`GameEntry.RegisterComponent()` 会校验唯一性。

### 启动顺序

1. Godot 加载 `Framework/GameFramework.tscn`
2. `GameFrameworkComponent.OnInit()` → `GameEntry.RegisterComponent(this)`
3. `GameEntry._Process()` 驱动 `GameFrameworkEntry.Update()` 轮询所有模块
4. `GameEntry.CheckProcedure()` 在 `ProcedureComponent` 注册后自动调用 `StartProcedure()`
5. `ProcedureLaunch` 验证组件 → `ProcedureUpdate` 检测更新 → `ProcedurePrelode` 加载子包和配置 → `ProcedureGame`

---

## 🎯 使用示例

### 实体系统

```csharp
// 1. 定义实体类（直接继承 Godot 类型 + 框架接口）
public partial class CatEntity : ActorEntity
{
    [Export] private Sprite2D m_CatSprite;

    public override void OnInit(int entityId, string entityAssetName,
        IEntityGroup entityGroup, bool isNewInstance, object userData)
    {
        base.OnInit(entityId, entityAssetName, entityGroup, isNewInstance, userData);
        m_Config = ConfigSystem.Instance.Tables.TbCharacterConfig.DataList
            .FirstOrDefault(x => x.EntityId == EntityId.Cat);
    }

    public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        // 每帧移动、自动攻击逻辑
    }
}

// 2. 显示实体
int catId = GF.Entity.ShowEntity(EntityId.Cat);

// 3. 异步显示并获取实体引用
CatEntity cat = await GF.Entity.ShowEntityAsync<CatEntity>(EntityId.Cat);
cat.GlobalPosition = new Vector2(100, 200);

// 4. 隐藏实体
GF.Entity.HideEntity(catId);
GF.Entity.HideEntitySafe(catId);
```

### UI 系统

```csharp
// Ge partial（自动生成，覆盖）—— 提供 IUIForm 属性 + [Export] 子节点字段
public partial class MenuForm : Control, IUIForm
{
    [Export] public Button m_StartButton;
    [Export] public Label m_TitleLabel;
    // ... 框架属性（SerialId, UIFormAssetName, UIGroup 等）
}

// Logic partial（仅首次生成，不覆盖）—— 用户生命周期代码
public partial class MenuForm : IStringKey
{
    public void OnInit(int serialId, string uiFormAssetName,
        IUIGroup uiGroup, bool pauseCoveredUIForm, bool isNewInstance, object userData)
    {
        if (isNewInstance) m_StartButton.Pressed += OnStartButtonPressed;
    }

    public void OnOpen(object userData)
    {
        GF.Sound.PlayBGM(ResourcesCollectionConstant.Music_Menu);
    }
}

// 打开界面
int menuId = GF.UI.OpenUIForm(UIFormId.MenuForm);
// 或异步
await GF.UI.OpenUIFormAsync<MenuForm>(UIFormId.MenuForm);
```

### 音频系统

```csharp
// 播放背景音乐（Music 组）
int bgmId = GF.Sound.PlayBGM("res://Audio/background.mp3");

// 播放音效（SFX 组）
int sfxId = GF.Sound.PlaySFX("res://Audio/Click.wav");

// 使用完整 PlaySound API
var sfxParams = PlaySoundParams.Create();
sfxParams.VolumeInSoundGroup = 0.8f;
GF.Sound.PlaySound("res://Audio/Shoot.wav", "SFX", sfxParams);

// 停止
GF.Sound.StopSound(bgmId, 1f);
```

### 资源加载

```csharp
// 同步加载文本/二进制文件
string text = GF.Resource.LoadText("res://Data/Config.txt");
byte[] data = GF.Resource.LoadBinary("res://Data/Config.dat");

// 异步加载资源
Godot.Resource res = await GF.Resource.LoadAssetAsync("res://Sprites/Player.png");

// 检查资源是否存在
if (GF.Resource.HasAsset("res://Scenes/Enemy.tscn")) { }
```

### 事件系统

```csharp
// 定义自定义事件
public sealed class ScoreChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(ScoreChangedEventArgs).GetHashCode();
    public override int Id => EventId;
    public int ScoreDelta { get; private set; }

    public static ScoreChangedEventArgs Create(int delta)
    {
        var e = ReferencePool.Acquire<ScoreChangedEventArgs>();
        e.ScoreDelta = delta;
        return e;
    }

    public override void Clear() { ScoreDelta = 0; }
}

// 订阅 / 触发
GF.Event.Subscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);
GF.Event.Fire(this, ScoreChangedEventArgs.Create(100));
```

### 数据表

```csharp
// 通过 ConfigSystem 访问 Luban 生成的强类型配置表
var cfg = ConfigSystem.Instance.Tables.TbEntityConfig.Get(entityId);
var allChars = ConfigSystem.Instance.Tables.TbCharacterConfig.DataList
    .Where(x => x.Level > 5);
```

### 存档系统

> 目录名 / 加密开关 / 密钥盐由 `ArchiveSetting.tres` 配置，勾选 `EnableAesEncryption` 后经 `Rijindael`（AES-256-CBC）加密存储。`LoadAsync()` 仅在存档文件**不存在**时新建；存在但读取失败（如密钥变更）时**拒绝覆盖**，防止吞档。

```csharp
// 初始化（自动加载最新存档，不存在则创建）
await GF.Archive.LoadAsync();

// 修改数据并保存
GF.Archive.CurrentData.Score += 100;
await GF.Archive.OverWriteAsync();

// 创建新存档
await GF.Archive.SaveAsync();

// 删除存档
await GF.Archive.Delete(someUnitId);
```

---

## 📊 数据管线

> 📖 详细文档：[DataTableSystem.md](Godot/docs/DataTableSystem.md)

集成 **Luban** 配置表解决方案：

```
Configs/GameConfig/Datas/*.xlsx
         │
         │ gen_code_bin_to_project.bat
         ▼
TheGame/GameScripts/GameProto/GameConfig/*.cs   ← C# 数据类（强类型访问）
TheGame/DataTables/GameConfigs/*.bytes          ← 二进制数据（运行时加载）
```

- **源文件**: `__tables__.xlsx`（表定义）、`__beans__.xlsx`（数据结构）、`__enums__.xlsx`（枚举）+ 业务 Excel
- **运行时**: `ConfigSystem.Instance.Tables` 返回类型安全的 `Tables` 实例（懒加载）
- **底层**: `LubanLib/ByteBuf` + `BeanBase` 反序列化

---

## 🔧 编辑器插件

| 插件 | 功能 |
|------|------|
| **ComponentInsoector** | 框架组件属性监视（含各组件 helper 类型下拉）+ UIForm/Entity 脚本生成器（Ge/Logic 双文件）+ NodePoolInspectorPlugin 扫描 IPoolable 场景 |
| **ExportInspector** | 子包可视化导出管理面板 — 扫描 AssetBundle 标记、展开查看包内资源、一键导出 .pck + 版本清单，支持完整模式和仅产物模式 |
| **asset_bundle** | 资源包标记（GDScript）— 在目录下创建 `AssetBundle.tres` 标记文件，构建时自动通过 export_plugin 打包为 .pck |
| **ezpz_inspector** | C# Inspector 增强 — `[ExportButton]`、`[UpperDescription]`、`[ControlMargin]` 等注解 |
| **TopMenu** | 日志级别切换（Debug / Info / Warning / Error / Fatal / 全部关闭，改写 csproj DefineConstants）+ 本地化导出（`Configs/Localization/*.xlsx` → `.txt`）+ GameConfig File（Luban：`gen_code_bin_to_project.bat/.sh`）+ 资源路径常量生成（`ResourcesCollectionConstant.cs`）。合并自原 LocalizationEditor / Resources 插件 |

> TopMenu 通过 **Project > Tools** 菜单调用；ComponentInsoector 和 ezpz_inspector 直接作用于检视面板；ExportInspector 在编辑器底部面板显示；asset_bundle 在构建时自动生效。

### UIForm / Entity 脚本生成

选中任意 **Control** 节点或 **实体节点** 后，检视面板出现 **Generate Script** 按钮，一键拆分为**双 partial 文件**：

- `<类名>.cs`（Ge 目录）— 框架样板（`[Export]` 子节点字段、`IUIForm`/`IEntity` 属性），**每次生成都覆盖**
- `<类名>.Logic.cs`（Logic 目录）— 用户生命周期代码，**仅首次创建，不覆盖已有逻辑**

模板位于 `Framework/GodotGameFrameworkCore/Templet/`（`UIFormTemplet.txt` + `EntityTemplet.txt` 及对应 Logic 模板）。

**配置文件** `TheGame/MainPack/Resources/ScriptGenerateRes.tres`:

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `NameSpace` | 生成代码的命名空间 | `"GameLogic"` |
| `UIOutPutPathGe` / `EntityOutPutPathGe` | Ge 脚本输出目录 | `"res://TheGame/"` |
| `UIOutPutPathLogic` / `EntityOutPutPathLogic` | Logic 脚本输出目录 | `"res://TheGame/"` |
| `NodePrefix` | 子节点名称前缀（用于自动收集） | `"m_"` |

---

## 🤖 AI 开发工作流（Claude Code）

GGF 集成了完整的 **Claude Code AI 开发工作流**，可用自然语言驱动框架开发。相关配置集中管理在项目根 `.claude/` 目录：

```
开发者（自然语言需求）
      │
      ▼
┌──────────────────────────────────────────────┐
│                Claude Code                   │
│  ┌─────────┐ ┌────────┐ ┌────────┐ ┌───────┐  │
│  │ Skills  │ │ Agents │ │ Hooks  │ │  MCP  │  │
│  │/ggf-dev │ │godot-* │ │Pre/Post│ │Code-  │  │
│  │/luban-  │ │csharp- │ │ToolUse │ │graph  │  │
│  │dev      │ │special-│ │ 校验   │ │知识图谱│  │
│  │/grill-  │ │ist 等  │ │        │ │       │  │
│  │with-docs│ │        │ │        │ │       │  │
│  └─────────┘ └────────┘ └────────┘ └───────┘  │
└───────────────────┬────────────────────────────┘
                    │ 读取 / 校验 / 生成
        ┌───────────┼─────────────┐
        ▼           ▼             ▼
  ┌──────────┐ ┌─────────┐ ┌─────────────┐
  │ docs/*.md │ │ CLAUDE.md│ │ .claude/docs│
  │ 模块系统文档│ │ 主配置/约定│ │ 技能索引等   │
  └──────────┘ └─────────┘ └─────────────┘
                    │ 产出代码 / 文档
                    ▼
        ┌─────────────────────────┐
        │   GGF 框架代码与项目文档   │
        │  Framework/ + TheGame/   │
        └─────────────────────────┘
```

| 组件 | 说明 |
|------|------|
| **技能（Skills）** | `skills/` 下 **17 个活跃技能**：`/ggf-dev`（框架开发红线 + 模块文档路由）、`/luban-dev`（配表工具）、`/grill-with-docs`（代码深度审查）、`/improve-codebase-architecture`（架构加深分析）等。游戏生产流水线技能（GDD/故事/冲刺/QA/发布/团队编排，61 个）已归档至 `skills-archived/`（2026-08 精简），需要时移回即恢复 |
| **代理（Agents）** | `agents/` — 40+ 专职子代理（`godot-specialist`、`godot-csharp-specialist`、`gameplay-programmer` 等）。引擎特化代理（`unity-*`/`ue-*`/`unreal-*`）已移除（2026-07） |
| **文档** | 仓库根 `docs/` — 20+ 篇模块系统文档（每模块一篇）；`CLAUDE.md` — 主配置与开发约定；`.claude/docs/` — 技能索引 / 快速入门 / 工作流目录 |
| **钩子（Hooks）** | `hooks/` — SessionStart、PreToolUse/PostToolUse（Bash 与编辑校验）、Notification、Stop 等自动化护栏 |
| **MCP** | CodeGraph 代码知识图谱（`.mcp.json`）— SQLite 索引全量符号 / 文件 / 调用关系，支持代码智能问答 |

**推荐入口**：

- 框架开发 → `/ggf-dev`（红线 + 模块文档路由 + 关键 API 速查）
- 配置表增删改查 / 导表 → `/luban-dev`
- 代码深度审查 → `/grill-with-docs`
- 架构加深分析 → `/improve-codebase-architecture`
- 项目现状审计 → `/project-stage-detect`

技能完整索引见 `.claude/docs/skills-reference.md`。

---

## ⚠️ 开发注意事项

### Async/Task 时序

`ShowEntityAsync` 和 `OpenUIFormAsync` 等异步方法依赖 `TaskCompletionSource`。如果 Manager 在对象池中找到缓存实例，事件会**同步触发**——需在调用 Manager 方法**之前**注册 tcs。

### 事件参数池化（Fire 后不得持有）

事件参数在所有处理函数返回后立即被 `ReferencePool.Release` 回收。不可把 `e` 存到字段/闭包中跨帧使用，不可在 `async` 处理函数的 `await` 之后再读 `e` 的属性。

### 组件事件转发必须复制

纯层管理器回调返回后会立即 `ReferencePool.Release` 事件参数。Godot 组件若把同一实例直接 `Fire` 入事件池，会导致双重归还崩溃——必须 `Create()` 新实例再转发。

### PhysicsCheck2D 归还

`PhysicsCheck2D` 实现了 `IReference`，使用完毕后必须调用 `ReferencePool.Release(check)` 归还对象池。

---

## 🚧 待实现

- [ ] **UpdatableWhilePlaying 模式** — 边玩边下载
- [ ] **NetworkComponent** — Godot 桥接组件（纯 C# 层 `NetworkManager` 已完整实现）

---

## 🌟 开源项目推荐

| 项目 | 描述 | 链接 |
|------|------|------|
| **Game Framework** | 核心框架来源，Unity 游戏框架 by Jiang Yin | [GitHub](https://github.com/EllanJiang/GameFramework) |
| **Luban** | 游戏配置解决方案（Excel → C# + 二进制） | [GitHub](https://github.com/focus-creative-games/luban) |
| **TEngine** | TEngine 是一个简单（新手友好、开箱即用）且强大的 Unity 框架全平台解决方案。对于需要一套上手快、文档清晰、高性能且可拓展性极强的商业级解决方案的开发者或团队来说，TEngine 是一个很好的选择。 | [GitHub](https://github.com/Alex-Rachel/TEngine.git) |
| **Godot Engine** | 开源游戏引擎 | [GitHub](https://github.com/godotengine/godot) |
| **Newtonsoft.Json** | 高性能 JSON 框架（本地 .dll 引用） | [GitHub](https://github.com/JamesNK/Newtonsoft.Json) |
| **Ezpz Inspector** | Godot C# Inspector 增强插件 | [GitHub](https://github.com/Calcatz/ezpz-inspector) |
| **CodeGraph** | 代码知识图谱 MCP 工具 | [GitHub](https://github.com/colbymchenry/codegraph) |
| **leanclr-godot** | 与 leanclr-unity 类似，在 Godot 发布管线中集成 LeanCLR 运行时与 LeanAOT，使 GDScript / C# 游戏逻辑能以更小体积与更低内存发布到多平台（含 Web）。 | [GitHub](https://github.com/focus-creative-games/leanclr-godot.git) |
---


