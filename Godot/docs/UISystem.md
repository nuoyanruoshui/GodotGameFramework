# UI 系统 (UI Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/UI/`、`Framework/GodotGameFrameworkCore/UI/`、`addons/ComponentInsoector/ScriptGenerateInspector.cs`
> 本文档描述 GGF 的界面系统：界面组与层级、UIForm 生命周期、打开/关闭流程、实例池化、本地化文本收集、UIItem 池化，以及 UIForm 脚本生成器的完整工作流。

---

## 1. 概述

UI 系统是 [Game Framework](https://gameframework.cn/) UI 模块的 Godot 移植，遵循框架的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/UI/` | UIManager：界面组管理、打开/关闭调度、实例对象池、事件定义 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/UI/` | UIComponent 组件封装、CanvasLayer 界面组容器、PackedScene 实例化、可 await API、事件转发 | ✅ |

**与 Unity 版（UGF）的关键差异**：GGF 没有独立的 `UIFormLogic` 逻辑类，也没有统一的 `ControlUIForm` 基类——每个界面是一个**直接继承 Godot `Control` 并实现 `IUIForm` 的 partial class**，由脚本生成器拆成 Ge（框架样板，可再生）/ Logic（业务代码，永不覆盖）两个文件（见 §6）。

### 能力清单

- ✅ 界面组（UIGroup）→ `CanvasLayer`，`Depth` 直接映射 `CanvasLayer.Layer` 控制渲染层级
- ✅ 组内界面链表排序 + 暂停/遮挡传播（`OnPause/OnResume/OnCover/OnReveal`）
- ✅ 界面实例对象池（关闭 ≠ 销毁，池满/过期才 `QueueFree`）
- ✅ `OpenUIForm`（serialId 事件驱动）与 `OpenUIFormAsync`（TCS 可 await）两种消费方式
- ✅ Luban 配置驱动：`OpenUIForm(UIFormId.MenuForm)` 由 `TbUIFormConfig` 解析资源路径与组名
- ✅ `IStringKey` 本地化文本自动收集（`OnInit` 时统一 `SetLocalizationValue()`）
- ✅ UIItem 池化基础设施（`UIItemBase` + `UIItemInstanceObject`）
- ✅ 编辑器一键生成 UIForm 脚本 + `m_` 前缀子节点自动收集与 `[Export]` 赋值

---

## 2. 架构与数据流

```
调用方（Procedure / 界面逻辑）
    │  GF.UI.OpenUIForm(UIFormId.Xxx) / OpenUIFormAsync<T>(...)
    ▼
UIExtension（配置驱动扩展，查 TbUIFormConfig → AssetPath + UIGroupName）
    ▼
UIComponent (Godot 桥接层，场景节点 "UI")
    │  委托                                        ▲ C# 事件
    ▼                                              │
UIManager : GameFrameworkModule (纯 C# 层)         │
    ├── UIGroup × N（链表管理 UIFormInfo，Refresh 算法）
    ├── IObjectPool<UIFormInstanceObject>（"UI Instance Pool"，按资源名 Spawn）
    │        │ 未命中
    │        ▼
    └── IResourceManager.LoadAsset（异步加载 PackedScene）
             │ 成功
             ▼
DefaultUIFormHelper（InstantiateUIForm → NodeUtility.InstantiatePack；
                     CreateUIForm → AddChild 到组容器；ReleaseUIForm → 释放节点）
```

场景树运行时结构（`DefaultUIGroupHelper : CanvasLayer`，`SetDepth(depth)` → `Layer = depth`）：

```
GameFramework
└── UI (UIComponent)
    └── InstanceRoot (CanvasLayer，找不到时自动创建)
        └── DefaultUIGroupHelper-Normal (CanvasLayer, Layer=Depth)
            ├── UIForm_MenuForm  (Control, IUIForm)
            └── UIForm_MainForm  (Control, IUIForm)
```

事件向上流转：

```
UIManager(C# 事件) → UIComponent(转发) → EventComponent(全局事件, 池化 EventArgs)
                                       └→ OpenUIFormAsync 的 TCS（serialId 匹配）
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/UI/IUIManager.cs` / `UIManager.cs` | 管理器接口 / 实现（打开、关闭、激活、实例池参数） |
| `GameFramework/UI/UIManager.UIGroup.cs` | 界面组：链表、Refresh 暂停/遮挡算法、深度分配 |
| `GameFramework/UI/UIManager.UIGroup.UIFormInfo.cs` | 组内界面信息（Paused/Covered 标记，池化） |
| `GameFramework/UI/UIManager.UIFormInstanceObject.cs` | （纯 C# 层实例对象定义，实际使用 Godot 层版本） |
| `GameFramework/UI/IUIForm.cs` / `IUIGroup.cs` | 界面 / 界面组接口 |
| `GameFramework/UI/IUIFormHelper.cs` / `IUIGroupHelper.cs` | 辅助器抽象 |
| `GameFramework/UI/Open/CloseUIForm*EventArgs.cs` | 管理器层事件参数（池化） |
| `GodotGameFrameworkCore/UI/UIComponent.cs` | 组件封装 + `OpenUIFormAsync`（TCS） |
| `GodotGameFrameworkCore/UI/UIExtension.cs` | 配置驱动扩展：`OpenUIForm(UIFormId)`、`OpenUIFormAsync<T>`、`GetTopUIForm` 等 |
| `GodotGameFrameworkCore/UI/DefaultUIFormHelper.cs` | PackedScene 实例化 / 挂树 / 释放 |
| `GodotGameFrameworkCore/UI/UIGroupHelperBase.cs` / `DefaultUIGroupHelper.cs` | 组容器基类（CanvasLayer）/ 默认实现（Layer=Depth） |
| `GodotGameFrameworkCore/UI/UIFormInstanceObject.cs` | 界面实例池对象（ObjectBase，Release 时真正销毁节点） |
| `GodotGameFrameworkCore/UI/UIItemBase.cs` / `UIItemInstanceObject.cs` | UIItem 逻辑基类 / 池对象（OnSpawn 显示、OnUnspawn 隐藏、Release 销毁） |
| `GodotGameFrameworkCore/UI/IStringKey.cs` | 本地化文本收集接口（`void SetLocalizationValue()`） |
| `GodotGameFrameworkCore/Templet/UIFormTemplet.txt` / `UIFormLogicTemplet.txt` | 脚本生成模板（Ge / Logic） |
| `addons/ComponentInsoector/ScriptGenerateInspector.cs` | 编辑器脚本生成器（Inspector 按钮） |
| `TheGame/MainPack/Scripts/Resources/ScriptGenerateRes.cs` + `TheGame/MainPack/Resources/ScriptGenerateRes.tres` | 生成器配置 |
| `TheGame/GameScripts/GameProto/UIGe/*.cs` | 已生成的 Ge 文件（MenuForm/MainForm/GameOver/SettingForm；LogInForm/QuestionTips 的 Ge 已移至 `TheGame/MainPack/Scripts/UI/`） |
| `TheGame/GameScripts/UI/*.Logic.cs` + `TheGame/MainPack/Scripts/UI/*.Logic.cs` | 已生成的 Logic 文件（业务逻辑；LogInForm/QuestionTips 在 MainPack，其余在 GameScripts） |

---

## 3. 核心机制

### 3.1 UIForm 生命周期

`IUIForm` 定义 11 个生命周期回调，由 UIManager / UIGroup 驱动：

```
OnInit(serialId, assetName, uiGroup, pauseCovered, isNewInstance, userData)
    │  每次打开都会调用（含池中复用）；isNewInstance 区分新建/复用
    ▼
OnOpen(userData)          → 模板默认 Visible = true
    │
    ├─ OnCover()/OnReveal()   ← 被上层界面遮挡 / 遮挡解除（Refresh 驱动）
    ├─ OnPause()/OnResume()   ← 上层界面 PauseCoveredUIForm=true 时暂停 / 恢复
    ├─ OnRefocus(userData)    ← RefocusUIForm 激活到组内最前
    ├─ OnUpdate(elapse, real) ← 每帧轮询（组内自上而下，遇 Paused 即停）
    ├─ OnDepthChanged(groupDepth, depthInGroup) ← 组内排序变化
    ▼
OnClose(isShutdown, userData) → 模板默认 Visible = false
    ▼
OnRecycle()               ← 下一帧 UIManager.Update 出队时调用，随后 Unspawn 回池
```

**注意**：`OnInit` 在每次打开时都会执行（不同于 Unity GF 的"仅首次"语义），信号订阅必须用 `isNewInstance` 保护，否则复用时会重复订阅：

```csharp
public void OnInit(int serialId, ..., bool isNewInstance, object userData)
{
    m_SerialId = serialId; /* ...框架字段赋值... */
    UIStringKeys.ForEach(key => key.SetLocalizationValue());     // 本地化刷新
    if (isNewInstance)
    {
        m_StartButton.Pressed += OnStartButtonPressed;  // 仅新实例订阅一次
    }
}
```

### 3.2 界面组与 Refresh 算法（`UIManager.UIGroup.Refresh`）

- 组内界面用**链表**管理，`AddUIForm` 永远 `AddFirst`——**链表头 = 最上层界面**
- `Refresh` 从头遍历：
  - 深度从 `UIFormCount` 递减分配（最上层深度最大），触发 `OnDepthChanged`
  - 第一个未被遮挡的界面收到 `OnReveal`，其余全部 `OnCover`
  - 某界面 `PauseCoveredUIForm == true` → 其下所有界面 `OnCover` + `OnPause`
  - 组自身 `Pause = true` → 组内全部暂停
- `RefocusUIForm` = 把节点移回链表头 + `Refresh` + `OnRefocus`
- 渲染层级两级：组间靠 `CanvasLayer.Layer`（= 组 Depth），组内靠 Godot 子节点树顺序 + `OnDepthChanged` 通知（框架不主动 MoveChild，界面可在回调中自行处理）

### 3.3 打开流程与实例池

```
OpenUIForm(assetName, groupName, priority, pauseCovered, userData) → serialId (自增)
    │
    ├─ m_InstancePool.Spawn(assetName) 命中
    │       └→ InternalOpenUIForm（同步完成：CreateUIForm → OnInit → AddUIForm → OnOpen → Refresh）
    │
    └─ 未命中 → m_UIFormsBeingLoaded[serialId] = assetName
            → IResourceManager.LoadAsset(assetName, priority, callbacks, OpenUIFormInfo)
            → 成功：InstantiateUIForm → UIFormInstanceObject.Create → 池注册(spawned=true)
                    → InternalOpenUIForm（异步完成）
            → 失败：OpenUIFormFailure 事件
```

- **加载途中关闭**：`CloseUIForm(serialId)` 若界面仍在加载，仅记入 `m_UIFormsToReleaseOnLoad`，加载完成后直接释放，不打开
- **关闭**：`RemoveUIForm`（触发补偿性 OnCover/OnPause）→ `OnClose` → `Refresh` → `CloseUIFormComplete` 事件 → 入回收队列；下一帧 `OnRecycle` + `Unspawn` 回池
- **真正销毁**：池容量（默认 16）/ 过期（默认 60s）触发 `UIFormInstanceObject.Release` → `ReleaseUIForm` 释放节点
- `SetUIFormInstanceLocked / SetUIFormInstancePriority` 控制池内实例的锁定与释放优先级

### 3.4 OpenUIFormAsync（serialId + TCS 模式）

`UIComponent.OpenUIFormAsyncInternal`：

```
new TaskCompletionSource<IUIForm>()
    → serialId = m_UIManager.OpenUIForm(...)
    → GetUIForm(serialId) != null ?           // 池命中 → 已同步打开
          直接 TrySetResult
        : m_UIFormTask[serialId] = tcs        // 异步路径，等事件回调
OnOpenUIFormSuccess → tcs.TrySetResult(e.UIForm)，移除字典项
OnOpenUIFormFailure → tcs.TrySetException(GameFrameworkException)，移除字典项
```

参数非法（空资源名/组名）时返回 `Task.FromResult<IUIForm>(null)`，**不抛异常**；加载失败时 await 处**会抛** `GameFrameworkException`。

### 3.5 IStringKey 本地化文本自动收集

Ge 模板生成如下属性（懒加载，递归收集所有实现 `IStringKey` 的子节点，含界面自身不在内——`FindChildrenOfType` 只遍历子树）：

```csharp
public List<IStringKey> UIStringKeys => m_UIStringKeys ??=
    this.FindChildrenOfType<IStringKey>() ?? new List<IStringKey>();
```

Logic 模板在 `OnInit` 中统一调用 `UIStringKeys.ForEach(key => key.SetLocalizationValue())`。TheGame 的实际用法是让**界面类自身**实现 `IStringKey`（如 `MenuForm : IStringKey`），在 `SetLocalizationValue()` 中集中刷新文本：

```csharp
public void SetLocalizationValue()
{
    m_Title.Text    = GF.Localization.GetString("BulletShoot");
    m_Subtitle.Text = GF.Localization.GetString("Demo");
}
```

> 注意：由于收集只扫子树，界面自身的 `SetLocalizationValue()` 不会被 `UIStringKeys` 收到，需要在别处（如切换语言事件）手动调用，或者使用实现了 `IStringKey` 的内置 `LabelTr` / `ButtonTr` 组件挂在树里（`GodotGameFrameworkCore/Localization/ButtonTr.cs` / `LabelTr.cs`）。

### 3.6 UIItem 池化（基础设施）

`UIItemBase`（纯 C# 逻辑基类，`OnInit`/`OnRecycle` + `CachedNode`）与 `UIItemInstanceObject`（`ObjectBase` 池对象）用于界面内部子元素（列表项等）的复用：

- `OnSpawn`：位置归零 + `Visible = true`
- `OnUnspawn`：`Visible = false`（隐藏不销毁）
- `Release`：`ItemLogic.OnRecycle()` → `QueueFree()`

> ⚠️ **当前状态**：框架**尚未提供** `SpawnItem<TLogic>/UnspawnItem` 之类的封装 API（注释中提及但代码不存在）。使用需自行通过 `GF.ObjectPool` 创建 `IObjectPool<UIItemInstanceObject>` 并调用 `UIItemInstanceObject.Create(...)` 注册。TheGame 当前仅通过 NodePool 管理池化 UI 元素（如 `DamagePop`），未使用 UIItem 池化路径。

---

## 4. UIComponent 与 API

场景节点：`Framework/GameFramework.tscn` 中的 `UI` 节点，经 `GF.UI` 访问。初始化放在 `OnInit`（对应 `_Ready`，子→父顺序保证兄弟组件已注册）而非 `OnEnter`。

### 4.1 Inspector 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `m_EnableOpenUIFormSuccessEvent` 等 4 项（Success/Failure/CloseComplete 开，Update 关） | [Export] 配置 | 是否转发到全局 EventComponent。Godot 自动管理依赖，无 DependencyAsset |
| `m_InstanceAutoReleaseInterval` | 60 | 实例池自动释放间隔（秒） |
| `m_InstanceCapacity` | 16 | 实例池容量 |
| `m_InstanceExpireTime` | 60 | 实例过期秒数 |
| `m_InstancePriority` | 0 | 实例池优先级 |
| `m_InstanceRoot` | null | UI 根节点；为空时查找/创建名为 `InstanceRoot` 的 CanvasLayer |
| `m_UIFormHelperTypeName` | `GodotGameFramework.UI.DefaultUIFormHelper` | 界面辅助器类型名（反射创建） |
| `m_UIGroupHelperTypeName` | `GodotGameFramework.UI.DefaultUIGroupHelper` | 界面组辅助器类型名 |
| `UIGroupRes` | `TheGame/MainPack/Resources/UIGroupRes.tres` | 界面组定义资源（`UIGroup[]{ Name, Depth }`，TheGame 仅 "Normal"） |

界面组由 `ProcedurePrelode.LoadUIGroup()` 在启动时遍历 `GF.UI.UIGroupRes.Groups` 调用 `GF.UI.AddUIGroup(name, depth)` 注册。

### 4.2 方法总览

```csharp
// 界面组
GF.UI.AddUIGroup(name);  GF.UI.AddUIGroup(name, depth);
GF.UI.HasUIGroup(name);  GF.UI.GetUIGroup(name);  GF.UI.GetAllUIGroups();

// 打开（按资源名，8 个重载：priority / pauseCoveredUIForm / userData 任意组合）
int serialId = GF.UI.OpenUIForm(assetName, groupName, priority, pauseCovered, userData);

// 打开（配置驱动，UIExtension 扩展方法，查 TbUIFormConfig）
int serialId = GF.UI.OpenUIForm(UIFormId.MenuForm, userData);
MenuForm form = await GF.UI.OpenUIFormAsync<MenuForm>(UIFormId.MenuForm);
Task<IUIForm> t = GF.UI.OpenUIFormAsync(assetName, groupName, userData);

// 关闭 / 激活
GF.UI.CloseUIForm(serialId);          // 界面不存在会抛 GameFrameworkException
GF.UI.CloseUIForm(uiForm, userData);
GF.UI.CloseUIForm(assetName);         // 扩展：按资源名找到第一个再关
GF.UI.CloseUIForms(groupName);        // 扩展：关组内全部
GF.UI.CloseAllLoadedUIForms();  GF.UI.CloseAllLoadingUIForms();
GF.UI.RefocusUIForm(uiForm / serialId);

// 查询
GF.UI.HasUIForm(serialId / assetName);   GF.UI.HasUIForm<T>(assetName);  GF.UI.HasUIForm(formId);
GF.UI.GetUIForm(serialId / assetName);   GF.UI.GetUIForms(assetName);
GF.UI.GetAllLoadedUIForms();             GF.UI.GetTopUIForm();
GF.UI.IsLoadingUIForm(serialId / assetName);  GF.UI.IsValidUIForm(uiForm);

// 池控制
GF.UI.SetUIFormInstanceLocked(uiForm, locked);
GF.UI.SetUIFormInstancePriority(uiForm, priority);
```

### 4.3 使用示例（TheGame）

```csharp
// MenuForm.Logic.cs：按钮切界面
private void OnStartButtonPressed()
{
    GF.UI.CloseUIForm(this);
    GF.UI.OpenUIForm(UIFormId.MainForm);
}

// MainForm.Logic.cs：OnInit 中异步组织游戏场景
Node2D scene = (Node2D)await GF.Scene.LoadSceneAsync(ResourcesCollectionConstant.Scenes_Map);
CatEntity cat = await GF.Entity.ShowEntityAsync<CatEntity>(EntityId.Cat);
```

---

## 5. Luban 配置驱动

`Configs/GameConfig/Datas/界面UI.xlsx` → 生成 `GameConfig.UIFormId` 枚举（`MenuForm=0`, `MainForm=1`）与 `GameConfig.UI.UIFormConfig`（含 `UIFormId`、`AssetPath`、`UIGroupName`）。`UIExtension.OpenUIForm(UIFormId)` 在 `ConfigSystem.Instance.Tables.TbUIFormConfig.DataList` 中查找配置，找不到抛异常。新增界面 = 配表 → 跑 Luban 生成 → 生成脚本（§6）→ 直接用枚举打开。

---

## 6. UIForm 脚本生成器（ScriptGenerateInspector）

`addons/ComponentInsoector/ScriptGenerateInspector.cs`（`EditorInspectorPlugin`，`#if TOOLS`）。`_CanHandle` 返回 `@object is CanvasItem or Node3D`：**Control → UIForm 模板；Node2D / Node3D → Entity 模板**（EntityTemplet/EntityLogicTemplet，机制相同，本节以 UI 为主）。

Inspector 底部三个按钮：**Bind UI Script**（生成+挂载）、**Delete Gen**（删 Ge 并清脚本引用）、**Delete Logic**（删 Logic），均有确认弹窗。

### 6.1 双文件 partial class 布局

| 文件 | 输出目录（默认） | 覆盖策略 | 内容 |
|------|------------------|----------|------|
| `<类名>.cs`（Ge） | `res://TheGame/GameScripts/GameProto/UIGe/` | **每次生成覆盖** | `partial class <类名> : <节点类型>, IUIForm`：框架属性（SerialId 等）、`UIStringKeys` 收集器、`[Export]` 子节点字段 |
| `<类名>.Logic.cs`（Logic） | `res://TheGame/GameScripts/UI/` | **仅首次生成创建** | 同名 partial class：11 个生命周期方法骨架（含框架必需赋值段） |

- 类名 = 节点名经 `Sanitize`（去非法字符，数字开头加 `_`）；父类 = 节点的**实际 C# 类型名**（`@object.GetType().Name`，即 Control/Panel/自定义类等）
- Godot 要求**文件名与类名一致**才能在 Inspector 显示 `[Export]` 字段，因此 Ge 文件必须叫 `<类名>.cs`；Logic 文件带 `.Logic` 后缀放另一目录，两者互不冲突
- 模板占位符：`_NAMESPACE_` / `_PARENT_` / `_CLASSNAME_` / `_CHILDNODES_`

### 6.2 配置（`TheGame/MainPack/Resources/ScriptGenerateRes.tres` → `ScriptGenerateRes : Resource`）

| 字段 | 默认值 | 用途 |
|------|--------|------|
| `NameSpace` | `"GameLogic"` | 生成命名空间 |
| `NodePrefix` | `"m_"` | 子节点收集前缀 |
| `UIOutPutPathGe` | `res://TheGame/GameScripts/GameProto/UIGe/` | UI Ge 输出目录 |
| `UIOutPutPathLogic` | `res://TheGame/GameScripts/UI/` | UI Logic 输出目录 |
| `EntityOutPutPathGe` | `res://TheGame/GameScripts/GameProto/EntityGe/` | Entity Ge 输出目录 |
| `EntityOutPutPathLogic` | `res://TheGame/GameScripts/Entity/` | Entity Logic 输出目录 |

插件**按属性名**（`res.Get(prop)`）读取配置而非强类型转换，即使 `ScriptGenerateRes` C# 类型尚未在编辑器注册也能工作；`.tres` 中未显式写入的字段读到空串时回退硬编码默认 `res://TheGame/`。

### 6.3 子节点自动收集与赋值（`ReadChildNodes`）

1. 递归遍历节点树，名称以 `NodePrefix`（默认 `m_`）开头的节点被收集（重名仅收第一个并警告）
2. 生成 `[Export] private <节点类型> <节点名>;` 字段替换 `_CHILDNODES_`
3. `SetScript(script)` 后对每个收集到的节点执行 `node.Set(child.Name, child)` 自动赋引用
4. `MarkSceneAsUnsaved()` 标记场景待保存

### 6.4 完整工作流

```
1. 场景中创建 Control 节点（如 "MenuForm"），子节点按 m_ 前缀命名（m_Title, m_StartButton...）
2. 选中节点 → Inspector → "Bind UI Script" → 确认
3. 写出 Ge（覆盖）+ Logic（若无）→ fs.UpdateFile + fs.Scan()
4. GD.Load<CSharpScript>(gePath) → SetScript → 自动赋值子节点 → 场景标记未保存
5. dotnet build（首次生成的脚本需构建一次，Inspector 才能显示 [Export] 字段；
   若第 4 步加载失败会提示"重新构建后手动附加"）
6. 保存场景；配表 UIFormId 后即可 GF.UI.OpenUIForm(UIFormId.Xxx) 打开
```

---

## 7. 注意事项 / FAQ

**Q: 为什么我的按钮回调触发了两次？**
`OnInit` 每次打开（含池中复用）都会调用，信号订阅必须包在 `if (isNewInstance)` 里。

**Q: 关闭界面后节点还在场景树里？**
正常。关闭只是 `Visible = false` + 回池（`UIFormInstanceObject`），池满（16）或过期（60s）才真正 `QueueFree`。不希望复用可用 `SetUIFormInstanceLocked` 或调小池参数。

**Q: `CloseUIForm(serialId)` 抛异常？**
界面不存在时 UIManager 直接抛 `GameFrameworkException`（原版 GF 行为）。不确定是否存在时先 `HasUIForm(serialId)`，或用扩展方法 `GF.UI.CloseUIForm(assetName)`（内部判空）。

**Q: 同一界面能开多个实例吗？**
能。每次 `OpenUIForm` 返回新 serialId；池未命中会加载新实例。`GetUIForm(assetName)` 只返回第一个，多实例用 `GetUIForms`。

**Q: 事件参数能否持有？**
所有 `XxxEventArgs` 来自 `ReferencePool`，回调返回后即回收，跨帧使用必须先拷贝字段。

**Q: 手写 UIForm（不用生成器）可以吗？**
可以：任意 `Control` 子类实现 `IUIForm` 全部成员即可（`DefaultUIFormHelper.CreateUIForm` 只检查 `uiFormInstance is IUIForm`）。生成器只是消除样板。

**Q: 修改了 Ge 文件里的代码，下次生成没了？**
Ge 文件头部注明"生成时会被覆盖，请勿手动修改"，业务代码一律写在 `.Logic.cs`。

### 与 CLAUDE.md 描述的差异（以本文/代码为准）

- 不存在 `ControlUIForm` 基类与 `Base/Node/UI/` 目录；生成的界面类直接继承 `Control`（或节点实际类型）
- 本地化收集接口是 `IStringKey`（非 `UIStringLabelKey`）
- Logic 文件名为 `<类名>.Logic.cs`（非与 Ge 同名的 `<类名>.cs`）；配置字段为 `UIOutPutPathGe/UIOutPutPathLogic/EntityOutPutPathGe/EntityOutPutPathLogic`（非 `OutPutPathGe/OutPutPathLogic`）
- 生成按钮同样对 Node2D/Node3D 生效（生成 Entity 脚本），不仅限 Control
