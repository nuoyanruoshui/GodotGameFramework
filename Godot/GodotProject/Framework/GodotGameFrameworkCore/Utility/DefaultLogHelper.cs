//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework;
using Godot;
using GodotGameFramework.Extensions;
using System;

namespace GodotGameFramework
{
    /// <summary>
    /// 默认游戏框架日志辅助器。
    ///
    /// 实现了核心框架的 ILogHelper 接口，
    /// 将框架内部的日志输出桥接到 Godot 的 GD 日志系统。
    ///
    /// 日志级别映射：
    /// - Debug → GD.Print（灰色输出）
    /// - Info  → GD.Print（普通输出）
    /// - Warning → GD.PushWarning（黄色警告）
    /// - Error → GD.PushError（红色错误）
    /// - Fatal → GD.PushError（红色错误，带 [FATAL] 前缀）
    ///
    /// 此外，Warning/Error/Fatal 级别日志会持久化到 user://session.log，
    /// 用于崩溃后排查（即使日志缓冲区丢失也有现场数据）。
    /// </summary>
    public class DefaultLogHelper : GameFrameworkLog.ILogHelper
    {
        /// <summary>
        /// 记录日志。
        /// 由核心框架的 GameFrameworkLog 类自动调用。
        /// </summary>
        /// <param name="level">日志等级</param>
        /// <param name="message">日志内容</param>
        public void Log(GameFrameworkLogLevel level, object message)
        {
            switch (level)
            {
                case GameFrameworkLogLevel.Debug:
                    GD.PrintRich("[DEBUG]".ColorString(Colors.White) + message);
                    break;

                case GameFrameworkLogLevel.Info:
                    GD.PrintRich("[INFO]".ColorString(Colors.Green) + message);
                    break;

                case GameFrameworkLogLevel.Warning:
                    GD.PrintRich($"{"[WARNING]".ColorString(Colors.Yellow)} {message}");
                    GD.PushWarning(message.ToString());
                    break;

                case GameFrameworkLogLevel.Error:
                    GD.PrintRich($"{"[ERROR]".ColorString(Colors.Red)} {message}");
                    GD.PushError(message.ToString());
                    break;

                case GameFrameworkLogLevel.Fatal:
                    GD.PrintRich($"{"[FATAL]".ColorString(Colors.Red)} {message}");
                    GD.PushError($"[FATAL] {message}");
                    break;

                default:
                    GD.PrintRich($"[UNKNOWN LOG LEVEL] {message}");
                    GD.PushError($"[UNKNOWN LOG LEVEL] {message}");
                    break;
            }
        }
    }
}
