using GameFramework;
using GameFramework.Download;

namespace GodotGameFramework.Debugger;

public sealed partial class DebuggerComponent
{
    /// <summary>
    /// 下载信息调试器窗口（代理计数）。
    /// </summary>
    private sealed class DownloadInformationWindow : ScrollableDebuggerWindowBase
    {
        protected override void OnDrawScrollableWindow()
        {
            Draw.Title("Download Information");

            IDownloadManager downloadManager = GameFrameworkEntry.GetModule<IDownloadManager>();
            if (downloadManager == null)
            {
                Draw.Label("Download manager is invalid.");
                return;
            }

            Draw.BeginTable();
            Draw.DrawItem("Total Agents", downloadManager.TotalAgentCount.ToString());
            Draw.DrawItem("Free Agents", downloadManager.FreeAgentCount.ToString());
            Draw.DrawItem("Working Agents", downloadManager.WorkingAgentCount.ToString());
            Draw.DrawItem("Waiting Tasks", downloadManager.WaitingTaskCount.ToString());
            Draw.EndTable();
        }
    }
}
