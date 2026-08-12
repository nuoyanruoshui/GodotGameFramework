using GameFramework;
using GameFramework.WebRequest;
using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GodotGameFramework.Web;

/// <summary>
/// Web 请求组件，提供基于 Godot HttpRequest 的 HTTP 通信能力。
/// 遵循组件 → IWebRequestManager → TaskPool&lt;WebRequestTask&gt; → WebRequestAgent → DefaultWebRequestAgentHelper 的三层委托模式。
/// 支持同步 fire-and-forget 和异步 Task 两种调用方式，结果同时通过 EventComponent 事件和 Task 返回。
/// </summary>
public partial class WebRequestComponent : GameFrameworkComponent
{
    /// <summary>
    /// 默认请求超时时间（秒）。
    /// </summary>
    private const float DefaultTimeout = 30f;

    public static class Parameters
    {
        public static readonly string WebRequestAgentHelper = "m_WebRequestAgentHelperTypeName";
    }

    private IWebRequestManager m_WebRequestManager;
    private EventComponent m_EventComponent;

    [Export]
    private string m_WebRequestAgentHelperTypeName = "GodotGameFramework.Web.DefaultWebRequestAgentHelper";

    [Export]
    private int m_WebRequestAgentHelperCount = 4;

    private readonly Dictionary<int, TaskCompletionSource<WebRequestCompleteEventArgs>> m_WebRequestTasks = new();
    private Node m_InstanceRoot;

    public override void OnInit()
    {
        base.OnInit();
        m_WebRequestManager = GameFrameworkEntry.GetModule<IWebRequestManager>();
        m_EventComponent = GameEntry.GetComponent<EventComponent>();

        m_WebRequestManager.WebRequestSuccess += OnWebRequestSuccess;
        m_WebRequestManager.WebRequestFailure += OnWebRequestFailure;

        if (m_InstanceRoot == null)
        {
            m_InstanceRoot = FindChild("WebRequestInstanceRoot");
            if (m_InstanceRoot == null)
            {
                m_InstanceRoot = new Node();
                m_InstanceRoot.Name = "WebRequestInstanceRoot";
                AddChild(m_InstanceRoot);
            }
        }

        for (int i = 0; i < m_WebRequestAgentHelperCount; i++)
        {
            if (Create(m_WebRequestAgentHelperTypeName) is WebRequestAgentHelperBase helper)
            {
                helper.Name = $"{helper.GetType().Name}{i}";
                m_InstanceRoot.AddChild(helper);
                m_WebRequestManager.AddWebRequestAgentHelper(helper);
            }
            else
            {
                Log.Error("[WebRequestComponent] Can not create web request agent helper '{0}'.", m_WebRequestAgentHelperTypeName);
            }
        }
    }

    public override void OnExitTree()
    {
        if (m_WebRequestManager != null)
        {
            m_WebRequestManager.WebRequestSuccess -= OnWebRequestSuccess;
            m_WebRequestManager.WebRequestFailure -= OnWebRequestFailure;
        }

        // 关闭时补全所有挂起的请求任务，避免 await 调用方永久挂起（与"url 无效 → null"约定一致）
        foreach (TaskCompletionSource<WebRequestCompleteEventArgs> tcs in m_WebRequestTasks.Values)
        {
            tcs.TrySetResult(null);
        }
        m_WebRequestTasks.Clear();

        base.OnExitTree();
    }

    // ──────────────────────────────────────
    //  事件驱动 API（fire-and-forget，结果通过 EventComponent 获取）
    // ──────────────────────────────────────

    /// <summary>
    /// 发送一个 GET 请求，结果通过 EventComponent 的 WebRequestCompleteEventArgs 事件获取。
    /// 适合多请求、集中处理的场景。
    /// </summary>
    /// <param name="url">请求地址。</param>
    public void SendRequest(string url)
    {
        if (!ValidateUrl(url))
            return;
        m_WebRequestManager.AddWebRequest(url, DefaultTimeout);
    }

    /// <summary>
    /// 发送一个 GET 请求，指定超时时间。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <param name="timeout">超时时间（秒），0 或负数表示不超时。</param>
    public void SendRequest(string url, float timeout)
    {
        if (!ValidateUrl(url))
            return;
        m_WebRequestManager.AddWebRequest(url, timeout);
    }

