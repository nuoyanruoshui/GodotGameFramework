# 本地化系统 (Localization Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Localization/`、`Framework/GodotGameFrameworkCore/Localization/`、`Framework/GodotGameFrameworkCore/Event/OnLanagueChangeEventArgs.cs`、`addons/TopMenu/`（导表 + 键常量）、`addons/ComponentInsoector/`（键选择器）、`Framework/GodotGameFrameworkCore/UI/IStringKey.cs` ｜ 翻译源：仓库根 `Configs/Localization/*.xlsx`
> 本文档描述 GGF 的本地化系统：语言枚举与切换、字典文件格式、GetString 用法、TranslationServer 桥接、UI 文案刷新机制（IStringKey）、键常量生成、编辑器键选择器与 Excel 翻译工作流。

---

## 1. 概述

本地化系统是 [Game Framework](https://gameframework.cn/) Localization 模块的 Godot 移植，核心是"**语言 → 字典文件 → GetString(key)**"，并额外把字典**桥接进 Godot TranslationServer**，让 Godot 原生 `Tr("key")` / 控件自动翻译也能工作。

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Localization/` | `LocalizationManager`：字典存储、GetString(1~16 参数格式化)、Language 枚举、DataProvider 解析框架 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Localization/` | `LocalizationComponent`（组件 + TranslationServer 桥接）、`DefaultLocalizationHelper`（TSV 解析、系统语言检测）、`LocalizationHelperBase`（Language↔locale 映射） | ✅ |
| 编辑器插件 | `addons/TopMenu/`（`GameFrameworkTopMenu.Generate.cs`） | TopMenu → Generate File → Localization File：Excel（`Configs/Localization/`）→ `.txt` 字典导出 + `LocalizationKeyConstants.cs` 键常量生成 | ✅（EditorPlugin） |
| 键常量（生成物） | `GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs` | 全部本地化键的 C# 常量（`LocalizationKeyConstants.MainForm.Score`）+ `All` 数组，替代裸键字符串 | ❌ |
| UI 刷新 | `GodotGameFrameworkCore/UI/IStringKey.cs` + `GodotGameFrameworkCore/Localization/ButtonTr.cs` / `LabelTr.cs` + UIForm 生成模板 | 界面文案收集与刷新（`UIStringKeys` / `SetLocalizationValue()`） | ✅ |

### 能力清单

- ✅ 完整 Language 枚举（GF 原版，`Unspecified` + 数十种语言）
- ✅ 启动语言自动决定：编辑器指定语言（`GF.Base.EditorLanguage`）优先，否则跟随 OS（`OS.GetLocale()`）
- ✅ `GetString(key)` + 1~16 参数格式化重载；键缺失返回 `<NoKey>{key}`（不炸不空）
- ✅ TSV 文本字典 + 二进制字典两种解析路径
- ✅ 自动注册 `OptimizedTranslation` 到 TranslationServer（`Tr()` 可用）
- ✅ Excel 一键导出：每个 Sheet 一种语言 → `res://TheGame/DataTables/Localizations/<语言名>.txt`
- ✅ 一键生成键常量：同一次导出把全部键写成 `LocalizationKeyConstants`（嵌套静态类 + `All` 数组），代码里不再写裸键字符串
- ✅ UI 文案集中于 `SetValue()`，经 `UIStringKeys` 批量刷新

---

## 2. 架构与数据流

### 2.1 编辑期（翻译工作流）

```
Configs/Localization/本地化.xlsx（仓库根，每个 Sheet = 一种语言，Sheet 名 = Language 枚举名）
    │  Godot 编辑器菜单 Project → Tools → Generate File → Localization File
    ▼  addons/TopMenu/GameFrameworkTopMenu.Generate.cs（解析 xlsx ZIP/XML，仅导出 A–D 列）
res://TheGame/DataTables/Localizations/
    ├── ChineseSimplified.txt
    └── English.txt              ← TSV，文件名即 Language 枚举名
res://Framework/GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs
                                 ← 全部键的 C# 常量（同一次生成产出，见 §3.5）
```

### 2.2 运行期

```
ProcedurePrelode.LoadLocalization()
    │  if (!GF.Base.EnableEditorResLoad)
    │      GF.Localization.Language = (Language)GF.Setting.GetInt("Language", (int)Language.English);
    │  else
    │      GF.Localization.Language = EditorLanguage != Unspecified ? EditorLanguage : SystemLanguage;
    │  说明：赋值 Language 属性即触发全部加载流程（见 §3.2），无需手动 ReadData
    ▼
LocalizationComponent (Godot 桥接层，场景节点 "Localization")
    │  Language setter 自动完成：
    │      1. m_LocalizationManager.Language = value
    │      2. TranslationServer.SetLocale(GetLocaleByLanguage(value))
    │      3. RemoveAllRawStrings()  ← 清旧字典 + 注销旧 Translation
    │      4. ReadData(Utility.Text.Format(GameFolderConstant.LocalizationFiles, value.ToString()))
    │             ├── ResourceComponent.LoadText 读文件 → ParseData(text)
    │             │        ├── UnregisterTranslation() ← 清旧 OptimizedTranslation
    │             │        ├── m_LocalizationManager.ParseData() ← 填充字典
    │             │        └── BridgeToTranslationServer(text) ← 填充 OptimizedTranslation，AddTranslation + SetLocale
    │        5. GF.Event.Fire(OnLanagueChangeEventArgs.EventId, OnLanagueChangeEventArgs.Create(value))
    │        6. GF.Setting.SetInt("Language", (int)value) + GF.Setting.Save()  ← 持久化
    ▼
LocalizationManager (纯 C# 层)  m_Dictionary<key, value>
    ▲
    │  GF.Localization.GetString("BulletShoot")
UI（MenuForm.SetLocalizationValue 等 + LabelTr/ButtonTr 自动刷新）
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Localization/Language.cs` | 语言枚举（`Unspecified=0` + GF 全量语言表） |
| `GameFramework/Localization/ILocalizationManager.cs` | 管理器接口 |
| `GameFramework/Localization/LocalizationManager.cs` | 字典、GetString 格式化、AddRawString/GetRawString、SystemLanguage |
| `GameFramework/Localization/ILocalizationHelper.cs` | 辅助器抽象 |
| `GodotGameFrameworkCore/Localization/LocalizationHelperBase.cs` | Node 基类；`GetLocaleByLanguage` / `GetLanguageByLocale` 静态映射 |
| `GodotGameFrameworkCore/Localization/DefaultLocalizationHelper.cs` | TSV/二进制解析；`SystemLanguage` = `OS.GetLocale()` 反查 |
| `GodotGameFrameworkCore/Localization/LocalizationComponent.cs` | 组件（`GF.Localization`）、ReadData、TranslationServer 桥接 |
| `GodotGameFrameworkCore/UI/IStringKey.cs` | UI 文案刷新接口（唯一成员 `void SetLocalizationValue()`） |
| `GodotGameFrameworkCore/Localization/ButtonTr.cs` | Button 实现（`Button : IStringKey`，运行时自动刷新 Text） |
| `GodotGameFrameworkCore/Localization/LabelTr.cs` | Label 实现（`Label : IStringKey`，运行时自动刷新 Text） |
| `GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs` | **生成物**：全部本地化键的 C# 常量（嵌套静态类 + `All` 数组），随 Localization File 一起重写 |
| `GodotGameFrameworkCore/Config/GameFolderConstant.cs` | `LocalizationPath = "res://TheGame/DataTables/Localizations/"`、`LocalizationFiles = "res://TheGame/DataTables/Localizations/{0}.txt"` |
| `GodotGameFrameworkCore/Event/OnLanagueChangeEventArgs.cs` | 语言变更事件参数（`OnLanagueChangeEventArgs : GameEventArgs`，`Language Language` 属性） |
| `addons/TopMenu/GameFrameworkTopMenu.Generate.cs` | Excel → TSV 导出 + 键常量生成（TopMenu → Generate File → Localization File） |
| `addons/ComponentInsoector/LocalizationKeyInspectorPlugin.cs` | 编辑器里的键选择器：把 `StringKey` 换成带搜索/分组/译文的值下拉（`EditorProperty`，支持 undo/redo），见 §3.6 |

---

## 3. 核心机制

### 3.1 字典文件格式（TSV）

制表符分隔，**至少 4 列**，`#` 开头为注释行（Excel 导出的表头行均以 `##` 开头，天然被跳过）：

```
##var	id	Key	Value          ← 注释行（跳过）
##type	int	string	string
	1	BulletShoot	The BulletShoot    ← A 列空、B 列序号、C 列=键、D 列=值
	2	Demo	Godot GameFrameWork Demo Game
```

解析规则（`DefaultLocalizationHelper.ParseData`）：跳过空行与 `#` 行 → 按 `\t` 切分 → 列数 < 4 跳过 → **`列[2]` 为键、`列[3]` 为值** → `AddRawString(key, value)`。键重复或非法会解析失败（返回 false 并 Warning）。

另支持二进制格式（`ReadDataBinary` / `ParseData(byte[])`）：`BinaryReader` 连续读 `string key + string value` 对，当前项目未使用。

### 3.2 语言决定与切换

**决定链（在 `ProcedurePrelode.LoadLocalization()` 中执行）：**

```
GF.Base.EnableEditorResLoad
    ├── false（正式运行）→ GF.Setting.GetInt("Language", (int)Language.English)
    │                      读取上次持久化的语言，首次启动默认为 English
    └── true（编辑器调试）→ GF.Base.EditorLanguage != Unspecified
                                 ├── true → EditorLanguage（Inspector 指定）
                                 └── false → SystemLanguage（OS.GetLocale() 反查）
```

- **初始化位置**（注意）：语言在 `ProcedurePrelode.OnEnter → LoadLocalization()` 中设置，**不在 `LocalizationComponent.OnInit()`**。`OnInit` 仅完成管理器初始化与辅助器装配，不设置语言（旧版本的启动自动决定语言逻辑已移除）。
- **`Language` setter 是完整流程入口**：给 `GF.Localization.Language` 赋值即触发以下全部操作（顺序固定）：
  1. `m_LocalizationManager.Language = value` —— 更新纯 C# 层当前语言
  2. `TranslationServer.SetLocale(GetLocaleByLanguage(value))` —— 更新 Godot locale
  3. `RemoveAllRawStrings()` —— 清空旧字典 + 注销旧 `OptimizedTranslation`
  4. `ReadData(LocalizationFiles 模板格式化)` —— 加载新语言字典文件（文本 TSV），桥接 TranslationServer
  5. `GF.Event.Fire(OnLanagueChangeEventArgs.EventId, OnLanagueChangeEventArgs.Create(value))` —— **广播语言变更事件**（`LabelTr`/`ButtonTr` 订阅此事件自动刷新文案）
  6. `GF.Setting.SetInt("Language", (int)value)` + `GF.Setting.Save()` —— 持久化语言选择到 `user://Settings.cfg`

- **字典文件名要求**：`GameFolderConstant.LocalizationPath` 指定文件夹（`res://TheGame/DataTables/Localizations/`），`LocalizationFiles` 为模板 `"res://TheGame/DataTables/Localizations/{0}.txt"`，其中 `{0}` = `value.ToString()`，即字典文件名必须与 Language 枚举名一致（`ChineseSimplified.txt`、`English.txt` 等）。
- **`GetLocalizationFileNames()` 方法**：扫描 `LocalizationPath` 文件夹下所有 `.txt` 文件，返回不含扩展名的文件名数组（按名称排序）。用于 UI（如 SettingForm）动态生成语言下拉列表。**实现走 `DirAccess` 而非 `System.IO.Directory`**（2026-08 修复）：打包后 `res://` 位于 `.pck` 虚拟文件系统内，`System.IO` 磁盘扫描读不到，只有 `DirAccess` 能跨编辑器 / 主包 / 已加载子包枚举。
- **Language<->locale 映射**：硬编码在 `LocalizationHelperBase.GetLocaleByLanguage()` / `GetLanguageByLocale()` 中（覆盖 GF 全量 51 种语言，未命中回退 `en`）。
- **运行时切换语言**：只需一行 `GF.Localization.Language = Language.English;` —— setter 自动完成清理旧字典、加载新字典、广播事件、持久化等全部操作。**无需手动调用 `RemoveAllRawStrings` / `ReadData` / `Save`**。

> ⚠️ **旧版手动切换代码已废弃**。之前文档描述的三步操作（`Language =`、`RemoveAllRawStrings`、`ReadData`）已全部收敛到 `Language` setter 内部，外部只需赋值 `Language` 属性即可。文档中先前的"运行时切换语言"示例代码仅作历史说明，请勿在新代码中使用。

### 3.3 TranslationServer 桥接

`ParseData(string)` 成功后，组件用**相同的解析规则重新扫一遍文本**，把键值灌入新建的 `OptimizedTranslation`（`Locale` = 当前语言），注册进 `TranslationServer` 并 `SetLocale`。因此：

- `GF.Localization.GetString(key)`（框架字典）与 Godot `Tr(key)` / 控件 `auto_translate`（TranslationServer）**两条通道同时可用**
- `RemoveAllRawStrings()` / 再次 `ParseData` 会先注销旧 `OptimizedTranslation`，不会重复注册
- 带 `userData` 的 `ParseData(string, object)` 及所有二进制重载**不桥接** TranslationServer（仅填框架字典）

### 3.4 UI 文案刷新（IStringKey）

> 注：即 CLAUDE.md 早期描述的 "UIStringLabelKey 机制"，实际接口名为 **`IStringKey`**。

- 接口：`GodotGameFramework.UI.IStringKey`，唯一成员 `void SetLocalizationValue()`——约定"把自己的文案从 `GF.Localization.GetString` 刷一遍"。
- 内置实现：`LabelTr : Label, IStringKey` 和 `ButtonTr : Button, IStringKey`（`GodotGameFrameworkCore/Localization/`）：
  - 声明 `[Export] public string StringKey` 属性，Inspector 中指定翻译键
  - **自动刷新**：在 `_Ready()` 订阅 `OnLanagueChangeEventArgs` → 调用参数化重载 `SetLocalizationValue(object, GameEventArgs)` 刷新 Text；`_ExitTree()` 取消订阅
  - 同时提供无参 `SetLocalizationValue()`（用于界面 `UIStringKeys` 批量刷新）和参数化 `SetLocalizationValue(object, GameEventArgs)`（事件驱动刷新）
  - **运行时切换语言后，所有场景中的 LabelTr / ButtonTr 自动更新文案，无需手动干预**
- UIForm 生成模板（`UIFormTemplet.txt`）为每个界面生成属性 `List<IStringKey> UIStringKeys`：懒加载调用 `FindChildrenOfType<IStringKey>()`**递归收集所有实现 IStringKey 的子孙节点**（不含界面自身）。
- Logic 模板在 `OnInit` 中调用 `UIStringKeys.ForEach(key => key.SetLocalizationValue())`，界面初始化即完成子部件文案本地化。
- TheGame 惯例（见 `MenuForm.Logic.cs`）：界面类自身也实现 `IStringKey`，把所有文案赋值集中写进 `SetLocalizationValue()`：

```csharp
public partial class MenuForm : IStringKey
{
    public void SetLocalizationValue()
    {
        m_Title.Text = GF.Localization.GetString("BulletShoot");
        m_Subtitle.Text = GF.Localization.GetString("Demo");
    }
}
```

> ⚠️ `FindChildrenOfType` 只收集**子孙节点**，界面自身的 `SetLocalizationValue()` 不会被 `UIStringKeys.ForEach` 调用到——需在 `OnInit`/`OnOpen` 中自行调用，或运行时切换语言后对所有打开界面手动触发刷新。

### 3.5 本地化键常量（LocalizationKeyConstants）

`Localization File` 在导出 `.txt` 的**同一次**执行里，把所有 Sheet 的 C 列（键）汇总，重写
`res://Framework/GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs`（命名空间 `GodotGameFramework.Localization`，**纯生成物，不要手改**）：

```csharp
public static class LocalizationKeyConstants
{
    public const string StartGame = "StartGame";

    public static class MainForm                       // 键 "MainForm.Score" 的 '.' 段 → 嵌套类
    {
        public const string Score = "MainForm.Score";
        public const string Timer = "MainForm.Timer";
    }

    public static readonly string[] All = { "MainForm.Score", "MainForm.Timer", "StartGame" };
}
```

生成规则（`GameFrameworkTopMenu.Generate.cs` → `GenerateLocalizationKeyConstants`）：

| 输入键 | 生成结果 | 说明 |
|---|---|---|
| `StartGame` | `public const string StartGame = "StartGame"` | 无 `.` 的键 → 直接常量 |
| `MainForm.Score` | 嵌套静态类 `MainForm` + 常量 `Score` | 按 `.` 分段逐层建类，层级不限 |
| `class` / `1Start` / `A-B` | `class_` / `_1Start` / `A_B` | 净化成合法标识符：关键字加 `_`、数字开头补 `_`、非法字符转 `_`（中文可直接作成员名） |
| `A-B` 与 `A_B` 并存 | `A_B` + `A_B_2` | 同层撞名追加 `_2`、`_3`… |
| `MainForm` 与 `MainForm.Score` 并存 | 常量 `MainForm_` + 类 `MainForm` | 嵌套类优先占用原名，常量加 `_` 避让（C# 不允许同名 const 与 class） |
| `A..B` | 归一到 `A.B` | 空分段直接忽略 |

- **去重 + 升序**：同一个键在每种语言的 Sheet 里各出现一次，生成时 `Distinct` + `Ordinal` 升序展开；`.xlsx` 也按文件名排序处理，因此多次生成的 diff 稳定。
- **`All` 数组**：全部键的字符串数组（嵌套类没法枚举出"所有键"），用于运行时校验、调试器翻页、批量补翻译检查等。
- **重复键检查**：同一个 Sheet 内出现重复键会在编辑器 Output 报错（运行时 `AddRawString` 同样会失败），跨 Sheet 的重复（各语言同一键）属正常，不报错。
- **生效时机**：生成的是普通 `.cs`，需一次 `dotnet build`（或 `--build-solutions`）才会出现在 IntelliSense 里；运行时行为不变（字典仍由 `.txt` 驱动）。

用法：

```csharp
m_Title.Text = GF.Localization.GetString(LocalizationKeyConstants.MainForm.Score);
labelTr.StringKey = LocalizationKeyConstants.StartGame;   // LabelTr/ButtonTr 的 [Export] 键
```

### 3.6 编辑器里的键选择器（本地化键值下拉）

Godot 没有 Odin 的 `[ValueDropdown]`。`addons/ComponentInsoector/LocalizationKeyInspectorPlugin.cs`
用 `EditorInspectorPlugin` + 自定义 `EditorProperty` 把脚本变量 `StringKey` 换成了**值下拉选择器**
（架构上等价于 ValueDropdown：`EmitChanged` 走编辑器 undo/redo、场景自动标脏）。

#### 识别规则（只有一条）

| 条件 | 说明 |
|---|---|
| 属性名**正好是 `StringKey`** | `LabelTr` / `ButtonTr` 的现有写法；新建 Tr 控件照抄即可 |
| 类型是 `string`（`Variant.Type.String`） | `StringName` 不接管，照常显示默认编辑器 |
| 判定依据是 **Godot 的属性列表**（`GetPropertyList()`：名字命中 + `Usage` 含 `ScriptVariable`） | **不用 C# 反射**，见下方踩坑记录 |

- 范围不限于 UI：任意 **Node 或 Resource**（含 `UIForm`、存档配置 `.tres`）上的 `StringKey` 都适用
- 需要 `[Export]`：只有导出成员才会进属性列表，非导出的 `StringKey` 编辑器里根本看不到
- 编辑器侧读写都用"插件传进来的属性名"，不依赖 `EditorProperty.GetEditedProperty()` 的时序

新建一个 Tr 控件的全套成本（和 `LabelTr`/`ButtonTr` 一样，**不需要** `[Tool]`）：

```csharp
public partial class RichTextLabelTr : RichTextLabel, IStringKey
{
    [Export] public string StringKey { get; private set; }   // ← 只有这行和本地化有关，不用改插件

    public void SetLocalizationValue() => Text = GF.Localization.GetString(StringKey);
}
```

#### 选择器行为

- 属性行变成按钮，显示 `键  (译文)`；点击弹出选择面板
- 顶部搜索框同时匹配**键**与**译文**（忽略大小写），回车选第一条
- 按键名前缀分组（`MainForm` / `SettingForm` / `通用`…），组内按名字排序
- 节点所在场景名能对上键前缀时（`MainForm.tscn` → `MainForm`），多出「只看 `MainForm.*` + 通用键」勾选项，默认勾上
- 搜索匹配不到任何键时，列表顶部出现「（手动使用 “…”)」一行，兜底手填；另有「（清空）」项
- 数据每次打开面板都重读 `res://TheGame/DataTables/Localizations/*.txt`（译文优先 `ChineseSimplified`，其次 `English`，否则取按名排序第一个），
  所以**导表后不必重新编译**就能选到新键——这一点和 `LocalizationKeyConstants` 常量不同

