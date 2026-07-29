//------------------------------------------------------------
// 启动流程（LaunchProcedure）
// 游戏的入口流程，完成框架初始化、加载配置和数据表、创建实体组
//------------------------------------------------------------

using System.Collections.Concurrent;
using System.Linq;
using GameConfig.Constant;
using GameFramework;
using GameFramework.Procedure;
using GodotGameFramework;
using GodotGameFramework.NodePool;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

/// <summary>
/// 启动流程。检测框架组件是否正常
/// </summary>
public class ProcedureLaunch : ProcedureBase
{
    private static readonly ConcurrentDictionary<string, bool> m_Components = new ConcurrentDictionary<string, bool>();
    private static readonly string[] m_NeedComponents = { "Base", "Event", "Fsm", "Setting", "DataNode", "Resource", "Entity", "UI", "Sound", "Localization", "WebRequest", "Download" };
    /// <summary>
    /// 状态初始化。
    /// </summary>
    protected internal override void OnInit(ProcedureOwner procedureOwner)
    {
        base.OnInit(procedureOwner);
        foreach (var component in m_NeedComponents)
        {
            m_Components.TryAdd(component, false);
        }
    }

    /// <summary>
    /// 进入流程。
    /// 执行所有初始化工作后立即切换到菜单流程。
    /// </summary>
    protected internal override void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        Log.FeiBi();
        Log.Info($"[LaunchProcedure] 验证框架组件...");
        m_Components.TryUpdate(m_NeedComponents[0], GF.Base != null, false);
        m_Components.TryUpdate(m_NeedComponents[1], GF.Event != null, false);
        m_Components.TryUpdate(m_NeedComponents[2], GF.Fsm != null, false);
        m_Components.TryUpdate(m_NeedComponents[3], GF.Setting != null, false);
        m_Components.TryUpdate(m_NeedComponents[4], GF.DataNode != null, false);
        m_Components.TryUpdate(m_NeedComponents[5], GF.Resource != null, false);
        m_Components.TryUpdate(m_NeedComponents[6], GF.Entity != null, false);
        m_Components.TryUpdate(m_NeedComponents[7], GF.UI != null, false);
        m_Components.TryUpdate(m_NeedComponents[8], GF.Sound != null, false);
        m_Components.TryUpdate(m_NeedComponents[9], GF.Localization != null, false);
        m_Components.TryUpdate(m_NeedComponents[10], GF.WebRequest != null, false);
        m_Components.TryUpdate(m_NeedComponents[11], GF.Download != null, false);

        if (m_Components.All(x => x.Value))
        {
            Log.Info($"[LaunchProcedure] 框架组件验证通过");
            ChangeState<ProcedureUpdate>(procedureOwner);
        }
        else
        {
            Log.Fatal($"[LaunchProcedure] 框架组件{m_Components.Where(x => !x.Value).Select(x => x.Key).Aggregate((a, b) => a + "," + b)}验证失败");
        }
    }


    /// <summary>
    /// 离开流程。
    /// </summary>
    protected internal override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
    }
}
