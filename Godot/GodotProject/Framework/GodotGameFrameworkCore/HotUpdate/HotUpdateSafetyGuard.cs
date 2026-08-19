using System;
using System.IO;
using Godot;
using GameFramework.Resource;

namespace GodotGameFramework.HotUpdate
{
    /// <summary>
    /// 热更安全守护。
    /// 通过在 user:// 下写入标记文件，检测"上次启动是否崩溃"，
    /// 防止坏的热更补丁导致无限崩溃循环。
    ///
    /// 状态机：
    ///   .startup_lock 存在 + .startup_ok 不存在 → 上次崩溃 → 安全模式
    ///   .startup_lock 不存在 + .startup_ok 存在 → 上次正常
    ///   两者都不存在 → 首次启动
    /// </summary>
    public static class HotUpdateSafetyGuard
    {
        private const string LockFileName = ".hotupdate_startup_lock";
        private const string OkFileName = ".hotupdate_startup_ok";

        private static string UserDir => ProjectSettings.GlobalizePath("user://");
        private static string LockPath => Path.Combine(UserDir, LockFileName);
        private static string OkPath => Path.Combine(UserDir, OkFileName);

        /// <summary>
        /// 检测上次启动是否因热更崩溃。
        /// 应在 ProcedureUpdate 最开始时调用。
        /// </summary>
        /// <returns>true = 上次崩溃了，本次应跳过所有热更补丁</returns>
        public static bool WasLastSessionCrashed()
        {
            bool lockExists = File.Exists(LockPath);
            bool okExists = File.Exists(OkPath);

            if (lockExists && !okExists)
            {
                Log.Warning("[HotUpdateSafety] 检测到上次启动未完成（可能崩溃），进入安全模式。");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 从备份恢复版本文件（.bak → GameFrameworkVersion.dat）。
        /// 存在备份并恢复成功返回 true；无备份或失败返回 false。
        /// </summary>
        public static bool RestoreVersionFileFromBackup()
        {
            try
            {
                string versionPath = Path.Combine(UserDir, ResourceManager.GameFrameworkVersionData);
                string backupPath = versionPath + ".bak";
                if (!File.Exists(backupPath))
                    return false;

                TryDelete(versionPath);
                File.Move(backupPath, versionPath);
                Log.Info("[HotUpdateSafety] 已回退版本文件到上一版本。");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[HotUpdateSafety] 版本回退失败: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 进入安全模式：清除崩溃标记，回退版本文件，跳过本次热更。
        /// </summary>
        public static void EnterSafeMode()
        {
            try
            {
                // 清除崩溃标记
                TryDelete(LockPath);

                // 回退版本文件到备份；无备份则清除版本文件，使用内置版本
                if (!RestoreVersionFileFromBackup())
                {
                    TryDelete(Path.Combine(UserDir, ResourceManager.GameFrameworkVersionData));
                    Log.Info("[HotUpdateSafety] 无备份，已清除版本文件，使用内置版本。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[HotUpdateSafety] 安全模式恢复失败: {0}", ex.Message);
            }
        }

        /// <summary>
        /// 标记"开始加载热更补丁"。
        /// 应在 ProcedureUpdate 开始加载子包前调用。
        /// </summary>
        public static void MarkStartupBegin()
        {
            try
            {
                // 清除上次的成功标记
                TryDelete(OkPath);

                // 写入启动锁
                File.WriteAllText(LockPath, DateTime.UtcNow.ToString("O"));
                Log.Info("[HotUpdateSafety] 启动锁已写入。");
            }
            catch (Exception ex)
            {
                Log.Error("[HotUpdateSafety] 写入启动锁失败: {0}", ex.Message);
            }
        }

        /// <summary>
        /// 标记"启动成功，游戏已进入可玩状态"。
        /// 应在游戏主流程开始后调用（如 ProcedureGame.OnEnter）。
        /// </summary>
        public static void MarkStartupSuccess()
        {
            try
            {
                // 删除启动锁（启动完成）
                TryDelete(LockPath);

                // 写入成功标记
                File.WriteAllText(OkPath, DateTime.UtcNow.ToString("O"));
                Log.Info("[HotUpdateSafety] 启动成功标记已写入。");
            }
            catch (Exception ex)
            {
                Log.Error("[HotUpdateSafety] 写入成功标记失败: {0}", ex.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* 删除失败不阻塞启动 */ }
        }
    }
}
