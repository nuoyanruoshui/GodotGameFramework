# Web 请求系统 (WebRequest Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/WebRequest/`、`Framework/GodotGameFrameworkCore/WebRequest/`
> 本文档描述 GGF 的 HTTP 短请求通道：架构、SendRequest/SendRequestAsync API、超时模型、双结果通道（事件 + Task）、与 Download 模块的分工及热更版本清单请求实战。

---

## 1. 概述

Web 请求系统提供**小体积 HTTP 通信**能力（GET/POST，结果驻留内存），基于 **Godot `HttpRequest` 节点**实现。

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/WebRequest/` | 原版 GF 的 `WebRequestManager`（TaskPool + 代理 + 事件）移植代码，**当前未接线**（见下） | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/WebRequest/` | `WebRequestComponent`（API + 超时轮询 + 双结果通道）、`WebRequestAgent : HttpRequest`、`WebRequestCompleteEventArgs` | ✅ |

> ⚠️ **与其他模块不同的实现现状**：`WebRequestComponent` **没有**走"组件 → GetModule\<IWebRequestManager\> → 纯 C# Manager"的委托模式，而是每次请求直接 `new WebRequestAgent()`（Godot `HttpRequest` 子类）挂树、信号驱动完成。`GameFramework/WebRequest/` 下的管理器/任务池代码为完整移植但处于休眠状态。文档以实际生效的组件实现为准。

**项目内下载分工**（唯一通道原则，与 `DownloadSystem.md` 对应）：

| 场景 | 通道 |
|------|------|
| 小体积文本/JSON（版本清单、接口调用），结果在内存中处理 | **`GF.WebRequest`（本模块）** |
| 大文件落盘（热更 .pck、补丁等），需断点续传/校验 | `GF.Download` |

### 能力清单

- ✅ GET / POST（POST 体为 UTF-8 字节）
- ✅ 事件驱动（fire-and-forget）与 `async/await`（TCS 模式）两种消费方式，**同一请求两条通道同时推送**
- ✅ 每请求独立超时（默认 30s，可传 0/负数关闭），组件每帧轮询检测
- ✅ 超时自动 `CancelRequest` + 统一失败约定（`Result = -1, ResponseCode = 0`）
- ✅ 每请求独立 `HttpRequest` 节点，完成/超时/发起失败后自动 `QueueFree`，无并发上限限制
- ❌ 无内置重试（调用方自行实现，参考 §5）、暂不支持自定义 Header / 其他 HTTP 方法

---

## 2. 架构与数据流

```
调用方（ProcedureUpdate / 业务代码）
    │  GF.WebRequest.SendRequestAsync(url) / SendRequest(url)
    ▼
WebRequestComponent (Godot 桥接层，场景节点 "WebRequest")
    │  CreateAndSend:
    │    1. new WebRequestAgent() → AddChild()          ← 每请求一个 HttpRequest 节点
    │    2. 订阅 RequestCompleted 信号（completed 标志防重入）
    │    3. 登记 m_PendingRequests[agent] = {Url, Tcs, Timeout, Elapsed}
    │    4. agent.Request(url [, POST body])
    │
    ├── OnUpdate(delta)：仅做超时累计；到点 → CancelRequest + 失败结果 + QueueFree
    │
    └── RequestCompleted 信号（Godot 主线程）
            │
            ├──► EventComponent.Fire(WebRequestCompleteEventArgs.Create(...))   ← 池化实例，全局事件
            └──► tcs.TrySetResult(new WebRequestCompleteEventArgs(...))         ← 全新实例，await 方安全持有
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GodotGameFrameworkCore/WebRequest/WebRequestComponent.cs` | 组件（`GF.WebRequest`）：API、超时轮询、双通道结果分发 |
| `GodotGameFrameworkCore/WebRequest/WebRequestAgent.cs` | `HttpRequest` 子类；`Send(url)` / `Send(url, byte[])` 辅助方法 |
| `GodotGameFrameworkCore/WebRequest/WebRequestCompleteEventArgs.cs` | 结果事件参数（Url/Result/ResponseCode/Headers/Body），池化 `Create` + 普通构造双形态 |
| `GameFramework/WebRequest/IWebRequestManager.cs` 等 8 个文件 | 原版 GF WebRequest 管理器移植（TaskPool/Agent/Start/Success/Failure 事件），**休眠未接线** |

