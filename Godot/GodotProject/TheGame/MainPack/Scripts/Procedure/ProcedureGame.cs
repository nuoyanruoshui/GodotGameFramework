using GameConfig;
using GameConfig.Entity;
using GameFramework.Procedure;
using GodotGameFramework;
using GodotGameFramework.Entity;
using GodotGameFramework.HotUpdate;
using GodotGameFramework.UI;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using GameLogic;

/// <summary>
/// 游戏流程。
/// </summary>
public class ProcedureGame : ProcedureBase
{

    /// <summary>
    /// 状态初始化（只调用一次）。
    /// 获取组件引用，订阅事件。
    /// </summary>
    protected internal override void OnInit(ProcedureOwner procedureOwner)
    {
        base.OnInit(procedureOwner);

    }

    /// <summary>
    /// 进入流程。
    /// 加载配置、重置游戏状态、创建并启动游戏状态 FSM。
    /// </summary>
    protected internal override async void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        // 标记启动成功：游戏已进入可玩状态，后续崩溃不再归因于热更
        HotUpdateSafetyGuard.MarkStartupSuccess();

        // await GF.UI.OpenUIFormAsync<MenuForm>(UIFormId.MenuForm);

        // 主菜单已打开，收掉加载遮罩（遮罩由 ProcedurePrelode 打开并保持到此）
        // LoadingForm.Current?.CloseLoading();
    }

    /// <summary>
    /// 每帧更新。
    /// </summary>
    protected internal override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

    }

    /// <summary>
    /// 离开流程。
    /// </summary>
    protected internal override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);


    }
}
