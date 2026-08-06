using Godot;
namespace GameLogic
{
	/// <summary>
	/// 界面,生成时会被覆盖，请勿手动修改
	/// </summary>
	public partial class QuestionTips : Control
	{
		[Export]
		private Label m_Label;
		[Export]
		private Button m_Btn_Cancle;
		[Export]
		private Button m_Btn_Confirm;
	}
}