#### 使用前提

- `addons/ComponentInsoector` 已启用（`project.godot` 的 `editor_plugins/enabled` 里已有）
- **改过插件代码后**：`dotnet build` → 重启编辑器（或 `Project → Reload Current Project`）。Inspector 插件只在编辑器加载时注册，热编译不会让它生效
- 想临时关掉：去掉 `ComponentInsoector.cs` 里的 `AddInspectorPlugin(m_LocalizationKeyInspector)`，`StringKey` 会退回普通字符串输入框

#### ⚠️ 两条已被证伪的路线（2026-11，别再走一遍）

| 尝试 | 结果 | 原因 |
|---|---|---|
| `[LocalizationKey]` 特性，给任意名字的 `[Export] string` 开权限 | ❌ 不生效，且**偶发让编辑器直接关闭** | 特性已删除；崩溃机制未定位，不再尝试 |
| C# 反射扫 `[Export]` 成员来判断 | ❌ 不生效 | 非 `[Tool]` 脚本的节点在编辑器里**不是 C# 类的实例**，`GetType()` 得到的是引擎类型（如 `Godot.Label`），反射扫不到 `StringKey`；只有 `GetPropertyList()` 里能看到它 |

结论：**识别必须基于属性列表**。若以后真要支持自定义字段名，应换成
`_ParseBegin` + `AddCustomControl`（参考 `LocalizationComponentInspectorPlugin` 的 Helper 下拉），
而不是 `AddPropertyEditor` + 特性——后者正是上面崩溃路的形态。

