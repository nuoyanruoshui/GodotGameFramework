#if TOOLS
using GameFramework;
using GameFramework.WebRequest;
using Godot;
using GodotGameFramework.Web;
using System;
namespace GodotGameFramework.Editor
{
    [Tool]
    public partial class WebRequestComponentInspectorPlugin : EditorInspectorPlugin
    {
        public override bool _CanHandle(GodotObject @object)
        {
            var scriptVar = @object.Get("script");
            if (scriptVar.VariantType == Variant.Type.Object &&
                scriptVar.AsGodotObject() is CSharpScript csScript)
            {
                return csScript.ResourcePath.EndsWith($"WebRequestComponent.cs");
            }
            return @object is WebRequestComponent;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, PropertyHint hintType, string hintString, PropertyUsageFlags usage, bool wide)
        {
            if (name == WebRequestComponent.Parameters.WebRequestAgentHelper)
                return true;
            return false;
        }

        public override void _ParseBegin(GodotObject @object)
        {
            base._ParseBegin(@object);
            DrawWebRequestAgentHelperDropDown(@object);
        }

        private void DrawWebRequestAgentHelperDropDown(GodotObject @object)
        {
            Type[] helperTypes = Utility.Assembly.GetAssignableFormTypes(typeof(IWebRequestAgentHelper));
            // 左右排版：Label | OptionButton 放在同一行
            HBoxContainer hbox = new HBoxContainer();
            hbox.CustomMinimumSize = new Vector2(0, 28);
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            Label enterLabel = new Label();
            enterLabel.Text = "Web Request Agent Helper";
            enterLabel.VerticalAlignment = VerticalAlignment.Center;
            enterLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.AddChild(enterLabel);

            OptionButton dropdown = new OptionButton();
            dropdown.CustomMinimumSize = new Vector2(0, 0);
            dropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            string currentEnter = "";
            var enterVar = @object.Get(WebRequestComponent.Parameters.WebRequestAgentHelper);
            if (enterVar.VariantType == Variant.Type.String)
                currentEnter = enterVar.AsString();

            int selectedIdx = -1;
            foreach (var procType in helperTypes)
            {
                if (procType.IsAbstract)
                    continue;
                int idx = dropdown.ItemCount;
                dropdown.AddItem(procType.FullName);
                if (procType.FullName == currentEnter)
                    selectedIdx = idx;
            }
            if (selectedIdx >= 0)
                dropdown.Select(selectedIdx);

            dropdown.ItemSelected += (long index) =>
            {
                string selected = dropdown.GetItemText((int)index);
                @object.Set(WebRequestComponent.Parameters.WebRequestAgentHelper, selected);
            };

            hbox.AddChild(dropdown);
            AddCustomControl(hbox);
        }
    }
}
#endif
