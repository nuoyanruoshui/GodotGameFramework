using GameConfig;
using GameFramework.Localization;
using GameFramework.UI;
using Godot;
using GodotGameFramework;
using GodotGameFramework.NodePool;
using GodotGameFramework.Sound;
using GodotGameFramework.UI;
using System;
namespace GameLogic
{
	/// <summary>
	/// 界面逻辑（此文件仅在首次生成时创建，之后不会被覆盖）。
	/// </summary>
	public partial class SettingForm
	{
		/// <summary>
		/// 初始化界面。
		/// </summary>
		/// <param name="serialId">界面序列编号。</param>
		/// <param name="uiFormAssetName">界面资源名称。</param>
		/// <param name="uiGroup">界面所处的界面组。</param>
		/// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
		/// <param name="isNewInstance">是否是新实例。</param>
		/// <param name="userData">用户自定义数据。</param>
		public void OnInit(int serialId, string uiFormAssetName, IUIGroup uiGroup, bool pauseCoveredUIForm, bool isNewInstance, object userData)
		{
			#region 框架逻辑
			m_SerialId = serialId;
			m_UIFormAssetName = uiFormAssetName;
			m_UIGroup = uiGroup;
			m_DepthInUIGroup = 0;
			m_PauseCoveredUIForm = pauseCoveredUIForm;
			UIStringKeys.ForEach(key => key.SetLocalizationValue());
			#endregion

			if (isNewInstance)
			{
				m_CloseButton.Pressed += OnCloseButtonPressed;
				for (int i = 0; i < GF.Localization.GetLocalizationFileNames().Length; i++)
				{
					string localizationFileName = GF.Localization.GetLocalizationFileNames()[i];
					switch (localizationFileName)
					{
						case "ChineseSimplified":
							localizationFileName = "简体中文";
							break;
						case "English":
							localizationFileName = "English";
							break;
						case "French":
							localizationFileName = "Français";
							break;
						case "German":
							localizationFileName = "Deutsch";
							break;
						case "Italian":
							localizationFileName = "Italiano";
							break;
						case "Japanese":
							localizationFileName = "日本語";
							break;
						case "Korean":
							localizationFileName = "한국어";
							break;
						case "Portuguese":
							localizationFileName = "Português";
							break;
						case "Russian":
							localizationFileName = "Русский";
							break;
						case "Spanish":
							localizationFileName = "Español";
							break;
					}
					m_OptionButton.AddItem(localizationFileName, i);
				}
				m_OptionButton.ItemSelected += (index) =>
				{
					string selectedFileName = GF.Localization.GetLocalizationFileNames()[index];
					GF.Localization.Language = Enum.TryParse<Language>(selectedFileName, out var lang) ? lang : Language.English;
				};

				m_MusicHSlider.ValueChanged += (value) =>
				{
					GF.Sound.SetVolume(SoundComponent.DefaultMusicGroup, (float)value / 100);
				};
				m_EffectHSlider.ValueChanged += (value) =>
				{
					GF.Sound.SetVolume(SoundComponent.DefaultSfxGroup, (float)value / 100);
					GF.Sound.SetVolume(SoundComponent.DefaultUiGroup, (float)value / 100);
				};

				m_BackToMenuButton.Pressed += OnBackToMenu;
			}
		}

		private async void OnBackToMenu()
		{
			var loading = await GF.UI.OpenLoadingUIFormAsync();
			try
			{
				GF.UI.CloseUIForm(this);
				GF.UI.CloseUIForm((VarInt32)GF.DataNode.GetData(nameof(MainForm)));
				GF.UI.OpenUIForm(UIFormId.MenuForm);
				GF.Entity.HideAllLoadedEntities();
				NodePool.ReleaseAll();
				GF.Scene.UnloadScene((VarString)GF.DataNode.GetData("Scene"));
			}
			finally
			{
				// 清理完成（或中途异常）后显式收掉加载遮罩
				loading?.CloseLoading();
			}
		}

		private void OnCloseButtonPressed()
		{
			GF.UI.CloseUIForm(this);
		}

		/// <summary>
		/// 界面回收。
		///
		/// </summary>
		public void OnRecycle()
		{
			#region 框架逻辑
			m_SerialId = 0;
			m_DepthInUIGroup = 0;
			m_PauseCoveredUIForm = true;
			Visible = false;
			#endregion
		}

		/// <summary>
		/// 界面打开。
		/// </summary>
		public void OnOpen(object userData)
		{
			#region 框架逻辑
			Visible = true;
			#endregion
			GF.Base.PauseGame();

			m_BackToMenuButton.Visible = userData != null && userData is MainForm;
		}

		/// <summary>
		/// 界面关闭。
		/// </summary>
		public void OnClose(bool isShutdown, object userData)
		{
			#region 框架逻辑
			Visible = false;
			#endregion
			GF.Base.ResumeGame();
		}

		/// <summary>
		/// 界面暂停。
		/// </summary>
		public void OnPause()
		{

		}

		/// <summary>
		/// 界面暂停恢复。
		/// </summary>
		public void OnResume()
		{

		}

		/// <summary>
		/// 界面遮挡。
		/// </summary>
		public void OnCover()
		{

		}

		/// <summary>
		/// 界面遮挡恢复。
		/// </summary>
		public void OnReveal()
		{

		}

		/// <summary>
		/// 界面重新获得焦点。
		/// </summary>
		public void OnRefocus(object userData)
		{

		}

		/// <summary>
		/// 界面轮询。
		/// </summary>
		public void OnUpdate(float elapseSeconds, float realElapseSeconds)
		{

		}

		/// <summary>
		/// 界面深度改变。
		/// </summary>
		public void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
		{
			#region 框架逻辑
			m_DepthInUIGroup = depthInUIGroup;
			#endregion
		}
	}
}
