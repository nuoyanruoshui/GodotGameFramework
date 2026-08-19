//------------------------------------------------------------
// 热更流程跨状态共享上下文
// 供 ProcedureUpdateVersion / ProcedureCheckResources / ProcedureUpdateResources
// 三个链式流程共享 LoadingForm、版本清单、下载列表与热更目录。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameFramework.Resource;
using GameLogic;
using Godot;
using GodotGameFramework;
using GodotGameFramework.UI;

/// <summary>
/// 热更流程共享上下文。
/// 静态缓存跨流程状态；SubpackDir 首次访问时计算一次（避免每次探测可写性）。
/// </summary>
public static class HotUpdateContext
{
    /// <summary>当前更新链上的 LoadingForm（正常路径由 ProcedureUpdateVersion 打开，终态关闭）。</summary>
    public static LoadingForm LoadingForm { get; private set; }

    /// <summary>服务器版本清单（ProcedureUpdateVersion 写入，后续流程读取）。</summary>
    public static PackVersionList ServerVersion { get; set; }

    /// <summary>本地版本清单（ProcedureCheckResources 校验后写入，供 .bak 备份使用）。</summary>
    public static PackVersionList LocalVersion { get; set; }

    /// <summary>待下载包列表（ProcedureCheckResources 差量计算写入，ProcedureUpdateResources 消费）。</summary>
    public static List<(Pack Pack, string Url)> ToDownload { get; set; }

    /// <summary>待下载总字节数（CheckResources 磁盘预检时计算，UpdateResources 日志复用，避免重复 Sum）。</summary>
    public static long ToDownloadTotalSize { get; set; }

    private static string m_SubpackDir;

    /// <summary>热更补丁存储目录（首次访问时探测并缓存）。</summary>
    public static string SubpackDir => m_SubpackDir ?? (m_SubpackDir = GetOrCreateHotUpdateDir());

    /// <summary>
    /// 确保 LoadingForm 已打开。
    /// 已打开则复用；否则（未打开 / 已关闭）新开一个。
    /// </summary>
    public static async Task<LoadingForm> EnsureLoadingFormAsync()
    {
        if (LoadingForm != null && GF.UI.IsValidUIForm(LoadingForm))
            return LoadingForm;

        LoadingForm = await GF.UI.OpenLoadingUIFormAsync();
        return LoadingForm;
    }

    /// <summary>关闭当前 LoadingForm 并清空引用。</summary>
    public static void CloseLoadingForm()
    {
        if (LoadingForm != null)
        {
            GF.UI.CloseUIForm(LoadingForm);
            LoadingForm = null;
        }
    }

    public static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static string GetOrCreateHotUpdateDir()
    {
        // 1. 开发者显式配置的路径
        string customPath = GF.Resource?.UpdateSettingRes?.HotUpdatePath;
        if (!string.IsNullOrEmpty(customPath))
        {
            EnsureDirectory(customPath);
            return customPath;
        }

        // 2. 游戏安装目录（大多数 PC 游戏不装在 C:\Program Files\）
        string exeDir = OS.HasFeature("editor")
            ? $"{ProjectSettings.GlobalizePath("res://")}" + "../../Godot"
            : System.IO.Path.GetDirectoryName(OS.GetExecutablePath());

        if (!string.IsNullOrEmpty(exeDir))
        {
            string gameSubpackDir = Path.Combine(exeDir, "subpackages");
            if (IsDirectoryWritable(gameSubpackDir))
                return gameSubpackDir;
        }

        // 3. 回退 user://（一定能写，但在 C 盘）
        string userSubpackDir = Path.Combine(
            ProjectSettings.GlobalizePath("user://"), "subpackages");
        EnsureDirectory(userSubpackDir);
        return userSubpackDir;
    }

    private static bool IsDirectoryWritable(string path)
    {
        try
        {
            EnsureDirectory(path);
            string testFile = Path.Combine(path, ".write_test");
            File.WriteAllText(testFile, " ");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
