using System;
using GameFramework;
using GameFramework.WebRequest;
using Godot;

namespace GodotGameFramework.Web
{
    /// <summary>
    /// 默认 Web 请求代理辅助器，基于 Godot HttpRequest 节点。
    /// 每个实例可复用于顺序请求；Reset 取消在途请求并抑制迟到的 RequestCompleted 信号。
    /// </summary>
    public partial class DefaultWebRequestAgentHelper : WebRequestAgentHelperBase
    {
        private EventHandler<WebRequestAgentHelperCompleteEventArgs> m_WebRequestAgentHelperCompleteEventHandler;
        private EventHandler<WebRequestAgentHelperErrorEventArgs> m_WebRequestAgentHelperErrorEventHandler;

        private bool m_Cancelled;
        private string m_CurrentUri;

        /// <summary>
        /// 初始化默认 Web 请求代理辅助器的新实例。
        /// </summary>
        public DefaultWebRequestAgentHelper()
        {
            RequestCompleted += OnRequestCompleted;
        }

        public override event EventHandler<WebRequestAgentHelperCompleteEventArgs> WebRequestAgentHelperComplete
        {
            add
            {
                m_WebRequestAgentHelperCompleteEventHandler += value;
            }
            remove
            {
                m_WebRequestAgentHelperCompleteEventHandler -= value;
            }
        }

        public override event EventHandler<WebRequestAgentHelperErrorEventArgs> WebRequestAgentHelperError
        {
            add
            {
                m_WebRequestAgentHelperErrorEventHandler += value;
            }
            remove
            {
                m_WebRequestAgentHelperErrorEventHandler -= value;
            }
        }

        public override void Request(string webRequestUri, string postData, object userData)
        {
            m_Cancelled = false;
            m_CurrentUri = webRequestUri;

            Error err = postData != null
                ? Request(webRequestUri, null, HttpClient.Method.Post, postData)
                : Request(webRequestUri);

            if (err != Error.Ok)
            {
                var e = WebRequestAgentHelperErrorEventArgs.Create(webRequestUri, (long)err, GameFramework.Utility.Text.Format("Request failed: {0}", err));
                if (m_WebRequestAgentHelperErrorEventHandler != null)
                {
                    m_WebRequestAgentHelperErrorEventHandler(this, e);
                }
                ReferencePool.Release(e);
            }
        }

        private void OnRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
        {
            if (m_Cancelled)
            {
                return;
            }

            var e = WebRequestAgentHelperCompleteEventArgs.Create(m_CurrentUri, result, responseCode, headers, body);
            if (m_WebRequestAgentHelperCompleteEventHandler != null)
            {
                m_WebRequestAgentHelperCompleteEventHandler(this, e);
            }
            ReferencePool.Release(e);
        }

        public override void Reset()
        {
            m_Cancelled = true;
            CancelRequest();
        }
    }
}
