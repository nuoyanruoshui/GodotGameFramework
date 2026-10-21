#if TOOLS
using Godot;
using System;
namespace GodotGameFramework.Editor
{
	[Tool]
	public partial class ComponentInsoector : EditorPlugin
	{
		ProcedureComponentInspectorPlugin m_ProcedureComponent;
		BaseComponentInspectorPlugin m_BaseComponent;
		SceneComponentInspectorPlugin m_SceneComponent;
		SettingComponentInspectorPlugin m_SettingComponent;
		EntityComponentInspectorPlugin m_EntityComponent;
		UIComponentInspectorPlugin m_UIComponent;
		SoundComponentInspectorPlugin m_SoundComponent;
		LocalizationComponentInspectorPlugin m_LocalizationComponent;
		DownloadComponentInspectorPlugin m_DownloadComponent;
		WebRequestComponentInspectorPlugin m_WebRequestComponent;
		ResourceComponentInspectorPlugin m_ResourceComponent;
		ScriptGenerateInspector m_ScriptGenerateInspector;
		NodePoolInspectorPlugin m_NodePoolInspector;
		ArchiveSettingInspectorPlugin m_ArchiveSettingInspector;
		public override void _EnterTree()
		{
			m_ProcedureComponent = new ProcedureComponentInspectorPlugin();
			m_BaseComponent = new BaseComponentInspectorPlugin();
			m_SceneComponent = new SceneComponentInspectorPlugin();
			m_SettingComponent = new SettingComponentInspectorPlugin();
			m_EntityComponent = new EntityComponentInspectorPlugin();
			m_UIComponent = new UIComponentInspectorPlugin();
			m_SoundComponent = new SoundComponentInspectorPlugin();
			m_LocalizationComponent = new LocalizationComponentInspectorPlugin();
			m_DownloadComponent = new DownloadComponentInspectorPlugin();
			m_WebRequestComponent = new WebRequestComponentInspectorPlugin();
			m_ResourceComponent = new ResourceComponentInspectorPlugin();
			m_ScriptGenerateInspector = new ScriptGenerateInspector();
			m_NodePoolInspector = new NodePoolInspectorPlugin();
			m_ArchiveSettingInspector = new ArchiveSettingInspectorPlugin();
			AddInspectorPlugin(m_BaseComponent);
			AddInspectorPlugin(m_ProcedureComponent);
			AddInspectorPlugin(m_SceneComponent);
			AddInspectorPlugin(m_SettingComponent);
			AddInspectorPlugin(m_EntityComponent);
			AddInspectorPlugin(m_UIComponent);
			AddInspectorPlugin(m_SoundComponent);
			AddInspectorPlugin(m_LocalizationComponent);
			AddInspectorPlugin(m_DownloadComponent);
			AddInspectorPlugin(m_WebRequestComponent);
			AddInspectorPlugin(m_ResourceComponent);
			AddInspectorPlugin(m_ScriptGenerateInspector);
			AddInspectorPlugin(m_NodePoolInspector);
			AddInspectorPlugin(m_ArchiveSettingInspector);
		}

		public override void _ExitTree()
		{
			ReleaseInspectorPlugin(m_ProcedureComponent);
			ReleaseInspectorPlugin(m_BaseComponent);
			ReleaseInspectorPlugin(m_SceneComponent);
			ReleaseInspectorPlugin(m_SettingComponent);
			ReleaseInspectorPlugin(m_EntityComponent);
			ReleaseInspectorPlugin(m_UIComponent);
			ReleaseInspectorPlugin(m_SoundComponent);
			ReleaseInspectorPlugin(m_LocalizationComponent);
			ReleaseInspectorPlugin(m_DownloadComponent);
			ReleaseInspectorPlugin(m_WebRequestComponent);
			ReleaseInspectorPlugin(m_ResourceComponent);
			ReleaseInspectorPlugin(m_ScriptGenerateInspector);
			ReleaseInspectorPlugin(m_NodePoolInspector);
			ReleaseInspectorPlugin(m_ArchiveSettingInspector);
		}

		/// <summary>
		/// 注销并释放一个 Inspector 插件。
		/// <see cref="EditorInspectorPlugin"/> 派生自 RefCounted，不能用 <c>Free()</c>
		/// （会报 "Can't free a RefCounted object" / "Nonexistent function 'free'"），
		/// 只能通过 <see cref="GodotObject.Dispose"/> 释放 C# 包装持有的引用。
		/// </summary>
		private void ReleaseInspectorPlugin(EditorInspectorPlugin plugin)
		{
			if (plugin == null) return;

			if (IsInstanceValid(plugin))
			{
				RemoveInspectorPlugin(plugin);
				plugin.Dispose();
			}
		}
	}
}
#endif
