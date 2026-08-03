#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
namespace GodotGameFramework.Editor
{
    [Tool]
    public partial class ScriptGenerateInspector : EditorInspectorPlugin
    {
        const string UI_SCRIPT_TEMPLATE = "res://Framework/GodotGameFrameworkCore/Templet/UIFormTemplet.txt";
        const string UI_LOGIC_TEMPLATE = "res://Framework/GodotGameFrameworkCore/Templet/UIFormLogicTemplet.txt";
        const string ENTITY_SCRIPT_TEMPLATE = "res://Framework/GodotGameFrameworkCore/Templet/EntityTemplet.txt";
        const string ENTITY_LOGIC_TEMPLATE = "res://Framework/GodotGameFrameworkCore/Templet/EntityLogicTemplet.txt";
        const string Resc = "res://TheGame/MainPack/Resources/ScriptGenerateRes.tres";
        const string NameSpaceReplace = "_NAMESPACE_";
        const string ParentClassReplace = "_PARENT_";
        const string ClassNameReplace = "_CLASSNAME_";
        const string DefaultNameSpace = "GameLogic";
        const string DefaultOutputPath = "res://TheGame/";
        const string ChildNodes = "_CHILDNODES_";
        private Dictionary<string, string> m_Prs = new Dictionary<string, string>(); // 参数
        private List<Node> m_MatchingChildren = new List<Node>(); // 匹配到的子节点，用于自动赋值
        public override bool _CanHandle(GodotObject @object)
        {
            // Control（含所有 CanvasItem 中的 UI）→ 生成 UIForm；Node2D / Node3D → 生成 Entity
            return @object is CanvasItem or Node3D;
        }

        /// <summary>
        /// 根据节点类型解析 Ge / Logic 输出目录（Control→UI，Node2D/Node3D→Entity），并保证以 "/" 结尾。
        /// 生成与删除共用此方法，避免两处路径逻辑不一致。
        /// </summary>
        private bool ResolvePaths(GodotObject @object, Godot.Resource config, out string geDir, out string logicDir)
        {
            geDir = null;
            logicDir = null;
            if (@object is Control)
            {
                geDir = ReadProp(config, ScriptGenerateRes.Parameters.UIOutPutPathGe, DefaultOutputPath);
                logicDir = ReadProp(config, ScriptGenerateRes.Parameters.UIOutPutPathLogic, DefaultOutputPath);
            }
            else if (@object is Node2D or Node3D)
            {
                geDir = ReadProp(config, ScriptGenerateRes.Parameters.EntityOutPutPathGe, DefaultOutputPath);
                logicDir = ReadProp(config, ScriptGenerateRes.Parameters.EntityOutPutPathLogic, DefaultOutputPath);
            }
            else
            {
                return false;
            }
            if (!geDir.EndsWith("/")) geDir += "/";
            if (!logicDir.EndsWith("/")) logicDir += "/";
            return true;
        }

        public override void _ParseEnd(GodotObject @object)
        {
            base._ParseEnd(@object);

            // 只判断资源是否存在，不做强类型转换：即使 ScriptGenerateRes 尚未在编辑器注册，
            // 也能通过属性名读取配置，避免 InvalidCastException 和类型未注册导致按钮消失
            if (!ResourceLoader.Exists(Resc))
            {
                GD.PushWarning($"[ScriptGenerateInspector] 找不到配置资源: {Resc}");
                return;
            }
            VBoxContainer vbox = new VBoxContainer();
            Button m_GenerateButton = new Button();
            m_GenerateButton.Text = "Bind UI Script";
            vbox.AddChild(m_GenerateButton);
            Button m_DeleteGeButton = new Button();
            m_DeleteGeButton.Text = "Delete Gen";
            m_DeleteGeButton.AddThemeColorOverride("font_color", Colors.Red);
            vbox.AddChild(m_DeleteGeButton);
            Button m_DeleteLogicButton = new Button();
            m_DeleteLogicButton.Text = "Delete Logic";
            m_DeleteLogicButton.AddThemeColorOverride("font_color", Colors.Red);
            vbox.AddChild(m_DeleteLogicButton);
            m_GenerateButton.Pressed += () => OnGeneratePressed(@object);
            m_DeleteGeButton.Pressed += () => OnDeleteGenPressed(@object);
            m_DeleteLogicButton.Pressed += () => OnDeleteLogicPressed(@object);
            AddCustomControl(vbox);
        }

