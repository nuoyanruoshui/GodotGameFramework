using GameFramework;
using GameFramework.Debugger;
using Godot;
using GodotGameFramework.Extensions;
using System;
using System.Collections.Generic;

namespace GodotGameFramework.Debugger;

public sealed partial class DebuggerComponent
{
    /// <summary>
    /// 控制台调试器窗口
    /// 捕获框架日志，支持级别过滤、锁定滚动、行选中查看堆栈与复制。
    /// </summary>
    public sealed class ConsoleWindow : IDebuggerWindow
    {
        private const string SettingLockScroll = "Debugger.Console.LockScroll";
        private const string SettingDebugFilter = "Debugger.Console.DebugFilter";
        private const string SettingInfoFilter = "Debugger.Console.InfoFilter";
        private const string SettingWarningFilter = "Debugger.Console.WarningFilter";
        private const string SettingErrorFilter = "Debugger.Console.ErrorFilter";
        private const string SettingFatalFilter = "Debugger.Console.FatalFilter";

        private readonly Queue<LogNode> m_LogNodes = new Queue<LogNode>();
        private readonly Queue<LogNode> m_PendingLogNodes = new Queue<LogNode>();
        private readonly object m_PendingLock = new object();

        private DebuggerComponent m_Component;
        private LogNode m_SelectedNode;
        private int m_MaxLine = 100;
        private bool m_LockScroll = true;
        private bool m_DebugFilter = true;
        private bool m_InfoFilter = true;
        private bool m_WarningFilter = true;
        private bool m_ErrorFilter = true;
        private bool m_FatalFilter = true;

        // Godot 原生日志捕获（文件尾部轮询）
        private string m_GodotLogPath;
        private long m_GodotLogPos;
        private const string GodotLogFileName = "user://logs/godot.log";

        /// <summary>
        /// 获取或设置最大日志行数。
        /// </summary>
        public int MaxLine
        {
            get => m_MaxLine;
            set => m_MaxLine = Math.Max(1, value);
        }

        /// <summary>
        /// 获取或设置是否锁定滚动（自动滚动到最新日志）。
        /// </summary>
        public bool LockScroll
        {
            get => m_LockScroll;
            set
            {
                m_LockScroll = value;
                SaveBoolSetting(SettingLockScroll, value);
            }
        }

        public bool DebugFilter
        {
            get => m_DebugFilter;
            set
            {
                m_DebugFilter = value;
                SaveBoolSetting(SettingDebugFilter, value);
            }
        }

        public bool InfoFilter
        {
            get => m_InfoFilter;
            set
            {
                m_InfoFilter = value;
                SaveBoolSetting(SettingInfoFilter, value);
            }
        }

        public bool WarningFilter
        {
            get => m_WarningFilter;
            set
            {
                m_WarningFilter = value;
                SaveBoolSetting(SettingWarningFilter, value);
            }
        }

        public bool ErrorFilter
        {
            get => m_ErrorFilter;
            set
            {
                m_ErrorFilter = value;
                SaveBoolSetting(SettingErrorFilter, value);
            }
        }

        public bool FatalFilter
        {
            get => m_FatalFilter;
            set
            {
                m_FatalFilter = value;
                SaveBoolSetting(SettingFatalFilter, value);
            }
        }

        public void Initialize(params object[] args)
        {
            if (args != null && args.Length > 0)
            {
                m_Component = args[0] as DebuggerComponent;
            }

            m_Component ??= GameEntry.GetComponent<DebuggerComponent>();
            LoadSettings();

            // 启用 Godot 文件日志以捕获引擎原生输出（GD.PrintErr / C++ 错误等）
            EnableGodotFileLogging();
        }

        public void Shutdown()
        {
            Clear();
        }

        public void OnEnter()
        {
        }

        public void OnLeave()
        {
        }

        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            Pump();
            PollGodotLog();
        }

