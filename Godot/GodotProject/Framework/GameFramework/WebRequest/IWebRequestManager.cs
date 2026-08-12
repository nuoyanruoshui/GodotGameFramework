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
    /// Web 请求管理器接口。
    /// </summary>
    public interface IWebRequestManager
    {
        /// <summary>
        /// 获取 Web 请求代理总数量。
        /// </summary>
        int TotalAgentCount
        {
            get;
        }

        /// <summary>
        /// 获取可用 Web 请求代理数量。
        /// </summary>
        int FreeAgentCount
        {
            get;
        }

        /// <summary>
        /// 获取工作中 Web 请求代理数量。
        /// </summary>
        int WorkingAgentCount
        {
            get;
        }

        /// <summary>
        /// 获取等待 Web 请求任务数量。
        /// </summary>
        int WaitingTaskCount
        {
            get;
        }

        /// <summary>
        /// Web 请求成功事件。
        /// </summary>
        event EventHandler<WebRequestSuccessEventArgs> WebRequestSuccess;

        /// <summary>
        /// Web 请求失败事件。
        /// </summary>
        event EventHandler<WebRequestFailureEventArgs> WebRequestFailure;

        /// <summary>
        /// 增加 Web 请求代理辅助器。
        /// </summary>
        /// <param name="webRequestAgentHelper">要增加的 Web 请求代理辅助器。</param>
        void AddWebRequestAgentHelper(IWebRequestAgentHelper webRequestAgentHelper);

        /// <summary>
        /// 根据 Web 请求任务的序列编号获取 Web 请求任务的信息。
        /// </summary>
        /// <param name="serialId">要获取信息的 Web 请求任务的序列编号。</param>
        /// <returns>Web 请求任务的信息。</returns>
        TaskInfo GetWebRequestInfo(int serialId);

        /// <summary>
        /// 增加 Web 请求任务。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        int AddWebRequest(string webRequestUri);

        /// <summary>
        /// 增加 Web 请求任务，指定超时时间。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        int AddWebRequest(string webRequestUri, float timeout);

        /// <summary>
        /// 增加 Web 请求任务，指定 POST 数据与超时时间。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="postData">POST 请求体数据（null 表示 GET）。</param>
        /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        int AddWebRequest(string webRequestUri, string postData, float timeout);

        /// <summary>
        /// 增加 Web 请求任务（完整参数）。
        /// </summary>
        /// <param name="webRequestUri">Web 请求地址。</param>
        /// <param name="priority">Web 请求任务的优先级。</param>
        /// <param name="postData">POST 请求体数据（null 表示 GET）。</param>
        /// <param name="timeout">超时时长，以秒为单位（0 或负数表示不超时）。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>新增 Web 请求任务的序列编号。</returns>
        int AddWebRequest(string webRequestUri, int priority, string postData, float timeout, object userData);

        /// <summary>
        /// 根据 Web 请求任务的序列编号移除 Web 请求任务。
        /// </summary>
        /// <param name="serialId">要移除 Web 请求任务的序列编号。</param>
        /// <returns>是否移除 Web 请求任务成功。</returns>
        bool RemoveWebRequest(int serialId);

        /// <summary>
        /// 移除所有 Web 请求任务。
        /// </summary>
        /// <returns>移除 Web 请求任务的数量。</returns>
        int RemoveAllWebRequests();
    }
}
