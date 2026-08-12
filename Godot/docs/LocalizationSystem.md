# 本地化系统 (Localization Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Localization/`、`Framework/GodotGameFrameworkCore/Localization/`、`Framework/GodotGameFrameworkCore/Event/OnLanagueChangeEventArgs.cs`、`addons/TopMenu/`、`Framework/GodotGameFrameworkCore/UI/IStringKey.cs` ｜ 翻译源：仓库根 `Configs/Localization/*.xlsx`
> 本文档描述 GGF 的本地化系统：语言枚举与切换、字典文件格式、GetString 用法、TranslationServer 桥接、UI 文案刷新机制（IStringKey）与 Excel 翻译工作流。

---

## 1. 概述

本地化系统是 [Game Framework](https://gameframework.cn/) Localization 模块的 Godot 移植，核心是"**语言 → 字典文件 → GetString(key)**"，并额外把字典**桥接进 Godot TranslationServer**，让 Godot 原生 `Tr("key")` / 控件自动翻译也能工作。

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Localization/` | `LocalizationManager`：字典存储、GetString(1~16 参数格式化)、Language 枚举、DataProvider 解析框架 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Localization/` | `LocalizationComponent`（组件 + TranslationServer 桥接）、`DefaultLocalizationHelper`（TSV 解析、系统语言检测）、`LocalizationHelperBase`（Language↔locale 映射） | ✅ |
| 编辑器插件 | `addons/TopMenu/`（`GameFrameworkTopMenu.Generate.cs`） | TopMenu → Generate File → Localization File：Excel（`Configs/Localization/`）→ `.txt` 字典导出 | ✅（EditorPlugin） |
| UI 刷新 | `GodotGameFrameworkCore/UI/IStringKey.cs` + `GodotGameFrameworkCore/Localization/ButtonTr.cs` / `LabelTr.cs` + UIForm 生成模板 | 界面文案收集与刷新（`UIStringKeys` / `SetLocalizationValue()`） | ✅ |

### 能力清单

- ✅ 完整 Language 枚举（GF 原版，`Unspecified` + 数十种语言）
- ✅ 启动语言自动决定：编辑器指定语言（`GF.Base.EditorLanguage`）优先，否则跟随 OS（`OS.GetLocale()`）
- ✅ `GetString(key)` + 1~16 参数格式化重载；键缺失返回 `<NoKey>{key}`（不炸不空）
- ✅ TSV 文本字典 + 二进制字典两种解析路径
- ✅ 自动注册 `OptimizedTranslation` 到 TranslationServer（`Tr()` 可用）
- ✅ Excel 一键导出：每个 Sheet 一种语言 → `res://TheGame/DataTables/Localizations/<语言名>.txt`
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
| `GodotGameFrameworkCore/Config/GameFolderConstant.cs` | `LocalizationPath = "res://TheGame/DataTables/Localizations/"`、`LocalizationFiles = "res://TheGame/DataTables/Localizations/{0}.txt"` |
| `GodotGameFrameworkCore/Event/OnLanagueChangeEventArgs.cs` | 语言变更事件参数（`OnLanagueChangeEventArgs : GameEventArgs`，`Language Language` 属性） |
| `addons/TopMenu/GameFrameworkTopMenu.Generate.cs` | Excel → TSV 导出（TopMenu → Generate File → Localization File） |

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
- **`GetLocalizationFileNames()` 方法**：扫描 `LocalizationPath` 文件夹下所有 `.txt` 文件，返回不含扩展名的文件名数组（大小写与磁盘实际一致）。用于 UI（如 SettingForm）动态生成语言下拉列表。
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
2. Godot 编辑器菜单 **Project → Tools → Generate File → Localization File**：生成器遍历 `Configs/Localization/*.xlsx`，每个 Sheet 导出为 `res://TheGame/DataTables/Localizations/<Sheet名>.txt`（覆盖旧文件，仅 A–D 列），并刷新文件系统。
3. 代码中使用 `GF.Localization.GetString("新键名")`；UI 文案写入界面的 `SetValue()`。
4. 运行验证——键拼错时界面会显示 `<NoKey>键名`，直观可查。

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
- [ ] 界面自身 `SetLocalizationValue()` 纳入 `UIStringKeys` 统一调度（当前需手动调用）
- [ ] 二进制字典格式的生成工具链（当前仅文本 TSV 有 Excel 导出）