---

## 4. 组件与 API

场景节点：`Framework/GameFramework.tscn` 中的 `Localization` 节点，经 `GF.Localization`（`LocalizationComponent`）访问。

### 4.1 Inspector 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `m_LocalizationHelperTypeName` | `GodotGameFramework.Localization.DefaultLocalizationHelper` | 辅助器类型名（反射创建并挂为子节点，可替换自定义格式） |
| （Base 组件）`EditorLanguage` | `Unspecified` | 非 Unspecified 时强制启动语言 |

### 4.2 属性 / 方法总览

```csharp
// 语言
GF.Localization.Language            // get/set；set 触发完整流程：更新管理器语言 → 设置 locale
                                    //       → 清旧字典 → 加载新字典文件（桥接 TS）
                                    //       → 广播 OnLanagueChangeEventArgs 事件（LabelTr/ButtonTr 自动刷新）
                                    //       → 持久化到 GF.Setting("Language") 并 Save()
GF.Localization.SystemLanguage      // OS 语言（OS.GetLocale 反查）
GF.Localization.DictionaryCount     // 字典条目数

// 获取可用语言列表
string[] names = GF.Localization.GetLocalizationFileNames();  // 扫描 LocalizationPath 下所有 .txt 文件名（无扩展名）

// 加载与解析
bool ok = GF.Localization.ReadData("res://.../English.txt");   // LoadText + ParseData（桥接 TS）
bool ok = GF.Localization.ReadDataBinary(assetName);           // LoadBinary + ParseData
bool ok = GF.Localization.ParseData(text);                     // 桥接 TranslationServer
bool ok = GF.Localization.ParseData(bytes / bytes,start,len);  // 不桥接

// 取值
string s = GF.Localization.GetString("Key");                   // 缺键 → "<NoKey>Key"
string s = GF.Localization.GetString("HpFmt", hp, maxHp);      // 1~16 参数，Utility.Text.Format

// 原始字典操作
bool GF.Localization.HasRawString(key);
string GF.Localization.GetRawString(key);        // 缺键返回 null（区别于 GetString）
bool GF.Localization.AddRawString(key, value);   // 重复键返回 false
bool GF.Localization.RemoveRawString(key);
void GF.Localization.RemoveAllRawStrings();      // 清字典 + 注销 TranslationServer
```

