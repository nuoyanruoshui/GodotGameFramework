using GameFramework;
using GameFramework.Resource;

namespace GodotGameFramework.Debugger;

public sealed partial class DebuggerComponent
{
    /// <summary>
    /// 资源系统信息调试器窗口（资产/二进制加载代理计数）。
    /// </summary>
    private sealed class ResourceInformationWindow : ScrollableDebuggerWindowBase
    {
        protected override void OnDrawScrollableWindow()
        {
            Draw.Title("Resource Information");

            IResourceManager resourceManager = GameFrameworkEntry.GetModule<IResourceManager>();
            if (resourceManager == null)
            {
                Draw.Label("Resource manager is invalid.");
                return;
            }

            Draw.Space();
            Draw.Title("Asset Load Agents");
            Draw.BeginTable();
            Draw.DrawItem("Resource Mode", resourceManager.ResourceMode.ToString());
            Draw.DrawItem("Total Agents", resourceManager.TotalAssetAgentCount.ToString());
            Draw.DrawItem("Free Agents", resourceManager.FreeAssetAgentCount.ToString());
            Draw.DrawItem("Working Agents", resourceManager.WorkingAssetAgentCount.ToString());
            Draw.DrawItem("Waiting Tasks", resourceManager.WaitingAssetTaskCount.ToString());
            Draw.EndTable();

            Draw.Space();
            Draw.Title("Binary Load Agents");
            Draw.BeginTable();
            Draw.DrawItem("Total Agents", resourceManager.TotalBinaryAgentCount.ToString());
            Draw.DrawItem("Free Agents", resourceManager.FreeBinaryAgentCount.ToString());
            Draw.DrawItem("Working Agents", resourceManager.WorkingBinaryAgentCount.ToString());
            Draw.DrawItem("Waiting Tasks", resourceManager.WaitingBinaryTaskCount.ToString());
            Draw.EndTable();
        }
    }
}
