using GameConfig;
using GameConfig.Constant;
using GameFramework.Event;
using GameFramework.UI;
using GodotGameFramework;
using GodotGameFramework.Localization;
using GodotGameFramework.Sound;
using GodotGameFramework.UI;

namespace GameLogic
{
	/// <summary>
	/// 界面逻辑（此文件仅在首次生成时创建，之后不会被覆盖）。
	/// </summary>
	public partial class MainForm
	{
		private CatEntity m_Cat => LevelManager.Instance.Cat;
		private float m_Interal = 0;
		private float m_Timer = 0;
		/// <summary>
		/// 初始化界面。
		/// </summary>
		/// <param name="serialId">界面序列编号。</param>
		/// <param name="uiFormAssetName">界面资源名称。</param>
		/// <param name="uiGroup">界面所处的界面组。</param>
		/// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
		/// <param name="isNewInstance">是否是新实例。</param>
		/// <param name="userData">用户自定义数据。</param>
		public async void OnInit(int serialId, string uiFormAssetName, IUIGroup uiGroup, bool pauseCoveredUIForm, bool isNewInstance, object userData)
		{
			#region 框架逻辑
			m_SerialId = serialId;
			m_UIFormAssetName = uiFormAssetName;
			m_UIGroup = uiGroup;
			m_DepthInUIGroup = 0;
			m_PauseCoveredUIForm = pauseCoveredUIForm;
			UIStringKeys.ForEach(key => key.SetLocalizationValue());
			#endregion

			m_LevelLabel.Text = $"{LevelManager.Instance.Level}-{LevelManager.Instance.WaveIndex + 1}";
			GF.Sound.PlayBGM(ResourcesCollectionConstant.Music_Fight);

			if (isNewInstance)
			{
				#region 界面逻辑
				m_SettingButton.Pressed += OnSettingButtonPressed;
				m_Cat.HpChanged += OnCatHpChanged;
				#endregion
			}


			m_HpHSlider.MaxValue = m_Cat.ActorData.MaxHp;
			m_HpHSlider.Value = m_Cat.ActorData.Hp;
			m_HpLabel.Text = $"{GF.Localization.GetString("MainForm.Hp")}{m_Cat.ActorData.Hp.ToString()}/{m_Cat.ActorData.MaxHp.ToString()}";
		}

		private void OnCatHpChanged(float obj)
		{
			m_HpHSlider.Value = obj;
			m_HpLabel.Text = $"{GF.Localization.GetString("MainForm.Hp")}{m_Cat.ActorData.Hp.ToString()}/{m_Cat.ActorData.MaxHp.ToString()}";
		}

		private void OnSettingButtonPressed()
		{
			GF.UI.OpenUIForm(UIFormId.SettingForm, this);
		}

		/// <summary>
		/// 界面回收。
		///
		/// </summary>
		public void OnRecycle()
		{
			m_SerialId = 0;
			m_DepthInUIGroup = 0;
			m_PauseCoveredUIForm = true;
			Visible = false;
		}

		/// <summary>
		/// 界面打开。
		/// </summary>
		public void OnOpen(object userData)
		{
			Visible = true;
			m_ScoreLabel.Text = $"{GF.Localization.GetString("MainForm.Score")}{GF.Archive.CurrentData.Score.ToString()}";
			GF.Event.Subscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);
		}

		/// <summary>
		/// 界面关闭。
		/// </summary>
		public void OnClose(bool isShutdown, object userData)
		{
			Visible = false;
			GF.Event.Unsubscribe(ScoreChangedEventArgs.EventId, OnScoreChanged);
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
			m_Interal += elapseSeconds;
			if (m_Interal >= 1 && LevelManager.Instance.Timer > m_Timer)
			{
				m_Interal = 0;
				m_Timer++;
				float time = LevelManager.Instance.Timer - m_Timer;
				m_TimerLabel.Text = $"{GF.Localization.GetString("MainForm.Timer")}{time}";
			}

			if (m_Timer >= LevelManager.Instance.Timer)
			{
				LevelManager.Instance.NextWave();
				m_LevelLabel.Text = $"{LevelManager.Instance.Level}-{LevelManager.Instance.WaveIndex + 1}";
				m_Timer = 0;

			}
		}

		/// <summary>
		/// 界面深度改变。
		/// </summary>
		public void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
		{
			m_DepthInUIGroup = depthInUIGroup;
		}

		private void OnScoreChanged(object sender, GameEventArgs e)
		{
			m_ScoreLabel.Text = $"{GF.Localization.GetString("MainForm.Score")}{GF.Archive.CurrentData.Score.ToString()}";
		}
	}
}
