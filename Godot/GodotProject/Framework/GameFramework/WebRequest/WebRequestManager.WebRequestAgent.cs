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
        /// Web 请求代理。
        /// </summary>
        private sealed class WebRequestAgent : ITaskAgent<WebRequestTask>
        {
            private readonly IWebRequestAgentHelper m_Helper;
            private WebRequestTask m_Task;
            private float m_WaitTime;

            /// <summary>
            /// Web 请求代理成功委托。
            /// </summary>
            public GameFrameworkAction<WebRequestAgent, long, long, string[], byte[]> WebRequestAgentSuccess;

            /// <summary>
            /// Web 请求代理失败委托。
            /// </summary>
            public GameFrameworkAction<WebRequestAgent, long, string> WebRequestAgentFailure;

            /// <summary>
            /// 初始化 Web 请求代理的新实例。
            /// </summary>
            /// <param name="webRequestAgentHelper">Web 请求代理辅助器。</param>
            public WebRequestAgent(IWebRequestAgentHelper webRequestAgentHelper)
            {
                if (webRequestAgentHelper == null)
                {
                    throw new GameFrameworkException("Web request agent helper is invalid.");
                }

                m_Helper = webRequestAgentHelper;
                m_Task = null;
                m_WaitTime = 0f;

                WebRequestAgentSuccess = null;
                WebRequestAgentFailure = null;
            }

            /// <summary>
            /// 获取 Web 请求任务。
            /// </summary>
            public WebRequestTask Task
            {
                get
                {
                    return m_Task;
                }
            }

            /// <summary>
            /// 初始化 Web 请求代理。
            /// </summary>
            public void Initialize()
            {
                m_Helper.WebRequestAgentHelperComplete += OnWebRequestAgentHelperComplete;
                m_Helper.WebRequestAgentHelperError += OnWebRequestAgentHelperError;
            }

            /// <summary>
            /// Web 请求代理轮询，负责超时检测。
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                if (m_Task == null || m_Task.Done || m_Task.Timeout <= 0f)
                {
                    return;
                }

                m_WaitTime += realElapseSeconds;
                if (m_WaitTime >= m_Task.Timeout)
                {
                    // 超时：取消底层请求并上报失败（Result = -1 约定）
                    m_Helper.Reset();
                    if (WebRequestAgentFailure != null)
                    {
                        WebRequestAgentFailure(this, -1L, "Web request timeout.");
                    }
                    m_Task.Done = true;
                }
            }

            /// <summary>
            /// 关闭并清理 Web 请求代理。
            /// </summary>
            public void Shutdown()
            {
                Reset();
                m_Helper.WebRequestAgentHelperComplete -= OnWebRequestAgentHelperComplete;
                m_Helper.WebRequestAgentHelperError -= OnWebRequestAgentHelperError;
            }

            /// <summary>
            /// 开始处理 Web 请求任务。
            /// </summary>
            /// <param name="task">要处理的 Web 请求任务。</param>
            /// <returns>开始处理任务的状态。</returns>
            public StartTaskStatus Start(WebRequestTask task)
            {
                if (task == null)
                {
                    throw new GameFrameworkException("Task is invalid.");
                }

                m_Task = task;
                m_WaitTime = 0f;
                m_Helper.Request(m_Task.WebRequestUri, m_Task.PostData, m_Task.UserData);
                return StartTaskStatus.CanResume;
            }

            /// <summary>
            /// 重置 Web 请求代理。
            /// </summary>
            public void Reset()
            {
                m_Helper.Reset();
                m_Task = null;
                m_WaitTime = 0f;
            }

            private void OnWebRequestAgentHelperComplete(object sender, WebRequestAgentHelperCompleteEventArgs e)
            {
                if (m_Task == null || m_Task.Done)
                {
                    return;
                }

                if (WebRequestAgentSuccess != null)
                {
                    WebRequestAgentSuccess(this, e.Result, e.ResponseCode, e.Headers, e.Body);
                }

                m_Task.Done = true;
            }

            private void OnWebRequestAgentHelperError(object sender, WebRequestAgentHelperErrorEventArgs e)
            {
                if (m_Task == null || m_Task.Done)
                {
                    return;
                }

                if (WebRequestAgentFailure != null)
                {
                    WebRequestAgentFailure(this, e.Result, e.ErrorMessage);
                }

                m_Task.Done = true;
            }
        }
    }
}
