using GameFramework;
using GameFramework.WebRequest;

namespace GodotGameFramework.Debugger;

public sealed partial class DebuggerComponent
{
    /// <summary>
    /// Web 请求信息调试器窗口（代理计数）。
    /// </summary>
    private sealed class WebRequestInformationWindow : ScrollableDebuggerWindowBase
    {
        protected override void OnDrawScrollableWindow()
        {
            Draw.Title("Web Request Information");

            IWebRequestManager webRequestManager = GameFrameworkEntry.GetModule<IWebRequestManager>();
            if (webRequestManager == null)
            {
                Draw.Label("Web request manager is invalid.");
                return;
            }

            Draw.BeginTable();
            Draw.DrawItem("Total Agents", webRequestManager.TotalAgentCount.ToString());
            Draw.DrawItem("Free Agents", webRequestManager.FreeAgentCount.ToString());
            Draw.DrawItem("Working Agents", webRequestManager.WorkingAgentCount.ToString());
            Draw.DrawItem("Waiting Tasks", webRequestManager.WaitingTaskCount.ToString());
            Draw.EndTable();
        }
    }
}
