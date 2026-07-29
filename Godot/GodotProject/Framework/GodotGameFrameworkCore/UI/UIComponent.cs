using GameFramework;
using GameFramework.ObjectPool;
using GameFramework.Resource;
using GameFramework.UI;
using Godot;
using System;
using System.Collections.Generic;
using GameConfig;
using System.Threading.Tasks;

namespace GodotGameFramework.UI
{
    /// <summary>
    /// 界面组件。
    /// </summary>
    public sealed partial class UIComponent : GameFrameworkComponent
    {
        public static class Parameters
        {
            public static readonly string UIFormHelper = "m_UIFormHelperTypeName";
            public static readonly string UIGroupHelper = "m_UIGroupHelperTypeName";
        }
        private const int DefaultPriority = 0;

        private IUIManager m_UIManager = null;
        private EventComponent m_EventComponent = null;

        private readonly List<IUIForm> m_InternalUIFormResults = new List<IUIForm>();

        [Export]
        private bool m_EnableOpenUIFormSuccessEvent = true;

        [Export]
        private bool m_EnableOpenUIFormFailureEvent = true;

        [Export]
        private bool m_EnableOpenUIFormUpdateEvent = false;

        [Export]
        private bool m_EnableCloseUIFormCompleteEvent = true;

        [Export]
        private float m_InstanceAutoReleaseInterval = 60f;

        [Export]
        private int m_InstanceCapacity = 16;

        [Export]
        private float m_InstanceExpireTime = 60f;

        [Export]
        private int m_InstancePriority = 0;

        [Export]
        private Node m_InstanceRoot = null;

        [Export]
        private string m_UIFormHelperTypeName = "GodotGameFramework.UI.DefaultUIFormHelper";

        private UIFormHelperBase m_CustomUIFormHelper = null;

        [Export]
        private string m_UIGroupHelperTypeName = "GodotGameFramework.UI.DefaultUIGroupHelper";

        [Export]
        public UIGroupRes UIGroupRes;
        /// <summary>
        /// 获取界面组数量。
        /// </summary>
        public int UIGroupCount
        {
            get
            {
                return m_UIManager.UIGroupCount;
            }
        }

        /// <summary>
        /// 获取或设置界面实例对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float InstanceAutoReleaseInterval
        {
            get
            {
                return m_UIManager.InstanceAutoReleaseInterval;
            }
            set
            {
                m_UIManager.InstanceAutoReleaseInterval = m_InstanceAutoReleaseInterval = value;
            }
        }

        /// <summary>
        /// 获取或设置界面实例对象池的容量。
        /// </summary>
        public int InstanceCapacity
        {
            get
            {
                return m_UIManager.InstanceCapacity;
            }
            set
            {
                m_UIManager.InstanceCapacity = m_InstanceCapacity = value;
            }
        }

        /// <summary>
        /// 获取或设置界面实例对象池对象过期秒数。
        /// </summary>
        public float InstanceExpireTime
        {
            get
            {
                return m_UIManager.InstanceExpireTime;
            }
            set
            {
                m_UIManager.InstanceExpireTime = m_InstanceExpireTime = value;
            }
        }

        /// <summary>
        /// 获取或设置界面实例对象池的优先级。
        /// </summary>
        public int InstancePriority
        {
            get
            {
                return m_UIManager.InstancePriority;
            }
            set
            {
                m_UIManager.InstancePriority = m_InstancePriority = value;
            }
        }
        private static readonly Dictionary<int, TaskCompletionSource<IUIForm>> m_UIFormTask = new Dictionary<int, TaskCompletionSource<IUIForm>>();

