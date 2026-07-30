using GameFramework;
using GameFramework.Event;
using GameFramework.Localization;
using Godot;
using System;

public partial class OnLanagueChangeEventArgs : GameEventArgs
{
    public static int EventId => typeof(OnLanagueChangeEventArgs).GetHashCode();
    public override int Id => EventId;
    public Language Language { get; private set; }

    public static OnLanagueChangeEventArgs Create(Language language)
    {
        OnLanagueChangeEventArgs e = ReferencePool.Acquire<OnLanagueChangeEventArgs>();
        e.Language = language;
        return e;
    }
    public override void Clear()
    {
        Language = Language.English;
    }

}