### 4.3 翻译工作流（新增一条文案）

1. 打开仓库根 `Configs/Localization/本地化.xlsx`，在**每个语言 Sheet** 中追加一行：B 列序号、C 列键名、D 列译文（Sheet 名必须是 Language 枚举名）。
2. Godot 编辑器菜单 **Project → Tools → Generate File → Localization File**：生成器遍历 `Configs/Localization/*.xlsx`，每个 Sheet 导出为 `res://TheGame/DataTables/Localizations/<Sheet名>.txt`（覆盖旧文件，仅 A–D 列），并**同时重写 `LocalizationKeyConstants.cs`**（见 §3.5），最后刷新文件系统。
3. **挂到界面上**：选中 `LabelTr` / `ButtonTr`，点 `String Key` 行的按钮，从弹出的选择器里挑键（可搜索、可按 `MainForm.*` 过滤、直接看译文，见 §3.6）。这一步不依赖第 2 步的重新编译，导表就能选。
4. **代码里用**：`GF.Localization.GetString(LocalizationKeyConstants.新键名)`（新常量需先 `dotnet build` 一次才进 IntelliSense）；界面自身文案写进 `SetValue()` / `SetLocalizationValue()`。
5. 运行验证——键拼错时界面会显示 `<NoKey>键名`，直观可查；改用常量后拼错会在编译期暴露。

