//------------------------------------------------------------
// 本地资源校验流程（热更链第二环）
// 校验本地包完整性，与服务器版本差量比对，磁盘空间预检。
// 无需下载 → 加载本地补丁进入 Prelode；需要下载 → 进入下载流程。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using GameFramework;
using GameFramework.Resource;
using GodotGameFramework;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using GodotGameFramework.Extensions;

/// <summary>
/// 本地资源校验流程。
/// 步骤：本地完整性校验 → 差量比对 → 磁盘预检 → 决定进入下载或 Prelode。
/// </summary>
public class ProcedureCheckResources : ProcedureUpdateBase
{
    protected internal override async void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        try
        {
            await RunCheckResourcesAsync(procedureOwner);
        }
        catch (Exception ex)
        {
            await HandleUpdateErrorAsync(procedureOwner, ex);
        }
    }

    private async Task RunCheckResourcesAsync(ProcedureOwner procedureOwner)
    {
        // 防御：无服务器版本（异常跳转导致）→ 跳过
        var serverVersion = HotUpdateContext.ServerVersion;
        if (serverVersion == null)
        {
            await SkipToNextAsync(procedureOwner);
            MarkSuccessAndCloseLoading();
            return;
        }

        // ── 1. 加载本地版本并校验完整性 ──
        var localVersion = ResourceManager.LocalPackVersionList
            ?? NodeUtility.LoadAndValidateVersionList(ResourceManager.GameFrameworkVersionData);
        HotUpdateContext.LocalVersion = localVersion;
        HotUpdateContext.LoadingForm?.SetLogState("校验本地数据...", 5);

        int damagedCount = await VerifyLocalPackIntegrityAsync(localVersion);
        if (damagedCount > 0)
        {
            Log.Warning("[ProcedureUpdate] 本地客户端不完整！{0} 个包已损坏或丢失，将重新下载。", damagedCount);
            HotUpdateContext.LoadingForm?.SetLogState($"检测到 {damagedCount} 个文件损坏,即将修复", 8);
        }

        // ── 2. 与服务器版本比对 ──
        var toDownload = FindPacksToUpdate(serverVersion, localVersion);

        if (toDownload.Count > 0 && serverVersion.ForceUpdate)
        {
            Log.Info("[ProcedureUpdate] 本次为强制更新。");
        }
        HotUpdateContext.ToDownload = toDownload;

        // ── 3. 无需下载 → 加载本地补丁进入 Prelode ──
        if (toDownload.Count == 0)
        {
            await LoadDownloadedPacksAsync(localVersion);
            ChangeState<ProcedurePrelode>(procedureOwner);
            Log.Info("[ProcedureUpdate] 所有包已是最新，无需下载。");
            MarkSuccessAndCloseLoading();
            return;
        }

        // ── 4. 磁盘空间预检 ──
        long totalSize = HotUpdateContext.ToDownloadTotalSize = toDownload.Sum(x => x.Pack.Size);
        long freeSpace = NodeUtility.GetFreeDiskSpace(SubpackDir);
        if (freeSpace > 0 && freeSpace < totalSize * 2)
        {
            Log.Warning("[ProcedureUpdate] 磁盘空间不足: 需要 {0}, 可用 {1}",
                StringExtension.FormatBytes(totalSize * 2), StringExtension.FormatBytes(freeSpace));
            HotUpdateContext.LoadingForm?.SetLogState("磁盘空间不足", 100);

            if (serverVersion.ForceUpdate)
            {
                await Task.Delay(1000);
                bool retry = await ShowForceUpdateDialogAsync(
                    $"磁盘空间不足（需要 {StringExtension.FormatBytes(totalSize * 2)}），请清理后重试。");
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

            await Task.Delay(2000);
            await SkipToNextAsync(procedureOwner);
            MarkSuccessAndCloseLoading();
            return;
        }

        // ── 需要下载 → 进入下载流程（LoadingForm 保持开启） ──
        ChangeState<ProcedureUpdateResources>(procedureOwner);
    }

    // ── 版本比对 ──

    /// <summary>
    /// 比对本机与服务器版本，返回需要下载的包列表（含下载 URL）。
    /// </summary>
    private List<(Pack Pack, string Url)> FindPacksToUpdate(
        PackVersionList server, PackVersionList local)
    {
        var toDownload = new List<(Pack, string)>();

        if (server?.Packs == null || server.Packs.Length == 0)
            return toDownload;

        var localDict = new Dictionary<string, Pack>();
        if (local?.Packs != null)
        {
            foreach (var lp in local.Packs)
            {
                if (!string.IsNullOrEmpty(lp.Name))
                    localDict[lp.Name] = lp;
            }
        }

        foreach (var sp in server.Packs)
        {
            if (!sp.IsValid())
            {
                Log.Warning("[ProcedureUpdate] 服务器版本中的包数据无效: {0}", sp.Name ?? "(null)");
                continue;
            }

            string url = !string.IsNullOrEmpty(sp.Url)
                ? sp.Url
                : $"{Utility.Path.GetRemotePath(GF.Resource.UpdateSettingRes?.RemoteUrl)}/{sp.Name}.pck";

            if (!localDict.TryGetValue(sp.Name, out var lp))
            {
                Log.Info("[ProcedureUpdate] 发现新包: {0} ({1} bytes)", sp.Name, sp.Size);
                toDownload.Add((sp, url));
            }
            else if (!string.Equals(lp.Hash, sp.Hash, StringComparison.OrdinalIgnoreCase) || lp.Size != sp.Size)
            {
                Log.Info("[ProcedureUpdate] 包有更新: {0} ({1}→{2} bytes)",
                    sp.Name, lp.Size, sp.Size);
                toDownload.Add((sp, url));
            }
        }

        return toDownload;
    }
}
