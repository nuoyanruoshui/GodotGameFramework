# 下载系统 (Download Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Download/`、`Framework/GodotGameFrameworkCore/Download/`
> 本文档描述 GGF 的统一下载通道：架构、关键机制、API 用法、热更流程集成与注意事项。

---

## 1. 概述

下载系统是 [Game Framework](https://gameframework.cn/) Download 模块的 Godot 移植，遵循框架的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Download/` | 任务队列、代理调度、断点文件管理、超时、事件定义 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Download/` | HttpClient 传输实现、组件封装、可 await API、事件转发 | ✅ |

**项目内下载分工**（唯一通道原则）：

| 场景 | 通道 |
|------|------|
| 小体积文本/JSON（版本清单等） | `GF.WebRequest`（Godot HttpRequest 节点） |
| 大文件落盘（热更 .pck、补丁等） | **`GF.Download`（本模块）** |

> 历史说明：曾存在独立的 `StreamingDownloader` 静态工具类（热更流程专用），因与本模块传输层重复且存在 120s 总时长超时缺陷，已于 2026-07 删除，其独有能力（大小/SHA256 校验、可 await）合并进 `DownloadComponent.DownloadFileAsync`。

### 能力清单

- ✅ 任务队列 + 多代理并发（默认 3 agent，自动限流）
- ✅ 流式下载（64KB 缓冲，内存占用与文件大小无关）
- ✅ 断点续传（HTTP Range + `.download` 临时文件，重试自动续传）
- ✅ 无进度超时（默认 30s，**不是**总时长限制，大文件慢网不受影响）
- ✅ 大小 + SHA256 校验（`DownloadFileAsync`）
- ✅ 事件驱动 + `async/await` 两种消费方式
- ✅ 优先级 / 标签 / 暂停 / 下载速度统计
- ✅ `user://`、`res://` 虚拟路径自动转换

---

## 2. 架构与数据流

```
调用方（ProcedureUpdate / 业务代码）
    │  GF.Download.DownloadFileAsync() / AddDownload()
    ▼
DownloadComponent (Godot 桥接层，场景节点 "Download")
    │  委托                                      ▲ C# 事件
    ▼                                            │
DownloadManager : GameFrameworkModule (纯 C# 层) │
    ├── TaskPool<DownloadTask>   ← 队列/优先级/调度（每帧 Update 轮询）
    ├── DownloadAgent × N        ← 写盘（.download 文件流）、超时计时、断点管理
    │       │ Download()/Reset()          ▲ UpdateBytes/UpdateLength/Complete/Error
    │       ▼                             │
    └── IDownloadAgentHelper（传输抽象）───┘
            ▲
            │ 实现
WebRequestDownloadAgentHelper : DownloadAgentHelperBase : Node (Godot 桥接层)
    └── System.Net.Http.HttpClient 流式下载（Range 断点续传）
```

事件向上流转：

```
Helper(传输事件) → DownloadAgent(写盘/进度) → DownloadManager(C# 事件)
    → DownloadComponent(转发) → EventComponent(全局事件, 下一帧分发)
                              └→ DownloadFileAsync 的 TCS（serialId 匹配）
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Download/IDownloadManager.cs` | 管理器接口 |
| `GameFramework/Download/DownloadManager.cs` | 任务管理、代理调度、事件 |
| `GameFramework/Download/DownloadManager.DownloadAgent.cs` | 单代理：写盘、断点、超时、完成校验 |
| `GameFramework/Download/DownloadManager.DownloadTask.cs` | 任务数据（serialId 自增） |
| `GameFramework/Download/DownloadManager.DownloadCounter.cs` | 下载速度统计 |
| `GameFramework/Download/IDownloadAgentHelper.cs` | 传输抽象（4 事件 + 3 Download 重载 + Reset） |
| `GameFramework/Download/DownloadAgentHelper*.EventArgs.cs` | 传输层事件参数（池化） |
| `GameFramework/Download/Download*EventArgs.cs` | 管理器层事件参数（池化） |
| `GodotGameFrameworkCore/Download/DownloadAgentHelperBase.cs` | Helper 基类（Node，可挂树） |
| `GodotGameFrameworkCore/Download/WebRequestDownloadAgentHelper.cs` | HttpClient 传输实现 |
| `GodotGameFrameworkCore/Download/DownloadComponent.cs` | 组件封装 + `DownloadFileAsync` |
| `GodotGameFrameworkCore/Download/Download*EventArgs.cs` | Godot 层全局事件参数（经 EventComponent 分发） |

---

## 3. 核心机制

### 3.1 断点续传（`.download` 机制）

1. `DownloadAgent.Start` 时，目标路径追加 `.download` 后缀作为临时文件
2. 临时文件已存在 → `Seek(End)` 追加写，并以已有长度作为 `fromPosition` 发起 **HTTP Range** 请求
3. Helper 校验续传响应必须为 **206 Partial Content**：
   - 服务器不支持 Range 返回 200 全量 → 报错并**删除** `.download`，任务从头重下（防止"部分+全量"拼接损坏）
   - 416（Range 越界，本地临时文件已损坏/超长）→ 删除 `.download` 重下
4. 下载完成 → 大小一致性内部校验 → `File.Move(xxx.download, xxx)` 原子替换
5. **下载失败默认保留 `.download`**，下次重试自动续传（详见 §3.4 错误语义表）

`FlushSize`（默认 1MB）控制缓冲区落盘节奏：每写满 FlushSize 字节强制 `Flush()`，崩溃时最多丢失 FlushSize 的进度。

### 3.2 超时模型

`Timeout`（默认 30s）是**无进度超时**：`DownloadAgent` 每帧累计等待时间，任何 `UpdateBytes/UpdateLength` 事件都会清零计时。只要还有数据在流动，下载多大、多久都不会超时。Helper 内部的 `HttpClient.Timeout` 被设为 `Infinite`，完全交由该机制控制。

### 3.3 线程模型

- 所有事件回调（含 `DownloadFileAsync` 的 `onProgress` 和 await 续体）都在 **Godot 主线程**执行：Helper 的 async 方法从主线程启动，`await` 续体经 `GodotSynchronizationContext` 回到主线程
- 唯一的例外：`DownloadFileAsync` 的 SHA256 计算走 `Task.Run`（线程池），避免大文件哈希卡帧
- 因此调用方**无需加锁**，可在回调中直接操作 UI / 节点

### 3.4 传输层错误语义（`deleteDownloading` 约定）

| 情形 | 是否删除 `.download` | 后果 |
|------|:--:|------|
| 网络错误 / HTTP 5xx 等 | ❌ 保留 | 重试自动续传 |
| 超时（无进度 30s） | ❌ 保留 | 重试自动续传 |
| 续传收到 200（服务器不支持 Range） | ✅ 删除 | 从头重下（防文件损坏） |
| 416 Range Not Satisfiable | ✅ 删除 | 从头重下 |
| `Reset()` 主动取消（移除任务/代理复用） | — | **静默，不发任何事件** |
| `DownloadFileAsync` 大小/哈希校验失败 | 删除**成品文件** | 返回 false，重试从头下 |

> ⚠️ **自定义 Helper 实现契约**：`Reset()` 之后**严禁再触发任何事件**——此时 Agent 可能已被回收（`m_Task == null`）或已接手下一个任务，补发事件会导致空引用或污染新任务。`WebRequestDownloadAgentHelper` 通过"每个 await 恢复点 + 每次事件触发之间检查 `CancellationToken`、`OperationCanceledException` 静默返回"来保证这一点。

### 3.5 事件参数池化

所有 `XxxEventArgs` 均来自 `ReferencePool`，事件回调返回后即被回收。**不可持有引用、不可在异步续体中访问** —— 需要跨帧/异步使用的字段必须在回调内先拷贝（`DownloadComponent.OnDownloadSuccess` 即为示例）。

---

## 4. DownloadComponent

场景节点：`Framework/GameFramework.tscn` 中的 `Download` 节点，经 `GF.Download` 访问。

### 4.1 Inspector 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `m_DownloadAgentHelperTypeName` | `GodotGameFramework.Download.WebRequestDownloadAgentHelper` | Helper 类型名（反射创建，可替换自定义实现） |
| `m_DownloadAgentHelperCount` | 3 | 并发代理数。机械硬盘/低速设备可调为 1 |
| `m_Timeout` | 30 | 无进度超时（秒） |
| `m_FlushSize` | 1MB | 缓冲落盘临界值 |

### 4.2 属性 / 方法总览

```csharp
// 状态
GF.Download.Paused              // 暂停/恢复整个下载队列
GF.Download.TotalAgentCount     // 代理总数
GF.Download.FreeAgentCount      // 空闲代理数
GF.Download.WorkingAgentCount   // 工作中代理数
GF.Download.WaitingTaskCount    // 排队任务数
GF.Download.CurrentSpeed        // 当前下载速度 (bytes/s)
GF.Download.Timeout / FlushSize // 运行时可改

// 任务管理（事件驱动方式）
int serialId = GF.Download.AddDownload(downloadPath, downloadUri);
// 重载：+tag / +priority / +userData 任意组合，共 8 个
GF.Download.RemoveDownload(serialId);   // ⚠️ 静默移除，不触发 Failure 事件
GF.Download.RemoveDownloads(tag);
GF.Download.RemoveAllDownloads();
TaskInfo info = GF.Download.GetDownloadInfo(serialId);

// 可 await 方式（推荐）
Task<bool> ok = GF.Download.DownloadFileAsync(uri, path, onProgress);
Task<bool> ok = GF.Download.DownloadFileAsync(uri, path,
    expectedSize, expectedHash, onProgress, cancellationToken);
```

所有 `downloadPath` 支持 `user://`、`res://` 虚拟路径，内部经 `EasySave.GlobalizeDownloadPath()` 转为绝对路径（`DownloadManager` 使用 `System.IO`，不识别 Godot 虚拟路径）。

### 4.3 DownloadFileAsync 详解

**契约：任何失败/取消都返回 `false`，不抛异常。**

```
AddDownload → serialId 存入 m_FileOperations 字典
    │
    ├─ DownloadUpdate 事件 → onProgress(已下载字节, expectedSize)   [主线程]
    │
    ├─ DownloadSuccess 事件 → VerifyAndCompleteAsync:
    │       1. expectedSize > 0 且大小不符 → 删文件 → false
    │       2. expectedHash 非空 → Task.Run 计算 SHA256 → 不匹配 → 删文件 → false
    │       3. 全部通过 → true
    │
    ├─ DownloadFailure 事件 → false（.download 保留，重试可续传）
    │
    └─ CancellationToken 取消 → false + 主线程延迟 RemoveDownload
        （TaskPool 移除任务不发事件，由注册回调自行完结 TCS）
```

组件 `OnExitTree` 时，所有未完成的 awaiter 一律按 `false` 完结，不会悬挂。

### 4.4 使用示例

**方式一：await（推荐，含校验）**

```csharp
bool ok = await GF.Download.DownloadFileAsync(
    downloadUri: "https://cdn.example.com/patch/ui.pck",
    downloadPath: "user://subpackages/ui.pck",     // 虚拟路径自动转换
    expectedSize: pack.Size,                       // 来自版本清单
    expectedHash: pack.Hash,                       // SHA256 hex
    onProgress: (downloaded, total) =>
        GD.Print($"进度 {downloaded}/{total}"));   // 主线程回调，可直接刷 UI

if (!ok) { /* 重试或提示；.download 断点已保留 */ }
```

**方式二：事件驱动（适合后台批量任务）**

```csharp
// 订阅（EventId = typeof(XxxEventArgs).GetHashCode()）
GF.Event.Subscribe(DownloadSuccessEventArgs.EventId, OnDownloadSuccess);
GF.Event.Subscribe(DownloadFailureEventArgs.EventId, OnDownloadFailure);

int serialId = GF.Download.AddDownload("user://dlc/ep2.pck", url, tag: "dlc");

private void OnDownloadSuccess(object sender, GameEventArgs e)
{
    var args = (DownloadSuccessEventArgs)e;
    if (args.SerialId != serialId) return;   // 或用 tag/userData 区分
    Log.Info("下载完成: {0}", args.DownloadPath);
    // ⚠️ args 是池化对象，本回调返回后即回收，不可持有
}
```

**取消与暂停**

```csharp
var cts = new CancellationTokenSource();
var task = GF.Download.DownloadFileAsync(url, path, cancellationToken: cts.Token);
cts.Cancel();                    // → task 结果为 false，任务被移除

GF.Download.Paused = true;       // 暂停整个队列（进行中的代理完成当前块后挂起）
```

---

## 5. 热更流程集成（ProcedureUpdate）

`TheGame/MainPack/Scripts/Procedure/ProcedureUpdate.cs` 是本模块的主要消费者。完整流程：

```
ProcedureLaunch → ProcedureUpdate → ProcedurePrelode → ProcedureGame
```

| 步骤 | 内容 | 失败策略 |
|------|------|----------|
| 0 | Package 模式 / 崩溃安全模式（HotUpdateSafetyGuard）检测 | 跳过热更 |
| 1 | `GF.WebRequest` 拉取远端 `GameFrameworkVersion.dat`（3 次重试） | 跳过热更 |
| 2 | `MinAppVersion` 兼容性检查 | 引导商店更新 |
| 3 | 本地清单加载 + 逐包完整性自检（存在+大小+SHA256） | 损坏包重新下载 |
| 4 | 与服务器清单比对（`FindPacksToUpdate`：新包 / Hash / Size 变化） | — |
| 5 | 磁盘空间预检（需 2× 总大小） | 跳过下载 |
| 6 | **并发下载**（见下） | 单包失败不影响其他包 |
| 7 | `LoadResourcePack` 加载子包 → 保存新版本清单（旧清单先备份） | — |

### 5.1 并发下载与进度聚合

- 所有待更新包**一次性投递** `Task.WhenAll`，由 DownloadComponent 的 TaskPool 自动限流至 agent 数（默认 3）
- 每包独立重试：`MaxRetries = 3`，指数退避（1.5s → 3s → 6s），失败续传 `.download`
- 进度按**字节加权聚合**：`perPackBytes[]` 槽位（回调均在主线程，无锁），UI 显示 `下载中 {完成数}/{总数} (已下/总量)`，进度条区间 10% ~ 90%

### 5.2 版本清单结构（PackVersionList）

```csharp
public class PackVersionList {
    string Version;        // 如 "2.3.0"
    Pack[] Packs;          // 子包列表
    string MinAppVersion;  // 低于此版本必须去商店更新
    bool   ForceUpdate;    // 强制更新标记
}
public struct Pack {
    string Name;     // 包名（不含扩展名）
    long   Size;     // 字节数（进度与校验用）
    string Hash;     // SHA256 hex (64 字符)
    string Url;      // 下载地址（空则用 RemoteUrlBase/{Name}.pck）
    PackType Type;   // Resource / Config / Script
}
```

### 5.3 热更目录选择（SubpackDir）

优先级：`UpdateSettingRes.HotUpdatePath`（显式配置）→ 游戏安装目录 `subpackages/`（可写时）→ `user://subpackages/`（兜底）。

---

## 6. 注意事项 / FAQ

**Q: `RemoveDownload` 后为什么收不到 Failure 事件？**
TaskPool 移除任务是静默的（原版 GF 行为）。需要通知请自行管理状态；`DownloadFileAsync` 的取消路径已内部处理。

**Q: 下载路径能直接传 `user://xxx` 吗？**
能。`DownloadComponent` 所有入口都会经 `EasySave.GlobalizeDownloadPath` 转换。**不要**绕过组件直接调 `IDownloadManager`。

**Q: 并发下载导致卡顿/磁盘压力大？**
调低场景中 `Download` 节点的 `m_DownloadAgentHelperCount`（机械硬盘建议 1），无需改代码。

**Q: 局域网高速下载时偶发帧尖峰？**
Helper 的读循环在数据同步就绪时不主动让帧，极端情况下单帧内处理大量数据属已知特性；`FlushSize` 只控制落盘不控制循环。如成为问题可在 Helper 循环中按字节数主动让出。

**Q: 如何自定义传输实现（如 P2P、CDN SDK）？**
继承 `DownloadAgentHelperBase`，实现 3 个 `Download` 重载 + `Reset` + 4 个事件，然后把 Inspector 的 `m_DownloadAgentHelperTypeName` 改为你的类型全名。**务必遵守 §3.4 的 Reset 契约。**

**Q: Web 导出？**
`DownloadAgent` 完成时会调用 `WebGLPersistence.Sync()` 同步 IndexedDB；传输层 HttpClient 在 Web 平台受浏览器限制，未经验证，暂不支持。

**Q: 旧版 `.tmp` 临时文件？**
旧 `StreamingDownloader` 使用 `.tmp` 后缀，现行机制为 `.download`。`ProcedureUpdate` 在每包下载前会清理同名 `.tmp` 遗留文件。

---

## 7. 已知边界与后续计划

- [ ] `DownloadCounter` 速度统计接入热更 UI（当前 UI 仅显示字节进度）
- [ ] Helper 读循环按帧让出（超高速局域网场景）
- [ ] Web 平台传输验证
- [ ] 下载完成后 `LoadResourcePack` 的失败回滚与 §5 步骤 7 的备份恢复联动（见 `ResourceHotUpdateAudit.md`）
