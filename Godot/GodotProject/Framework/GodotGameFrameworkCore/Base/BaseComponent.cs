using GameFramework;
using GameFramework.Localization;
using Godot;
using Godot.Collections;
using System;

namespace GodotGameFramework
{
    /// <summary>
    /// 基础组件。
    /// </summary>
    public sealed partial class BaseComponent : GameFrameworkComponent
    {
        public static class Parameters
        {
            public static readonly string JsonHelper = "m_JsonHelper";
            public static readonly string TextHelper = "m_TextHelper";
            public static readonly string LogHelper = "m_LogHelper";
        }
        [Export]
        public Language EditorLanguage;
        [Export]
        private string m_TextHelper = "GodotGameFramework.DefaultTextHelper";
        [Export]
        private string m_LogHelper = "GodotGameFramework.DefaultLogHelper";
        [Export]
        private string m_JsonHelper = "GodotGameFramework.DefaultJsonHelper";
        /// <summary>
        /// 帧率设置。默认 60 帧。
        /// </summary>
        [Export(PropertyHint.Range, "30,120,1")]
        private int m_FrameRate = 60;

        /// <summary>
        /// 游戏速度。
        /// </summary>
        [Export(PropertyHint.Range, "0,8,1")]
        private float m_GameSpeed = 1f;

        /// <summary>
        /// 框架是否已经关闭。
        /// </summary>
        private bool m_Shutdown = false;

        /// <summary>
        /// 暂停前保存的游戏速度。
        /// </summary>
        private float m_GameSpeedBeforePause = 1f;

        /// <summary>
        /// 获取或设置游戏帧率。
        /// 直接映射到 Godot 的 Engine.MaxFps。
        /// </summary>
        public int FrameRate
        {
            get => m_FrameRate;
            set
            {
                m_FrameRate = value;
                Engine.MaxFps = value;
            }
        }

        /// <summary>
        /// 获取或设置游戏速度。
        /// </summary>
        public float GameSpeed
        {
            get
            {
                return m_GameSpeed;
            }
            set
            {
                Engine.TimeScale = m_GameSpeed = value >= 0f ? value : 0f;
            }
        }

        /// <summary>
        /// 获取游戏是否暂停。
        /// </summary>
        public bool IsGamePaused
        {
            get
            {
                return m_GameSpeed <= 0f;
            }
        }

        /// <summary>
        /// 获取是否正常游戏速度。
        /// </summary>
        public bool IsNormalGameSpeed
        {
            get
            {
                return m_GameSpeed == 1f;
            }
        }
        [Export]
        /// <summary>
        /// 此模式下，框架会从 res://TheGame/ 目录下直接加载资源，而不是从 .pck 包中加载。
        /// 仅在编辑器中有效。
        /// </summary>
        /// <value></value>
        public bool EnableEditorResLoad { get; private set; }

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public void PauseGame()
        {
            if (IsGamePaused)
            {
                return;
            }

            m_GameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public void ResumeGame()
        {
            if (!IsGamePaused)
            {
                return;
            }

            GameSpeed = m_GameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
        /// </summary>
        public void ResetNormalGameSpeed()
        {
            if (IsNormalGameSpeed)
            {
                return;
            }

            GameSpeed = 1f;
        }

        public override void OnInit()
        {
            base.OnInit();
            InitLogHelper();
            InitTextHelper();
            InitJsonHelper();
            Log.Info("Godot Engine Version: {0}", Engine.GetVersionInfo()["string"].AsString());
            EnableEditorResLoad &= OS.HasFeature("editor");
            // 设置帧率和游戏速度
            Engine.MaxFps = m_FrameRate;
            Engine.TimeScale = m_GameSpeed;
        }


        /// <summary>
        /// 节点被销毁时调用。
        /// 触发核心框架的 Shutdown 流程。
        /// </summary>
        public override void OnPreDestroy()
        {
            base.OnPreDestroy();
            Shutdown();
        }


        /// <summary>
        /// 执行框架关闭。
        /// 清理核心框架中的所有模块，释放资源。
        /// </summary>
        internal void Shutdown()
        {
            if (m_Shutdown)
            {
                return;
            }

            m_Shutdown = true;
            Log.Info("Shutdown Game Framework...");
            GameFrameworkEntry.Shutdown();
        }

        /// <summary>
        /// 初始化文本格式化 Helper。
        ///
        /// TextHelper 用于优化 string.Format 的性能，
        /// 通过 StringBuilder 缓存减少内存分配。
        /// 如果不设置，核心框架会使用默认的 string.Format。
        /// </summary>
        private void InitTextHelper()
        {
            try
            {
                Type textHelperType = Utility.Assembly.GetType(m_TextHelper);
                if (textHelperType != null)
                {
                    Utility.Text.SetTextHelper((Utility.Text.ITextHelper)Activator.CreateInstance(textHelperType));
                    Log.Info("TextHelper: {0}", textHelperType.FullName);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Can not create text helper instance with exception '{0}'.", exception);
            }
        }

        /// <summary>
        /// 初始化日志 Helper。
        ///
        /// LogHelper 将框架的日志输出桥接到 Godot 的 GD.Print 系统。
        /// 支持 Debug/Info/Warning/Error/Fatal 五个级别。
        /// </summary>
        private void InitLogHelper()
        {
            try
            {
                Type logHelperType = Utility.Assembly.GetType(m_LogHelper);
                if (logHelperType != null)
                {
                    GameFrameworkLog.SetLogHelper((GameFrameworkLog.ILogHelper)Activator.CreateInstance(logHelperType));
                    Log.Info("LogHelper: {0}", logHelperType.FullName);
                }
            }
            catch (Exception exception)
            {
                Log.Fatal("Can not create log helper instance with exception '{0}'.", exception);
            }
        }

        private void InitJsonHelper()
        {
            try
            {
                Type jsonHelperType = Utility.Assembly.GetType(m_JsonHelper);
                if (jsonHelperType != null)
                {
                    Utility.Json.SetJsonHelper((Utility.Json.IJsonHelper)Activator.CreateInstance(jsonHelperType));
                    Log.Info("JsonHelper: {0}", jsonHelperType.FullName);
                }
            }
            catch (Exception exception)
            {
                Log.Fatal("Can not create json helper instance with exception '{0}'.", exception);
            }
        }
    }
}
