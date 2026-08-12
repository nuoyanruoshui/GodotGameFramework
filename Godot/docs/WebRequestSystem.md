# Web 请求系统 (WebRequest Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/WebRequest/`、`Framework/GodotGameFrameworkCore/WebRequest/`
> 本文档描述 GGF 的 HTTP 短请求通道：三层架构（Component / Manager / Helper）、SendRequest/SendRequestAsync API、超时模型、双结果通道（事件 + Task）、与 Download 模块的分工及热更版本清单请求实战。

---

## 1. 概述

Web 请求系统提供**小体积 HTTP 通信**能力（GET/POST，结果驻留内存），底层基于 **Godot `HttpRequest` 节点**实现。架构遵循框架通用的 **组件 → 纯 C# Manager → Helper** 三层委托模式（与 Download 模块同构）：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/WebRequest/` | `IWebRequestManager` + `WebRequestManager`（`TaskPool<WebRequestTask>` 调度、serialId、超时、事件分发）、`IWebRequestAgentHelper` 接口、池化事件参数 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/WebRequest/` | `WebRequestComponent`（公开 API + 事件转发 + TCS 桥接）、`WebRequestAgentHelperBase : HttpRequest`、`DefaultWebRequestAgentHelper`（HttpRequest 实现） | ✅ |

**数据流**：组件创建 N 个 helper 节点注册进 Manager；每请求 = 一个 `WebRequestTask`（唯一 serialId），进入 `TaskPool` 按优先级排队；空闲 `WebRequestAgent`（包装一个 helper）取任务并调用 `helper.Request(url)` 发起 HTTP；`HttpRequest` 的 `request_completed` 信号 → helper 发完成事件 → agent 上报 → Manager 发 Success/Failure 事件 → 组件复制为全局 `WebRequestCompleteEventArgs` 事件 + 解析 await 的 Task。

**项目内下载分工**（唯一通道原则，与 `DownloadSystem.md` 对应）：

| 场景 | 通道 |
|------|------|
| 小体积文本/JSON（版本清单、接口调用），结果在内存中处理 | **`GF.WebRequest`（本模块）** |
| 大文件落盘（热更 .pck、补丁等），需断点续传/校验 | `GF.Download` |

### 能力清单

- ✅ GET / POST（POST 体为 UTF-8 字节）
- ✅ 事件驱动（fire-and-forget）与 `async/await`（TCS 模式）两种消费方式，**同一请求两条通道同时推送**
- ✅ 每请求独立超时（默认 30s，可传 0/负数关闭），由纯 Manager 每帧轮询检测（`realElapseSeconds`，不受 TimeScale 影响）
- ✅ 并发有界（默认 4 个 agent，`WebRequestComponent.m_WebRequestAgentHelperCount` 可配），超出排队，`TaskPool` 按优先级调度
- ✅ 超时自动 `CancelRequest` + 统一失败约定（`Result = -1, ResponseCode = 0`）
- ✅ Helper 节点复用（顺序请求），`Reset` 取消在途请求并抑制迟到的信号
- ❌ 无内置重试（调用方自行实现，参考 §5）、暂不支持自定义 Header / 其他 HTTP 方法

---

## 2. 架构与数据流

```
调用方（ProcedureUpdate / 业务代码）
    │  GF.WebRequest.SendRequestAsync(url) / SendRequest(url)
    ▼
WebRequestComponent (Godot 桥接层，场景节点 "WebRequest")
    │  OnInit: GetModule<IWebRequestManager> → 创建 N 个 DefaultWebRequestAgentHelper → AddWebRequestAgentHelper
    │  SendRequestAsync: AddWebRequest(url, ...) 返回 serialId → m_WebRequestTasks[serialId] = TCS
    ▼
IWebRequestManager / WebRequestManager (纯 C#，GameFrameworkEntry 单例模块)
    │  WebRequestTask.Create(++s_Serial, ...) → TaskPool.AddTask（按优先级排队）
    │  Update() → TaskPool.Update() → 空闲 WebRequestAgent.Start(task)
    ▼
WebRequestAgent ×N（纯 C#，包装一个 IWebRequestAgentHelper）
    │  m_Helper.Request(uri, postData, userData)
    │  Update(): 累计 realElapseSeconds，超时 → helper.Reset() + 上报失败(Result=-1)
    ▼
DefaultWebRequestAgentHelper : HttpRequest（Godot 节点，复用）
    │  request_completed 信号 → 发 WebRequestAgentHelperComplete/Error 事件
    ▼
WebRequestAgent → Manager
    │  OnWebRequestAgentSuccess/Failure → 池化 WebRequestSuccess/FailureEventArgs → 事件分发
    ▼
WebRequestComponent（订阅 Manager 事件）
    ├──► EventComponent.Fire(WebRequestCompleteEventArgs.Create(...))   ← 池化副本，全局事件
    └──► tcs.TrySetResult(new WebRequestCompleteEventArgs(...))          ← 全新实例，await 方安全持有
```