        private void OnDeleteGenPressed(GodotObject @object)
        {
            if (@object is not Node node) return;

            string className = Sanitize(node.Name);
            if (string.IsNullOrEmpty(className)) return;

            Godot.Resource config = ResourceLoader.Load(Resc);
            if (!ResolvePaths(node, config, out string outputDirGe, out _)) return;
            string gePath = outputDirGe + className + ".cs";

            if (!FileAccess.FileExists(gePath))
            {
                GD.PushWarning($"[ScriptGenerateInspector] 文件不存在: {gePath}");
                return;
            }

            ShowConfirmDialog($"确定删除 Generated 脚本？\n{gePath}", () =>
            {
                DirAccess.RemoveAbsolute(gePath);

                // 如果当前节点挂载了该脚本，一并清除引用
                if (node.GetScript().AsGodotObject() is CSharpScript currentScript && currentScript.ResourcePath == gePath)
                {
                    node.SetScript(default);
                    EditorInterface.Singleton.MarkSceneAsUnsaved();
                    GD.Print($"[ScriptGenerateInspector] 已清除节点上的脚本引用: {node.Name}");
                }

                EditorInterface.Singleton.GetResourceFilesystem().Scan();
                GD.Print($"[ScriptGenerateInspector] 已删除: {gePath}");
            });
        }

        private void OnDeleteLogicPressed(GodotObject @object)
        {
            if (@object is not Node node) return;

            string className = Sanitize(node.Name);
            if (string.IsNullOrEmpty(className)) return;

            Godot.Resource config = ResourceLoader.Load(Resc);
            if (!ResolvePaths(node, config, out _, out string outputDirLogic)) return;
            string logicPath = outputDirLogic + className + ".Logic.cs";

            if (!FileAccess.FileExists(logicPath))
            {
                GD.PushWarning($"[ScriptGenerateInspector] 文件不存在: {logicPath}");
                return;
            }

            ShowConfirmDialog($"确定删除 Logic 脚本？\n{logicPath}", () =>
            {
                DirAccess.RemoveAbsolute(logicPath);
                EditorInterface.Singleton.GetResourceFilesystem().Scan();
                GD.Print($"[ScriptGenerateInspector] 已删除: {logicPath}");
            });
        }

        private static void ShowConfirmDialog(string message, Action onConfirm)
        {
            var dialog = new ConfirmationDialog();
            dialog.Title = "确认";
            dialog.DialogText = message;
            dialog.Confirmed += () =>
            {
                onConfirm();
                dialog.QueueFree();
            };
            dialog.Canceled += () => dialog.QueueFree();
            EditorInterface.Singleton.GetBaseControl().AddChild(dialog);
            dialog.PopupCentered();
        }

