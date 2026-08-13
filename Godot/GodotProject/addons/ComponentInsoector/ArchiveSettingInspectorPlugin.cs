#if TOOLS
using Godot;
using GodotGameFramework.Archive;

namespace GodotGameFramework.Editor
{
    [Tool]
    public partial class ArchiveSettingInspectorPlugin : EditorInspectorPlugin
    {
        public override bool _CanHandle(GodotObject @object)
        {
            var scriptVar = @object.Get("script");
            if (scriptVar.VariantType == Variant.Type.Object &&
                scriptVar.AsGodotObject() is CSharpScript csScript)
            {
                return csScript.ResourcePath.EndsWith("ArchiveSetting.cs");
            }
            return false;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, PropertyHint hintType, string hintString, PropertyUsageFlags usage, bool wide)
        {
            bool enabled = @object.Get(ArchiveSetting.Parameters.EnableAesEncryption).AsBool();

            if (name == ArchiveSetting.Parameters.EnableAesEncryption)
            {
                DrawEnableToggle(@object, enabled);
                return true;
            }
            else if (name == ArchiveSetting.Parameters.KEY)
            {
                return !enabled;
            }
            else if (name == ArchiveSetting.Parameters.Salt)
            {
                if (!enabled) return true;
                DrawSaltEditor(@object);
                return true;
            }
            return false;
        }

        private void DrawEnableToggle(GodotObject @object, bool enabled)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = "AES Encrypt";
            checkBox.ButtonPressed = enabled;
            checkBox.Toggled += on =>
            {
                EditorUndoRedoManager undoRedo = EditorInterface.Singleton.GetEditorUndoRedo();
                undoRedo.CreateAction("AES Encrypt");
                undoRedo.AddDoProperty(@object, ArchiveSetting.Parameters.EnableAesEncryption, on);
                undoRedo.CommitAction();
                // 刷新属性列表，让 KEY / Salt 响应式地显示或隐藏
                @object.NotifyPropertyListChanged();
            };
            AddCustomControl(checkBox);
        }

        private void DrawSaltEditor(GodotObject @object)
        {
            HBoxContainer hbox = new HBoxContainer();
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            Label label = new Label();
            label.Text = "Salt";
            label.VerticalAlignment = VerticalAlignment.Center;
            hbox.AddChild(label);

            LineEdit lineEdit = new LineEdit();
            lineEdit.Text = @object.Get(ArchiveSetting.Parameters.Salt).AsString();
            lineEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            lineEdit.TextSubmitted += text => ApplySalt(@object, text);
            lineEdit.FocusExited += () => ApplySalt(@object, lineEdit.Text);
            hbox.AddChild(lineEdit);

            Button button = new Button();
            button.Text = "Random Salt";
            button.TooltipText = "Create a new random salt";
            button.Pressed += () =>
            {
                string generated = Rijindael.GenerateIV();
                lineEdit.Text = generated;
                ApplySalt(@object, generated);
            };
            hbox.AddChild(button);

            AddCustomControl(hbox);
        }

        private void ApplySalt(GodotObject @object, string value)
        {
            if (string.IsNullOrEmpty(value) || value == @object.Get(ArchiveSetting.Parameters.Salt).AsString())
                return;

            EditorUndoRedoManager undoRedo = EditorInterface.Singleton.GetEditorUndoRedo();
            undoRedo.CreateAction("Change Salt");
            undoRedo.AddDoProperty(@object, ArchiveSetting.Parameters.Salt, value);
            undoRedo.CommitAction();
        }
    }
}
#endif