### 文件清单

| 文件 | 层 | 职责 |
|------|----|------|
| `GodotGameFrameworkCore/WebRequest/WebRequestComponent.cs` | 桥接 | 组件（`GF.WebRequest`）：公开 API、helper 创建注册、事件转发、TCS 桥接 |
| `GodotGameFrameworkCore/WebRequest/WebRequestAgentHelperBase.cs` | 桥接 | `HttpRequest, IWebRequestAgentHelper` 抽象基类 |
| `GodotGameFrameworkCore/WebRequest/DefaultWebRequestAgentHelper.cs` | 桥接 | HttpRequest 实现：发起请求、完成/错误事件、Reset 取消 |
| `GodotGameFrameworkCore/WebRequest/WebRequestCompleteEventArgs.cs` | 桥接 | 结果事件参数（Url/Result/ResponseCode/Headers/Body），池化 `Create` + 普通构造双形态 |
| `GameFramework/WebRequest/IWebRequestManager.cs` | 纯 C# | Manager 接口：事件、AddWebRequest/Remove、查询 |
| `GameFramework/WebRequest/WebRequestManager.cs` | 纯 C# | Manager 实现：TaskPool、serialId、超时、事件分发 |
| `GameFramework/WebRequest/WebRequestManager.WebRequestTask.cs` | 纯 C# | `WebRequestTask : TaskBase`（Uri/PostData/Timeout），池化 `++s_Serial` |
| `GameFramework/WebRequest/WebRequestManager.WebRequestAgent.cs` | 纯 C# | `ITaskAgent<WebRequestTask>`，包装 `IWebRequestAgentHelper`，超时检测 |
| `GameFramework/WebRequest/IWebRequestAgentHelper.cs` | 纯 C# | 辅助器接口（Complete/Error 事件 + Request/Reset） |
| `GameFramework/WebRequest/WebRequestSuccessEventArgs.cs` 等 4 个 | 纯 C# | 池化事件参数（Manager 级 Success/Failure + Helper 级 Complete/Error） |

---

## 3. 核心机制

### 3.1 超时模型

- **每请求总时长超时**（区别于 Download 模块的"无进度超时"）：`WebRequestTask.Timeout` 由调用方传入（`SendRequestAsync` 的 timeout 参数，默认 30f）。传 `0` 或负数表示**不超时**（Task 创建时归一化为 0）。
- 超时检测在**纯 C# 层** `WebRequestAgent.Update`（经 `WebRequestManager.Update` → `TaskPool.Update` 每帧驱动），用 `realElapseSeconds` 累计（不受 `Engine.TimeScale` 影响）。
- 超时处理：`m_Helper.Reset()`（底层 `CancelRequest` + 置 `m_Cancelled` 抑制迟到信号）→ 上报 `WebRequestFailure`（`Result = -1`）→ 任务置 `Done`。
- 小文本请求 30s 足够；预期较大的响应请改用 `GF.Download`。

### 3.2 结果约定（判定成功必读）

`WebRequestCompleteEventArgs` 字段：

| 字段 | 含义 |
|------|------|
| `Url` | 请求地址 |
| `Result` | Godot `HttpRequest.Result`（`(long)Error.Ok` = 传输成功）；**发起失败**时为 `(long)Error` 错误码；**超时**时为 `-1` |
| `ResponseCode` | HTTP 状态码（如 200）；超时/发起失败为 `0` |
| `Headers` | 响应头（`string[]`，可能为 null） |
| `Body` | 响应体原始字节（`byte[]`，可能为 null） |

| 情形 | Task 返回值 | 事件 |
|------|-------------|------|
| 正常完成（含 4xx/5xx！） | args（看 `ResponseCode` 区分） | ✅ |
| 超时 | args（`Result=-1, ResponseCode=0`） | ✅ |
| `Request()` 发起失败 | args（`Result=错误码`） | ✅ |
| url 为空 | **`null`**（`Task.FromResult<...>(null)`） | ❌ |

> ⚠️ **HTTP 4xx/5xx 也走"完成"路径**（`request_completed` 的 `Result` 反映传输成败，HTTP 状态在 `ResponseCode`），必须自行检查 `ResponseCode == 200`；且 await 结果**可能为 null**。完整成功判定见 §5 的 `IsHttpSuccess`。