        private void OnGeneratePressed(GodotObject @object)
        {
            if (@object is not Node node) return;

            ShowConfirmDialog("是否生成脚本？\n注意：Node挂载的脚本若为自定义的脚本请不要确认否则会被替换为流程内的脚本", () =>
            {
                string parent = @object.GetType().Name;
                string className = Sanitize(node.Name);
                if (string.IsNullOrEmpty(className))
                {
                    GD.PushError("[ScriptGenerateInspector] 节点名称无法转换为合法的类名。");
                    return;
                }

                Godot.Resource config = ResourceLoader.Load(Resc);
                if (!ResolvePaths(node, config, out string outputDirGe, out string outputDirLogic))
                {
                    GD.PushError($"[ScriptGenerateInspector] 不支持的节点类型: {parent}");
                    return;
                }

                string geTemplate;
                string logicTemplate;
                string namespaceStr = ReadProp(config, ScriptGenerateRes.Parameters.NameSpace, DefaultNameSpace);
                if (@object is Control)
                {
                    geTemplate = ReadText(UI_SCRIPT_TEMPLATE);
                    logicTemplate = ReadText(UI_LOGIC_TEMPLATE);
                }
                else // Node2D or Node3D（已由 ResolvePaths 校验类型）
                {
                    geTemplate = ReadText(ENTITY_SCRIPT_TEMPLATE);
                    logicTemplate = ReadText(ENTITY_LOGIC_TEMPLATE);
                }

                if (geTemplate == null) return; // Ge 模板缺失，无法生成，ReadText 已报错

                // 生成部分（Ge）：包含框架样板代码，每次都覆盖重写
                m_Prs.Clear();
                m_MatchingChildren.Clear();
                string geScript = geTemplate
                    .Replace(NameSpaceReplace, namespaceStr)
                    .Replace(ParentClassReplace, parent)
                    .Replace(ClassNameReplace, className)
                    .Replace(ChildNodes, ReadChildNodes(node, config));
                string gePath = outputDirGe + className + ".cs"; // Godot只有文件名与类名相同才可显示在Inspector上，否则无法
                if (!WriteText(gePath, geScript)) return;

                // 逻辑部分（Logic）：用户业务代码，仅在首次生成时创建，避免覆盖已有逻辑
                string logicPath = outputDirLogic + className + ".Logic.cs";
                if (!FileAccess.FileExists(logicPath))
                {
                    if (logicTemplate != null)
                    {
                        string logicScript = logicTemplate
                            .Replace(NameSpaceReplace, namespaceStr)
                            .Replace(ClassNameReplace, className);
                        WriteText(logicPath, logicScript);
                    }
                }

                // 刷新文件系统，让 Godot 识别新生成或更新的脚本文件
                var fs = EditorInterface.Singleton.GetResourceFilesystem();
                fs.UpdateFile(gePath);
                if (FileAccess.FileExists(logicPath)) fs.UpdateFile(logicPath);
                fs.Scan();

                // 加载 CSharpScript 资源并赋值给节点
                var script = GD.Load<CSharpScript>(gePath);
                if (script != null)
                {
                    node.SetScript(script);

                    // 自动赋值子节点到 [Export] 字段
                    foreach (var child in m_MatchingChildren)
                    {
                        node.Set(child.Name, child);
                    }

                    // 标记场景为已修改
                    EditorInterface.Singleton.MarkSceneAsUnsaved();
                    GD.Print($"[ScriptGenerateInspector] 已生成并赋值脚本: {gePath}");
                }
                else
                {
                    GD.PushWarning($"[ScriptGenerateInspector] 脚本已生成，但无法加载。请重新构建后手动附加: {gePath}");
                }
            });
        }
        private string ReadChildNodes(Node node, Godot.Resource config)
        {
            string prefix = ReadProp(config, ScriptGenerateRes.Parameters.NodePrefix, "m_");
            foreach (var child in node.GetChildren())
            {
                if (child.GetChildCount() > 0)
                {
                    ReadChildNodes(child, config);
                }
                if (child.Name.ToString().StartsWith(prefix))
                {
                    if (!m_Prs.ContainsKey(child.Name))
                    {
                        m_Prs.Add(child.Name, child.GetType().Name);
                        m_MatchingChildren.Add(child);
                    }
                    else
                    {
                        GD.PushWarning($"[ScriptGenerateInspector] {child.Name}重复");
                    }
                }
            }
            return string.Join("\n", m_Prs.Select(x => $"\t\t[Export]\n\t\tprivate {x.Value} {x.Key};"));
        }

        private static string ReadProp(Godot.Resource res, string prop, string fallback)
        {
            if (res == null) return fallback;
            string value = res.Get(prop).AsString();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string ReadText(string path)
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PushError($"[ScriptGenerateInspector] 无法读取模板: {path} ({FileAccess.GetOpenError()})");
                return null;
            }
            return file.GetAsText();
        }

        private static bool WriteText(string path, string content)
        {
            //检测文件夹是否存在，不存在创建
            string dir = ProjectSettings.GlobalizePath(path);
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PushError($"[ScriptGenerateInspector] 无法写入文件: {path} ({FileAccess.GetOpenError()})");
                return false;
            }
            file.StoreString(content);
            return true;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            }
            // C# 标识符不能以数字开头
            if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }
}
#endif