        public void OnDraw()
        {
            Pump();
            RefreshCounts(out int debugCount, out int infoCount, out int warningCount, out int errorCount, out int fatalCount);

            DebuggerDraw draw = m_Component.Draw;
            draw.ScrollFollowing = m_LockScroll && m_SelectedNode == null;

            // ---- 工具条 ----
            draw.Button("Clear All", Clear);
            draw.Toggle(m_LockScroll, "Lock Scroll", value => LockScroll = value);
            draw.Toggle(m_DebugFilter, $"Debug ({debugCount})", value => DebugFilter = value);
            draw.Toggle(m_InfoFilter, $"Info ({infoCount})", value => InfoFilter = value);
            draw.Toggle(m_WarningFilter, $"Warning ({warningCount})", value => WarningFilter = value);
            draw.Toggle(m_ErrorFilter, $"Error ({errorCount})", value => ErrorFilter = value);
            draw.Toggle(m_FatalFilter, $"Fatal ({fatalCount})", value => FatalFilter = value);
            draw.NewLine();
            draw.Separator();

            // ---- 日志行 ----
            foreach (LogNode logNode in m_LogNodes)
            {
                if (!IsPassedFilter(logNode.LogLevel))
                {
                    continue;
                }

                bool selected = logNode == m_SelectedNode;
                string line = $"[{logNode.LogTime:HH:mm:ss.fff}][{logNode.LogFrameCount}] {logNode.LogMessage}";
                string inner = Utility.Text.Format(
                    "{0}[color={1}]{2}[/color]{3}",
                    selected ? "[bgcolor=#2A3A50]" : string.Empty,
                    GetLogLevelColor(logNode.LogLevel),
                    DebuggerDraw.Esc(line),
                    selected ? "[/bgcolor]" : string.Empty);

                LogNode capturedNode = logNode;
                draw.Link(inner, () => m_SelectedNode = m_SelectedNode == capturedNode ? null : capturedNode);
                draw.NewLine();
            }

            // ---- 选中详情 ----
            if (m_SelectedNode != null)
            {
                draw.Separator();
                draw.Button("Copy", () =>
                {
                    string stack = string.IsNullOrEmpty(m_SelectedNode?.StackTrack) ? string.Empty : "\n" + m_SelectedNode.StackTrack;
                    DisplayServer.ClipboardSet(m_SelectedNode?.LogMessage + stack);
                });
                draw.Button("Deselect", () => m_SelectedNode = null);
                draw.NewLine();
                draw.AppendRaw(Utility.Text.Format(
                    "[color={0}]{1}[/color]\n",
                    GetLogLevelColor(m_SelectedNode.LogLevel),
                    DebuggerDraw.Esc(m_SelectedNode.LogMessage)));
                if (!string.IsNullOrEmpty(m_SelectedNode.StackTrack))
                {
                    draw.AppendRaw(Utility.Text.Format("[color=#8899AA]{0}[/color]\n", DebuggerDraw.Esc(m_SelectedNode.StackTrack)));
                }
            }
        }

        /// <summary>
        /// 获取各级别日志数量（供 FPS 图标变色等使用）。
        /// </summary>
        public void GetLogCounts(out int debugCount, out int infoCount, out int warningCount, out int errorCount, out int fatalCount)
        {
            Pump();
            RefreshCounts(out debugCount, out infoCount, out warningCount, out errorCount, out fatalCount);
        }

        /// <summary>
        /// 清空全部日志。
        /// </summary>
        public void Clear()
        {
            m_SelectedNode = null;
            lock (m_PendingLock)
            {
                while (m_PendingLogNodes.Count > 0)
                {
                    ReferencePool.Release(m_PendingLogNodes.Dequeue());
                }
            }

            while (m_LogNodes.Count > 0)
            {
                ReferencePool.Release(m_LogNodes.Dequeue());
            }
        }


        private void Pump()
        {
            lock (m_PendingLock)
            {
                while (m_PendingLogNodes.Count > 0)
                {
                    m_LogNodes.Enqueue(m_PendingLogNodes.Dequeue());
                }
            }

            while (m_LogNodes.Count > m_MaxLine)
            {
                LogNode dropped = m_LogNodes.Dequeue();
                if (dropped == m_SelectedNode)
                {
                    m_SelectedNode = null;
                }

                ReferencePool.Release(dropped);
            }
        }

        private void RefreshCounts(out int debugCount, out int infoCount, out int warningCount, out int errorCount, out int fatalCount)
        {
            debugCount = infoCount = warningCount = errorCount = fatalCount = 0;
            foreach (LogNode logNode in m_LogNodes)
            {
                switch (logNode.LogLevel)
                {
                    case GameFrameworkLogLevel.Debug:
                        debugCount++;
                        break;
                    case GameFrameworkLogLevel.Info:
                        infoCount++;
                        break;
                    case GameFrameworkLogLevel.Warning:
                        warningCount++;
                        break;
                    case GameFrameworkLogLevel.Error:
                        errorCount++;
                        break;
                    case GameFrameworkLogLevel.Fatal:
                        fatalCount++;
                        break;
                }
            }
        }

