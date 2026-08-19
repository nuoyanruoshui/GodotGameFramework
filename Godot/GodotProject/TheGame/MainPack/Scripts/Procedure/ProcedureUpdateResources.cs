//------------------------------------------------------------
// 资源下载流程（热更链第三环）
// 并发下载差量包（SHA256 校验 + 断点重试），加载子包，保存版本文件。
// 下载成功后弹重启对话框——两按钮均退出，此流程永不直接进入 Prelode。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using GameFramework.Resource;
using GodotGameFramework;
using GodotGameFramework.HotUpdate;
using GodotGameFramework.Json;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using GodotGameFramework.Extensions;
using GodotGameFramework.UI;

/// <summary>
/// 资源下载流程。
/// 步骤：并发下载 → 失败处理 → 加载子包 → 保存版本 → 重启提示。
/// </summary>
public class ProcedureUpdateResources : ProcedureUpdateBase
{
    protected internal override async void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        try
        {
            await RunUpdateResourcesAsync(procedureOwner);
        }
        catch (Exception ex)
        {
            await HandleUpdateErrorAsync(procedureOwner, ex);
        }
    }

    private async Task RunUpdateResourcesAsync(ProcedureOwner procedureOwner)
    {
        // 防御：上下文缺失（异常跳转导致）→ 跳过
        var serverVersion = HotUpdateContext.ServerVersion;
        var toDownload = HotUpdateContext.ToDownload;
        if (serverVersion == null || toDownload == null || toDownload.Count == 0)
        {
            await SkipToNextAsync(procedureOwner);
            MarkSuccessAndCloseLoading();
            return;
        }

        long totalSize = HotUpdateContext.ToDownloadTotalSize;
        Log.Info("[ProcedureUpdate] 共 {0} 个包需要更新，总计 {1}，开始下载...",
            toDownload.Count, StringExtension.FormatBytes(totalSize));

        // ── 1. 并发下载 ──
        int downloaded = await DownloadPacksWithProgressAsync(toDownload);
        Log.Info("[ProcedureUpdate] 下载完成: {0}/{1}", downloaded, toDownload.Count);

        // ── 2. 全部下载失败 ──
        if (downloaded == 0)
        {
            if (serverVersion.ForceUpdate)
            {
                bool retry = await ShowForceUpdateDialogAsync(
                    "更新下载失败，请检查网络后重试。");
                if (retry)
                {
                    // 重试整个更新流程（关闭当前表单，重新进入时新开；DefaultUIFormHelper 会 MoveToFront 置顶新弹窗，不会遮挡）
                    HotUpdateContext.CloseLoadingForm();
                    ChangeState<ProcedureUpdateVersion>(procedureOwner);
                    return;
                }
                MarkSuccessAndCloseLoading();
                GameEntry.Shutdown(ShutdownType.Quit);
                return;
            }

            HotUpdateContext.LoadingForm?.SetLogState("下载失败，请检查网络", 100);
            await Task.Delay(2000);
            await SkipToNextAsync(procedureOwner);
            MarkSuccessAndCloseLoading();
            return;
        }

        // ── 3. 先加载子包（验证可用） ──
        HotUpdateContext.LoadingForm?.SetLogState("加载资源...", 95);
        HotUpdateSafetyGuard.MarkStartupBegin();

        int failedCount = await LoadDownloadedPacksAsync(serverVersion);

        // ── 4. 部分加载失败 → 回退已生效，不得保存新版本，阻塞提示 ──
        // LoadDownloadedPacksAsync 内部失败时会 RollbackVersionFile（.bak 恢复旧版本），
        // 这里必须跳过保存 serverVersion，否则回退被覆盖 → 下次启动仍缺包重下。
        if (failedCount > 0)
        {
            Log.Warning("[ProcedureUpdate] {0} 个子包加载失败，本次更新未生效（版本文件已回退）。", failedCount);
            bool retry = await ShowForceUpdateDialogAsync(
                $"{failedCount} 个子包加载失败，本次更新未生效。\n请重试或退出游戏。");
            if (retry)
            {
                // 重试整个更新流程
                HotUpdateContext.CloseLoadingForm();
                ChangeState<ProcedureUpdateVersion>(procedureOwner);
                return;
            }
            MarkSuccessAndCloseLoading();
            GameEntry.Shutdown(ShutdownType.Quit);
            return;
        }

        // ── 5. 全部加载成功 → 刷新本地数据 ──
        // 不能只在"版本号变化"时保存：若服务端版本号没变但 .pck 哈希变了
        // （如重新导出过包），重启后完整性校验会拿旧哈希比对磁盘新文件 → 判定损坏
        // → 反复重下 + 反复弹"是否重启"，形成死循环。
        var localVersion = HotUpdateContext.LocalVersion;
        if (localVersion != null && EasySave.ExistsInUser(ResourceManager.GameFrameworkVersionData))
        {
            EasySave.SaveInUser(localVersion,
                ResourceManager.GameFrameworkVersionData + ".bak");
        }

        await EasySave.SaveInUserAsync(serverVersion, ResourceManager.GameFrameworkVersionData);
        ResourceManager.LocalPackVersionList = serverVersion;
        Log.Info("[ProcedureUpdate] 版本文件已保存。");

        HotUpdateContext.LoadingForm?.SetLogState("更新完成", 100);
        await Task.Delay(500);

        // 两按钮均退出：更新后需重启使 .pck 生效
        GF.UI.OpenQuestionTipsAsync("更新完成，是否重启？", "退出", "确认", () =>
        {
            GameEntry.Shutdown(ShutdownType.Quit);
        }, () =>
        {
            GameEntry.Shutdown(ShutdownType.Restart);
        });

        MarkSuccessAndCloseLoading();
    }

    // ── 下载逻辑 ──

    /// <summary>
    /// 批量并发下载（并发数由 DownloadComponent 的 agent 数调度），带聚合进度报告和 SHA256 校验。
    /// </summary>
    private async Task<int> DownloadPacksWithProgressAsync(
        List<(Pack Pack, string Url)> packs)
    {
        long totalBytes = 0;

        foreach (var (pack, _) in packs)
        {
            try
            {
                totalBytes = checked(totalBytes + pack.Size);
            }
            catch (OverflowException)
            {
                Log.Error("[ProcedureUpdate] 包大小累加溢出！已截断。请检查服务器 Pack.Size 配置。");
                totalBytes = long.MaxValue;
                break;
            }
        }

        HotUpdateContext.EnsureDirectory(SubpackDir);

        // 每包一个进度槽位；下载事件回调都在主线程，无需加锁
        long[] perPackBytes = new long[packs.Count];
        int completedCount = 0;

        void ReportAggregateProgress()
        {
            long sum = 0;
            for (int i = 0; i < perPackBytes.Length; i++)
                sum += perPackBytes[i];

            // 进度按字节加权
            int pct = totalBytes > 0
                ? 10 + (int)(80.0 * sum / totalBytes)
                : 10 + (int)(80.0 * completedCount / packs.Count);
            HotUpdateContext.LoadingForm?.SetLogState(
                $"下载中 {completedCount}/{packs.Count} ({StringExtension.FormatBytes(sum)}/{StringExtension.FormatBytes(totalBytes)})",
                Math.Min(pct, 90));
        }

        async Task<bool> RunPackAsync(int slot, Pack pack, string url, string savePath)
        {
            bool ok = await DownloadSinglePackWithRetryAsync(pack, url, savePath, bytes =>
            {
                perPackBytes[slot] = bytes;
                ReportAggregateProgress();
            });

            perPackBytes[slot] = ok ? pack.Size : 0;
            completedCount++;
            ReportAggregateProgress();
            return ok;
        }

        HotUpdateContext.LoadingForm?.SetLogState($"下载中 0/{packs.Count}", 10);

        var tasks = new List<Task<bool>>(packs.Count);
        for (int i = 0; i < packs.Count; i++)
        {
            var (pack, url) = packs[i];
            string savePath = Path.Combine(SubpackDir, pack.Name + ".pck");
            tasks.Add(RunPackAsync(i, pack, url, savePath));
        }

        bool[] results = await Task.WhenAll(tasks);

        int downloaded = 0;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i])
            {
                downloaded++;
                Log.Info("[ProcedureUpdate] 下载+校验成功: {0}", packs[i].Pack.Name);
            }
            else
            {
                Log.Error("[ProcedureUpdate] 下载失败（已重试 {0} 次）: {1}", MaxRetries, packs[i].Pack.Name);
                // 不跳过后续包——一个失败不影响其他包
            }
        }

        return downloaded;
    }

    /// <summary>
    /// 下载单个包（含重试 + 断点续传 + SHA256 校验），经由 GF.Download 统一下载通道。
    /// 失败时保留 .download 断点文件，下次重试自动续传。
    /// </summary>
    private async Task<bool> DownloadSinglePackWithRetryAsync(
        Pack pack, string url, string savePath, Action<long> onPackBytes)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                float delay = RetryBaseDelaySeconds * (1 << (attempt - 1));
                Log.Info("[ProcedureUpdate] 重试 {0}/{1}: {2}（{3:F1}s 后）",
                    attempt + 1, MaxRetries, pack.Name, delay);
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            try
            {
                bool ok = await GF.Download.DownloadFileAsync(
                    downloadUri: url,
                    downloadPath: savePath,
                    expectedSize: pack.Size,
                    expectedHash: pack.Hash,
                    onProgress: (downloaded, _) => onPackBytes(downloaded));

                if (ok)
                {
                    Log.Info("[ProcedureUpdate] 下载+校验成功: {0},路径:{1}", pack.Name, savePath);
                    return true;
                }

                Log.Warning("[ProcedureUpdate] 下载失败 (attempt {0}/{1}): {2}",
                    attempt + 1, MaxRetries, pack.Name);
            }
            catch (Exception ex)
            {
                Log.Error("[ProcedureUpdate] 下载异常: {0} — {1}", pack.Name, ex.Message);
            }
        }

        return false;
    }
}