---

## 5. 注意事项 / FAQ

**Q: `GetString` 和 `GetRawString` 的缺键行为不同？**
是。`GetString` 返回 `<NoKey>{key}`（保证 UI 不空白且可发现），`GetRawString` 返回 `null`（用于逻辑判断）。

**Q: 什么时候用 `Tr()`，什么时候用 `GF.Localization.GetString()`？**
参数化文案（占位符 `{0}`）只能用 `GetString`。纯静态文案两者皆可——`Tr()` 依赖桥接生成的 `OptimizedTranslation`，仅当字典经 `ReadData`/`ParseData(string)`（无 userData）加载后可用。

**Q: 语言文件是在 ProcedureLaunch 还是 ProcedurePrelode 加载？**
`ProcedurePrelode.LoadLocalization()`（流程顺序：ProcedureLaunch → ProcedureUpdate → **ProcedurePrelode** → ProcedureGame）。ProcedureLaunch 只校验组件存在。注意这与 CLAUDE.md 中"ProcedureLaunch 加载本地化"的旧描述不一致，以代码为准。

**Q: 新增语言要做什么？**
① Excel 加同名 Sheet（名字 = Language 枚举值，如 `Japanese`）→ 重新导出；② 无需改映射（`GetLocaleByLanguage` 已覆盖 GF 全量语言）；③ 若默认语言逻辑需要，设置 `EditorLanguage` 或运行期切换。