        private bool IsPassedFilter(GameFrameworkLogLevel level)
        {
            return level switch
            {
                GameFrameworkLogLevel.Debug => m_DebugFilter,
                GameFrameworkLogLevel.Info => m_InfoFilter,
                GameFrameworkLogLevel.Warning => m_WarningFilter,
                GameFrameworkLogLevel.Error => m_ErrorFilter,
                GameFrameworkLogLevel.Fatal => m_FatalFilter,
                _ => true,
            };
        }

        private static string GetLogLevelColor(GameFrameworkLogLevel level)
        {
            return level switch
            {
                GameFrameworkLogLevel.Debug => "#888888",
                GameFrameworkLogLevel.Info => "#FFFFFF",
                GameFrameworkLogLevel.Warning => "#FFFF00",
                GameFrameworkLogLevel.Error => "#FF5050",
                GameFrameworkLogLevel.Fatal => "#FF00FF",
                _ => "#FFFFFF",
            };
        }

        private void LoadSettings()
        {
            try
            {
                var setting = GF.Setting;
                if (setting == null)
                {
                    return;
                }

                m_LockScroll = setting.GetBool(SettingLockScroll, true);
                m_DebugFilter = setting.GetBool(SettingDebugFilter, true);
                m_InfoFilter = setting.GetBool(SettingInfoFilter, true);
                m_WarningFilter = setting.GetBool(SettingWarningFilter, true);
                m_ErrorFilter = setting.GetBool(SettingErrorFilter, true);
                m_FatalFilter = setting.GetBool(SettingFatalFilter, true);
            }
            catch (Exception)
            {
                // 设置组件不可用时使用默认值
            }
        }

        private static void SaveBoolSetting(string key, bool value)
        {
            try
            {
                var setting = GF.Setting;
                if (setting == null)
                {
                    return;
                }

                setting.SetBool(key, value);
                setting.Save();
            }
            catch (Exception)
            {
            }
        }

        // ── Godot 原生日志捕获 ──

        /// <summary>
        /// 启用 Godot 文件日志，以便通过尾部轮询捕获 GD.PrintErr / C++ 引擎错误等原生输出。
        /// </summary>
        private void EnableGodotFileLogging()
        {
            try
            {
                // 确保日志目录存在
                string logDir = ProjectSettings.GlobalizePath("user://logs");
                if (!System.IO.Directory.Exists(logDir))
                    System.IO.Directory.CreateDirectory(logDir);

                m_GodotLogPath = ProjectSettings.GlobalizePath(GodotLogFileName);

                // 清理旧日志，避免无限增长
                if (System.IO.File.Exists(m_GodotLogPath))
                {
                    // 记录当前文件大小作为起始位置（跳过已有历史日志）
                    m_GodotLogPos = new System.IO.FileInfo(m_GodotLogPath).Length;
                }
                else
                {
                    m_GodotLogPos = 0;
                }

                // 启用文件日志（Godot 4.x project setting）
                ProjectSettings.SetSetting("debug/file_logging/enable_file_logging", true);
                ProjectSettings.SetSetting("debug/file_logging/log_path", GodotLogFileName);

                // 日志立即刷新，确保尾部轮询及时
                ProjectSettings.SetSetting("debug/file_logging/flush_stdout_on_print", true);
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[ConsoleWindow] 无法启用 Godot 文件日志: {ex.Message}");
            }
        }

