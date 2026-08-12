namespace GameFramework.Resource
{
    internal sealed class LoadBinaryTask : TaskBase
    {
        private static int s_Serial = 0;

        private string m_Path;
        private LoadBinaryCallbacks m_Callbacks;
        private object m_UserData;

        public string Path => m_Path;
        public LoadBinaryCallbacks Callbacks => m_Callbacks;
        public new object UserData => m_UserData;

        public LoadBinaryTask() { }

        public static LoadBinaryTask Create(string path,
            LoadBinaryCallbacks callbacks, object userData)
        {
            LoadBinaryTask task = ReferencePool.Acquire<LoadBinaryTask>();
            task.Initialize(++s_Serial, nameof(LoadBinaryTask), 0, userData);
            task.m_Path = path;
            task.m_Callbacks = callbacks;
            task.m_UserData = userData;
            return task;
        }

        public override void Clear()
        {
            base.Clear();
            m_Path = null;
            m_Callbacks = null;
            m_UserData = null;
        }
    }
}
