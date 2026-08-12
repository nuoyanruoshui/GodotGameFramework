//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.WebRequest
{
    /// <summary>
    /// Web 请求代理辅助器完成事件。
    /// </summary>
    public sealed class WebRequestAgentHelperCompleteEventArgs : GameFrameworkEventArgs
    {
        /// <summary>
        /// 初始化 Web 请求代理辅助器完成事件的新实例。
        /// </summary>
        public WebRequestAgentHelperCompleteEventArgs()
        {
            WebRequestUri = null;
            Result = 0L;
            ResponseCode = 0L;
            Headers = null;
            Body = null;
        }

        /// <summary>
        /// 获取 Web 请求地址。
        /// </summary>
        public string WebRequestUri
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取 Web 请求结果（Godot HttpRequest.Result）。
        /// </summary>
        public long Result
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取 HTTP 状态码。
        /// </summary>
        public long ResponseCode
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取响应头。
        /// </summary>
        public string[] Headers
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取响应体字节。
        /// </summary>
        public byte[] Body
        {
            get;
            private set;
        }

        /// <summary>
        /// 创建 Web 请求代理辅助器完成事件。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="result">Web 请求结果。</param>
        /// <param name="responseCode">HTTP 状态码。</param>
        /// <param name="headers">响应头。</param>
        /// <param name="body">响应体字节。</param>
        /// <returns>创建的 Web 请求代理辅助器完成事件。</returns>
        public static WebRequestAgentHelperCompleteEventArgs Create(string webRequestUri, long result, long responseCode, string[] headers, byte[] body)
        {
            WebRequestAgentHelperCompleteEventArgs webRequestAgentHelperCompleteEventArgs = ReferencePool.Acquire<WebRequestAgentHelperCompleteEventArgs>();
            webRequestAgentHelperCompleteEventArgs.WebRequestUri = webRequestUri;
            webRequestAgentHelperCompleteEventArgs.Result = result;
            webRequestAgentHelperCompleteEventArgs.ResponseCode = responseCode;
            webRequestAgentHelperCompleteEventArgs.Headers = headers;
            webRequestAgentHelperCompleteEventArgs.Body = body;
            return webRequestAgentHelperCompleteEventArgs;
        }

        /// <summary>
        /// 清理 Web 请求代理辅助器完成事件。
        /// </summary>
        public override void Clear()
        {
            WebRequestUri = null;
            Result = 0L;
            ResponseCode = 0L;
            Headers = null;
            Body = null;
        }
    }
}