        public override void OnInit()
        {
            base.OnInit();

            m_UIManager = GameFrameworkEntry.GetModule<IUIManager>();
            if (m_UIManager == null)
            {
                Log.Fatal("UI manager is invalid.");
                return;
            }

            m_EventComponent = GameEntry.GetComponent<EventComponent>();
            if (m_EventComponent == null)
            {
                Log.Fatal("Event component is invalid.");
                return;
            }

            if (m_EnableOpenUIFormSuccessEvent)
            {
                m_UIManager.OpenUIFormSuccess += OnOpenUIFormSuccess;
            }

            m_UIManager.OpenUIFormFailure += OnOpenUIFormFailure;

            if (m_EnableOpenUIFormUpdateEvent)
            {
                m_UIManager.OpenUIFormUpdate += OnOpenUIFormUpdate;
            }

            {
            }

            if (m_EnableCloseUIFormCompleteEvent)
            {
                m_UIManager.CloseUIFormComplete += OnCloseUIFormComplete;
            }

            m_UIManager.SetResourceManager(GameFrameworkEntry.GetModule<IResourceManager>());
            m_UIManager.SetObjectPoolManager(GameFrameworkEntry.GetModule<IObjectPoolManager>());
            m_UIManager.InstanceAutoReleaseInterval = m_InstanceAutoReleaseInterval;
            m_UIManager.InstanceCapacity = m_InstanceCapacity;
            m_UIManager.InstanceExpireTime = m_InstanceExpireTime;
            m_UIManager.InstancePriority = m_InstancePriority;

            UIFormHelperBase uiFormHelper = (UIFormHelperBase)Create(m_UIFormHelperTypeName);
            if (uiFormHelper == null)
            {
                Log.Error("Can not create UI form helper.");
                return;
            }
            uiFormHelper.Name = m_UIFormHelperTypeName;
            m_CustomUIFormHelper = uiFormHelper;
            AddChild(uiFormHelper);
            m_UIManager.SetUIFormHelper(uiFormHelper);

            if (m_InstanceRoot == null)
            {
                m_InstanceRoot = FindChild("InstanceRoot");
                if (m_InstanceRoot == null)
                {
                    m_InstanceRoot = new CanvasLayer();
                    m_InstanceRoot.Name = "InstanceRoot";
                    AddChild(m_InstanceRoot);
                }
            }
        }

        /// <summary>
        /// 是否存在界面组。
        /// </summary>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <returns>是否存在界面组。</returns>
        public bool HasUIGroup(string uiGroupName)
        {
            return m_UIManager.HasUIGroup(uiGroupName);
        }

        /// <summary>
        /// 获取界面组。
        /// </summary>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <returns>要获取的界面组。</returns>
        public IUIGroup GetUIGroup(string uiGroupName)
        {
            return m_UIManager.GetUIGroup(uiGroupName);
        }

        /// <summary>
        /// 获取所有界面组。
        /// </summary>
        /// <returns>所有界面组。</returns>
        public IUIGroup[] GetAllUIGroups()
        {
            return m_UIManager.GetAllUIGroups();
        }

        /// <summary>
        /// 获取所有界面组。
        /// </summary>
        /// <param name="results">所有界面组。</param>
        public void GetAllUIGroups(List<IUIGroup> results)
        {
            m_UIManager.GetAllUIGroups(results);
        }

        /// <summary>
        /// 增加界面组。
        /// </summary>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <returns>是否增加界面组成功。</returns>
        public bool AddUIGroup(string uiGroupName)
        {
            return AddUIGroup(uiGroupName, 0);
        }

        /// <summary>
        /// 增加界面组。
        /// </summary>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="depth">界面组深度。</param>
        /// <returns>是否增加界面组成功。</returns>
        public bool AddUIGroup(string uiGroupName, int depth)
        {
            if (m_UIManager.HasUIGroup(uiGroupName))
            {
                return false;
            }

            UIGroupHelperBase uiGroupHelper = Create(m_UIGroupHelperTypeName) as UIGroupHelperBase;
            if (uiGroupHelper == null)
            {
                Log.Error("Can not create UI group helper.");
                return false;
            }
            uiGroupHelper.Name = Utility.Text.Format("{0}-{1}", m_UIGroupHelperTypeName, uiGroupName);
            m_InstanceRoot.AddChild(uiGroupHelper);
            return m_UIManager.AddUIGroup(uiGroupName, depth, uiGroupHelper);
        }

