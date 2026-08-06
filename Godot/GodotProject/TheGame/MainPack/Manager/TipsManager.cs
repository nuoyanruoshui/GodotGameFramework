using GameConfig.Constant;
using GameLogic;
using Godot;
using GodotGameFramework;
using GodotGameFramework.NodePool;
using GodotGameFrameworkCore.SingletonSystem;
using System;
public interface ITips
{
    void SetAction(string content, string cancelTxt, string confirmTxt, Action cancel, Action confirm);
}
public partial class TipsManager : Singleton<TipsManager>
{
    public T ShowTips<T>(string content, string cancelTxt, string confirmTxt, Action cancel, Action confirm) where T : class, ITips, IPoolable
    {
        if (GetLayer() == null) return null;
        T v = NodePool.Get<T>(ResourcesCollectionConstant.UI_QuestionTips, GetLayer());
        v?.SetAction(content, cancelTxt, confirmTxt, cancel, confirm);
        return v;
    }

    private CanvasLayer GetLayer()
    {
        return GF.UI.GetUIGroup("MainPack")?.Helper as CanvasLayer;
    }
}
