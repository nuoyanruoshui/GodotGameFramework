using Calcatz.EzpzInspector;
using GameFramework;
using GameFramework.Resource;
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GodotGameFramework.Resource
{
    public sealed partial class ResourceComponent : GameFrameworkComponent
    {
        private const int DefaultPriority = 0;

        public static class Parameters
        {
            public static readonly string ResourceLoadHelper = "m_ResourceLoadHelperTypeName";
        }

        [Export]
        private string m_ResourceLoadHelperTypeName = "GameFramework.Resource.DefaultResourceLoadHelper";
        private ResourceLoadHelperBase m_ResourceLoadHelper;

        private EventComponent m_EventComponent;
        private IResourceManager m_ResourceManager;
        [Export]
        private ResourceMode _resourceMode = ResourceMode.Package;
        [Export]
        private UpdateSettingRes _UpdateSettingRes;

        /// <summary>
        /// 获取更新配置（RemoteUrl 等）。
        /// </summary>
        public UpdateSettingRes UpdateSettingRes => _UpdateSettingRes;

        private LoadAssetCallbacks m_LoadAssetCallbacks;

        public ResourceMode ResourceMode
        {
            get => _resourceMode;
            set => _resourceMode = value;
        }
        [UpperDescription("资源加载代理数量")]
        [Export(PropertyHint.Range, "0,20,1")]
        public int AgentCount = 10;

        [UpperDescription("二进制加载代理数量")]
        [Export(PropertyHint.Range, "0,10,1")]
        public int BinaryAgentCount = 2;
        private readonly Dictionary<string, TaskCompletionSource<Godot.Resource>> m_LoadingTasks = new();
        public override void OnInit()
        {
            base.OnInit();
            m_LoadAssetCallbacks = new LoadAssetCallbacks(LoadAssetSuccessCallback, LoadAssetFailureCallback, LoadAssetUpdateCallback);
            m_ResourceManager = GameFrameworkEntry.GetModule<IResourceManager>();
            m_EventComponent = GameEntry.GetComponent<EventComponent>();
            m_ResourceManager.SetResourceMode(_resourceMode);
            m_ResourceManager.SetReadWritePath(ProjectSettings.GlobalizePath("user://"));
            ProcessMode = ProcessModeEnum.Always;

            // 创建资源加载辅助器并注入 Manager（须在创建加载代理之前）
            if (Create(m_ResourceLoadHelperTypeName) is ResourceLoadHelperBase resourceLoadHelper)
            {
                resourceLoadHelper.Name = m_ResourceLoadHelperTypeName;
                m_ResourceLoadHelper = resourceLoadHelper;
                AddChild(resourceLoadHelper);
                m_ResourceManager.SetResourceLoadHelper(resourceLoadHelper);
            }
            else
            {
                Log.Error("[ResourceComponent] Can not create resource load helper '{0}'.", m_ResourceLoadHelperTypeName);
            }

            m_ResourceManager.SetLoadAssetAgentCount(AgentCount);
            m_ResourceManager.SetLoadBinaryAgentCount(BinaryAgentCount);
            Log.Info("[ResourceComponent] Initialized. Mode: {0}, AssetAgents: {1}, BinaryAgents: {2}",
                _resourceMode, AgentCount, BinaryAgentCount);
        }

        public override void OnExitTree()
        {
            // 关闭时补全所有挂起的资源加载任务，避免 await 调用方永久挂起
            foreach (TaskCompletionSource<Godot.Resource> tcs in m_LoadingTasks.Values)
            {
                tcs.TrySetCanceled();
            }
            m_LoadingTasks.Clear();
            base.OnExitTree();
        }

        /// <summary>
        /// 同步加载二进制文件。返回 null 表示文件不存在。
        /// 小文件可用（<1MB），大文件请用 LoadBinaryAsync 避免卡帧。
        /// </summary>
        public byte[] LoadBinary(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!FileAccess.FileExists(path)) return null;
            try
            {
                using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                return file?.GetBuffer((long)file.GetLength());
            }
            catch (Exception ex)
            {
                Log.Warning("[ResourceComponent] LoadBinary failed: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 异步加载二进制文件（线程池 IO + 主线程回调）。
        /// 适合大文件读取场景。
        /// </summary>
        public Task<byte[]> LoadBinaryAsync(string path)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            if (string.IsNullOrEmpty(path))
            {
                tcs.TrySetResult(null);
                return tcs.Task;
            }

            var callbacks = new LoadBinaryCallbacks(
                (binaryAssetName, binaryData, duration, userData) =>
                {
                    tcs.TrySetResult(binaryData);
                },
                (binaryAssetName, status, errorMessage, userData) =>
                {
                    tcs.TrySetException(new Exception(
                        Utility.Text.Format("LoadBinaryAsync failed: {0} {1} {2}", binaryAssetName, status, errorMessage)));
                });

            m_ResourceManager.LoadBinaryAsync(path, callbacks, null);
            return tcs.Task;
        }

        /// <summary>
        /// 同步加载文本文件。返回 null 表示文件不存在。
        /// </summary>
        public string LoadText(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!FileAccess.FileExists(path)) return null;
            try
            {
                using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                return file?.GetAsText();
            }
            catch (Exception ex)
            {
                Log.Warning("[ResourceComponent] LoadText failed: {0}", ex.Message);
                return null;
            }
        }
        /// <summary>
        /// 加载资源。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <returns></returns>
        public T LoadAsset<T>(string path) where T : Godot.Resource
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!Exists(path)) return null;
            try
            {
                return (T)Godot.ResourceLoader.Load(path);
            }
            catch (Exception ex)
            {
                Log.Warning("[ResourceComponent] LoadAsset failed: {0}", ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        public bool Exists(string path)
        {
            return !string.IsNullOrEmpty(path) && (Godot.ResourceLoader.Exists(path) || FileAccess.FileExists(path));
        }
        public Task<Godot.Resource> LoadAssetAsync(string path) => LoadAssetAsync(path, DefaultPriority);
        /// <summary>
        /// 异步加载资源。
        /// </summary>
        public Task<Godot.Resource> LoadAssetAsync(string path, int priority, object userData = null)
        {
            var tcs = new TaskCompletionSource<Godot.Resource>();
            if (string.IsNullOrEmpty(path))
            {
                tcs.TrySetException(new ArgumentNullException(nameof(path)));
                return tcs.Task;
            }

            if (!Godot.ResourceLoader.Exists(path))
            {
                tcs.TrySetException(new InvalidOperationException(
                    Utility.Text.Format("Resource '{0}' does not exist.", path)));
                return tcs.Task;
            }

            m_ResourceManager.LoadAsset(path, priority, m_LoadAssetCallbacks, userData);
            if (!m_LoadingTasks.TryAdd(path, tcs))
            {
                tcs.TrySetException(new InvalidOperationException(
                    Utility.Text.Format("Resource '{0}' is already being loaded.", path)));
            }
            return tcs.Task;
        }

        private void LoadAssetSuccessCallback(string entityAssetName, object entityAsset, float duration, object userData)
        {
            if (m_LoadingTasks.TryGetValue(entityAssetName, out var tcs))
            {
                tcs.TrySetResult((Godot.Resource)entityAsset);
                m_LoadingTasks.Remove(entityAssetName);
            }
        }

        private void LoadAssetFailureCallback(string entityAssetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            if (m_LoadingTasks.TryGetValue(entityAssetName, out var tcs))
            {
                tcs.TrySetException(new Exception(Utility.Text.Format(
                    "LoadAssetFailureCallback: {0} {1} {2}", entityAssetName, status, errorMessage)));
                m_LoadingTasks.Remove(entityAssetName);
            }
        }

        private void LoadAssetUpdateCallback(string entityAssetName, float progress, object userData)
        {

        }

    }
}