    // ──────────────────────────────────────
    //  异步 API（返回 Task，结果也同时通过事件推送）
    // ──────────────────────────────────────

    /// <summary>
    /// 发送一个 GET 请求，异步等待结果。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <returns>包含响应数据的结果。</returns>
    public Task<WebRequestCompleteEventArgs> SendRequestAsync(string url)
    {
        return SendRequestAsync(url, DefaultTimeout);
    }

    /// <summary>
    /// 发送一个 GET 请求，异步等待结果，指定超时时间。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <param name="timeout">超时时间（秒），0 或负数表示不超时。</param>
    /// <returns>包含响应数据的结果。</returns>
    public Task<WebRequestCompleteEventArgs> SendRequestAsync(string url, float timeout)
    {
        if (!ValidateUrl(url))
            return Task.FromResult<WebRequestCompleteEventArgs>(null);

        var tcs = new TaskCompletionSource<WebRequestCompleteEventArgs>();
        int serialId = m_WebRequestManager.AddWebRequest(url, timeout);
        m_WebRequestTasks[serialId] = tcs;
        return tcs.Task;
    }

    /// <summary>
    /// 发送一个 POST 请求，异步等待结果。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <param name="postData">POST 请求体数据。</param>
    /// <param name="timeout">超时时间（秒），0 或负数表示不超时。</param>
    /// <returns>包含响应数据的结果。</returns>
    public Task<WebRequestCompleteEventArgs> SendRequestAsync(string url, byte[] postData, float timeout = DefaultTimeout)
    {
        if (!ValidateUrl(url))
            return Task.FromResult<WebRequestCompleteEventArgs>(null);
        if (postData == null)
        {
            Log.Error("[WebRequestComponent] SendRequestAsync: postData is null.");
            return Task.FromResult<WebRequestCompleteEventArgs>(null);
        }

        var tcs = new TaskCompletionSource<WebRequestCompleteEventArgs>();
        string body = System.Text.Encoding.UTF8.GetString(postData);
        int serialId = m_WebRequestManager.AddWebRequest(url, body, timeout);
        m_WebRequestTasks[serialId] = tcs;
        return tcs.Task;
    }

    // ──────────────────────────────────────
    //  事件处理
    // ──────────────────────────────────────

    private void OnWebRequestSuccess(object sender, WebRequestSuccessEventArgs e)
    {
        // 全局事件：池化副本（EventPool 拥有并回收，此处不 Release）
        m_EventComponent?.Fire(this, WebRequestCompleteEventArgs.Create(e.WebRequestUri, e.Result, e.ResponseCode, e.Headers, e.Body));

        // 异步通道：全新实例，await 方可安全持有
        if (m_WebRequestTasks.Remove(e.SerialId, out TaskCompletionSource<WebRequestCompleteEventArgs> tcs))
        {
            tcs.TrySetResult(new WebRequestCompleteEventArgs(e.WebRequestUri, e.Result, e.ResponseCode, e.Headers, e.Body));
        }

        Log.Info("[WebRequestComponent] Request completed: {0}, response code: {1}", e.WebRequestUri, e.ResponseCode);
    }

    private void OnWebRequestFailure(object sender, WebRequestFailureEventArgs e)
    {
        // 失败约定：Result = 错误码（超时 -1 / 发起失败 Godot Error 码），ResponseCode = 0
        m_EventComponent?.Fire(this, WebRequestCompleteEventArgs.Create(e.WebRequestUri, e.Result, 0, null, null));

        if (m_WebRequestTasks.Remove(e.SerialId, out TaskCompletionSource<WebRequestCompleteEventArgs> tcs))
        {
            tcs.TrySetResult(new WebRequestCompleteEventArgs(e.WebRequestUri, e.Result, 0, null, null));
        }

        Log.Warning("[WebRequestComponent] Request failure: {0}, error message: {1}", e.WebRequestUri, e.ErrorMessage);
    }

    private static bool ValidateUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Log.Error("[WebRequestComponent] SendRequest: url is null or empty.");
            return false;
        }
        return true;
    }
}