        /// <summary>
        /// 是否存在界面。
        /// </summary>
        /// <param name="serialId">界面序列编号。</param>
        /// <returns>是否存在界面。</returns>
        public bool HasUIForm(int serialId)
        {
            return m_UIManager.HasUIForm(serialId);
        }

        /// <summary>
        /// 是否存在界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <returns>是否存在界面。</returns>
        public bool HasUIForm(string uiFormAssetName)
        {
            return m_UIManager.HasUIForm(uiFormAssetName);
        }

        /// <summary>
        /// 获取界面。
        /// </summary>
        /// <param name="serialId">界面序列编号。</param>
        /// <returns>要获取的界面。</returns>
        public IUIForm GetUIForm(int serialId)
        {
            return m_UIManager.GetUIForm(serialId);
        }

        /// <summary>
        /// 获取界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <returns>要获取的界面。</returns>
        public IUIForm GetUIForm(string uiFormAssetName)
        {
            return m_UIManager.GetUIForm(uiFormAssetName);
        }

        /// <summary>
        /// 获取界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <returns>要获取的界面。</returns>
        public IUIForm[] GetUIForms(string uiFormAssetName)
        {
            return m_UIManager.GetUIForms(uiFormAssetName);
        }

        /// <summary>
        /// 获取界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="results">要获取的界面。</param>
        public void GetUIForms(string uiFormAssetName, List<IUIForm> results)
        {
            if (results == null)
            {
                Log.Error("Results is invalid.");
                return;
            }

            results.Clear();
            m_UIManager.GetUIForms(uiFormAssetName, m_InternalUIFormResults);
            foreach (IUIForm uiForm in m_InternalUIFormResults)
            {
                results.Add(uiForm);
            }
        }

        /// <summary>
        /// 获取所有已加载的界面。
        /// </summary>
        /// <returns>所有已加载的界面。</returns>
        public IUIForm[] GetAllLoadedUIForms()
        {
            return m_UIManager.GetAllLoadedUIForms(); ;
        }

        /// <summary>
        /// 获取所有已加载的界面。
        /// </summary>
        /// <param name="results">所有已加载的界面。</param>
        public void GetAllLoadedUIForms(List<IUIForm> results)
        {
            if (results == null)
            {
                Log.Error("Results is invalid.");
                return;
            }

            results.Clear();
            m_UIManager.GetAllLoadedUIForms(m_InternalUIFormResults);
            foreach (IUIForm uiForm in m_InternalUIFormResults)
            {
                results.Add(uiForm);
            }
        }

        /// <summary>
        /// 获取所有正在加载界面的序列编号。
        /// </summary>
        /// <returns>所有正在加载界面的序列编号。</returns>
        public int[] GetAllLoadingUIFormSerialIds()
        {
            return m_UIManager.GetAllLoadingUIFormSerialIds();
        }

        /// <summary>
        /// 获取所有正在加载界面的序列编号。
        /// </summary>
        /// <param name="results">所有正在加载界面的序列编号。</param>
        public void GetAllLoadingUIFormSerialIds(List<int> results)
        {
            m_UIManager.GetAllLoadingUIFormSerialIds(results);
        }

        /// <summary>
        /// 是否正在加载界面。
        /// </summary>
        /// <param name="serialId">界面序列编号。</param>
        /// <returns>是否正在加载界面。</returns>
        public bool IsLoadingUIForm(int serialId)
        {
            return m_UIManager.IsLoadingUIForm(serialId);
        }

        /// <summary>
        /// 是否正在加载界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <returns>是否正在加载界面。</returns>
        public bool IsLoadingUIForm(string uiFormAssetName)
        {
            return m_UIManager.IsLoadingUIForm(uiFormAssetName);
        }

