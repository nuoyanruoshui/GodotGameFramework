using System;
using GameFramework.WebRequest;
using Godot;

namespace GodotGameFramework.Web
{
    /// <summary>
    /// Web 请求代理辅助器基类。
    /// </summary>
    public abstract partial class WebRequestAgentHelperBase : HttpRequest, IWebRequestAgentHelper
    {
        /// <summary>
        /// Web 请求代理辅助器完成事件。
        /// </summary>
        public abstract event EventHandler<WebRequestAgentHelperCompleteEventArgs> WebRequestAgentHelperComplete;

        /// <summary>
        /// Web 请求代理辅助器错误事件。
        /// </summary>
        public abstract event EventHandler<WebRequestAgentHelperErrorEventArgs> WebRequestAgentHelperError;

        /// <summary>
        /// 通过 Web 请求代理辅助器发起 Web 请求。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="postData">POST 请求体数据（null 表示 GET）。</param>
        /// <param name="userData">用户自定义数据。</param>
        public abstract void Request(string webRequestUri, string postData, object userData);

        /// <summary>
        /// 重置 Web 请求代理辅助器。
        /// </summary>
        public abstract void Reset();
    }
}
