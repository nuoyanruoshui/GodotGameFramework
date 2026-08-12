namespace GameFramework.Resource
{
    internal sealed class LoadAssetTask : TaskBase
    {
        private static int s_Serial = 0;

        public string AssetPath { get; set; }
        public float Duration { get; set; }
        public LoadAssetCallbacks Callbacks { get; set; }

        public LoadAssetTask() { }

        public static LoadAssetTask Create(string assetName, int priority, LoadAssetCallbacks callbacks, object userData = null)
        {
            LoadAssetTask task = ReferencePool.Acquire<LoadAssetTask>();
            task.Initialize(++s_Serial, nameof(LoadAssetTask), priority, userData);
            task.AssetPath = assetName;
            task.Duration = 0;
            task.Callbacks = callbacks;
            return task;
        }

        public override void Clear()
        {
            base.Clear();
            Callbacks = null;
            AssetPath = null;
            Duration = 0;
        }
    }
}