### 3.3 双结果通道与池化差异

同一请求的结果同时通过两条通道推送。纯 C# 层 Manager 发 Success/Failure 事件时使用**池化参数**（回调返回即回收）；组件订阅后**复制**成两份：

| 通道 | 实例来源 | 生命周期 |
|------|----------|----------|
| `EventComponent` 全局事件（`WebRequestCompleteEventArgs.EventId`） | `ReferencePool.Acquire`（池化副本） | **回调返回即回收，不可持有**（同 Download 模块约定） |
| `SendRequestAsync` 返回的 Task | `new` 全新实例 | await 方可安全持有、跨帧使用 |

> ⚠️ **复制原则（重要）**：组件必须用 `WebRequestCompleteEventArgs.Create(...)` 新建池化副本发给全局事件，**不能直接转发**纯层 Manager 的事件参数实例——否则事件参数会被 ReferencePool 二次释放（严格检查下抛异常）。成功/失败路径同理。

### 3.4 线程模型

`HttpRequest.RequestCompleted` 信号在 Godot 主线程触发；纯 C# Manager 的事件也在主线程同步分发（经 GameEntry.Update 驱动）。事件回调与 await 续体均在主线程，**无需加锁**，可直接操作 UI/节点。

---

## 4. 组件与 API

场景节点：`Framework/GameFramework.tscn` 中的 `WebRequest` 节点，经 `GF.WebRequest`（`WebRequestComponent`）访问。

Inspector 参数：

| 参数 | 默认 | 说明 |
|------|------|------|
| `m_WebRequestAgentHelperTypeName` | `GodotGameFramework.Web.DefaultWebRequestAgentHelper` | Helper 类型名（按名反射创建） |
| `m_WebRequestAgentHelperCount` | `4` | 并发 agent 数量（= HttpRequest 节点数），超出排队 |

### 4.1 方法总览

```csharp
// 事件驱动（fire-and-forget，结果经全局事件）
GF.WebRequest.SendRequest(url);                    // GET，默认 30s 超时
GF.WebRequest.SendRequest(url, timeout);           // GET，自定义超时（0/负 = 不超时）

// 可 await（推荐；结果同时也会广播事件）
Task<WebRequestCompleteEventArgs> t = GF.WebRequest.SendRequestAsync(url);
Task<WebRequestCompleteEventArgs> t = GF.WebRequest.SendRequestAsync(url, timeout);
Task<WebRequestCompleteEventArgs> t = GF.WebRequest.SendRequestAsync(url, postData, timeout = 30f);
                                                   // POST：postData 为 byte[]，按 UTF-8 编码发送
```

### 4.2 Manager API（纯 C#，供组件/高级调用方）

`IWebRequestManager`（经 `GF.WebRequest` 内部使用，也可直接 `GameFrameworkEntry.GetModule<IWebRequestManager>()` 获取）：

```csharp
int serialId = manager.AddWebRequest(url);                          // GET，无超时
int serialId = manager.AddWebRequest(url, timeout);                 // GET，带超时
int serialId = manager.AddWebRequest(url, postData, timeout);       // POST（postData 为 UTF-8 字符串）
int serialId = manager.AddWebRequest(url, priority, postData, timeout, userData); // 完整参数（priority 供 TaskPool 排序）
bool removed = manager.RemoveWebRequest(serialId);
int removedCount = manager.RemoveAllWebRequests();
TaskInfo info = manager.GetWebRequestInfo(serialId);
manager.WebRequestSuccess += ...;   // 池化 WebRequestSuccessEventArgs
manager.WebRequestFailure += ...;   // 池化 WebRequestFailureEventArgs
```

### 4.3 使用示例

**方式一：await（推荐）**

```csharp
var result = await GF.WebRequest.SendRequestAsync("https://api.example.com/notice");
if (result == null || result.ResponseCode != 200 || result.Result != (long)Error.Ok
    || result.Body == null)
{
    Log.Warning("请求失败: HTTP {0}", result?.ResponseCode);
    return;
}
string json = System.Text.Encoding.UTF8.GetString(result.Body);   // result 为全新实例，可安全持有
```

**方式二：事件驱动**

```csharp
GF.Event.Subscribe(WebRequestCompleteEventArgs.EventId, OnWebRequestComplete);
GF.WebRequest.SendRequest("https://api.example.com/report", timeout: 10f);

private void OnWebRequestComplete(object sender, GameEventArgs e)
{
    var args = (WebRequestCompleteEventArgs)e;
    if (args.Url != m_MyUrl) return;          // 事件是全局的，必须过滤
    // ⚠️ args 是池化对象，回调返回后即回收；Body 等需跨帧使用先拷贝
}
```

