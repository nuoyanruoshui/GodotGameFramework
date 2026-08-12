namespace GameFramework.Resource
{
    /// <summary>
    /// 资源加载代理。每个代理对应一个后台加载槽位，
    /// 由 TaskPool&lt;LoadAssetTask&gt; 调度，代理数量即并发上限。
    /// 加载操作委托给 IResourceLoadHelper。
    /// </summary>
    internal sealed class LoadAssetAgent : ITaskAgent<LoadAssetTask>
    {
        public LoadAssetTask Task { get; private set; }

        private enum Phase { Idle, Loading, Delivering }

        private readonly IResourceLoadHelper m_Helper;
        private Phase m_Phase = Phase.Idle;
        private string m_AssetPath;
        private float m_Duration;
        private readonly Godot.Collections.Array m_ProgressArray = new();

        public LoadAssetAgent(IResourceLoadHelper resourceLoadHelper)
        {
            m_Helper = resourceLoadHelper;
        }

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

            if (m_Helper == null)
            {
                Task.Callbacks.LoadAssetFailureCallback?.Invoke(
                    m_AssetPath, LoadResourceStatus.AssetError, "Resource load helper is invalid.", Task.UserData);
                Task.Done = true;
                m_Phase = Phase.Idle;
                return StartTaskStatus.Done;
            }

            // 提交到辅助器后台加载，Agent 在 Update 中轮询状态
            m_Helper.LoadAssetAsync(m_AssetPath);
            return StartTaskStatus.CanResume;
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_Phase != Phase.Loading || Task == null)
                return;

            m_Duration += elapseSeconds;
            // 单次查询同时取状态与进度，避免重复查询
            m_ProgressArray.Clear();
            var state = m_Helper.GetLoadStatus(m_AssetPath, m_ProgressArray);

            switch (state)
            {
                case LoadResourceStatus.Success:
                    {
                        var result = m_Helper.GetAsset(m_AssetPath);
                        Task.Duration = m_Duration;
                        Task.Callbacks.LoadAssetSuccessCallback?.Invoke(
                            m_AssetPath, result, m_Duration, Task.UserData);
                        Task.Done = true;
                        m_Phase = Phase.Idle;
                        break;
                    }
                case LoadResourceStatus.InProgress:
                    {
                        Task.Duration = m_Duration;
                        // 读取真实加载进度（0.0 ~ 1.0）若出现[0] 卡住 / 直接跳 [1] ： (https://github.com/godotengine/godot/issues/65380)
                        float progress = m_ProgressArray.Count > 0 ? m_ProgressArray[0].AsSingle() : 0f;
                        Task.Callbacks.LoadAssetUpdateCallback?.Invoke(
                            m_AssetPath, progress, Task.UserData);
                        break;
                    }
                case LoadResourceStatus.AssetError:
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
