#if TOOLS
using GameConfig.Constant;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GodotGameFramework.Editor
{
	/// <summary>
	/// 本地化键选择器：把脚本变量 <c>StringKey</c>（<c>[Export] public string StringKey</c>）
	/// 换成"可搜索 / 按前缀分组 / 显示译文"的下拉选择器。
	///
	/// 识别只看 Godot 自己的属性列表（名字 == <c>StringKey</c> 且是脚本变量），<b>不用 C# 反射</b>：
	/// 编辑器里非 <c>[Tool]</c> 脚本的节点并不是 C# 那个类的实例（<c>GetType()</c> 得到的是引擎类型），
	/// 反射扫不到 <c>StringKey</c>，只有属性列表是可靠来源——这一点是踩坑踩出来的。
	///
	/// 键与译文直接读 res://TheGame/DataTables/Localizations/*.txt，导表后不用重新编译就能看到新键。
	/// </summary>
	[Tool]
	public partial class LocalizationKeyInspectorPlugin : EditorInspectorPlugin
	{
		/// <summary>约定的属性名：脚本变量叫这个名字才会被换成本地化键选择器。</summary>
		public const string KeyPropertyName = "StringKey";

		public override bool _CanHandle(GodotObject @object)
		{
			return HasKeyProperty(@object);
		}

		public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name,
			PropertyHint hintType, string hintString, PropertyUsageFlags usage, bool wide)
		{
			if (type != Variant.Type.String || !string.Equals(name, KeyPropertyName, StringComparison.Ordinal))
			{
				return false;
			}

			// 属性名显式传给选择器：不依赖 EditorProperty.GetEditedProperty() 的时序
			AddPropertyEditor(name, new LocalizationKeyEditorProperty(name));
			return true;
		}

		/// <summary>
		/// 对象上是否有名为 <c>StringKey</c> 的脚本变量（<c>[Export]</c> 的成员才会进属性列表）。
		/// 直接查 Godot 的属性列表，因此对"占位脚本实例"（非 [Tool] 脚本）同样有效。
		/// </summary>
		private static bool HasKeyProperty(GodotObject @object)
		{
			if (@object == null)
			{
				return false;
			}

			foreach (Godot.Collections.Dictionary info in @object.GetPropertyList())
			{
				Variant nameVar = info["name"];
				if (nameVar.VariantType != Variant.Type.StringName && nameVar.VariantType != Variant.Type.String)
				{
					continue;
				}

				if (!string.Equals(nameVar.AsString(), KeyPropertyName, StringComparison.Ordinal))
				{
					continue;
				}

				var usage = (PropertyUsageFlags)info["usage"].AsInt64();
				if ((usage & PropertyUsageFlags.ScriptVariable) != 0)
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>
	/// 本地化键选择器（自定义 <see cref="EditorProperty"/>）：只用在 <c>StringKey</c> 上，属性名由插件传入。
	/// 用 EditorProperty 而不是自绘 Control 的原因：EmitChanged 会把修改交给编辑器的 undo/redo 栈，并标记场景已修改。
	/// </summary>
	[Tool]
	public partial class LocalizationKeyEditorProperty : EditorProperty
	{
		private const string EmptyText = "（未设置本地化键）";

		private Button m_Button;
		private PopupPanel m_Popup;
		private LineEdit m_Search;
		private CheckBox m_ScopeCheck;
		private Tree m_Tree;

		private string m_Current = string.Empty;
		private string m_FormPrefix = string.Empty;
		private bool m_Rebuilding;

		/// <summary>由插件显式传入的属性名（不依赖 GetEditedProperty 的时序）。</summary>
		private readonly string m_PropertyName;

		public LocalizationKeyEditorProperty(string propertyName)
		{
			m_PropertyName = propertyName ?? string.Empty;

			m_Button = new Button
			{
				ClipText = true,
				Alignment = HorizontalAlignment.Left,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				TooltipText = "从本地化表中选择键（可搜索 / 分组 / 显示译文）",
			};
			m_Button.Pressed += OnButtonPressed;
			AddChild(m_Button);
			AddFocusable(m_Button);

			BuildPopup();
		}

		private string ResolveProperty()
		{
			return m_PropertyName.Length > 0 ? m_PropertyName : GetEditedProperty().ToString();
		}

		/// <summary>切换检视对象时先收掉弹窗，避免已显示的 Window 随选择器一起释放。</summary>
		public override void _ExitTree()
		{
			if (m_Popup != null && GodotObject.IsInstanceValid(m_Popup))
			{
				m_Popup.Hide();
			}

			base._ExitTree();
		}

		private void BuildPopup()
		{
			m_Popup = new PopupPanel
			{
				Exclusive = true,
				Size = new Vector2I(560, 480),
			};
			AddChild(m_Popup);

			var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			m_Popup.AddChild(box);
			box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 8);

			m_Search = new LineEdit
			{
				PlaceholderText = "搜索键 / 译文…（回车选第一个）",
				ClearButtonEnabled = true,
			};
			m_Search.TextChanged += OnSearchTextChanged;
			m_Search.TextSubmitted += OnSearchSubmitted;
			box.AddChild(m_Search);

			m_ScopeCheck = new CheckBox { Text = "只看本界面" };
			m_ScopeCheck.Toggled += OnScopeToggled;
			box.AddChild(m_ScopeCheck);

			m_Tree = new Tree
			{
				SizeFlagsVertical = SizeFlags.ExpandFill,
				Columns = 2,
				HideRoot = true,
				AllowReselect = true,
				SelectMode = Tree.SelectModeEnum.Row,
			};
			m_Tree.SetColumnTitle(0, "键");
			m_Tree.SetColumnTitle(1, "译文");
			m_Tree.SetColumnExpandRatio(0, 3);
			m_Tree.SetColumnExpandRatio(1, 2);
			m_Tree.ItemSelected += OnTreeItemSelected;
			box.AddChild(m_Tree);

			m_Popup.PopupHide += OnPopupHide;
		}

		public override void _UpdateProperty()
		{
			GodotObject @object = GetEditedObject();
			m_Current = @object == null ? string.Empty : @object.Get(ResolveProperty()).AsString();
			RefreshButtonText();
		}

		private void OnButtonPressed()
		{
			LocalizationKeySource.Reload();
			m_FormPrefix = ResolveFormPrefix();

			bool scoped = !string.IsNullOrEmpty(m_FormPrefix);
			m_ScopeCheck.Visible = scoped;
			m_ScopeCheck.Text = $"只看 {m_FormPrefix}.* + 通用键";
			m_ScopeCheck.SetPressedNoSignal(scoped);

			m_Search.Text = string.Empty;
			m_Search.PlaceholderText = string.IsNullOrEmpty(LocalizationKeySource.DisplayLanguage)
				? "搜索键 / 译文…（回车选第一个）"
				: $"搜索键 / 译文（{LocalizationKeySource.DisplayLanguage}）…（回车选第一个）";

			RebuildTree();

			Vector2I position = (Vector2I)m_Button.GetScreenPosition() + new Vector2I(0, (int)m_Button.Size.Y);
			m_Popup.Popup(new Rect2I(position, m_Popup.Size));
			m_Search.CallDeferred("grab_focus");
		}

		private void OnPopupHide()
		{
			m_Search.Text = string.Empty;
		}

		private void OnSearchTextChanged(string _)
		{
			RebuildTree();
		}

		private void OnScopeToggled(bool _)
		{
			RebuildTree();
		}

		private void OnSearchSubmitted(string _)
		{
			TreeItem root = m_Tree.GetRoot();
			if (root == null)
			{
				return;
			}

			foreach (TreeItem item in Enumerate(root))
			{
				Variant metadata = item.GetMetadata(0);
				if (metadata.VariantType != Variant.Type.String)
				{
					continue;
				}

				string key = metadata.AsString();
				if (key.Length == 0)
				{
					continue; // 跳过"（清空）"项
				}

				ApplyKey(key);
				return;
			}
		}

		private void OnTreeItemSelected()
		{
			if (m_Rebuilding)
			{
				return; // 重建时 item.Select() 也会发信号，别当成用户点击
			}

			TreeItem selected = m_Tree.GetSelected();
			if (selected == null)
			{
				return;
			}

			Variant metadata = selected.GetMetadata(0);
			if (metadata.VariantType != Variant.Type.String)
			{
				return; // 分组行不可选
			}

			ApplyKey(metadata.AsString());
		}

		private void ApplyKey(string key)
		{
			m_Current = key;
			RefreshButtonText();

			string propertyName = ResolveProperty();
			if (propertyName.Length == 0)
			{
				GD.PushWarning("[LocalizationKey] 拿不到属性名，写入被跳过。");
				m_Popup.Hide();
				return;
			}

			// 正常通道：交给编辑器（undo/redo + 场景标脏）
			EmitChanged(propertyName, key);

			// 兜底通道：直接写一次，避免个别编辑器环境下 EmitChanged 没落地
			GetEditedObject()?.Set(propertyName, key);

			m_Popup.Hide();
		}

		private void RefreshButtonText()
		{
			if (string.IsNullOrEmpty(m_Current))
			{
				m_Button.Text = EmptyText;
				return;
			}

			string value = LocalizationKeySource.GetValue(m_Current);
			m_Button.Text = string.IsNullOrEmpty(value)
				? $"{m_Current}    ▾"
				: $"{m_Current}    ({value}) ▾";
		}

		private void RebuildTree()
		{
			m_Rebuilding = true;

			try
			{
				m_Tree.Clear();

				string filter = m_Search.Text.Trim();
				string scopePrefix = m_ScopeCheck.Visible && m_ScopeCheck.ButtonPressed ? m_FormPrefix : null;
				List<LocalizationKeySource.Group> groups = LocalizationKeySource.GetGroups(filter, scopePrefix);

				TreeItem root = m_Tree.CreateItem();
				if (root == null)
				{
					return;
				}

				TreeItem clearItem = m_Tree.CreateItem(root);
				clearItem.SetText(0, "（清空）");
				clearItem.SetMetadata(0, string.Empty);

				// 没有任何键能匹配上时给一条"手动使用所输入文本"的逃生通道，避免列表为空就没法填
				if (filter.Length > 0 && !ContainsExactKey(groups, filter))
				{
					TreeItem manual = m_Tree.CreateItem(root);
					manual.SetText(0, $"（手动使用 “{filter}”）");
					manual.SetMetadata(0, filter);
					manual.SetCustomColor(0, new Color(1.0f, 0.78f, 0.35f));
				}

				if (groups.Count == 0)
				{
					TreeItem none = m_Tree.CreateItem(root);
					none.SetText(0, filter.Length == 0 ? "（没有可用的本地化键，请先 Generate File → Localization File）" : "（没有匹配的键）");
					none.SetSelectable(0, false);
					return;
				}

				foreach (LocalizationKeySource.Group group in groups)
				{
					TreeItem groupItem = m_Tree.CreateItem(root);
					groupItem.SetText(0, $"{group.Name}  ({group.Entries.Count})");
					groupItem.SetSelectable(0, false);
					groupItem.SetCustomColor(0, new Color(0.55f, 0.72f, 1.0f));

					foreach (LocalizationKeySource.Entry entry in group.Entries)
					{
						TreeItem item = m_Tree.CreateItem(groupItem);
						item.SetText(0, entry.Key);
						item.SetText(1, entry.Value);
						item.SetMetadata(0, entry.Key);

						if (string.Equals(entry.Key, m_Current, StringComparison.Ordinal))
						{
							item.Select(0);
						}
					}
				}
			}
			finally
			{
				m_Rebuilding = false;
			}
		}

		private static bool ContainsExactKey(List<LocalizationKeySource.Group> groups, string key)
		{
			return groups.Any(group => group.Entries.Any(entry => string.Equals(entry.Key, key, StringComparison.Ordinal)));
		}

		/// <summary>由节点所属场景名推断界面前缀（例如放在 MainForm.tscn 里就是 "MainForm"）。</summary>
		private string ResolveFormPrefix()
		{
			if (GetEditedObject() is not Node node)
			{
				return string.Empty;
			}

			string scenePath = node.SceneFilePath;
			if (string.IsNullOrEmpty(scenePath))
			{
				scenePath = node.Owner?.SceneFilePath ?? string.Empty;
			}
			if (string.IsNullOrEmpty(scenePath))
			{
				return string.Empty;
			}

			string prefix = Path.GetFileNameWithoutExtension(scenePath);
			return LocalizationKeySource.HasPrefix(prefix) ? prefix : string.Empty;
		}

		private static IEnumerable<TreeItem> Enumerate(TreeItem parent)
		{
			for (TreeItem child = parent.GetFirstChild(); child != null; child = child.GetNext())
			{
				yield return child;

				foreach (TreeItem grandChild in Enumerate(child))
				{
					yield return grandChild;
				}
			}
		}
	}

	/// <summary>
	/// 本地化键数据源：解析 res://TheGame/DataTables/Localizations/*.txt（与运行时同一份 TSV 规则）。
	/// 每次打开弹窗都会重新读盘，导表后立刻生效，不依赖 <c>LocalizationKeyConstants</c> 是否已重新编译。
	/// </summary>
	internal static class LocalizationKeySource
	{
		internal sealed class Entry
		{
			public string Key;
			public string Value;
		}

		internal sealed class Group
		{
			public string Name;
			public readonly List<Entry> Entries = new List<Entry>();
		}

		/// <summary>没有 '.' 的键（通用键）归到这个组。</summary>
		private const string GeneralGroupName = "通用";

		/// <summary>译文展示优先级：优先中文，其次英文，否则取按名字排序的第一个。</summary>
		private static readonly string[] PreferredLanguages = { "ChineseSimplified", "English" };

		private static readonly Dictionary<string, Dictionary<string, string>> s_Languages =
			new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

		private static string s_DisplayLanguage = string.Empty;
		private static bool s_Loaded;

		/// <summary>当前用于显示译文的语言文件名（不含扩展名）。</summary>
		public static string DisplayLanguage => s_DisplayLanguage;

		/// <summary>重新扫描并解析本地化目录（打开弹窗时调用，保证导表后立刻可见）。</summary>
		public static void Reload()
		{
			s_Languages.Clear();
			s_DisplayLanguage = string.Empty;
			s_Loaded = true;

			foreach (string path in GetLocalizationFiles())
			{
				s_Languages[Path.GetFileNameWithoutExtension(path)] = ParseFile(path);
			}

			foreach (string preferred in PreferredLanguages)
			{
				if (s_Languages.ContainsKey(preferred))
				{
					s_DisplayLanguage = preferred;
					break;
				}
			}

			if (string.IsNullOrEmpty(s_DisplayLanguage) && s_Languages.Count > 0)
			{
				s_DisplayLanguage = s_Languages.Keys.OrderBy(key => key, StringComparer.Ordinal).First();
			}
		}

		/// <summary>取某个键在展示语言下的译文（没有则返回空串）。</summary>
		public static string GetValue(string key)
		{
			EnsureLoaded();

			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(s_DisplayLanguage))
			{
				return string.Empty;
			}

			return s_Languages[s_DisplayLanguage].TryGetValue(key, out string value) ? value : string.Empty;
		}

		/// <summary>是否存在以 <paramref name="prefix"/> + '.' 开头的键（用来判断"只看本界面"是否可用）。</summary>
		public static bool HasPrefix(string prefix)
		{
			if (string.IsNullOrEmpty(prefix) || !EnsureLoaded())
			{
				return false;
			}

			string head = prefix + ".";
			return s_Languages.Values.Any(items => items.Keys.Any(key => key.StartsWith(head, StringComparison.Ordinal)));
		}

		/// <summary>
		/// 取分组后的条目。
		/// <paramref name="filter"/> 同时匹配键与译文（忽略大小写）；
		/// <paramref name="prefix"/> 非空时只保留该界面的键（<c>prefix.*</c>）与通用键（无 '.'）。
		/// </summary>
		public static List<Group> GetGroups(string filter, string prefix)
		{
			var groups = new List<Group>();
			if (!EnsureLoaded() || string.IsNullOrEmpty(s_DisplayLanguage))
			{
				return groups;
			}

			// 键取所有语言的并集：某个语言 sheet 缺行时，键也不会从列表里消失
			var keys = new SortedSet<string>(StringComparer.Ordinal);
			foreach (Dictionary<string, string> language in s_Languages.Values)
			{
				keys.UnionWith(language.Keys);
			}

			var groupMap = new Dictionary<string, Group>(StringComparer.Ordinal);
			foreach (string key in keys)
			{
				if (!MatchesFilter(key, filter))
				{
					continue;
				}

				if (prefix != null && key.IndexOf('.') >= 0 && !key.StartsWith(prefix + ".", StringComparison.Ordinal))
				{
					continue;
				}

				string groupName = GetGroupName(key);
				if (!groupMap.TryGetValue(groupName, out Group group))
				{
					group = new Group { Name = groupName };
					groupMap.Add(groupName, group);
					groups.Add(group);
				}

				group.Entries.Add(new Entry { Key = key, Value = GetValue(key) });
			}

			// "通用"排最前，其余按名字排序
			groups.Sort((left, right) => string.CompareOrdinal(GetSortKey(left.Name), GetSortKey(right.Name)));
			return groups;
		}

		private static string GetSortKey(string groupName)
		{
			return groupName == GeneralGroupName ? string.Empty : groupName;
		}

		private static string GetGroupName(string key)
		{
			int dot = key.IndexOf('.');
			return dot <= 0 ? GeneralGroupName : key.Substring(0, dot);
		}

		private static bool MatchesFilter(string key, string filter)
		{
			if (string.IsNullOrEmpty(filter))
			{
				return true;
			}

			if (key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}

			string value = GetValue(key);
			return value.Length > 0 && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool EnsureLoaded()
		{
			if (!s_Loaded)
			{
				Reload();
			}

			return s_Languages.Count > 0;
		}

		private static string[] GetLocalizationFiles()
		{
			var files = new List<string>();

			using DirAccess dir = DirAccess.Open(GameFolderConstant.LocalizationPath);
			if (dir == null)
			{
				return files.ToArray();
			}

			dir.ListDirBegin();
			string fileName;
			while ((fileName = dir.GetNext()) != string.Empty)
			{
				if (!dir.CurrentIsDir() && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
				{
					files.Add(GameFolderConstant.LocalizationPath + fileName);
				}
			}
			dir.ListDirEnd();

			files.Sort(StringComparer.Ordinal);
			return files.ToArray();
		}

		private static Dictionary<string, string> ParseFile(string resPath)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);

			try
			{
				// 编辑器里 res:// 是真实目录，直接按文本读（与 LocalizationComponent 的 TSV 规则一致：第 3 列键、第 4 列值）
				string text = File.ReadAllText(ProjectSettings.GlobalizePath(resPath));
				foreach (string rawLine in text.Split('\n'))
				{
					string line = rawLine.TrimEnd('\r');
					if (line.Length == 0 || line[0] == '#')
					{
						continue;
					}

					string[] columns = line.Split('\t');
					if (columns.Length < 4)
					{
						continue;
					}

					string key = columns[2].Trim();
					if (key.Length == 0)
					{
						continue;
					}

					result[key] = columns[3].Trim();
				}
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[LocalizationKeyInspector] 读取 {resPath} 失败: {ex.Message}");
			}

			return result;
		}
	}
}
#endif