**POST**

```csharp
byte[] body = System.Text.Encoding.UTF8.GetBytes("{\"uid\":1}");
var resp = await GF.WebRequest.SendRequestAsync("https://api.example.com/login", body, 15f);
```

---

## 5. 热更版本清单请求实战（ProcedureUpdate）

`TheGame/MainPack/Scripts/Procedure/ProcedureUpdate.cs` 是本模块的主要消费者：热更第 1 步用 WebRequest 拉取远端版本清单（小 JSON），后续大文件才交给 Download。

```csharp
// versionUrl = "{RemoteUrl}/GameFrameworkVersion.dat"
private async Task<PackVersionList> FetchVersionWithRetryAsync(string versionUrl)
{
    for (int attempt = 0; attempt < MaxRetries; attempt++)          // MaxRetries = 3
    {
        if (attempt > 0)   // 指数退避：1.5s → 3s
        {
            float delay = RetryBaseDelaySeconds * (1 << (attempt - 1));
            await Task.Delay(TimeSpan.FromSeconds(delay));
        }
        try
        {
            var result = await GF.WebRequest.SendRequestAsync(versionUrl);
            if (!IsHttpSuccess(result)) continue;                    // 失败 → 重试

            string json = Encoding.UTF8.GetString(result.Body);
            var version = Utility.Json.ToObject<PackVersionList>(json);
            if (version != null) return version;
        }
        catch (Exception ex) { Log.Error("版本文件请求异常: {0}", ex.Message); }
    }
    return null;   // 全部失败 → 上层跳过热更（SkipToNext）
}

// 完整成功判定（可作为通用模板）
private bool IsHttpSuccess(WebRequestCompleteEventArgs result)
{
    if (result == null) return false;                                // url 无效
    if (result.Result == -1 && result.ResponseCode == 0) return false; // 超时
    if (result.ResponseCode != 200 || result.Result != (long)Error.Ok) return false;
    if (result.Body == null || result.Body.Length == 0) return false;
    return true;
}
```

要点：**重试由调用方负责**（模块无内置重试）；清单拿到后，逐包下载走 `GF.Download.DownloadFileAsync`（见 `DownloadSystem.md` §5）。

---

## 6. 注意事项 / FAQ

**Q: 为什么 await 到的结果还要判 null？**
`url` 为空/非法时组件直接返回 `Task.FromResult(null)`（不抛异常、不发事件）。统一用 §5 的 `IsHttpSuccess` 模式判定。

**Q: 收到 404/500 会走失败分支吗？**
不会。只要传输完成就按"完成"分发，`ResponseCode` 才是 HTTP 状态。判成功必须同时检查 `Result == (long)Error.Ok && ResponseCode == 200`。

**Q: 能设置请求头 / PUT / DELETE 吗？**
当前 `DefaultWebRequestAgentHelper` 写死 `customHeaders: null`，方法仅 GET（无 body）与 POST（有 body）。需要时应扩展 Helper 的 `Request` 增加 headers/method 参数（底层 `HttpRequest.Request` 本身支持）。

**Q: 请求排队 / 限流 / 优先级？**
支持。组件创建 N 个 agent（默认 4）注册进 Manager，超出并发的请求进入 `TaskPool` 等待队列，按优先级降序调度（`AddWebRequest` 的 priority 参数）。当前组件公开 API 未暴露 priority（默认 0），高级调用方可直接用 `IWebRequestManager.AddWebRequest(url, priority, ...)`。

**Q: 大文件能用 WebRequest 下吗？**
不要。响应整体驻留内存（`Body` 为完整 `byte[]`），且超时是总时长模型，大文件慢网必超时。大文件落盘走 `GF.Download`（流式 + 断点续传 + 无进度超时），见 `DownloadSystem.md`。

**Q: 事件方式和 await 方式会不会重复处理同一结果？**
会各收到一次（两条通道同时推送）。项目约定：谁发起谁消费——await 发起的请求，事件订阅方应按 `Url`/业务上下文过滤跳过。

**Q: 并发请求有上限吗？**
有。上限 = `m_WebRequestAgentHelperCount`（默认 4 个 HttpRequest 节点），超出排队。高频场景可增大该值或自行控制并发。

---

## 7. 已知边界与后续计划

- [ ] 自定义 Header / 更多 HTTP 方法支持
- [ ] Web 导出平台（浏览器 CORS 限制）验证
- [ ] 场景重载（编辑器 Play/Stop）时 Manager 残留旧 agent 引用已销毁节点的问题（与 Download 模块同源的框架级限制）
