using Godot;

namespace GameFramework.Resource
{
    /// <summary>
    /// 资源加载代理。每个代理对应一个 Godot LoadThreadedRequest 槽位，
    /// 由 TaskPool&lt;LoadAssetTask&gt; 调度，代理数量即并发上限。
    /// </summary>
    internal sealed class LoadAssetAgent : ITaskAgent<LoadAssetTask>
    {
        public LoadAssetTask Task { get; private set; }

        private enum Phase { Idle, Loading, Delivering }

        private Phase m_Phase = Phase.Idle;
        private string m_AssetPath;
        private float m_Duration;
        private readonly Godot.Collections.Array m_ProgressArray = new();

        public void Initialize()
        {
        }

        public void Shutdown()
        {
            Reset();
        }

        public void Reset()
        {
            m_Phase = Phase.Idle;
            m_AssetPath = null;
            m_Duration = 0f;
            if (Task != null)
            {
                // 由 TaskPool 负责 Release(Task)，这里仅解除引用
                Task = null;
            }
        }

        public StartTaskStatus Start(LoadAssetTask task)
        {
            Task = task;
            m_AssetPath = task.AssetPath;
            m_Duration = 0f;
            m_Phase = Phase.Loading;

            // 提交到 Godot 后台线程加载，Agent 在 Update 中轮询状态
            ResourceLoader.LoadThreadedRequest(m_AssetPath);
            return StartTaskStatus.CanResume;
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_Phase != Phase.Loading || Task == null)
                return;

            m_Duration += elapseSeconds;
            var state = ResourceLoader.LoadThreadedGetStatus(m_AssetPath);

            switch (state)
            {
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    {
                        var result = ResourceLoader.LoadThreadedGet(m_AssetPath);
                        Task.Duration = m_Duration;
                        Task.Callbacks.LoadAssetSuccessCallback?.Invoke(
                            m_AssetPath, result, m_Duration, Task.UserData);
                        Task.Done = true;
                        m_Phase = Phase.Idle;
                        break;
                    }
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    {
                        Task.Duration = m_Duration;
                        m_ProgressArray.Clear();
                        // 读取真实加载进度（0.0 ~ 1.0）若出现[0] 卡住 / 直接跳 [1] ： (https://github.com/godotengine/godot/issues/65380)
                        ResourceLoader.LoadThreadedGetStatus(m_AssetPath, m_ProgressArray);
                        float progress = m_ProgressArray.Count > 0 ? m_ProgressArray[0].AsSingle() : 0f;
                        Task.Callbacks.LoadAssetUpdateCallback?.Invoke(
                            m_AssetPath, progress, Task.UserData);
                        break;
                    }
                case ResourceLoader.ThreadLoadStatus.Failed:
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                    {
                        Task.Callbacks.LoadAssetFailureCallback?.Invoke(
                            m_AssetPath, LoadResourceStatus.AssetError,
                            Utility.Text.Format("Failed to load '{0}'.", m_AssetPath), Task.UserData);
                        Task.Done = true;
                        m_Phase = Phase.Idle;
                        break;
                    }
            }
        }
    }
}
