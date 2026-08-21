---
name: ggf-dev
description: GGF（Godot Game Framework，Godot 4.7 + C#/.NET 8）开发指导。触发词：GGF, Godot, GameFramework, GodotGameFrameworkCore, GF, GameEntry, GF.Event, GF.Entity, GF.UI, GF.Resource, GF.Procedure, GF.Sound, GF.Scene, GF.Archive, UIForm, IUIForm, IEntity, ShowEntity, OpenUIForm, Fsm, FsmState, Procedure, ProcedureLaunch/ProcedureUpdate/ProcedurePrelode/ProcedureGame, ChangeState, 双层架构, 事件系统, 实体系统, UI开发, 资源加载, 热更, 存档, Archive, AES, Rijindael, ConfigSystem, Luban, 对象池, ObjectPool, 本地化, Localization, GTween, SingletonNode。即使用户未明说"GGF"，只要是本项目的 Godot 开发任务都应使用此技能。
---

# GGF 开发指导

GGF（Godot Game Framework）是 [Game Framework](https://gameframework.cn/)（Jiang Yin）的 **Godot 4.7 + C# (.NET 8)** 移植版。本技能提供开发红线、文档路由与关键 API 速查，确保生成的代码符合框架约定。**每个模块的详细文档已存在于仓库根 `docs/`，本文档只负责路由与速查，不重复展开。**

## 核心红线

1. **双层架构（最重要）**：`Framework/GameFramework/` 是**纯 C# 层，禁止依赖 Godot**（零 Godot `using`）；`Framework/GodotGameFrameworkCore/` 是 Godot 桥接层。**新系统：接口/逻辑放 `GameFramework/`，Godot 桥放 `GodotGameFrameworkCore/`。** 严禁把 Godot 类型泄漏进纯层。见 `docs/FrameworkCore.md`。
2. **组件访问走门面**：一律用 `GF.XXX`（`GF.Entity`/`GF.UI`/`GF.Resource`/`GF.Procedure`/`GF.Archive`…），不要直接 `GameEntry.GetComponent<T>()`；每个组件类型只能注册一个实例。`GF.Archive` 是 `new()` 惰性单例，其余 16 个组件由 `GameEntry` 注册。
3. **事件复制后转发（防二次释放）**：纯层管理器在回调返回后立即 `Release` 事件参数；Godot 组件转发到 `GF.Event.Fire` 必须传**副本**（`XxxEventArgs.Create(e)` 或全参 `Create(...)`），否则 ReferencePool **二次释放抛异常**、订阅者读到已清空数据。见 `docs/EventSystem.md`。
4. **生成代码勿改**：`TheGame/GameScripts/GameProto/GameConfig/`（Luban 生成）与 UIForm/Entity 的 **Ge 半类**都会被重新生成覆盖；业务逻辑只写在 **Logic 半类** 或 `XxxConfigMgr` 封装里。
5. **异步加载优先**：资源用 `GF.Resource` 的 `LoadAssetAsync<T>` / `LoadBinaryAsync`（`TaskPool` 代理，默认 10 并发），大资源禁止主线程同步加载。场景加载走 `GF.Scene`，不走 ResourceComponent。
6. **配置表统一入口**：经 `ConfigSystem.Instance.Tables.TbXxx` + 业务侧 `XxxConfigMgr` 封装访问，不在业务代码散落 `ConfigSystem` 直接调用。见 `/luban-dev`。
7. **生命周期挂对方法**：GodotComponent 用 `OnInit/OnEnter/OnUpdate(delta)/OnFixedUpdate(delta)/OnExitTree/OnPreDestroy`；实体用 `OnInit/OnShow/OnUpdate/OnHide/OnRecycle`；UIForm 用 `OnInit/OnOpen/OnCover/OnReveal/OnUpdate/OnClose`。

## 文档路由（按任务读取对应 doc）

| 任务类型 | 必读文档 |
|---------|---------|
| 框架入口 / 组件注册 / 生命周期 / GF 门面 | `docs/FrameworkCore.md` |
| 事件系统 / 事件参数 / 复制转发 | `docs/EventSystem.md` |
| 状态机 FSM / 嵌套 FSM | `docs/FsmSystem.md` |
| 流程 Procedure / 切状态 | `docs/ProcedureSystem.md` |
| 实体 / IEntity / ShowEntity | `docs/EntitySystem.md` |
| UI / UIForm / Ge+Logic 脚本生成 | `docs/UISystem.md` |
| 音频 / SoundGroup / 代理抢占 | `docs/SoundSystem.md` |
| 场景加载 / LoadSceneMode(Single/Additive) | `docs/SceneSystem.md` |
| 资源加载 / TaskPool / 热更子包 | `docs/ResourceSystem.md` |
| 下载 / .pck 热更流程 | `docs/DownloadSystem.md` |
| 热更健壮性审计 | `docs/ResourceHotUpdateAudit.md` |
| 对象池 / ReferencePool / 严格检查 | `docs/ObjectPoolSystem.md` |
| 节点池 NodePool | `docs/NodePoolSystem.md` |
| 数据结点 DataNode | `docs/DataNodeSystem.md` |
| 数据表 DataTable | `docs/DataTableSystem.md` |
| 存档 / AES 加密(Rijindael) | `docs/ArchiveSystem.md` |
| 设置 Setting | `docs/SettingSystem.md` |
| 本地化 / 语言切换 / LabelTr | `docs/LocalizationSystem.md` |
| 调试器 Debugger | `docs/DebuggerSystem.md` |
| Web 请求 | `docs/WebRequestSystem.md` |
| 配置表 Luban / 导表 / CRUD | 使用 `/luban-dev` 技能（docs/ 无对应系统 doc） |

> 所有 doc 位于仓库根 `docs/`（即 `Godot/docs/`，当前工作目录 `Godot/` 下）。`docs/CodeHotUpdateDesign.md`（C# 程序集热更）已搁置等待华佗团队适配，勿据此开发。

## 关键 API 速查

```csharp
// GF 门面（16 组件 + 1 存档）
GF.Base / GF.Event / GF.Fsm / GF.Procedure / GF.ObjectPool / GF.DataNode
GF.Resource / GF.Entity / GF.UI / GF.Sound / GF.Localization
GF.Setting / GF.Scene / GF.WebRequest / GF.Download / GF.Debugger / GF.Archive

// 实体：配置驱动（TbEntityConfig 解析场景路径）
await GF.Entity.ShowEntityAsync<CatEntity>(EntityId.Cat, userData);
GF.Entity.ShowEntity<CatEntity>(EntityId.Cat);

// UI：脚本生成的分部类
await GF.UI.OpenUIFormAsync<MenuForm>(UIFormId.MenuForm);
GF.UI.OpenUIForm(UIFormId.MenuForm);

// 资源：异步加载
var tex = await GF.Resource.LoadAssetAsync<Texture2D>(path);
var bytes = await GF.Resource.LoadBinaryAsync(path);

// 事件：复制后转发
GF.Event.Fire(this, BlockClickedEventArgs.Create(x, y));

// 配置表
var cfg = ConfigSystem.Instance.Tables.TbEntityConfig.Get((int)EntityId.Cat);

// 存档（AES 由 ArchiveSetting.tres 配置）
await GF.Archive.LoadAsync();
GF.Archive.CurrentData.Score += 100;
await GF.Archive.OverWriteAsync();
```

## 常用工具速查

| 工具 | 位置 | 用法 |
|------|------|------|
| `GTween` | `GodotGameFrameworkCore/Utility/GTween.cs`（命名空间 `GodotGameFramework.DoTween`） | `node.DoScale(v, t)` / `DOMove(v, t)` / `DOPunchScale(v, t)` / `Delay(t, cb)` |
| `LayerMask` | `GodotGameFrameworkCore/Utility/LayerMask.cs`（SingletonNode） | `LayerMask.LayerToMask2D("Player", "Enemy")` → uint |
| `PhysicsCheck2D` | `GodotGameFrameworkCore/Utility/PhysicsCheck2D.cs`（IReference） | `PhysicsCheck2D.Create(target, shape)` 走 ReferencePool，用完 Release |
| `SingletonNode<T>` | `GodotGameFrameworkCore/SingletonSystem/SingletonNode.cs` | `XxxManager.Instance` 懒创建单例节点 |
| `NodePool` | `TheGame/MainPack/Scripts/ObjectPool/NodePool.cs` | `NodePool.Get<DamagePop>(ScenePath, parent)` 用完自动归还 |

## 项目结构速查

```
Framework/
  GameFramework/              ← 纯 C# 模块（零 Godot 依赖）
  GodotGameFrameworkCore/     ← Godot 运行时组件 + GF 门面
TheGame/
  GameScripts/GameProto/GameConfig/  ← Luban 生成代码（勿改）
  GameScripts/Entity/ UI/            ← 逻辑半类（Ge 半类在 GameProto/EntityGe、UIGe）
  MainPack/Scripts/Procedure/        ← ProcedureLaunch → ProcedureUpdate → ProcedurePrelode → ProcedureGame
  MainPack/Resources/*.tres          ← 配置资源（ArchiveSetting、ScriptGenerateRes 等）
```
