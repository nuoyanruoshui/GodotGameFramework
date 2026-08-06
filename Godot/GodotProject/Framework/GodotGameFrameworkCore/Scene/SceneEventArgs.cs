using GameFramework;
using GameFramework.Event;
using Godot;

namespace GodotGameFramework.Scene
{
    /// <summary>加载场景成功事件。</summary>
    public sealed class LoadSceneSuccessEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(LoadSceneSuccessEventArgs).GetHashCode();

        public override int Id => EventId;

        /// <summary>
        /// 获取场景资源名称。
        /// </summary>
        public string SceneAssetName
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取加载持续时间。
        /// </summary>
        public float Duration
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取场景实例（从 PackedScene 实例化后的 Node）。
        /// </summary>
        public object SceneInstance
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


        public static LoadSceneSuccessEventArgs Create(GameFramework.Scene.LoadSceneSuccessEventArgs e)
        {
            LoadSceneSuccessEventArgs args = ReferencePool.Acquire<LoadSceneSuccessEventArgs>();
            args.SceneAssetName = e.SceneAssetName;
            args.Duration = e.Duration;
            args.SceneInstance = e.SceneInstance;
            args.UserData = e.UserData;
            return args;
        }

        public override void Clear()
        {
            SceneAssetName = null;
            Duration = 0f;
            SceneInstance = null;
            UserData = null;
        }
    }

    /// <summary>加载场景失败事件。</summary>
    public sealed class LoadSceneFailureEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(LoadSceneFailureEventArgs).GetHashCode();

        public override int Id => EventId;

        /// <summary>
        /// 获取场景资源名称。
        /// </summary>
        public string SceneAssetName
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取错误信息。
        /// </summary>
        public string ErrorMessage
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

        public static LoadSceneFailureEventArgs Create(GameFramework.Scene.LoadSceneFailureEventArgs e)
        {
            LoadSceneFailureEventArgs args = ReferencePool.Acquire<LoadSceneFailureEventArgs>();
            args.SceneAssetName = e.SceneAssetName;
            args.ErrorMessage = e.ErrorMessage;
            args.UserData = e.UserData;
            return args;
        }

        public override void Clear()
        {
            SceneAssetName = null;
            ErrorMessage = null;
            UserData = null;
        }
    }

    /// <summary>卸载场景成功事件。</summary>
    public sealed class UnloadSceneSuccessEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(UnloadSceneSuccessEventArgs).GetHashCode();

        public override int Id => EventId;

        /// <summary>
        /// 获取场景资源名称。
        /// </summary>
        public string SceneAssetName
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

        public static UnloadSceneSuccessEventArgs Create(GameFramework.Scene.UnloadSceneSuccessEventArgs e)
        {
            UnloadSceneSuccessEventArgs args = ReferencePool.Acquire<UnloadSceneSuccessEventArgs>();
            args.SceneAssetName = e.SceneAssetName;
            args.UserData = e.UserData;
            return args;
        }

        public override void Clear()
        {
            SceneAssetName = null;
            UserData = null;
        }
    }
    /// <summary>
    /// 加载场景更新事件（进度）。
    /// </summary>
    public sealed class LoadSceneUpdateEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(LoadSceneUpdateEventArgs).GetHashCode();

        public override int Id => EventId;

        /// <summary>
        /// 获取场景资源名称。
        /// </summary>
        public string SceneAssetName
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取加载场景进度。
        /// </summary>
        public float Progress
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

        public static LoadSceneUpdateEventArgs Create(GameFramework.Scene.LoadSceneUpdateEventArgs e)
        {
            LoadSceneUpdateEventArgs args = ReferencePool.Acquire<LoadSceneUpdateEventArgs>();
            args.SceneAssetName = e.SceneAssetName;
            args.Progress = e.Progress;
            args.UserData = e.UserData;
            return args;
        }

        public override void Clear()
        {
            SceneAssetName = null;
            Progress = 0f;
            UserData = null;
        }
    }
}
