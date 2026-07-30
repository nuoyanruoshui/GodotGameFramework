using GameFramework.Event;
using Godot;
using GodotGameFramework;
using GodotGameFramework.UI;
using System;
namespace GodotGameFramework.Localization;

public partial class LabelTr : Label, IStringKey
{
	[Export]
	public string StringKey { get; private set; }
	public override void _Ready()
	{
		GF.Event.Subscribe(OnLanagueChangeEventArgs.EventId, SetLocalizationValue);
	}
	public override void _ExitTree()
	{
		GF.Event.Unsubscribe(OnLanagueChangeEventArgs.EventId, SetLocalizationValue);
	}

	private void SetLocalizationValue(object sender, GameEventArgs e)
	{
		Text = GF.Localization.GetString(StringKey);
	}

	public void SetLocalizationValue()
	{
		Text = GF.Localization.GetString(StringKey);
	}

}
