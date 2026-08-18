# 资源系统 (Resource Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Resource/`、`Framework/GodotGameFrameworkCore/Resource/`、`addons/ExportInspector/`、`addons/asset_bundle/`、`addons/TopMenu/`（Generate File → Collection Res）
> 本文档描述 GGF 的资源加载与子包管理：架构、加载机制、API 用法、AB 包导出工作流与注意事项。
> 热更下载流程见 `DownloadSystem.md`，热更链路审计见 `ResourceHotUpdateAudit.md`。

---

## 1. 概述

资源系统是 [Game Framework](https://gameframework.cn/) Resource 模块的 Godot 移植。原版 Unity 侧约 97 个接口成员被大幅裁剪——Godot 的 `ResourceLoader` / `.pck` 机制天然覆盖了 AssetBundle 依赖管理，因此当前 `IResourceManager` 只保留 18 个成员（10 个核心操作 + 8 个资产/二进制加载代理计数）。遵循框架**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Resource/` | `IResourceManager` 接口、`ResourceMode`、`HasAssetResult`、加载回调委托（`LoadAssetCallbacks` / `LoadBinaryCallbacks`） | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Resource/` | `ResourceManager` 实现（ResourceLoader 线程加载 + 版本清单）、`ResourceComponent`、`PackVersionList`、加载任务、`IResourceLoadHelper` / `DefaultResourceLoadHelper` 加载辅助器 | ✅ |

> ✅（2026-08）`GameFramework/Resource/` 下 Unity 遗留死代码已清理：`PackageVersionList.*`、`LocalVersionList.*`、`UpdatableVersionList.*`、`*VersionListSerializer.cs`、update/apply/verify 回调与事件参数等 **55 个零运行时引用的文件已删除**，仅保留在用的 15 个（`IResourceManager`、回调委托、状态枚举、`Constant`、`UnloadSceneCallbacks`）。实际运行时使用的清单类型是 Godot 层的 `PackVersionList`（`GodotGameFrameworkCore/Resource/`，JSON 序列化）。加载操作现抽象为 `IResourceLoadHelper` / `DefaultResourceLoadHelper`（桥接层，可由 `ResourceComponent` Inspector 配置实现）。

### ResourceMode 现状

| 模式 | 枚举值 | 实现程度 |
|------|:--:|------|
| `ResourceMode.Package` | 1 | ✅ 单机模式。全部资源在主包内，`Godot.ResourceLoader` 直接加载；`ProcedureUpdateVersion` 跳过远端更新检测，但尝试通过 `TryLoadLocalSubpackagesAsync()` 加载安装目录 `subpackages/` 下的本地 `.pck` 子包（失败不阻塞启动） |
| `ResourceMode.Updatable` | 2 | ✅ 热更模式。启动时 `DeserializeUpdatablePackVersion()` 读取 `user://GameFrameworkVersion.dat` 得到本地清单；子包 `.pck` 的下载/校验/`LoadResourcePack` 由热更流程链（`ProcedureUpdateVersion` → `ProcedureCheckResources` → `ProcedureUpdateResources`）驱动（见 §3.3 与 `DownloadSystem.md` §5） |

### 能力清单

- ✅ 同步加载：`LoadAsset<T>` / `LoadBinary`（byte[]）/ `LoadText`（string）
- ✅ 异步加载资源：`LoadAssetAsync`（`Task<Godot.Resource>`，经 `IResourceLoadHelper`（默认走 `ResourceLoader.LoadThreadedRequest`）后台线程加载）
- ✅ 异步加载二进制：`LoadBinaryAsync`（`Task<byte[]>`，基于 `Task.Run` 后台线程 IO + 每帧主线程轮询完成）
- ✅ 存在性检查：`Exists` / `HasAsset`（区分 Godot 资源与 `.bytes` 二进制）
- ✅ 子包版本清单（`PackVersionList`，JSON）+ 热更子包加载（`ProjectSettings.LoadResourcePack`）
- ✅ 编辑器侧 AB 包工作流：`AssetBundle.tres` 标记 → ExportInspector 一键导出 `.pck` + 版本清单
- ✅ 资源路径常量生成（`ResourcesCollectionConstant.cs`）
- ❌ 场景专用异步接口（`LoadSceneAsync`）——场景就是 `PackedScene` 资源，直接用 `LoadAssetAsync` 加载

---

## 2. 架构与数据流

```
调用方（EntityComponent / UIComponent / 业务代码）
    │  GF.Resource.LoadAssetAsync(path) / LoadAsset<T>(path)
    ▼
ResourceComponent (Godot 桥接层，场景节点 "Resource")
    │  委托 IResourceManager.LoadAsset(path, priority, callbacks, userData)
    │  TaskCompletionSource 字典（path → TCS）把回调转成 await
    ▼
ResourceManager : GameFrameworkModule (实现放在 Godot 层，因直接调 Godot API)
    └── TaskPool<LoadAssetTask>                   ← N Agent 并发（AgentCount，默认 10），按优先级调度
        └── Update(): 遍历所有 WorkingAgent
                helper.GetLoadStatus(path)（默认走 LoadThreadedGetStatus）
                  ├─ Loaded     → LoadThreadedGet → SuccessCallback → Agent 归还池
                  ├─ InProgress → UpdateCallback（累计 Duration）
                  └─ Failed     → FailureCallback → Agent 归还池
            有闲 Agent 时从等待队列取下一个任务（按 Priority 降序）
```

子包加载（Updatable 模式，启动期一次性）：

```
ProcedureLaunch → ProcedureUpdateVersion → ProcedureCheckResources → ProcedureUpdateResources
    │  版本检查 / 本地校验+差量 / 并发下载+加载（见 DownloadSystem.md §5）
    ▼
LoadDownloadedPacks(version)
    │  Config 包优先排序 → 逐包 大小校验 + SHA256 重校验(>1MB)
    ▼
ProjectSettings.LoadResourcePack(SubpackDir/{Name}.pck)   ← 插入 Godot 资源解析链头部
    │  失败包 → RollbackVersionFile()（回退 user:// 清单为 .bak）
    ▼
之后所有 ResourceLoader.Load / LoadThreadedRequest 自动命中子包内容
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Resource/IResourceManager.cs` | 管理器接口（18 成员：10 核心 + 8 代理计数，含 `SetLoadAssetAgentCount`） |
| `GameFramework/Resource/ResourceMode.cs` | Package / Updatable 枚举 |
| `GameFramework/Resource/HasAssetResult.cs` | 资源存在性结果 |
| `GameFramework/Resource/LoadAssetCallbacks.cs` | 加载回调委托组（Success/Failure/Update）——Godot 自动管理依赖，无 DependencyAsset |
| `GameFramework/Resource/LoadBinaryCallbacks.cs` 等 | 二进制加载回调委托组 |
| `GodotGameFrameworkCore/Resource/ResourceManager.cs` | 实现：`TaskPool<LoadAssetTask>` + 可配置 Agent 并发、二进制读取、清单反序列化 |
| `GodotGameFrameworkCore/Resource/ResourceComponent.cs` | 组件封装：同步/异步 API、`UpdateSettingRes` 配置入口 |
| `GodotGameFrameworkCore/Resource/PackVersionList.cs` | 版本清单 `PackVersionList` / `Pack` / `PackType` |
| `GodotGameFrameworkCore/Resource/LoadAssetTask.cs` | 资源加载任务（`TaskBase` + `ReferencePool` 池化） |
| `GodotGameFrameworkCore/Resource/LoadAssetAgent.cs` | 加载代理（`ITaskAgent<LoadAssetTask>`，每 Agent 承载一个 LoadThreadedRequest 槽位） |
| `GodotGameFrameworkCore/Resource/LoadBinaryTask.cs` | 异步二进制加载任务（`TaskBase` + `ReferencePool` 池化） |
| `GodotGameFrameworkCore/Resource/LoadBinaryAgent.cs` | 异步二进制加载代理（`ITaskAgent<LoadBinaryTask>`，`Task.Run` 后台 `File.ReadAllBytes`，主线程每帧 `Update()` 轮询完成后回调） |
| `GodotGameFrameworkCore/Resource/IResourceLoadHelper.cs` | 加载辅助器接口（Asset 异步加载/轮询/取资源 + Binary 同步/异步读取） |
| `GodotGameFrameworkCore/Resource/ResourceLoadHelperBase.cs` | 加载辅助器抽象基类（`GodotComponent`） |
| `GodotGameFrameworkCore/Resource/DefaultResourceLoadHelper.cs` | 默认加载辅助器（`ResourceLoader` + `System.IO` 实现，Inspector 可配置） |

---

## 3. 核心机制

### 3.1 异步加载队列（TaskPool + 可配置 Agent 并发）

`ResourceManager.LoadAsset` 的做法：

1. 校验路径非空且 `ResourceLoader.Exists`（失败立即同步回调 `LoadResourceStatus.NotExist`）
2. `LoadAssetTask.Create(...)`（从 `ReferencePool` 取）入 `TaskPool<LoadAssetTask>`
3. 等待队列按 **`Priority` 降序**插入
4. 有闲 Agent 时，`TaskPool` 取出任务 → Agent.Start 调用 `ResourceLoader.LoadThreadedRequest(assetName)` 提交加载
5. 每帧 `Update()` **遍历所有 WorkingAgent**，经 `IResourceLoadHelper.GetLoadStatus`（默认实现走 `ResourceLoader.LoadThreadedGetStatus`）轮询，独立交付完成回调；`InProgress` 状态下读取**真实加载进度**（`progressArray[0].AsSingle()`，0.0~1.0）并回调 `LoadAssetUpdateCallback`，替换了早期基于时间的模拟进度。

要点：

- **并行加载 + 并行交付**——各 Agent 各自轮询自己的任务，互不阻塞。队首大资源加载慢不影响其他已完成任务的交付。Agent 数量由 `ResourceComponent.AgentCount`（Inspector，0–20，默认 10）经 `SetLoadAssetAgentCount` 在 `OnInit` 时注入（✅ 2026-07，取代原先硬编码的 16）。
- `priority` 参数控制**等待队列中的排序**，高优先级任务先被分配到 Agent（`TaskPool.AddTask` 按 Priority 降序插入链表）。
- 任务对象经 `ReferencePool` 回收，Agent 经 `ITaskAgent` 重置复用，无每帧分配。
- `ResourceComponent.LoadAssetAsync` 用 `Dictionary<string, TaskCompletionSource<Godot.Resource>>` 按**资源路径**匹配回调 → 因此**同一路径不允许并发 await 两次**，第二次会得到 `InvalidOperationException("is already being loaded")`。

### 3.2 二进制加载

同步通道：

- `IResourceManager.LoadBinary`：**同步**实现——经 `IResourceLoadHelper.LoadBinary`（默认 `FileAccess.Open` 整读 `GetBuffer`）后立即回调（回调签名保留异步形态，便于将来切换实现）。
- `ResourceComponent.LoadBinary/LoadText`：更直接的同步便捷方法，文件不存在或异常返回 `null`（不抛异常）。
- `GetBinaryLength`：返回文件字节数，不存在返回 `-1`。

异步通道（✅ 2026-07 已接线）：

- `ResourceManager` 维护独立 `TaskPool<LoadBinaryTask>`（与 Asset 的 `TaskPool<LoadAssetTask>` 分离），由 `Update()` 统一轮询。
- Agent 并发数由 `ResourceComponent.BinaryAgentCount`（Inspector，0–10，默认 2）在 `OnInit` 时经 `SetLoadBinaryAgentCount` 注入——大文件 IO 不需要太多并发，2 个足够覆盖绝大多数场景。
- `LoadBinaryAgent.Start()` 在后台线程池执行 `System.IO.File.ReadAllBytes`（避开 Godot `FileAccess` 的线程安全问题），完成后设置 `m_ResultData`/`m_Error`。
- `LoadBinaryAgent.Update()` 被 `TaskPool` 的 `Update()` 在主线程每帧调用；检测到结果就绪时通过 `LoadBinaryCallbacks` 回调交付。
- `ResourceComponent.LoadBinaryAsync(path)`（公共 API）用 `TaskCompletionSource<byte[]>` 把回调转成 `Task<byte[]>`，调用方直接 `await`。
- 与 `LoadAssetAsync` 不同，`LoadBinaryAsync` **没有路径去重字典**——同一文件允许多次并发异步读取（适合多位置并发消费同一份配置数据的场景）。

### 3.3 子包加载流程

**Updatable 模式**（当前唯一的子包链路）：

| 阶段 | 执行者 | 内容 |
|------|--------|------|
| 启动 | `ResourceComponent.OnInit` | `SetResourceMode(Inspector 配置)` → `SetReadWritePath(user://)` → `SetLoadAssetAgentCount(AgentCount)` → `DeserializeUpdatablePackVersion()` 读 `user://GameFrameworkVersion.dat`（`Utility.Json` 反序列化，缺失/损坏仅警告不中断） |
| 热更 | `ProcedureUpdateVersion` → `ProcedureCheckResources` → `ProcedureUpdateResources` | 版本检查 → 远端清单比对 → `GF.Download` 差量下载到 `SubpackDir` → `EasySave.SaveInUserAsync` 保存新清单（旧清单备份为 `.bak`） |
| 加载 | `ProcedureUpdateBase.LoadDownloadedPacks`（热更链共享） | 按 `PackType.Config` 优先排序（配置先于场景就绪）→ 逐包大小校验 + 大文件(>1MB) SHA256 重校验 → `ProjectSettings.LoadResourcePack(packPath)` → `CleanStalePacks` 清理不在清单中的废弃 `.pck` → 有失败包则 `RollbackVersionFile()` |

`SubpackDir` 选择优先级（`HotUpdateContext` 首次访问计算并缓存）：

1. `UpdateSettingRes.HotUpdatePath`（显式配置；资源文件位于 `TheGame/MainPack/Resources/UpdateSettingRes.tres`）
2. 游戏安装目录 `subpackages/`（写测试通过时；编辑器下为 `res://../../Godot/subpackages`）
3. `user://subpackages/`（兜底，一定可写）

> 注意：**版本清单固定存于 `user://`**（`EasySave.SaveInUser` / `ResourceManager` 只从 `m_ReadWritePath = user://` 读取），而 `.pck` 本体可能在游戏安装目录——两者路径策略不同，属有意设计。

**Package 模式**：不检测远端更新，但启动时自动尝试加载安装目录下的本地子包——`ProcedureUpdateVersion.TryLoadLocalSubpackagesAsync()` 读取 `SubpackDir/GameFrameworkVersion.dat` 清单，逐包加载 `.pck`（与 Updatable 共用 `LoadDownloadedPacksAsync`，含大小校验 + SHA256 校验 + Config 优先排序）。失败**不阻塞启动**——子包缺失或清单无效仅记录日志后静默跳过。随主包分发的资源直接走 `res://`。编辑器侧 `addons/asset_bundle/export_plugin.gd` 会在**工程导出时**把标记为 AssetBundle 的目录从主包剥离，单独产出 `<导出目录>/subpackages/*.pck`。

### 3.4 版本清单（PackVersionList）

```csharp
public enum PackType : byte { Resource = 0, Config = 1, Script = 2 }  // Config 最先加载

public class PackVersionList {
    string Version;         // 如 "2.3.0"
    Pack[] Packs;
    string MinAppVersion;   // 低于此版本必须去商店更新
    bool   ForceUpdate;
    bool IsValid();         // Version 非空且 Packs 非空
}
public struct Pack {
    string Name;      // 包名（不含扩展名）
    long   Size;      // 字节数（校验与进度用）
    string Hash;      // SHA256 hex（64 字符）
    string Url;       // 下载地址
    PackType Type;
}
```

清单文件名统一为 `ResourceManager.GameFrameworkVersionData`（`"GameFrameworkVersion.dat"`），内容为 JSON。

### 3.5 版本清单校验加载（`LoadAndValidateVersionList`）

`NodeUtility.LoadAndValidateVersionList(fileName)` 提供带严格校验的版本清单加载，热更流程链在多处使用该方法替代普通的 JSON 反序列化：

1. **JSON 结构检测**：空文件 / 截断（长度 < 2）/ 缺少花括号 → 返回 `null` 并记录具体原因。
2. **`PackVersionList.Validate()`** 深度校验：（a）`Version` 非空；（b）`Packs` 非 null 非空；（c）每个 `Pack` 有效性检查（名称/大小/SHA256 均合法）；（d）**重复包名检测**——`HashSet<string>` 逐包登记，重名直接拒绝。
3. 异常安全：`Newtonsoft.Json.JsonException` 和通用 `Exception` 分别捕获，均返回 `null` 不抛异常。

与普通 `Utility.Json.ToObject<T>()` 的区别：`LoadAndValidateVersionList` **不信任磁盘数据**——它假定任何环节（写入中断、磁盘故障、传输截断）都可能导致损坏的 JSON 或字段缺失，因此逐层校验后才返回可用对象。

---

## 4. ResourceComponent

场景节点：`Framework/GameFramework.tscn` 中的 `Resource` 节点，经 `GF.Resource` 访问（懒缓存）。`ProcessMode = Always`（暂停时仍可加载）。

### 4.1 Inspector 参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `_resourceMode` | `ResourceMode` | `Package` | 资源模式，`OnInit` 时写入 `ResourceManager` |
| `AgentCount` | `int`（0–20） | `10` | 资源加载代理数量（并发上限），`OnInit` 时经 `SetLoadAssetAgentCount` 注入 |
| `_UpdateSettingRes` | `UpdateSettingRes` | — | 热更配置资源（`RemoteUrl` 远端地址、`HotUpdatePath` 补丁目录），供热更链 / ExportInspector 读取 |

### 4.2 API 总览

```csharp
// 属性
GF.Resource.ResourceMode                  // 当前资源模式（可读写，运行时改无意义）
GF.Resource.UpdateSettingRes              // 热更配置（RemoteUrl / HotUpdatePath）
GF.Resource.LocalPackVersionList          // 本地版本清单（Package 模式或清单缺失时为 null）

// 同步加载
byte[] data  = GF.Resource.LoadBinary("user://save/slot0.bytes");  // null = 不存在
string text  = GF.Resource.LoadText("res://TheGame/Configs/a.txt");
PackedScene ps = GF.Resource.LoadAsset<PackedScene>("res://TheGame/Entitys/CatEntity.tscn");
bool exists  = GF.Resource.Exists(path);  // ResourceLoader.Exists || FileAccess.FileExists

// 异步加载（后台线程，await 回主线程续体）
Godot.Resource res = await GF.Resource.LoadAssetAsync(path);
Godot.Resource res = await GF.Resource.LoadAssetAsync(path, priority, userData);
```

### 4.3 使用示例

```csharp
// 异步加载实体场景（EntityComponent 内部即为此模式）
var scene = (PackedScene)await GF.Resource.LoadAssetAsync(
    ResourcesCollectionConstant.Entitys_CatEntity);   // 生成的路径常量，避免手写字符串
var node = scene.Instantiate<CatEntity>();

// 异常路径：资源不存在 / 重复加载中 → Task 以异常完结
try { await GF.Resource.LoadAssetAsync("res://not_exist.tscn"); }
catch (Exception e) { Log.Warning(e.Message); }

// 二进制（Luban 数据表加载即走此通道）
byte[] bytes = GF.Resource.LoadBinary("res://TheGame/GameProto/GameConfig/tbentityconfig.bytes");
```

---

## 5. 导出工作流（编辑器侧）

三个插件覆盖「标记 → 打包 → 引用」全链路：

### 5.1 AssetBundle 标记（`addons/asset_bundle/`，GDScript）

在目录下新建 `AssetBundle` 类型的 `.tres`，**该目录即成为一个资源包**：

| 属性 | 默认 | 说明 |
|------|:--:|------|
| `enabled` | true | 是否启用该包 |
| `export_enabled` | true | 是否导出该包 |
| `pack_external_dependencies` | true | 是否打包目录外依赖 |
| `export_only_imported` | false | true = 仅打包 Godot 导入产物（`.ctex`/`.fontdata`/`.sample`），不含源文件（`.png`/`.ttf`/`.wav`），体积可减 80%+ |

`export_plugin.gd`（`EditorExportPlugin`）在**工程导出**时生效：`_export_file` 将属于 bundle 目录的文件 `skip()` 出主包，`_export_end` 用 `PCKPacker` 把它们打进 `<导出目录>/subpackages/<包名>.pck`（场景压缩序列化为 `.scn`、生成 remap、按需携带外部依赖，见 `AssetBundlePackUtils.gd`）。

### 5.2 ExportInspector 面板（`addons/ExportInspector/`，C#）

编辑器右上 Dock「AssetBundle 导出管理」，**不依赖工程导出**，随时手动出包（热更工作流的主要工具）：

1. 扫描全项目 `.tres`/`.res`，脚本全局名含 `AssetBundle` 的即为包标记
2. Tree 展示：包名 / 类型 / 大小 / 导入状态（✅ 已导入 · ⚠️ 缓存缺失 · —）+ 三个可勾选列（启用 / 导出 / 仅产物），勾选直接 `ResourceSaver.Save` 回写 `.tres`
3. 「导出全部」→ 在导出目录下建**时间戳子目录**（`yyyy-MM-dd_HH-mm-ss`），逐包 `PckPacker` 打包：
   - 全量模式：源文件 + `.import` + 导入产物（解析 `.import` 的 `dest_files`）
   - 仅产物模式（`export_only_imported`）：跳过源文件，仅 `.import` + 导入产物
4. 生成 `GameFrameworkVersion.dat`：逐包填 `Name/Size/SHA256/Url`（`Url` = `UpdateSettingRes.RemoteUrl + "/{Name}.pck"`），`Version` 取面板输入（校验 `\d+.\d+.\d+` 格式）
5. 导出目录与版本号持久化在 EditorSettings（`godot_asset_bundle/*`）
6. 「打开」按钮：在导出目录行旁，点击调用 `OS.ShellOpen` 直接打开导出文件夹，方便检查产出或上传

产出目录整体上传服务器即完成一次热更发布（客户端流程见 `DownloadSystem.md` §5）。

### 5.3 路径常量生成（`addons/TopMenu/` → Generate File）

编辑器菜单 `Project → Tools → Generate File → Collection Res`：

- `DirAccess` 递归扫描 `res://TheGame/`，排除 `.cs`、`GameScripts/`、`.import`、`.uid`
- **同名文件（不同路径）直接报错中止**——因常量名按 `{所在文件夹}_{文件名}` 生成，必须全局唯一
- 输出 `TheGame/GameScripts/GameProto/ResourcesCollectionConstant.cs`（`GameConfig.Constant` 命名空间），如：
  ```csharp
  public const string Entitys_CatEntity = "res://TheGame/Entitys/CatEntity.tscn";
  ```

Luban 实体/UI 配置表中的场景路径引用这些常量对应的字符串，实现「路径改名编译期报错」。

---

## 6. 注意事项 / FAQ

**Q: `LoadAssetAsync` 同一路径能并发调用吗？**
不能。第二次调用在任务字典 `TryAdd` 失败，Task 以 `InvalidOperationException` 完结。需要共享结果请自行缓存首个 Task。

**Q: `priority` 参数生效吗？**
✅（2026-07 已修复）等待队列按 `TaskPool.AddTask` 的 `Priority` 降序排序，高优先级任务先分配到 Agent。同时多 Agent 并发加载（`AgentCount`，默认 10），不再 FIFO 队首阻塞。

**Q: 大量并发加载会过载吗？**
不会。`ResourceManager` 的 `LoadAssetAgent` 数量由 `ResourceComponent.AgentCount` 配置（默认 10，`TaskPool` 控制），同时最多提交同等数量的 `LoadThreadedRequest`，超出部分排队等待。

**Q: Package 模式下能读到版本清单吗？**
不能。`LocalPackVersionList` 返回 `null`。Package 模式不读任何清单。判空后再使用。

**Q: 热更子包加载失败怎么办？**
`ProcedureUpdateBase.LoadDownloadedPacks` 逐包容错：损坏包删除并计失败，任一失败即回退 `user://` 版本清单（`.bak` 恢复），下次启动重新下载。游戏继续以旧资源运行。

**Q: `.pck` 里要不要带源文件？**
纯运行时分发勾选「仅产物」即可（Godot 运行时只读 `.import` + 导入缓存）；需要在其他工程/编辑器中二次导入时才用全量模式。

**Q: 二进制大文件读取卡帧？**
`LoadBinary` 是主线程同步整读；大文件请用异步通道 `ResourceComponent.LoadBinaryAsync`（`Task<byte[]>`，经 `TaskPool<LoadBinaryTask>` + `LoadBinaryAgent` 后台 `File.ReadAllBytes`，主线程每帧轮询回调），无需自行 `Task.Run` 包装。

---

## 7. 已知边界与后续计划

- [x] `LoadAssetTask` 队列改为 TaskPool + 多 Agent 并发（消除队首阻塞，支持优先级调度；Agent 数由 `AgentCount` 配置，默认 10）✅ 2026-07
- [x] `LoadBinaryTask` / `LoadBinaryAgent` 接入 `ResourceManager`（异步二进制不再垄断主线程 IO）✅ 2026-07
- [x] Package 模式的本地子包加载（`TryLoadLocalSubpackagesAsync`，读取安装目录 `subpackages/GameFrameworkVersion.dat`）✅ 2026-07
- [x] `LoadAssetDependencyAssetCallback`/`LoadSceneDependencyAssetCallback` 清理（Godot 自动管理资源依赖）✅ 2026-07
- [x] Unity 遗留文件清理（`GameFramework/Resource/` 下 55 个零引用文件已删除，保留 15 个在用的）✅ 2026-08
- [ ] `PackType.Script` 子包的实际消费（GDScript 热更，见 `CodeHotUpdateDesign.md`；⚠️ 已随代码热更方案搁置，等待华佗团队 Godot 适配）
