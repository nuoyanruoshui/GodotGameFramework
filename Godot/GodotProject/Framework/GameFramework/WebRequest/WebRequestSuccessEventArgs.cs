//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.WebRequest
{
    /// <summary>
    /// Web 请求成功事件。
    /// </summary>
    public sealed class WebRequestSuccessEventArgs : GameFrameworkEventArgs
    {
        /// <summary>
        /// 初始化 Web 请求成功事件的新实例。
        /// </summary>
        public WebRequestSuccessEventArgs()
        {
            SerialId = 0;
            WebRequestUri = null;
            Result = 0L;
            ResponseCode = 0L;
            Headers = null;
            Body = null;
            UserData = null;
        }

        /// <summary>
        /// 获取 Web 请求任务的序列编号。
        /// </summary>
        public int SerialId
        {
            get;
            private set;
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
        /// 获取用户自定义数据。
        /// </summary>
        public object UserData
        {
            get;
            private set;
        }

        /// <summary>
        /// 创建 Web 请求成功事件。
        /// </summary>
        /// <param name="serialId">Web 请求任务的序列编号。</param>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="result">Web 请求结果。</param>
        /// <param name="responseCode">HTTP 状态码。</param>
        /// <param name="headers">响应头。</param>
        /// <param name="body">响应体字节。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>创建的 Web 请求成功事件。</returns>
        public static WebRequestSuccessEventArgs Create(int serialId, string webRequestUri, long result, long responseCode, string[] headers, byte[] body, object userData)
        {
            WebRequestSuccessEventArgs webRequestSuccessEventArgs = ReferencePool.Acquire<WebRequestSuccessEventArgs>();
            webRequestSuccessEventArgs.SerialId = serialId;
            webRequestSuccessEventArgs.WebRequestUri = webRequestUri;
            webRequestSuccessEventArgs.Result = result;
            webRequestSuccessEventArgs.ResponseCode = responseCode;
            webRequestSuccessEventArgs.Headers = headers;
            webRequestSuccessEventArgs.Body = body;
            webRequestSuccessEventArgs.UserData = userData;
            return webRequestSuccessEventArgs;
        }

        /// <summary>
        /// 清理 Web 请求成功事件。
        /// </summary>
        public override void Clear()
        {
            SerialId = 0;
            WebRequestUri = null;
            Result = 0L;
            ResponseCode = 0L;
            Headers = null;
            Body = null;
            UserData = null;
        }
    }
}
