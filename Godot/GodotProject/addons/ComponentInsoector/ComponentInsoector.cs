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
			RemoveInspectorPlugin(m_ProcedureComponent);
			RemoveInspectorPlugin(m_BaseComponent);
			RemoveInspectorPlugin(m_SceneComponent);
			RemoveInspectorPlugin(m_SettingComponent);
			RemoveInspectorPlugin(m_EntityComponent);
			RemoveInspectorPlugin(m_UIComponent);
			RemoveInspectorPlugin(m_SoundComponent);
			RemoveInspectorPlugin(m_LocalizationComponent);
			RemoveInspectorPlugin(m_DownloadComponent);
			RemoveInspectorPlugin(m_WebRequestComponent);
			RemoveInspectorPlugin(m_ResourceComponent);
			RemoveInspectorPlugin(m_ScriptGenerateInspector);
			RemoveInspectorPlugin(m_NodePoolInspector);
			RemoveInspectorPlugin(m_ArchiveSettingInspector);
			m_ProcedureComponent.Free();
			m_BaseComponent.Free();
			m_SceneComponent.Free();
			m_SettingComponent.Free();
			m_EntityComponent.Free();
			m_UIComponent.Free();
			m_SoundComponent.Free();
			m_LocalizationComponent.Free();
			m_DownloadComponent.Free();
			m_WebRequestComponent.Free();
			m_ResourceComponent.Free();
			m_ScriptGenerateInspector.Free();
			m_NodePoolInspector.Free();
			m_ArchiveSettingInspector.Free();
		}
	}
}
#endif
