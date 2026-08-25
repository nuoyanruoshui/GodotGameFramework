//------------------------------------------------------------
// 启动流程（检测更新）
// 游戏的入口流程，完成框架初始化、加载配置和数据表、创建实体组
//------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using GameConfig.Constant;
using GameFramework;
using GameFramework.Event;
using GameFramework.Localization;
using GameFramework.Procedure;
using GodotGameFramework;
using GodotGameFramework.NodePool;
using GodotGameFramework.Sound;
using GodotGameFramework.UI;
using GameLogic;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

/// <summary>
/// 启动流程。
/// </summary>
public class ProcedurePrelode : ProcedureBase
{
    private static readonly ConcurrentDictionary<string, bool> m_LoadFlagDic = new ConcurrentDictionary<string, bool>();
    private static readonly string[] m_LoadFlagKeys = { "Localization", "UIGroup", "EntityGroup", "SoundGroup" };
    /// <summary>
    /// 状态初始化。
    /// </summary>
    protected internal override void OnInit(ProcedureOwner procedureOwner)
    {
        base.OnInit(procedureOwner);
    }

    /// <summary>
    /// 进入流程。
    /// 执行所有初始化工作后立即切换到菜单流程。
    /// </summary>
    protected internal async override void OnEnter(ProcedureOwner procedureOwner)
    {
        base.OnEnter(procedureOwner);

        try
        {
            LoadEntityGroup();
        }
        catch (System.Exception ex)
        {
            Log.Fatal("[ProcedurePrelode] 加载实体组失败（.pck 可能缺失依赖资源）: {0}", ex);
        }

        try
        {
            LoadLocalization();
        }
        catch (System.Exception ex)
        {
            Log.Fatal("[ProcedurePrelode] 加载本地化失败: {0}", ex);
        }

        try
        {
            LoadUIGroup();
        }
        catch (System.Exception ex)
        {
            Log.Fatal("[ProcedurePrelode] 加载 UI 组失败（.pck 可能缺失依赖资源）: {0}", ex);
        }

        try
        {
            LoadSoundGroup();
        }
        catch (System.Exception ex)
        {
            Log.Fatal("[ProcedurePrelode] 加载声音组失败（.pck 可能缺失依赖资源）: {0}", ex);
        }
        NodePool.Instance.Active(); // 启动节点池
        LayerMask.Instance.Active(); // 启动层级工具\
        await GF.UI.OpenLoadingUIFormAsync();
        try
        {
            await GF.Archive.LoadAsync();

            if (IsLoadAll())
            {
                ChangeState<ProcedureGame>(procedureOwner);
            }
            else
            {
                Log.Warning("[ProcedurePrelode] 部分模块加载失败，继续进入游戏。");
                ChangeState<ProcedureGame>(procedureOwner);
            }
        }
        catch (System.Exception ex)
        {
            // 存档加载异常时收掉加载遮罩，避免永久挂住；正常路径由 ProcedureGame 在 MenuForm 打开后关闭
            Log.Fatal("[ProcedurePrelode] 存档加载失败: {0}", ex);
            LoadingForm.Current?.CloseLoading();
        }
    }
    private void LoadLocalization()
    {
        m_LoadFlagDic.TryAdd(m_LoadFlagKeys[0], false);
        if (!GF.Base.EnableEditorResLoad)
        {
            GF.Localization.Language = (Language)GF.Setting.GetInt("Language", (int)Language.English);
        }
        else
        {
            GF.Localization.Language = GF.Base.EditorLanguage != Language.Unspecified ? GF.Base.EditorLanguage : GF.Localization.SystemLanguage;
            Log.Info("[ProcedurePrelode] Editor res load enabled, set language to SystemLanguage: {0}.", GF.Localization.Language);
        }
        m_LoadFlagDic.TryUpdate(m_LoadFlagKeys[0], true, false);
    }
    private void LoadUIGroup()
    {
        m_LoadFlagDic.TryAdd(m_LoadFlagKeys[1], false);
        for (int i = 0; i < GF.UI.UIGroupRes.Groups.Length; i++)
        {
            if (!GF.UI.AddUIGroup(GF.UI.UIGroupRes.Groups[i].Name, GF.UI.UIGroupRes.Groups[i].Depth))
            {
                Log.Warning("Add UI group '{0}' failure.", GF.UI.UIGroupRes.Groups[i].Name);
                return;
            }
        }
        m_LoadFlagDic.TryUpdate(m_LoadFlagKeys[1], true, false);
    }
    private void LoadEntityGroup()
    {
        m_LoadFlagDic.TryAdd(m_LoadFlagKeys[2], false);
        var groups = GF.Entity.EntityGroupRes.EntityGroups;
        for (int i = 0; i < groups.Length; i++)
        {
            if (!GF.Entity.AddEntityGroup(groups[i].Name, groups[i].ReleaseInterval, groups[i].Capacity, groups[i].ExpireTime, groups[i].Priority))
            {
                Log.Warning("Add Entity group '{0}' failure.", groups[i].Name);
                return;
            }
        }
        m_LoadFlagDic.TryUpdate(m_LoadFlagKeys[2], true, false);
    }
    private void LoadSoundGroup()
    {
        m_LoadFlagDic.TryAdd(m_LoadFlagKeys[3], false);
        var groups = GF.Sound.SoundGroupRes.SoundGroups;
        for (int i = 0; i < groups.Length; i++)
        {
            if (!GF.Sound.AddSoundGroup(groups[i].Name, groups[i].AgentCounts, groups[i].AvoidBeingReplacedBySamePriority))
            {
                Log.Warning("Add UI group '{0}' failure.", groups[i].Name);
                return;
            }
        }
        GF.Sound.SetVolume(SoundComponent.DefaultMusicGroup, GF.Setting.GetFloat(SoundComponent.DefaultMusicGroup, 1));
        GF.Sound.SetVolume(SoundComponent.DefaultSfxGroup, GF.Setting.GetFloat(SoundComponent.DefaultSfxGroup, 1));
        GF.Sound.SetVolume(SoundComponent.DefaultUiGroup, GF.Setting.GetFloat(SoundComponent.DefaultUiGroup, 1));
        m_LoadFlagDic.TryUpdate(m_LoadFlagKeys[3], true, false);
    }

    private bool IsLoadAll()
    {
        return m_LoadFlagDic.All(x => x.Value);
    }
    /// <summary>
    /// 离开流程。
    /// </summary>
    protected internal override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        base.OnLeave(procedureOwner, isShutdown);
    }
}
