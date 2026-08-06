using GameFramework.Event;
using Godot;
using GodotGameFramework;
using GodotGameFramework.Scene;
using GodotGameFramework.UI;
using System;
using System.Threading.Tasks;
namespace GameLogic
{
	/// <summary>
	/// 界面逻辑（此文件仅在首次生成时创建，之后不会被覆盖）。
	/// </summary>
	public partial class LoadingForm
	{
		private Tween m_ProgressTween;
		/// <summary>
		/// 初始化界面。
		/// </summary>
		/// <param name="serialId">界面序列编号。</param>
		/// <param name="uiFormAssetName">界面资源名称。</param>
		/// <param name="uiGroup">界面所处的界面组。</param>
		/// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面。</param>
		/// <param name="isNewInstance">是否是新实例。</param>
		/// <param name="userData">用户自定义数据。</param>
		public void OnInit(int serialId, string uiFormAssetName, GameFramework.UI.IUIGroup uiGroup, bool pauseCoveredUIForm, bool isNewInstance, object userData)
		{
			#region 框架逻辑
			m_SerialId = serialId;
			m_UIFormAssetName = uiFormAssetName;
			m_UIGroup = uiGroup;
			m_DepthInUIGroup = 0;
			m_PauseCoveredUIForm = pauseCoveredUIForm;
			UIStringKeys.ForEach(key => key.SetLocalizationValue());
			#endregion
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
			GF.Event.Subscribe(OpenUIFormUpdateEventArgs.EventId, OnLoadingUpdate);
			GF.Event.Subscribe(LoadSceneUpdateEventArgs.EventId, OnLoadingUpdate);

			GF.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnLoadingSuccess);
			GF.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadingSuccess);
		}

		/// <summary>
		/// 界面关闭。
		/// </summary>
		public void OnClose(bool isShutdown, object userData)
		{
			#region 框架逻辑
			Visible = false;
			#endregion

			GF.Event.Unsubscribe(OpenUIFormUpdateEventArgs.EventId, OnLoadingUpdate);
			GF.Event.Unsubscribe(LoadSceneUpdateEventArgs.EventId, OnLoadingUpdate);

			GF.Event.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnLoadingSuccess);
			GF.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadingSuccess);
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



		public void SetLogState(string logState, float progress)
		{
			if (m_HSlider != null)
			{
				float clamped = Mathf.Clamp(progress, 0f, 100f);

				// 平滑过渡，避免进度条跳跃显得生硬
				m_ProgressTween?.Kill();
				m_ProgressTween = CreateTween();
				m_ProgressTween.TweenProperty(m_HSlider, "value", clamped, 0.25f)
					.SetTrans(Tween.TransitionType.Quad)
					.SetEase(Tween.EaseType.Out);
				if (m_State != null)
					m_State.Text = logState + $" {clamped:F0}%" ?? "";
			}
		}


		private async void OnLoadingSuccess(object sender, GameEventArgs e)
		{
			if (e is OpenUIFormSuccessEventArgs ui)
			{
				if (ui.UIForm == this)
				{
					return;
				}
			}
			SetLogState("加载完成", 100);
			//延迟一帧关闭界面，避免在加载完成后立即关闭界面导致的闪烁问题
			await Task.Delay(100);
			GF.UI.CloseUIForm(this);
		}


		private void OnLoadingUpdate(object sender, GameEventArgs e)
		{
			float progress = 0;
			if (e is OpenUIFormUpdateEventArgs ui)
			{
				progress = ui.Progress;
			}
			else if (e is LoadSceneUpdateEventArgs scene)
			{
				progress = scene.Progress;
			}
			SetLogState("加载中...", progress * 100);
		}
	}
}
