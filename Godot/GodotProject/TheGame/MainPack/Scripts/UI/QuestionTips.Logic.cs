using Godot;
using GodotGameFramework.NodePool;
using System;
namespace GameLogic
{
	/// <summary>
	/// 界面逻辑（此文件仅在首次生成时创建，之后不会被覆盖）。
	/// </summary>
	public partial class QuestionTips : ITips, IPoolable
	{
		private Action m_Cancel;
		private Action m_Confirm;

		/// <summary>
		/// 按钮一次性接线。放在 _Ready 而非 _EnterTree：
		/// _EnterTree 在每次 NodePool.Get/Release 进出树时都会触发，会造成按钮事件重复订阅。
		/// </summary>
		public override void _Ready()
		{
			base._Ready();
			m_Btn_Cancle.Pressed += () =>
			{
				// 先捕获回调再归还节点：Release 会同步调 OnRelease 清空 m_Cancel，若先 Release 回调就丢了
				Action cb = m_Cancel;
				NodePool.Release(this);
				cb?.Invoke();
			};
			m_Btn_Confirm.Pressed += () =>
			{
				Action cb = m_Confirm;
				NodePool.Release(this);
				cb?.Invoke();
			};
		}

		public void OnGet()
		{

		}

		public void OnRelease()
		{
			m_Cancel = null;
			m_Confirm = null;
		}
		public void SetAction(string content, string cancelTxt, string confirmTxt, Action cancel, Action confirm)
		{
			m_Label.Text = content;
			m_Cancel = cancel;
			m_Confirm = confirm;
			m_Btn_Cancle.Text = cancelTxt;
			m_Btn_Confirm.Text = confirmTxt;
		}
	}
}
