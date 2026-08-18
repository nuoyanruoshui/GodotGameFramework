//------------------------------------------------------------
// 版本检测流程（热更链第一环）
// 处理 Package 模式 / 崩溃恢复 / 无 RemoteUrl 短路分支，
// 请求服务器版本文件，检查 App 版本兼容性，成功后进入本地校验流程。
//------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GameFramework;
using GameFramework.Resource;
using Godot;
using GodotGameFramework;
using GodotGameFramework.HotUpdate;
using GodotGameFramework.Json;
using GodotGameFramework.Web;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;


/// <summary>
/// 版本检测流程。
/// 步骤：Package/崩溃/无地址短路 → 请求版本文件 → App 版本兼容性检查 → 进入本地校验。
/// </summary>
public class ProcedureUpdateVersion : ProcedureUpdateBase
{
    private const float VersionFetchTimeoutSeconds = 10f;    // 单次版本清单请求超时（秒）

    protected internal override async void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        try
        {
            await RunVersionCheckAsync(procedureOwner);
        }
        catch (Exception ex)
        {
            await HandleUpdateErrorAsync(procedureOwner, ex);
        }
    }

    private async Task RunVersionCheckAsync(ProcedureOwner procedureOwner)
    {
        // ── 1. Package 模式不检测更新，但尝试加载本地子包（安装目录 subpackages/） ──
        if (GF.Resource.ResourceMode == ResourceMode.Package)
        {
            Log.Info("[ProcedureUpdateVersion] Package 模式，跳过更新检测。");
            if (!GF.Base.EnableEditorResLoad)
            {
                await TryLoadLocalSubpackagesAsync();
            }
            else
            {
                Log.Info("[ProcedureUpdateVersion] EnableEditorResLoad，不加载子包");
            }
            ChangeState<ProcedurePrelode>(procedureOwner);
            return;
        }

        // ── 2. 崩溃恢复检测（必须在一切之前） ──
        if (HotUpdateSafetyGuard.WasLastSessionCrashed())
        {
            HotUpdateSafetyGuard.EnterSafeMode();
            Log.Warning("[ProcedureUpdateVersion] 上次启动崩溃，本次跳过所有热更补丁。");
            ChangeState<ProcedurePrelode>(procedureOwner);
            return;
        }

        // ── 3. 校验远程地址 ──
        string remoteUrl = GF.Resource.UpdateSettingRes?.RemoteUrl;
        if (string.IsNullOrEmpty(remoteUrl))
        {
            // 没有远程地址 → 跳过更新检测，但仍加载已下载的本地补丁
            Log.Warning("[ProcedureUpdateVersion] 未配置 RemoteUrl，跳过更新检测。");
            var localVersion = EasySave.LoadFromUser<PackVersionList>(
                ResourceManager.GameFrameworkVersionData);
            if (localVersion != null)
            {
                int damaged = await VerifyLocalPackIntegrityAsync(localVersion);
                if (damaged > 0)
                    Log.Warning("[ProcedureUpdateVersion] 本地 {0} 个文件损坏，但无法连接服务器修复。", damaged);
                await LoadDownloadedPacksAsync(localVersion);
            }
            ChangeState<ProcedurePrelode>(procedureOwner);
            return;
        }

        // ── 正常路径：打开 LoadingForm
        HotUpdateContext.ServerVersion = null;
        await HotUpdateContext.EnsureLoadingFormAsync();

        // ── 4. 请求服务器版本文件 ──
        bool isForceUpdate = false;
        PackVersionList serverVersion = null;

        string versionUrl = $"{remoteUrl.TrimEnd('/')}/{ResourceManager.GameFrameworkVersionData}";
        Log.Info("[ProcedureUpdateVersion] 请求版本文件: {0}", versionUrl);
        HotUpdateContext.LoadingForm?.SetLogState("检测更新...", 0);

        // 先加载本地版本，用于 ForceUpdate 回退判断（带完整性校验）
        var localVersionPre = ResourceManager.LocalPackVersionList
            ?? NodeUtility.LoadAndValidateVersionList(ResourceManager.GameFrameworkVersionData);
        if (localVersionPre != null)
            ResourceManager.LocalPackVersionList = localVersionPre;
        isForceUpdate = localVersionPre?.ForceUpdate == true;

        serverVersion = await FetchVersionWithRetryAsync(versionUrl);
        if (serverVersion == null || !serverVersion.IsValid())
        {
            // 更新模式下，服务器不可达时阻塞等待
            string msg = localVersionPre?.ForceUpdate == true
                ? "本次为强制更新，但无法连接服务器。\n请检查网络后重试。"
                : "版本检测失败，请检查网络后重试。";
            bool retry = await ShowForceUpdateDialogAsync(msg);
            if (retry)
            {
                // 重试整个更新流程（关闭当前表单，重新进入时新开）
                HotUpdateContext.CloseLoadingForm();
                ChangeState<ProcedureUpdateVersion>(procedureOwner);
                return;
            }
            MarkSuccessAndCloseLoading();
            GameEntry.Shutdown(ShutdownType.Quit);
            return;
        }

        isForceUpdate = serverVersion.ForceUpdate;
        Log.Info("[ProcedureUpdateVersion] 服务器版本: {0}, {1} 个子包",
            serverVersion.Version, serverVersion.Packs?.Length ?? 0);

        // ── 5. 版本兼容性检查 ──
        string appVersion = NodeUtility.GetAppVersion();
        if (!string.IsNullOrEmpty(serverVersion.MinAppVersion) && NodeUtility.CompareVersions(appVersion, serverVersion.MinAppVersion) < 0)
        {
            Log.Warning("[ProcedureUpdateVersion] App 版本过低 ({0} < {1})，需要去商店更新。",
                appVersion, serverVersion.MinAppVersion);
            HotUpdateContext.LoadingForm?.SetLogState("请更新App版本", 100);
            await Task.Delay(3000);
            await ShowForceUpdateDialogAsync("客户端版本过低，请升级客户端后再试。");
            MarkSuccessAndCloseLoading();
            GameEntry.Shutdown(ShutdownType.Quit);
            return;
        }

        // ── 进入本地校验流程（LoadingForm 保持开启） ──
        HotUpdateContext.ServerVersion = serverVersion;
        ChangeState<ProcedureCheckResources>(procedureOwner);
    }

    // ── 版本文件请求 ──

    private async Task<PackVersionList> FetchVersionWithRetryAsync(string versionUrl)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                float delay = RetryBaseDelaySeconds * (1 << (attempt - 1));
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            try
            {
                if (!GF.Base.EnableEditorResLoad)
                {
                    var result = await GF.WebRequest.SendRequestAsync(versionUrl, VersionFetchTimeoutSeconds);
                    if (!IsHttpSuccess(result))
                    {
                        HotUpdateContext.LoadingForm?.SetLogState($"版本文件请求失败(再次尝试:{attempt + 1}/{MaxRetries})", 0);
                        Log.Warning("[ProcedureUpdateVersion] 版本文件请求失败 (attempt {0}/{1}, HTTP {2})",
                            attempt + 1, MaxRetries, result?.ResponseCode);
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(result.Body);
                    var version = Utility.Json.ToObject<PackVersionList>(json);
                    if (version != null)
                    {
                        // 校验服务器版本清单完整性
                        if (!version.Validate(out string validateError))
                        {
                            Log.Warning("[ProcedureUpdateVersion] 服务器版本数据校验失败: {0} (attempt {1}/{2})",
                                validateError, attempt + 1, MaxRetries);
                            continue;
                        }
                        return version;
                    }

                    Log.Warning("[ProcedureUpdateVersion] 版本 JSON 解析为 null (attempt {0}/{1})",
                        attempt + 1, MaxRetries);
                }
                else
                {
                    string folder = GodotGameFramework.Editor.ExportInspector._exportFolder;
                    //获得时间最晚的文件夹
                    Directory.GetDirectories(folder);
                    //TODO:
                }
            }
            catch (Exception ex)
            {
                Log.Error("[ProcedureUpdateVersion] 版本文件请求异常: {0}", ex.Message);
            }
        }

        return null;
    }

    private bool IsHttpSuccess(WebRequestCompleteEventArgs result)
    {
        if (result == null) return false;
        if (result.Result == -1 && result.ResponseCode == 0) return false; // timeout
        if (result.ResponseCode != 200 || result.Result != (long)Error.Ok) return false;
        if (result.Body == null || result.Body.Length == 0) return false;
        return true;
    }
}
