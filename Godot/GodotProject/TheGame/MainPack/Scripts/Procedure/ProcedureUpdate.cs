//------------------------------------------------------------
// 更新检测流程
// 连接服务器检测版本更新，有更新则下载补丁包
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using GameConfig.Constant;
using GameFramework;
using GameFramework.Procedure;
using GameFramework.Resource;
using GameLogic;
using Godot;
using GodotGameFramework;
using GodotGameFramework.HotUpdate;
using GodotGameFramework.Json;
using GodotGameFramework.Web;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using GodotGameFramework.Download;
using GodotGameFramework.Extensions;
using GodotGameFramework.UI;

/// <summary>
/// 更新检测流程。
/// 步骤：校验本地客户端 -> 请求版本文件 → 比对本地 → 下载差量包 → SHA256 校验 → 保存版本 → 加载子包
/// </summary>
public class ProcedureUpdate : ProcedureBase
{
    private const int MaxRetries = 3;
    private const float RetryBaseDelaySeconds = 1.5f;
    private const float VersionFetchTimeoutSeconds = 10f;    // 单次版本清单请求超时（秒），不依赖全局 30s
    LoadingForm m_loadingForm;

    /// <summary>
    /// 热更补丁存储目录。
    /// 自动选择：配置优先 → 游戏目录（可写时） → user:// 回退
    /// </summary>
    private string SubpackDir => GetOrCreateHotUpdateDir();

    private string GetOrCreateHotUpdateDir()
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

    private bool IsDirectoryWritable(string path)
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

    protected internal override void OnInit(ProcedureOwner procedureOwner)
    {
        base.OnInit(procedureOwner);
    }

