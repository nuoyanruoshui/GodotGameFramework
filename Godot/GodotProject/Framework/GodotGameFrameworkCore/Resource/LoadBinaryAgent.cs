using System;

namespace GameFramework.Resource
{
    /// <summary>
    /// 二进制加载代理。后台线程读取由 IResourceLoadHelper 负责。
    /// </summary>
    internal sealed class LoadBinaryAgent : ITaskAgent<LoadBinaryTask>
    {
        public LoadBinaryTask Task { get; private set; }
        private readonly IResourceLoadHelper m_Helper;
        private string m_LoadingPath;
        private byte[] m_ResultData;
        private string m_Error;

        public LoadBinaryAgent(IResourceLoadHelper resourceLoadHelper)
        {
            m_Helper = resourceLoadHelper;
        }

        public void Initialize() { }
        public void Shutdown() { }

        public void Reset()
        {
            m_LoadingPath = null;
            m_ResultData = null;
            m_Error = null;
            // 由 TaskPool 负责 Release(Task)，这里仅解除引用
            Task = null;
        }

        public StartTaskStatus Start(LoadBinaryTask task)
        {
            Task = task;
            m_LoadingPath = task.Path;
            m_ResultData = null;
            m_Error = null;

            if (m_Helper == null)
            {
                m_Error = "Resource load helper is invalid.";
                return StartTaskStatus.CanResume; // Update 会投递该错误
            }

            // 后台线程读取由 helper 负责
            m_Helper.LoadBinaryAsync(m_LoadingPath,
                data => m_ResultData = data,
                error => m_Error = error);

            return StartTaskStatus.CanResume;
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_LoadingPath == null || Task == null) return;
            if (m_ResultData == null && m_Error == null) return; // 后台线程未完成

            if (m_Error != null)
            {
                Task.Callbacks.LoadBinaryFailureCallback?.Invoke(
                    Task.Path, LoadResourceStatus.AssetError, m_Error, Task.UserData);
            }
            else
            {
                Task.Callbacks.LoadBinarySuccessCallback?.Invoke(
                    Task.Path, m_ResultData, 0f, Task.UserData);
            }

            Task.Done = true;
            m_LoadingPath = null;
        }
    }
}