        /// <summary>
        /// 轮询 Godot 日志文件，将新行作为控制台日志入队。
        /// </summary>
        private void PollGodotLog()
        {
            if (string.IsNullOrEmpty(m_GodotLogPath))
                return;

            try
            {
                if (!System.IO.File.Exists(m_GodotLogPath))
                    return;

                var fileInfo = new System.IO.FileInfo(m_GodotLogPath);
                if (fileInfo.Length <= m_GodotLogPos)
                    return;

                using var fs = new System.IO.FileStream(
                    m_GodotLogPath,
                    System.IO.FileMode.Open,
                    System.IO.FileAccess.Read,
                    System.IO.FileShare.ReadWrite);

                fs.Seek(m_GodotLogPos, System.IO.SeekOrigin.Begin);
                using var reader = new System.IO.StreamReader(fs);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // 解析 Godot 日志行，确定级别
                    var (level, message) = ParseGodotLogLine(line);

                    // 过滤引擎引导噪声（时间戳/模块加载等无意义行）
                    if (message == null)
                        continue;

                    var logNode = LogNode.Create(level, message, null);
                    lock (m_PendingLock)
                    {
                        m_PendingLogNodes.Enqueue(logNode);
                    }
                }

                m_GodotLogPos = fs.Position;
            }
            catch (Exception)
            {
                // 文件可能被 Godot 锁定，下帧重试
            }
        }

        /// <summary>
        /// 解析 Godot 日志行，返回 (日志级别, 消息)。
        /// 返回 (Debug, null) 表示该行应被跳过（引擎引导噪声）。
        /// </summary>
        private static (GameFrameworkLogLevel level, string message) ParseGodotLogLine(string line)
        {
            string trimmed = line.Trim();

            // Godot 引擎错误格式: "ERROR: Message" 或 "ERROR: Condition ... is true."
            if (trimmed.StartsWith("ERROR:") || trimmed.Contains("ERROR:"))
            {
                string msg = ExtractMeaningfulMessage(trimmed, "ERROR:".ColorString(Colors.Red));
                return (GameFrameworkLogLevel.Error, msg);
            }

            // Godot 引擎警告格式: "WARNING: Message"
            if (trimmed.StartsWith("WARNING:") || trimmed.Contains("WARNING:"))
            {
                string msg = ExtractMeaningfulMessage(trimmed, "WARNING:".ColorString(Colors.Yellow));
                return (GameFrameworkLogLevel.Warning, msg);
            }

            if (trimmed.StartsWith("INFO:") || trimmed.Contains("INFO:"))
            {
                string msg = ExtractMeaningfulMessage(trimmed, "INFO:".ColorString(Colors.Green));
                return (GameFrameworkLogLevel.Info, msg);
            }

            if (trimmed.StartsWith("DEBUG:") || trimmed.Contains("DEBUG:"))
            {
                string msg = ExtractMeaningfulMessage(trimmed, "DEBUG:".ColorString(Colors.White));
                return (GameFrameworkLogLevel.Debug, msg);
            }

            // Godot 脚本错误: "E 0:00:01.234   ..." 或 "  <C++ 错误>  ..."
            if (trimmed.StartsWith("E ") || trimmed.StartsWith("E\t"))
            {
                return (GameFrameworkLogLevel.Error, trimmed);
            }

            // 堆栈/源码引用行：缩进的错误详情
            if ((trimmed.StartsWith("<C++") || trimmed.StartsWith("<C#"))
                && (trimmed.Contains("错误") || trimmed.Contains("Error") || trimmed.Contains("error")))
            {
                return (GameFrameworkLogLevel.Error, trimmed);
            }

            // Godot 调试输出: "  Message" 或普通 print
            if (trimmed.Length > 2 && !trimmed.StartsWith("//"))
            {
                // 跳过引擎启动横幅/模块加载等纯信息行
                if (trimmed.StartsWith("Godot Engine") ||
                    trimmed.StartsWith("Module ") ||
                    trimmed.StartsWith("  Module ") ||
                    trimmed.StartsWith("OpenGL") ||
                    trimmed.StartsWith("Vulkan") ||
                    trimmed.StartsWith("D3D12"))
                    return (GameFrameworkLogLevel.Info, null); // 跳过

                return (GameFrameworkLogLevel.Info, trimmed);
            }

            return (GameFrameworkLogLevel.Info, null); // 跳过不关心的行
        }

        /// <summary>
        /// 从 Godot 错误/警告行中提取有意义的消息。
        /// "ERROR: Condition "x" is true. ... at: func (file:line)" → 取第一句描述
        /// </summary>
        private static string ExtractMeaningfulMessage(string line, string prefix)
        {
            int idx = line.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) return line;

            string after = line.Substring(idx + prefix.Length).Trim();

            // 截取 at: 之前的部分作为摘要
            int atIdx = after.IndexOf("   at:", StringComparison.Ordinal);
            if (atIdx > 0)
                after = after.Substring(0, atIdx).Trim();

            return $"[Godot] {after}";
        }
    }
}
