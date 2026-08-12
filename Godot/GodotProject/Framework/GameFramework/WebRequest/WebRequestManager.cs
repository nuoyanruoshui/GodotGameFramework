//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;

namespace GameFramework.WebRequest
{
    /// <summary>
    /// Web 请求管理器。
    /// </summary>
    internal sealed partial class WebRequestManager : GameFrameworkModule, IWebRequestManager
    {
        private readonly TaskPool<WebRequestTask> m_TaskPool;
        private EventHandler<WebRequestSuccessEventArgs> m_WebRequestSuccessEventHandler;
        private EventHandler<WebRequestFailureEventArgs> m_WebRequestFailureEventHandler;

        /// <summary>
        /// 初始化 Web 请求管理器的新实例。
        /// </summary>
        public WebRequestManager()
        {
            m_TaskPool = new TaskPool<WebRequestTask>();
            m_WebRequestSuccessEventHandler = null;
            m_WebRequestFailureEventHandler = null;
        }

        /// <summary>
        /// 获取 Web 请求代理总数量。
        /// </summary>
        public int TotalAgentCount
        {
            get
            {
                return m_TaskPool.TotalAgentCount;
            }
        }

        /// <summary>
        /// 获取可用 Web 请求代理数量。
        /// </summary>
        public int FreeAgentCount
        {
            get
            {
                return m_TaskPool.FreeAgentCount;
            }
        }

        /// <summary>
        /// 获取工作中 Web 请求代理数量。
        /// </summary>
        public int WorkingAgentCount
        {
            get
            {
                return m_TaskPool.WorkingAgentCount;
            }
        }

        /// <summary>
        /// 获取等待 Web 请求任务数量。
        /// </summary>
        public int WaitingTaskCount
        {
            get
            {
                return m_TaskPool.WaitingTaskCount;
            }
        }

        /// <summary>
        /// Web 请求成功事件。
        /// </summary>
        public event EventHandler<WebRequestSuccessEventArgs> WebRequestSuccess
        {
            add
            {
                m_WebRequestSuccessEventHandler += value;
            }
            remove
            {
                m_WebRequestSuccessEventHandler -= value;
            }
        }

        /// <summary>
        /// Web 请求失败事件。
        /// </summary>
        public event EventHandler<WebRequestFailureEventArgs> WebRequestFailure
        {
            add
            {
                m_WebRequestFailureEventHandler += value;
            }
            remove
            {
                m_WebRequestFailureEventHandler -= value;
            }
        }

        /// <summary>
        /// Web 请求管理器轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_TaskPool.Update(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 关闭并清理 Web 请求管理器。
        /// </summary>
        internal override void Shutdown()
        {
            m_TaskPool.Shutdown();
        }

        /// <summary>
        /// 增加 Web 请求代理辅助器。
        /// </summary>
        /// <param name="webRequestAgentHelper">要增加的 Web 请求代理辅助器。</param>
        public void AddWebRequestAgentHelper(IWebRequestAgentHelper webRequestAgentHelper)
        {
            WebRequestAgent agent = new WebRequestAgent(webRequestAgentHelper);
            agent.WebRequestAgentSuccess += OnWebRequestAgentSuccess;
            agent.WebRequestAgentFailure += OnWebRequestAgentFailure;

            m_TaskPool.AddAgent(agent);
        }

        /// <summary>
        /// 根据 Web 请求任务的序列编号获取 Web 请求任务的信息。
        /// </summary>
        /// <param name="serialId">要获取信息的 Web 请求任务的序列编号。</param>
        /// <returns>Web 请求任务的信息。</returns>
        public TaskInfo GetWebRequestInfo(int serialId)
        {
            return m_TaskPool.GetTaskInfo(serialId);
        }

        /// <summary>
        /// 增加 Web 请求任务。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        public int AddWebRequest(string webRequestUri)
        {
            return AddWebRequest(webRequestUri, TaskBase.DefaultPriority, null, 0f, null);
        }

        /// <summary>
        /// 增加 Web 请求任务，指定超时时间。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        public int AddWebRequest(string webRequestUri, float timeout)
        {
            return AddWebRequest(webRequestUri, TaskBase.DefaultPriority, null, timeout, null);
        }

        /// <summary>
        /// 增加 Web 请求任务，指定 POST 数据与超时时间。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="postData">POST 请求体数据（null 表示 GET）。</param>
        /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        public int AddWebRequest(string webRequestUri, string postData, float timeout)
        {
            return AddWebRequest(webRequestUri, TaskBase.DefaultPriority, postData, timeout, null);
        }

        /// <summary>
        /// 增加 Web 请求任务（完整参数）。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="priority">Web 请求任务的优先级。</param>
        /// <param name="postData">POST 请求体数据（null 表示 GET）。</param>
        /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        public int AddWebRequest(string webRequestUri, int priority, string postData, float timeout, object userData)
        {
            if (string.IsNullOrEmpty(webRequestUri))
            {
                throw new GameFrameworkException("Web request uri is invalid.");
            }

            if (TotalAgentCount <= 0)
            {
                throw new GameFrameworkException("You must add web request agent first.");
            }

            WebRequestTask webRequestTask = WebRequestTask.Create(webRequestUri, postData, priority, timeout, userData);
            m_TaskPool.AddTask(webRequestTask);
            return webRequestTask.SerialId;
        }

        /// <summary>
        /// 根据 Web 请求任务的序列编号移除 Web 请求任务。
        /// </summary>
        /// <param name="serialId">要移除 Web 请求任务的序列编号。</param>
        /// <returns>是否移除 Web 请求任务成功。</returns>
        public bool RemoveWebRequest(int serialId)
        {
            return m_TaskPool.RemoveTask(serialId);
        }

        /// <summary>
        /// 移除所有 Web 请求任务。
        /// </summary>
        /// <returns>移除 Web 请求任务的数量。</returns>
        public int RemoveAllWebRequests()
        {
            return m_TaskPool.RemoveAllTasks();
        }

        private void OnWebRequestAgentSuccess(WebRequestAgent sender, long result, long responseCode, string[] headers, byte[] body)
        {
            if (m_WebRequestSuccessEventHandler != null)
            {
                WebRequestSuccessEventArgs webRequestSuccessEventArgs = WebRequestSuccessEventArgs.Create(sender.Task.SerialId, sender.Task.WebRequestUri, result, responseCode, headers, body, sender.Task.UserData);
                m_WebRequestSuccessEventHandler(this, webRequestSuccessEventArgs);
                ReferencePool.Release(webRequestSuccessEventArgs);
            }
        }

        private void OnWebRequestAgentFailure(WebRequestAgent sender, long result, string errorMessage)
        {
            if (m_WebRequestFailureEventHandler != null)
            {
                WebRequestFailureEventArgs webRequestFailureEventArgs = WebRequestFailureEventArgs.Create(sender.Task.SerialId, sender.Task.WebRequestUri, result, errorMessage, sender.Task.UserData);
                m_WebRequestFailureEventHandler(this, webRequestFailureEventArgs);
                ReferencePool.Release(webRequestFailureEventArgs);
            }
        }
    }
}
