//------------------------------------------------------------
// 热更流程基类
// 为 ProcedureUpdateVersion / ProcedureCheckResources / ProcedureUpdateResources
// 提供共享的完整性校验、子包加载、强制更新对话框、错误处理等公共逻辑。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using GameFramework;
using GameFramework.Procedure;
using GameFramework.Resource;
using Godot;
using GodotGameFramework;
using GodotGameFramework.HotUpdate;
using GodotGameFramework.Json;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using GodotGameFramework.UI;

/// <summary>
/// 热更流程基类。
/// 常量与公共工具方法，行为与原 ProcedureUpdate 保持一致。
/// </summary>
public abstract class ProcedureUpdateBase : ProcedureBase
{
    protected const int MaxRetries = 3;
    protected const float RetryBaseDelaySeconds = 1.5f;

    protected string SubpackDir => HotUpdateContext.SubpackDir;

    /// <summary>
    /// 强制更新/阻塞对话框。
    /// 显示提示信息，提供"退出游戏"和"重试"按钮。
    /// </summary>
    /// <returns>true = 用户选择重试</returns>
    protected async Task<bool> ShowForceUpdateDialogAsync(string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        HotUpdateContext.LoadingForm?.SetLogState(message, 100);

        GF.UI.OpenQuestionTipsAsync(message, "退出", "重试", () =>
        {
            tcs.TrySetResult(false); // 退出
        }, () =>
        {
            tcs.TrySetResult(true);  // 重试
        });

        return await tcs.Task;
    }