---

## 3. 核心机制

### 3.1 超时模型

- **每请求总时长超时**（区别于 Download 模块的"无进度超时"）：`PendingRequest.Elapsed` 在 `OnUpdate` 中累计，达到 `Timeout` 即判超时。默认 `DefaultTimeout = 30f` 秒；传 `0` 或负数表示**不超时**。
- 超时处理：从追踪字典移除 → `agent.CancelRequest()` 取消底层请求 → 按失败约定分发结果 → `agent.QueueFree()`。
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

> ⚠️ **HTTP 4xx/5xx 也走"完成"路径**，必须自行检查 `ResponseCode == 200`；且 await 结果**可能为 null**。完整成功判定见 §5 的 `IsHttpSuccess`。

### 3.3 双结果通道与池化差异

同一请求的结果同时通过两条通道推送：

| 通道 | 实例来源 | 生命周期 |
|------|----------|----------|
| `EventComponent` 全局事件（`WebRequestCompleteEventArgs.EventId`） | `ReferencePool.Acquire`（池化） | **回调返回即回收，不可持有**（同 Download 模块约定） |
| `SendRequestAsync` 返回的 Task | `new` 全新实例 | await 方可安全持有、跨帧使用 |

即使走 await 方式，事件仍会广播——事件订阅方注意用 `Url` 等字段过滤，避免误处理他人请求。

### 3.4 线程模型

`HttpRequest.RequestCompleted` 信号在 Godot 主线程触发，事件回调与 await 续体均在主线程，**无需加锁**，可直接操作 UI/节点。

---

## 4. 组件与 API

场景节点：`Framework/GameFramework.tscn` 中的 `WebRequest` 节点，经 `GF.WebRequest`（`WebRequestComponent`）访问。无 Inspector 参数（超时按调用传参）。

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

### 4.2 使用示例

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
当前组件写死 `customHeaders: null`，方法仅 GET（无 body）与 POST（有 body）。需要时应扩展 `CreateAndSend` 增加 headers/method 参数（底层 `HttpRequest.Request` 本身支持）。

**Q: 大文件能用 WebRequest 下吗？**
不要。响应整体驻留内存（`Body` 为完整 `byte[]`），且超时是总时长模型，大文件慢网必超时。大文件落盘走 `GF.Download`（流式 + 断点续传 + 无进度超时），见 `DownloadSystem.md`。

**Q: 事件方式和 await 方式会不会重复处理同一结果？**
会各收到一次（两条通道同时推送）。项目约定：谁发起谁消费——await 发起的请求，事件订阅方应按 `Url`/业务上下文过滤跳过。

**Q: `GameFramework/WebRequest/` 那套 Manager 还会启用吗？**
是完整移植的原版实现（任务池、优先级、Start/Success/Failure 事件），但组件未接线。若未来需要"请求排队/限流/优先级"，可将组件改造为委托模式接入；当前每请求独立节点的实现对低频短请求足够。

**Q: 并发请求有上限吗？**
组件层无限制（每请求一个节点）。高频场景注意节点创建/销毁开销与服务器压力，必要时自行排队。

---

## 7. 已知边界与后续计划

- [ ] 自定义 Header / 更多 HTTP 方法支持
- [ ] 纯 C# 层 `WebRequestManager`（排队/优先级）接线或移除，消除双实现歧义
- [ ] Web 导出平台（浏览器 CORS 限制）验证
