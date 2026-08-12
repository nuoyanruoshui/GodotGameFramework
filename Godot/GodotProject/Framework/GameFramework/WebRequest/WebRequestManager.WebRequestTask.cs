//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.WebRequest
{
    internal sealed partial class WebRequestManager : GameFrameworkModule, IWebRequestManager
    {
        /// <summary>
        /// Web 请求任务。
        /// </summary>
        private sealed class WebRequestTask : TaskBase
        {
            private static int s_Serial = 0;

            private string m_WebRequestUri;
            private string m_PostData;
            private float m_Timeout;

            /// <summary>
            /// 初始化 Web 请求任务的新实例。
            /// </summary>
            public WebRequestTask()
            {
                m_WebRequestUri = null;
                m_PostData = null;
                m_Timeout = 0f;
            }

            /// <summary>
            /// 获取 Web 请求地址。
            /// </summary>
            public string WebRequestUri
            {
                get
                {
                    return m_WebRequestUri;
                }
            }

            /// <summary>
            /// 获取 POST 请求体数据（null 表示 GET）。
            /// </summary>
            public string PostData
            {
                get
                {
                    return m_PostData;
                }
            }

            /// <summary>
            /// 获取超时时长，以秒为单位（0 或负数表示不超时）。
            /// </summary>
            public float Timeout
            {
                get
                {
                    return m_Timeout;
                }
            }

            /// <summary>
            /// 获取 Web 请求任务的描述。
            /// </summary>
            public override string Description
            {
                get
                {
                    return m_WebRequestUri;
                }
            }

            /// <summary>
            /// 创建 Web 请求任务。
            /// </summary>
            /// <param name="webRequestUri">Web 请求地址。</param>
            /// <param name="postData">POST 请求体数据（null 表示 GET）。</param>
            /// <param name="priority">Web 请求任务的优先级。</param>
            /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
            /// <param name="userData">用户自定义数据。</param>
            /// <returns>创建的 Web 请求任务。</returns>
            public static WebRequestTask Create(string webRequestUri, string postData, int priority, float timeout, object userData)
            {
                WebRequestTask webRequestTask = ReferencePool.Acquire<WebRequestTask>();
                webRequestTask.Initialize(++s_Serial, null, priority, userData);
                webRequestTask.m_WebRequestUri = webRequestUri;
                webRequestTask.m_PostData = postData;
                webRequestTask.m_Timeout = timeout > 0f ? timeout : 0f;
                return webRequestTask;
            }

            /// <summary>
            /// 清理 Web 请求任务。
            /// </summary>
            public override void Clear()
            {
                base.Clear();
                m_WebRequestUri = null;
                m_PostData = null;
                m_Timeout = 0f;
            }
        }
    }
}