        /// <summary>
        /// 是否是合法的界面。
        /// </summary>
        /// <param name="uiForm">界面。</param>
        /// <returns>界面是否合法。</returns>
        public bool IsValidUIForm(IUIForm uiForm)
        {
            return m_UIManager.IsValidUIForm(uiForm);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, DefaultPriority, false, null);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="priority">加载界面资源的优先级。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, int priority)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, priority, false, null);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, bool pauseCoveredUIForm)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, DefaultPriority, pauseCoveredUIForm, null);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, object userData)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, DefaultPriority, false, userData);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="priority">加载界面资源的优先级。</param>
        /// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, priority, pauseCoveredUIForm, null);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="priority">加载界面资源的优先级。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, int priority, object userData)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, priority, false, userData);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, bool pauseCoveredUIForm, object userData)
        {
            return OpenUIForm(uiFormAssetName, uiGroupName, DefaultPriority, pauseCoveredUIForm, userData);
        }

        /// <summary>
        /// 打开界面。
        /// </summary>
        /// <param name="uiFormAssetName">界面资源名称。</param>
        /// <param name="uiGroupName">界面组名称。</param>
        /// <param name="priority">加载界面资源的优先级。</param>
        /// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>界面的序列编号。</returns>
        public int OpenUIForm(string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm, object userData)
        {
            return m_UIManager.OpenUIForm(uiFormAssetName, uiGroupName, priority, pauseCoveredUIForm, userData);
        }

        /// <summary>
        /// 关闭界面。
        /// </summary>
        /// <param name="serialId">要关闭界面的序列编号。</param>
        public void CloseUIForm(int serialId)
        {
            m_UIManager.CloseUIForm(serialId);
        }

        /// <summary>
        /// 关闭界面。
        /// </summary>
        /// <param name="serialId">要关闭界面的序列编号。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void CloseUIForm(int serialId, object userData)
        {
            m_UIManager.CloseUIForm(serialId, userData);
        }

        /// <summary>
        /// 关闭界面。
        /// </summary>
        /// <param name="uiForm">要关闭的界面。</param>
        public void CloseUIForm(IUIForm uiForm)
        {
            m_UIManager.CloseUIForm(uiForm);
        }

        /// <summary>
        /// 关闭界面。
        /// </summary>
        /// <param name="uiForm">要关闭的界面。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void CloseUIForm(IUIForm uiForm, object userData)
        {
            m_UIManager.CloseUIForm(uiForm, userData);
        }

        /// <summary>
        /// 关闭所有已加载的界面。
        /// </summary>
        public void CloseAllLoadedUIForms()
        {
            m_UIManager.CloseAllLoadedUIForms();
        }

        /// <summary>
        /// 关闭所有已加载的界面。
        /// </summary>
        /// <param name="userData">用户自定义数据。</param>
        public void CloseAllLoadedUIForms(object userData)
        {
            m_UIManager.CloseAllLoadedUIForms(userData);
        }

        /// <summary>
        /// 关闭所有正在加载的界面。
        /// </summary>
        public void CloseAllLoadingUIForms()
        {
            m_UIManager.CloseAllLoadingUIForms();
        }

        /// <summary>
        /// 激活界面。
        /// </summary>
        /// <param name="uiForm">要激活的界面。</param>
        public void RefocusUIForm(IUIForm uiForm)
        {
            m_UIManager.RefocusUIForm(uiForm);
        }
        /// <summary>
        /// 激活界面。
        /// </summary>
        /// <param name="serialId">要激活的界面。</param>
        public void RefocusUIForm(int serialId)
        {
            m_UIManager.RefocusUIForm(serialId);
        }

        /// <summary>
        /// 激活界面。
        /// </summary>
        /// <param name="uiForm">要激活的界面。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void RefocusUIForm(IUIForm uiForm, object userData)
        {
            m_UIManager.RefocusUIForm(uiForm, userData);
        }

        /// <summary>
        /// 设置界面是否被加锁。
        /// </summary>
        /// <param name="uiForm">要设置是否被加锁的界面。</param>
        /// <param name="locked">界面是否被加锁。</param>
        public void SetUIFormInstanceLocked(IUIForm uiForm, bool locked)
        {
            if (uiForm == null)
            {
                Log.Warning("UI form is invalid.");
                return;
            }

            m_UIManager.SetUIFormInstanceLocked(uiForm.Handle, locked);
        }

        /// <summary>
        /// 设置界面的优先级。
        /// </summary>
        /// <param name="uiForm">要设置优先级的界面。</param>
        /// <param name="priority">界面优先级。</param>
        public void SetUIFormInstancePriority(IUIForm uiForm, int priority)
        {
            if (uiForm == null)
            {
                Log.Warning("UI form is invalid.");
                return;
            }

            m_UIManager.SetUIFormInstancePriority(uiForm.Handle, priority);
        }



        private void OnOpenUIFormSuccess(object sender, GameFramework.UI.OpenUIFormSuccessEventArgs e)
        {
            m_EventComponent.Fire(this, OpenUIFormSuccessEventArgs.Create(e));
            m_UIFormTask.TryGetValue(e.UIForm.SerialId, out TaskCompletionSource<IUIForm> tcs);
            if (tcs != null)
            {
                tcs.TrySetResult(e.UIForm);
                m_UIFormTask.Remove(e.UIForm.SerialId);
            }
        }

        private void OnOpenUIFormFailure(object sender, GameFramework.UI.OpenUIFormFailureEventArgs e)
        {
            Log.Warning("Open UI form failure, asset name '{0}', UI group name '{1}', pause covered UI form '{2}', error message '{3}'.", e.UIFormAssetName, e.UIGroupName, e.PauseCoveredUIForm, e.ErrorMessage);
            if (m_EnableOpenUIFormFailureEvent)
            {
                m_EventComponent.Fire(this, OpenUIFormFailureEventArgs.Create(e));
                m_UIFormTask.TryGetValue(e.SerialId, out TaskCompletionSource<IUIForm> tcs);
                if (tcs != null)
                {
                    Log.Fatal(e.ErrorMessage);
                    tcs.TrySetException(new GameFrameworkException(e.ErrorMessage));
                    m_UIFormTask.Remove(e.SerialId);
                }
            }
        }

        private void OnOpenUIFormUpdate(object sender, GameFramework.UI.OpenUIFormUpdateEventArgs e)
        {
            m_EventComponent.Fire(this, OpenUIFormUpdateEventArgs.Create(e));
        }


        private void OnCloseUIFormComplete(object sender, GameFramework.UI.CloseUIFormCompleteEventArgs e)
        {
            m_EventComponent.Fire(this, CloseUIFormCompleteEventArgs.Create(e));
        }


        public Task<IUIForm> OpenUIFormAsync(string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm, object userData = null)
        {
            return OpenUIFormAsyncInternal(uiFormAssetName, uiGroupName, priority, pauseCoveredUIForm, userData);
        }
        public Task<IUIForm> OpenUIFormAsync(string uiFormAssetName, string uiGroupName, bool pauseCoveredUIForm, object userData = null)
        {
            return OpenUIFormAsyncInternal(uiFormAssetName, uiGroupName, DefaultPriority, pauseCoveredUIForm, userData);
        }
        public Task<IUIForm> OpenUIFormAsync(string uiFormAssetName, string uiGroupName, object userData = null)
        {
            return OpenUIFormAsyncInternal(uiFormAssetName, uiGroupName, DefaultPriority, false, userData);
        }
        private Task<IUIForm> OpenUIFormAsyncInternal(string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm, object userData)
        {
            if (string.IsNullOrEmpty(uiFormAssetName))
            {
                Log.Warning("UI form asset name is invalid.");
                return Task.FromResult<IUIForm>(null);
            }
            if (string.IsNullOrEmpty(uiGroupName))
            {
                Log.Warning("UI group name is invalid.");
                return Task.FromResult<IUIForm>(null);
            }

            var task = new TaskCompletionSource<IUIForm>();
            int serialId = m_UIManager.OpenUIForm(uiFormAssetName, uiGroupName, priority, pauseCoveredUIForm, userData);
            // 检查是否已同步完成（缓存命中 → InternalOpenUIForm 已同步执行）
            IUIForm uiForm = m_UIManager.GetUIForm(serialId);
            if (uiForm != null)
            {
                task.TrySetResult(uiForm);
            }
            else
            {
                m_UIFormTask.Add(serialId, task);
            }
            return task.Task;
        }
    }
}