    protected internal override async void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        try
        {
            await RunUpdateFlowAsync(procedureOwner);
        }
        catch (Exception ex)
        {
            Log.Error("[ProcedureUpdate] 更新流程异常: {0}", ex);

            // 检查是否为强制更新，若是则阻塞而非跳过
            var localVersion = EasySave.LoadFromUser<PackVersionList>(
                ResourceManager.GameFrameworkVersionData);
            if (localVersion?.ForceUpdate == true)
            {
                bool retry = await ShowForceUpdateDialogAsync("更新流程异常: " + ex.Message + "请重试或退出游戏。");
                if (retry)
                {
                    await RunUpdateFlowAsync(procedureOwner);
                    return;
                }
                GameEntry.Shutdown(ShutdownType.Quit);
                return;
            }

            await SkipToNextAsync(procedureOwner);
        }
    }

    // ── 主流程 ──

    private async Task RunUpdateFlowAsync(ProcedureOwner procedureOwner)
    {
        // Package 模式不检测更新，但尝试加载本地子包（安装目录 subpackages/）
        if (GF.Resource.ResourceMode == ResourceMode.Package)
        {
            Log.Info("[ProcedureUpdate] Package 模式，跳过更新检测。");
            if (!GF.Base.EnableEditorResLoad)
            {
                await TryLoadLocalSubpackagesAsync();
            }
            ChangeState<ProcedurePrelode>(procedureOwner);
            return;
        }

        // ── 0. 崩溃恢复检测（必须在一切之前） ──
        if (HotUpdateSafetyGuard.WasLastSessionCrashed())
        {
            HotUpdateSafetyGuard.EnterSafeMode();
            Log.Warning("[ProcedureUpdate] 上次启动崩溃，本次跳过所有热更补丁。");
            ChangeState<ProcedurePrelode>(procedureOwner);
            return;
        }

        // 校验远程地址
        string remoteUrl = GF.Resource.UpdateSettingRes?.RemoteUrl;
        if (string.IsNullOrEmpty(remoteUrl))
        {
            // 没有远程地址 → 跳过更新检测，但仍加载已下载的本地补丁
            Log.Warning("[ProcedureUpdate] 未配置 RemoteUrl，跳过更新检测。");
            var localVersion = EasySave.LoadFromUser<PackVersionList>(
                ResourceManager.GameFrameworkVersionData);
            if (localVersion != null)
            {
                int damaged = await VerifyLocalPackIntegrityAsync(localVersion);
                if (damaged > 0)
                    Log.Warning("[ProcedureUpdate] 本地 {0} 个文件损坏，但无法连接服务器修复。", damaged);
                await LoadDownloadedPacksAsync(localVersion);
            }
            ChangeState<ProcedurePrelode>(procedureOwner);
            return;
        }

        m_loadingForm = await GF.UI.OpenLoadingUIFormAsync();

        try
        {
            bool isForceUpdate = false;
            PackVersionList serverVersion = null;

            // ── 1. 请求服务器版本文件 ──
            string versionUrl = $"{remoteUrl.TrimEnd('/')}/{ResourceManager.GameFrameworkVersionData}";
            Log.Info("[ProcedureUpdate] 请求版本文件: {0}", versionUrl);
            m_loadingForm?.SetLogState("检测更新...", 0);

            // 先加载本地版本，用于 ForceUpdate 回退判断（带完整性校验）
            var localVersionPre = ResourceManager.LocalPackVersionList
                ?? NodeUtility.LoadAndValidateVersionList(ResourceManager.GameFrameworkVersionData);
            if (localVersionPre != null)
                ResourceManager.LocalPackVersionList = localVersionPre;
            isForceUpdate = localVersionPre?.ForceUpdate == true;

            serverVersion = await FetchVersionWithRetryAsync(versionUrl);
            if (serverVersion == null || !serverVersion.IsValid())
            {
                if (isForceUpdate)
                {
                    // 强制更新模式下，服务器不可达时阻塞等待
                    string msg = localVersionPre?.ForceUpdate == true
                        ? "本次为强制更新，但无法连接服务器。\n请检查网络后重试。"
                        : "版本检测失败，请检查网络后重试。";
                    bool retry = await ShowForceUpdateDialogAsync(msg);
                    if (retry)
                    {
                        // 重试整个更新流程
                        GF.UI.CloseUIForm(m_loadingForm);
                        await RunUpdateFlowAsync(procedureOwner);
                        return;
                    }
                    GameEntry.Shutdown(ShutdownType.Quit);
                    return;
                }

                m_loadingForm?.SetLogState("版本检测失败", 100);
                await Task.Delay(1500);
                await SkipToNextAsync(procedureOwner);
                return;
            }

            isForceUpdate = serverVersion.ForceUpdate;
            Log.Info("[ProcedureUpdate] 服务器版本: {0}, {1} 个子包",
                serverVersion.Version, serverVersion.Packs?.Length ?? 0);

            // ── 2. 版本兼容性检查 ──
            string appVersion = NodeUtility.GetAppVersion();
            if (!string.IsNullOrEmpty(serverVersion.MinAppVersion) && NodeUtility.CompareVersions(appVersion, serverVersion.MinAppVersion) < 0)
            {
                Log.Warning("[ProcedureUpdate] App 版本过低 ({0} < {1})，需要去商店更新。",
                    appVersion, serverVersion.MinAppVersion);
                m_loadingForm?.SetLogState("请更新App版本", 100);
                await Task.Delay(3000);
                await ShowForceUpdateDialogAsync("客户端版本过低，请升级客户端后再试。");
                HotUpdateSafetyGuard.MarkStartupSuccess();
                GameEntry.Shutdown(ShutdownType.Quit);
                return;
            }

            // ── 3. 加载本地版本并校验完整性 ──
            var localVersion = ResourceManager.LocalPackVersionList
                ?? NodeUtility.LoadAndValidateVersionList(ResourceManager.GameFrameworkVersionData);
            m_loadingForm?.SetLogState("校验本地数据...", 5);

            int damagedCount = await VerifyLocalPackIntegrityAsync(localVersion);
            if (damagedCount > 0)
            {
                Log.Warning("[ProcedureUpdate] 本地客户端不完整！{0} 个包已损坏或丢失，将重新下载。", damagedCount);
                m_loadingForm?.SetLogState($"检测到 {damagedCount} 个文件损坏,即将修复", 8);
            }

            // ── 4. 与服务器版本比对 ──
            var toDownload = FindPacksToUpdate(serverVersion, localVersion);

            if (toDownload.Count > 0 && isForceUpdate)
            {
                Log.Info("[ProcedureUpdate] 本次为强制更新。");
            }

            // ── 4. 下载更新的包 ──
            if (toDownload.Count > 0)
            {
                // 磁盘空间预检
                long totalSize = toDownload.Sum(x => x.Pack.Size);
                long freeSpace = NodeUtility.GetFreeDiskSpace(SubpackDir);
                if (freeSpace > 0 && freeSpace < totalSize * 2)
                {
                    Log.Warning("[ProcedureUpdate] 磁盘空间不足: 需要 {0}, 可用 {1}",
                        StringExtension.FormatBytes(totalSize * 2), StringExtension.FormatBytes(freeSpace));
                    m_loadingForm?.SetLogState("磁盘空间不足", 100);

                    if (isForceUpdate)
                    {
                        await Task.Delay(1000);
                        bool retry = await ShowForceUpdateDialogAsync(
                            $"磁盘空间不足（需要 {StringExtension.FormatBytes(totalSize * 2)}），请清理后重试。");
                        if (retry)
                        {
                            GF.UI.CloseUIForm(m_loadingForm);
                            await RunUpdateFlowAsync(procedureOwner);
                            return;
                        }
                        GameEntry.Shutdown(ShutdownType.Quit);
                        return;
                    }

                    await Task.Delay(2000);
                    await SkipToNextAsync(procedureOwner);
                    return;
                }

                Log.Info("[ProcedureUpdate] 共 {0} 个包需要更新，总计 {1}，开始下载...",
                    toDownload.Count, StringExtension.FormatBytes(totalSize));
                int downloaded = await DownloadPacksWithProgressAsync(toDownload);
                Log.Info("[ProcedureUpdate] 下载完成: {0}/{1}", downloaded, toDownload.Count);

                if (downloaded == 0)
                {
                    if (isForceUpdate)
                    {
                        bool retry = await ShowForceUpdateDialogAsync(
                            "更新下载失败，请检查网络后重试。");
                        if (retry)
                        {
                            GF.UI.CloseUIForm(m_loadingForm);
                            await RunUpdateFlowAsync(procedureOwner);
                            return;
                        }
                        GameEntry.Shutdown(ShutdownType.Quit);
                        return;
                    }

                    m_loadingForm?.SetLogState("下载失败，请检查网络", 100);
                    await Task.Delay(2000);
                    await SkipToNextAsync(procedureOwner);
                    return;
                }

                // ── 4. 先加载子包（验证可用） ──
                m_loadingForm?.SetLogState("加载资源...", 95);
                HotUpdateSafetyGuard.MarkStartupBegin();

                await LoadDownloadedPacksAsync(serverVersion);

                // ── 5. 加载成功后再保存版本文件（带备份） ──
                if (localVersion == null || localVersion.Version != serverVersion.Version)
                {
                    if (EasySave.ExistsInUser(ResourceManager.GameFrameworkVersionData))
                    {
                        EasySave.SaveInUser(localVersion,
                            ResourceManager.GameFrameworkVersionData + ".bak");
                    }

                    await EasySave.SaveInUserAsync(serverVersion, ResourceManager.GameFrameworkVersionData);
                    ResourceManager.LocalPackVersionList = serverVersion;
                    Log.Info("[ProcedureUpdate] 版本文件已保存。");
                }

                m_loadingForm?.SetLogState("更新完成", 100);
                await Task.Delay(500);

                GF.UI.OpenQuestionTipsAsync("更新完成，是否重启？", "退出", "确认", () =>
                {
                    HotUpdateSafetyGuard.MarkStartupSuccess();
                    GameEntry.Shutdown(ShutdownType.Quit);
                }, () =>
                {
                    HotUpdateSafetyGuard.MarkStartupSuccess();
                    GameEntry.Shutdown(ShutdownType.Restart);
                });
            }
            else
            {
                await LoadDownloadedPacksAsync(localVersion);
                ChangeState<ProcedurePrelode>(procedureOwner);
                Log.Info("[ProcedureUpdate] 所有包已是最新，无需下载。");
            }
        }
        finally
        {
            GF.UI.CloseUIForm(m_loadingForm);
        }

    }

    /// <summary>
    /// 强制更新/阻塞对话框。
    /// 显示提示信息，提供"退出游戏"和"重试"按钮。
    /// </summary>
    /// <returns>true = 用户选择重试</returns>
    private async Task<bool> ShowForceUpdateDialogAsync(string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        m_loadingForm?.SetLogState(message, 100);

        GF.UI.OpenQuestionTipsAsync(message, "退出", "重试", () =>
        {
            tcs.TrySetResult(false); // 退出
        }, () =>
        {
            tcs.TrySetResult(true);  // 重试
        });

        return await tcs.Task;
    }


    // ── 版本比对 ──

    /// <summary>
    /// 校验本地版本中所有 .pck 文件的完整性。
    /// 检查：文件存在 + 大小匹配 + SHA256 匹配（>1MB 文件）。
    /// 损坏或丢失的包从 localVersion 中移除，后续会自动与服务器对齐重新下载。
    /// </summary>
    /// <returns>损坏/丢失的包数量</returns>
    private async Task<int> VerifyLocalPackIntegrityAsync(PackVersionList localVersion)
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
                // 3. SHA256 校验（>1MB 文件，线程池执行避免卡帧）
                else if (fileInfo.Length > 1024 * 1024)
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

        EnsureDirectory(SubpackDir);

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
            m_loadingForm?.SetLogState(
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

        m_loadingForm?.SetLogState($"下载中 0/{packs.Count}", 10);

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
                var result = await GF.WebRequest.SendRequestAsync(versionUrl, VersionFetchTimeoutSeconds);
                if (!IsHttpSuccess(result))
                {
                    m_loadingForm.SetLogState($"版本文件请求失败(再次尝试:{attempt + 1}/{MaxRetries})", 0);
                    Log.Warning("[ProcedureUpdate] 版本文件请求失败 (attempt {0}/{1}, HTTP {2})",
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
                        Log.Warning("[ProcedureUpdate] 服务器版本数据校验失败: {0} (attempt {1}/{2})",
                            validateError, attempt + 1, MaxRetries);
                        continue;
                    }
                    return version;
                }

                Log.Warning("[ProcedureUpdate] 版本 JSON 解析为 null (attempt {0}/{1})",
                    attempt + 1, MaxRetries);
            }
            catch (Exception ex)
            {
                Log.Error("[ProcedureUpdate] 版本文件请求异常: {0}", ex.Message);
            }
        }

        return null;
    }

    // ── 子包加载 ──

    /// <summary>
    /// 加载已下载的 .pck 子包。
    /// 先加载 Config 类型（Luban/本地化），再加载 Resource 类型（场景/贴图）。
    /// 加载前大小校验，对大文件做 SHA256 重校验。
    /// </summary>
    private async Task LoadDownloadedPacksAsync(PackVersionList version)
    {
        if (version?.Packs == null || version.Packs.Length == 0)
            return;

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

            // 大小校验
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

            // 对大文件做 SHA256 重校验（线程池执行，防御磁盘静默损坏）
            if (fileInfo.Length > 1024 * 1024)
            {
                try
                {
                    string actualHash = await Task.Run(() => NodeUtility.ComputeSHA256(packPath));
                    if (!string.Equals(actualHash, pack.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning("[ProcedureUpdate] SHA256 重校验失败，文件可能损坏: {0}", pack.Name);
                        if (!EasySave.TryDelete(packPath))
                            Log.Warning("[ProcedureUpdate] 无法删除损坏文件（可能被占用）: {0}", packPath);
                        failed++;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[ProcedureUpdate] SHA256 计算失败: {0} — {1}", pack.Name, ex.Message);
                    failed++;
                    continue;
                }
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
    }

    /// <summary>清理磁盘上不在版本清单中的废弃 .pck 文件。</summary>
    private void CleanStalePacks(PackVersionList version)
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

    /// <summary>回退版本文件到备份。</summary>
    private void RollbackVersionFile()
    {
        try
        {
            string versionPath = Path.Combine(
                ProjectSettings.GlobalizePath("user://"), ResourceManager.GameFrameworkVersionData);
            string backupPath = versionPath + ".bak";

            if (File.Exists(backupPath))
            {
                EasySave.TryDelete(versionPath);
                File.Move(backupPath, versionPath);
                Log.Info("[ProcedureUpdate] 已自动回退到上一版本。");
            }
        }
        catch (Exception ex)
        {
            Log.Error("[ProcedureUpdate] 版本回退失败: {0}", ex.Message);
        }
    }

    // ── 工具方法 ──

    /// <summary>
    /// Package 模式下尝试加载安装目录中的本地子包。
    /// 读取 SubpackDir 下的 GameFrameworkVersion.dat 清单，加载所有 .pck。
    /// 失败不阻塞启动——日志记录后静默跳过。
    /// </summary>
    private async Task TryLoadLocalSubpackagesAsync()
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

    private bool IsHttpSuccess(WebRequestCompleteEventArgs result)
    {
        if (result == null) return false;
        if (result.Result == -1 && result.ResponseCode == 0) return false; // timeout
        if (result.ResponseCode != 200 || result.Result != (long)Error.Ok) return false;
        if (result.Body == null || result.Body.Length == 0) return false;
        return true;
    }

    private void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private async Task SkipToNextAsync(ProcedureOwner procedureOwner)
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

    protected internal override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
    }
}