    /// <summary>
    /// 校验本地版本中所有 .pck 文件的完整性。
    /// 检查：文件存在 + 大小匹配 + SHA256 匹配（>1MB 文件）。
    /// 损坏或丢失的包从 localVersion 中移除，后续会自动与服务器对齐重新下载。
    /// </summary>
    /// <returns>损坏/丢失的包数量</returns>
    protected async Task<int> VerifyLocalPackIntegrityAsync(PackVersionList localVersion)
    {
        Log.Info("[ProcedureUpdate] 开始校验本地文件完整性...");
        if (localVersion?.Packs == null || localVersion.Packs.Length == 0)
            return 0;

        var validPacks = new List<Pack>();
        int damaged = 0;

        foreach (var pack in localVersion.Packs)
        {
            if (!pack.IsValid()) continue;

            string packPath = Path.Combine(SubpackDir, pack.Name + ".pck");
            string damageReason = null;

            // 1. 文件存在？
            if (!File.Exists(packPath))
            {
                damageReason = "文件不存在";
            }
            else
            {
                var fileInfo = new FileInfo(packPath);

                // 2. 大小匹配？
                if (fileInfo.Length != pack.Size)
                {
                    damageReason = $"大小不匹配 (期望 {pack.Size}, 实际 {fileInfo.Length})";
                }
                // 3. SHA256 校验（线程池执行避免卡帧；所有文件均校验，含小文件）
                else
                {
                    try
                    {
                        string actualHash = await Task.Run(() => NodeUtility.ComputeSHA256(packPath));
                        if (!string.Equals(actualHash, pack.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            damageReason = "SHA256 校验失败（文件已损坏或被修改）";
                        }
                    }
                    catch (Exception ex)
                    {
                        damageReason = $"SHA256 计算失败: {ex.Message}";
                    }
                }
            }

            if (damageReason != null)
            {
                Log.Warning("[ProcedureUpdate] 本地文件损坏: {0} — {1}", pack.Name, damageReason);
                if (!EasySave.TryDelete(packPath))
                    Log.Warning("[ProcedureUpdate] 无法删除损坏文件（可能被占用）: {0}", packPath);
                damaged++;
            }
            else
            {
                validPacks.Add(pack);
            }
        }

        // 更新本地版本列表：只保留通过校验的
        localVersion.Packs = validPacks.ToArray();

        if (damaged > 0)
        {
            Log.Warning("[ProcedureUpdate] 本地完整性校验完成: {0} 个损坏/丢失, {1} 个完好",
                damaged, validPacks.Count);
        }

        return damaged;
    }

    /// <summary>
    /// 加载已下载的 .pck 子包。
    /// 先加载 Config 类型（Luban/本地化），再加载 Resource 类型（场景/贴图）。
    /// 加载前大小校验，对大文件做 SHA256 重校验。
    /// </summary>
    protected Task<int> LoadDownloadedPacksAsync(PackVersionList version)
    {
        if (version?.Packs == null || version.Packs.Length == 0)
            return Task.FromResult(0);

        // 先 Config 后 Resource，确保场景加载时配置已就绪
        var ordered = version.Packs
            .OrderBy(p => p.Type == PackType.Config ? 0 : 1)
            .ToArray();

        int loaded = 0;
        int failed = 0;

        foreach (var pack in ordered)
        {
            if (!pack.IsValid()) continue;

            string packPath = Path.Combine(SubpackDir, pack.Name + ".pck");
            if (!File.Exists(packPath))
            {
                Log.Warning("[ProcedureUpdate] 子包不存在，跳过: {0}", packPath);
                failed++;
                continue;
            }

            // 大小校验（完整性已由 VerifyLocalPackIntegrityAsync 全量 SHA256 保证，此处避免重复哈希）
            var fileInfo = new FileInfo(packPath);
            if (fileInfo.Length != pack.Size)
            {
                Log.Warning("[ProcedureUpdate] 子包大小不匹配({0})，可能已损坏，跳过: {1}",
                    pack.Name, fileInfo.Length);
                if (!EasySave.TryDelete(packPath))
                    Log.Warning("[ProcedureUpdate] 无法删除损坏文件（可能被占用）: {0}", packPath);
                failed++;
                continue;
            }

            // 加载
            if (ProjectSettings.LoadResourcePack(packPath))
            {
                loaded++;
                Log.Info("[ProcedureUpdate] 子包加载成功: {0} ({1})", pack.Name, pack.Type);
            }
            else
            {
                Log.Warning("[ProcedureUpdate] 子包加载失败: {0}", packPath);
                failed++;
            }
        }

        // 清理不属于当前版本的废弃 .pck
        CleanStalePacks(version);

        // 如果有包加载失败，回退版本文件
        if (failed > 0)
        {
            Log.Warning("[ProcedureUpdate] {0} 个子包加载失败，回退版本文件。", failed);
            RollbackVersionFile();
        }

        Log.Info("[ProcedureUpdate] 子包加载完成: {0}/{1} (失败: {2})",
            loaded, version.Packs.Length, failed);

        return Task.FromResult(failed);
    }

    /// <summary>清理磁盘上不在版本清单中的废弃 .pck 文件。</summary>
    protected void CleanStalePacks(PackVersionList version)
    {
        if (!Directory.Exists(SubpackDir)) return;

        var validNames = new HashSet<string>(
            version.Packs.Where(p => p.IsValid()).Select(p => p.Name + ".pck"));

        try
        {
            foreach (string file in Directory.GetFiles(SubpackDir, "*.pck"))
            {
                string fileName = Path.GetFileName(file);
                if (!validNames.Contains(fileName))
                {
                    Log.Info("[ProcedureUpdate] 清理废弃子包: {0}", fileName);
                    if (!EasySave.TryDelete(file))
                        Log.Warning("[ProcedureUpdate] 无法删除废弃包（可能被占用）: {0}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[ProcedureUpdate] 清理废弃包异常: {0}", ex.Message);
        }
    }

    /// <summary>回退版本文件到备份（委托 HotUpdateSafetyGuard 公共逻辑，避免两处重复）。</summary>
    protected void RollbackVersionFile()
    {
        HotUpdateSafetyGuard.RestoreVersionFileFromBackup();
    }

    /// <summary>
    /// Package 模式下尝试加载安装目录中的本地子包。
    /// 读取 SubpackDir 下的 GameFrameworkVersion.dat 清单，加载所有 .pck。
    /// 失败不阻塞启动——日志记录后静默跳过。
    /// </summary>
    protected async Task TryLoadLocalSubpackagesAsync()
    {
        string manifestPath = Path.Combine(SubpackDir, ResourceManager.GameFrameworkVersionData);
        if (!File.Exists(manifestPath))
        {
            Log.Info("[ProcedureUpdate] 本地子包清单不存在，跳过: {0}", manifestPath);
            return;
        }

        Log.Info("[ProcedureUpdate] 检测到本地子包清单，尝试加载: {0}", manifestPath);

        try
        {
            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            var localVersion = Utility.Json.ToObject<PackVersionList>(json);

            if (localVersion == null)
            {
                Log.Warning("[ProcedureUpdate] 本地子包清单解析为 null");
                return;
            }
            if (!localVersion.Validate(out string validateError))
            {
                Log.Warning("[ProcedureUpdate] 本地子包清单校验失败: {0}", validateError);
                return;
            }

            ResourceManager.LocalPackVersionList = localVersion;
            await LoadDownloadedPacksAsync(localVersion);
        }
        catch (Exception ex)
        {
            Log.Warning("[ProcedureUpdate] 加载本地子包异常: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 跳过更新检测，加载已存在的本地版本后进入 Prelode。
    /// 注意：内部不做 LoadingForm 关闭 / 启动成功标记，由调用方在终态处理。
    /// </summary>
    protected async Task SkipToNextAsync(ProcedureOwner procedureOwner)
    {
        // 尝试加载已存在的本地版本（优先使用缓存的统一版本，带完整性校验）
        var local = ResourceManager.LocalPackVersionList
            ?? NodeUtility.LoadAndValidateVersionList(ResourceManager.GameFrameworkVersionData);
        if (local != null)
        {
            ResourceManager.LocalPackVersionList = local;
            await LoadDownloadedPacksAsync(local);
        }

        ChangeState<ProcedurePrelode>(procedureOwner);
    }

    /// <summary>标记启动成功并关闭 LoadingForm（热更链终态统一调用）。</summary>
    protected void MarkSuccessAndCloseLoading()
    {
        HotUpdateSafetyGuard.MarkStartupSuccess();
        HotUpdateContext.CloseLoadingForm();
    }

    /// <summary>
    /// 更新流程异常统一处理。
    /// 先标记启动成功；仅当确定不重试（退出/跳过）时才关闭 LoadingForm，
    /// 避免重试跳回 ProcedureUpdateVersion 时因表单已关闭触发二次线程加载卡死。
    /// </summary>
    protected async Task HandleUpdateErrorAsync(ProcedureOwner procedureOwner, Exception ex)
    {
        Log.Error("[ProcedureUpdate] 更新流程异常: {0}", ex);

        // 标记启动成功（先不关闭 LoadingForm，重试时可复用）
        HotUpdateSafetyGuard.MarkStartupSuccess();

        var localVersion = EasySave.LoadFromUser<PackVersionList>(
            ResourceManager.GameFrameworkVersionData);
        if (localVersion?.ForceUpdate == true)
        {
            bool retry = await ShowForceUpdateDialogAsync("更新流程异常: " + ex.Message + "请重试或退出游戏。");
            if (retry)
            {
                // 重试整个更新流程（LoadingForm 保持开启，EnsureLoadingFormAsync 复用）
                ChangeState<ProcedureUpdateVersion>(procedureOwner);
                return;
            }
            HotUpdateContext.CloseLoadingForm();
            GameEntry.Shutdown(ShutdownType.Quit);
            return;
        }

        HotUpdateContext.CloseLoadingForm();
        await SkipToNextAsync(procedureOwner);
    }
}