**Q: 字典 `.txt` 能热更吗？**
可以。`res://TheGame/DataTables/` 下的 `.txt` 可打入 Config 类型子包，`ProcedureUpdate` 先加载 Config 包，`ProcedurePrelode` 随后读取的即是补丁内容（Godot 资源解析链中子包路径优先）。

**Q: `addons/LocalizationEditor/` 目录里也有一个 `本地化.xlsx`？**
那是历史遗留副本（原 LocalizationEditor 插件目录已并入 TopMenu 的 Generate File），生成器**只读取仓库根 `Configs/Localization/`**（源码中 `res://` 上溯两级拼接），以该目录为唯一翻译源。

**Q: 运行时切换语言后已打开的界面没变？**
`LabelTr` 和 `ButtonTr` 会自动刷新（它们订阅了 `OnLanagueChangeEventArgs`）。但界面自身的 `SetLocalizationValue()`（如 `MenuForm` 直接给非 Tr 节点赋值）不会自动触发——需在监听到语言变更事件后手动调用每个打开界面的 `SetLocalizationValue()`（及 `UIStringKeys.ForEach(k => k.SetLocalizationValue())`）。

---

## 6. 已知边界与后续计划

- [x] 语言切换的一键封装（`Language` setter 已完成：重载字典 + 广播刷新事件 + 持久化）
- [x] 广播刷新事件（`OnLanagueChangeEventArgs`，`LabelTr`/`ButtonTr` 自动订阅刷新）
- [x] 键常量自动生成（`LocalizationKeyConstants`，随 Localization File 一起重写，含 `All` 数组）
- [x] 编辑器键选择器（`StringKey` 值下拉：搜索 / 分组 / 显示译文 / undo，见 §3.6）
- [ ] 界面自身 `SetLocalizationValue()` 纳入 `UIStringKeys` 统一调度（当前需手动调用）
- [ ] 二进制字典格式的生成工具链（当前仅文本 TSV 有 Excel 导出）
- [ ] 生成时校验各语言 Sheet 的键是否齐全（缺翻译告警）
