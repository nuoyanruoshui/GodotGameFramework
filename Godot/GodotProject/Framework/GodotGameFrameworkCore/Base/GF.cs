using GameConfig;
using GodotGameFramework.Archive;
using GodotGameFramework.Debugger;
using GodotGameFramework.Download;
using GodotGameFramework.Entity;
using GodotGameFramework.Localization;
using GodotGameFramework.Resource;
using GodotGameFramework.Scene;
using GodotGameFramework.Setting;
using GodotGameFramework.Sound;
using GodotGameFramework.UI;
using GodotGameFramework.Web;


namespace GodotGameFramework
{
    public static class GF
    {
        private static EventComponent m_Event;
        private static FsmComponent m_Fsm;
        private static ProcedureComponent m_Procedure;
        private static ObjectPoolComponent m_ObjectPool;
        private static DataNodeComponent m_DataNode;
        private static ResourceComponent m_Resource;
        private static EntityComponent m_Entity;
        private static UIComponent m_UI;
        private static SoundComponent m_Sound;
        private static LocalizationComponent m_Localization;
        private static SettingComponent m_Setting;
        private static BaseComponent m_Base;
        private static SceneComponent m_Scene;
        private static WebRequestComponent m_WebRequest;
        private static DownloadComponent m_Download;
        private static DebuggerComponent m_Debugger;
        private static ArchiveSystem<GameCatalogue, GameData> m_Archive;
        public static EventComponent Event
        {
            get
            {
                if (m_Event == null)
                {
                    m_Event = GameEntry.GetComponent<EventComponent>();
                }
                return m_Event;
            }
        }
        public static FsmComponent Fsm
        {
            get
            {
                if (m_Fsm == null)
                {
                    m_Fsm = GameEntry.GetComponent<FsmComponent>();
                }
                return m_Fsm;
            }
        }
        public static ProcedureComponent Procedure
        {
            get
            {
                if (m_Procedure == null)
                {
                    m_Procedure = GameEntry.GetComponent<ProcedureComponent>();
                }
                return m_Procedure;
            }
        }
        public static ObjectPoolComponent ObjectPool
        {
            get
            {
                if (m_ObjectPool == null)
                {
                    m_ObjectPool = GameEntry.GetComponent<ObjectPoolComponent>();
                }
                return m_ObjectPool;
            }
        }
        public static DataNodeComponent DataNode
        {
            get
            {
                if (m_DataNode == null)
                {
                    m_DataNode = GameEntry.GetComponent<DataNodeComponent>();
                }
                return m_DataNode;
            }
        }
        public static ResourceComponent Resource
        {
            get
            {
                if (m_Resource == null)
                {
                    m_Resource = GameEntry.GetComponent<ResourceComponent>();
                }
                return m_Resource;
            }
        }
        public static EntityComponent Entity
        {
            get
            {
                if (m_Entity == null)
                {
                    m_Entity = GameEntry.GetComponent<EntityComponent>();
                }
                return m_Entity;
            }
        }
        public static UIComponent UI
        {
            get
            {
                if (m_UI == null)
                {
                    m_UI = GameEntry.GetComponent<UIComponent>();
                }
                return m_UI;
            }
        }
        public static SoundComponent Sound
        {
            get
            {
                if (m_Sound == null)
                {
                    m_Sound = GameEntry.GetComponent<SoundComponent>();
                }
                return m_Sound;
            }
        }
        public static LocalizationComponent Localization
        {
            get
            {
                if (m_Localization == null)
                {
                    m_Localization = GameEntry.GetComponent<LocalizationComponent>();
                }
                return m_Localization;
            }
        }
        public static SettingComponent Setting
        {
            get
            {
                if (m_Setting == null)
                {
                    m_Setting = GameEntry.GetComponent<SettingComponent>();
                }
                return m_Setting;
            }
        }
        public static BaseComponent Base
        {
            get
            {
                if (m_Base == null)
                {
                    m_Base = GameEntry.GetComponent<BaseComponent>();
                }
                return m_Base;
            }
        }
        public static SceneComponent Scene
        {
            get
            {
                if (m_Scene == null)
                {
                    m_Scene = GameEntry.GetComponent<SceneComponent>();
                }
                return m_Scene;
            }
        }
        public static WebRequestComponent WebRequest
        {
            get
            {
                if (m_WebRequest == null)
                {
                    m_WebRequest = GameEntry.GetComponent<WebRequestComponent>();
                }
                return m_WebRequest;
            }
        }
        public static DownloadComponent Download
        {
            get
            {
                if (m_Download == null)
                {
                    m_Download = GameEntry.GetComponent<DownloadComponent>();
                }
                return m_Download;
            }
        }
        public static DebuggerComponent Debugger
        {
            get
            {
                if (m_Debugger == null)
                {
                    m_Debugger = GameEntry.GetComponent<DebuggerComponent>();
                }
                return m_Debugger;
            }
        }
        public static ArchiveSystem<GameCatalogue, GameData> Archive
        {
            get
            {
                if (m_Archive == null)
                {
                    m_Archive = new();
                }
                return m_Archive;
            }
        }

        /// <summary>
        /// 清除所有组件缓存。场景重载（Restart）后调用，避免指向旧场景已销毁的节点。
        /// </summary>
        public static void ClearCache()
        {
            m_Event = null;
            m_Fsm = null;
            m_Procedure = null;
            m_ObjectPool = null;
            m_DataNode = null;
            m_Resource = null;
            m_Entity = null;
            m_UI = null;
            m_Sound = null;
            m_Localization = null;
            m_Setting = null;
            m_Base = null;
            m_Scene = null;
            m_WebRequest = null;
            m_Download = null;
            m_Debugger = null;
            m_Archive = null;
        }
    }
}
