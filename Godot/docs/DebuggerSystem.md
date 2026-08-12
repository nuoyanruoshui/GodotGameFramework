# 调试器系统 (Debugger Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Debugger/`、`Framework/GodotGameFrameworkCore/Debugger/`
> 本文档描述 GGF 的运行时调试器：UGF 风格的 FPS 悬浮图标 + 全功能调试窗口（Console / Information / Profiler / Other 多级页签）、BBCode-IMGUI 绘制模型、日志捕获与自定义调试窗口扩展。

---

## 1. 概述

调试器系统是 [Game Framework](https://gameframework.cn/) Debugger 模块的 Godot 移植，显示效果与 UGF（UnityGameFramework）运行时调试器对齐。遵循框架**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Debugger/` | `DebuggerManager`：窗口树注册表（按 `'/'` 分级的 `DebuggerWindowGroup`）、`IDebuggerWindow` 生命周期轮询 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Debugger/` | `DebuggerComponent`：CanvasLayer UI、FPS 图标、页签导航、BBCode 内容渲染、交互路由、布局持久化 + 全部内置窗口实现 | ✅ |

### 能力清单

- ✅ **FPS 悬浮图标**：可拖拽；文本按控制台日志级别变色（有 Warning → 黄、有 Error/Fatal → 红）；点击展开全窗口
- ✅ **全功能调试窗口**：标题栏拖拽、`0.5x ~ 4x` 缩放、多级页签（根页签末尾附 `Close` 收起）
- ✅ **Console**：双源日志捕获（框架日志 + Godot 引擎原生输出）、跨线程安全、五级过滤（Debug/Info/Warning/Error/Fatal 各带计数）、Lock Scroll、行选中查看堆栈、复制到剪贴板、行数上限裁剪
- ✅ **Information** 页签组：System / Environment / Screen / Graphics / Input / Path / Scene / Time 八个信息窗口
- ✅ **Profiler** 页签组：内存与对象概况（Godot 监控项 + .NET GC）、对象池逐池详情（含释放按钮）、引用池计数表（含严格检查开关）、资源/Web 请求/下载代理计数
- ✅ **Other** 页签组：Settings（缩放/布局重置/控制台行数）、Operations（GC / 释放对象池 / `GameEntry.Shutdown` 三态）
- ✅ 图标与窗口的位置、缩放、控制台过滤开关经 `GF.Setting` 持久化
- ✅ `RegisterDebuggerWindow(path, window)` 注册自定义调试窗口（任意层级路径）

---

## 2. 架构与数据流

Godot 没有 Unity 的 OnGUI（IMGUI），本移植用 **BBCode + RichTextLabel 模拟 IMGUI**：每帧选中的叶子窗口向 `DebuggerDraw` 上下文写入 BBCode 文本，组件渲染到内容区 `RichTextLabel`；按钮/开关以 `[url=id]` 链接呈现，`meta_clicked` 信号把点击路由回**本帧注册**的回调。

```
DebuggerComponent.OnUpdate（每帧）
    ├─ FpsCounter.Update（0.5s 间隔刷新 CurrentFps）
    ├─ 图标模式 → RefreshIcon：FPS 文本 + 按控制台计数变色
    └─ 全窗口模式
         ├─ RefreshToolbar：窗口树"名称+选中索引"签名比对 → 变化才重建页签按钮行
         └─ RefreshContent：Draw.Clear() → 叶子窗口.OnDraw() 写 BBCode
              → 文本与上帧不同才赋值 RichTextLabel.Text（避免逐帧重排版）
              → ScrollFollowing 按窗口要求（控制台 Lock Scroll）

DebuggerManager.Update（模块轮询，ActiveWindow 时）
    └─ 窗口树选中链 OnUpdate（如控制台泵日志队列）

日志流（双源）：

    框架日志源：
    Log.Xxx → GameFrameworkLog → DefaultLogHelper
        ├─ GD.Print / PushWarning / PushError（原有输出）
        └─ LogMessageReceived 事件（Error/Fatal 附堆栈）→ ConsoleWindow 跨线程暂存队列

    Godot 引擎原生源：
    GD.PrintErr / C++ 引擎错误 → Godot 文件日志（user://logs/godot.log）
        └─ ConsoleWindow.PollGodotLog() 每帧尾部轮询新行 → ParseGodotLogLine() 解析级别
             → LogNode.Create() → 同一暂存队列

    合并 → 主线程泵入 LogNode 正式队列（池化）→ 统一展示
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Debugger/IDebuggerManager.cs` / `DebuggerManager.cs` | 管理器接口 / 实现（`ActiveWindow`、窗口树根、注册/选中） |
| `GameFramework/Debugger/IDebuggerWindow.cs` | 窗口接口：`Initialize/Shutdown/OnEnter/OnLeave/OnUpdate/OnDraw` |
| `GameFramework/Debugger/IDebuggerWindowGroup.cs` / `DebuggerManager.DebuggerWindowGroup.cs` | 窗口组（`'/'` 路径分级、`SelectedIndex`、名称集合） |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.cs` | 组件主体：CanvasLayer UI、图标/窗口拖拽、页签、内容渲染、布局持久化 |
| `GodotGameFrameworkCore/Debugger/DebuggerDraw.cs` | BBCode 绘制上下文（`DrawItem/Button/Toggle/Link` 等，`[url]` 交互路由） |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.ScrollableDebuggerWindowBase.cs` | 信息窗口基类（`Component`/`Draw` 注入、`GetByteLengthString`） |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.FpsCounter.cs` | 帧率计数器（UGF 同款累计/间隔算法） |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.ConsoleWindow.cs` | 控制台窗口（日志捕获、过滤、选中详情、复制） |
| `GodotGameFrameworkCore/Debugger/LogNode.cs` | 日志结点（`IReference` 池化：时间/帧号/级别/内容/堆栈） |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.*InformationWindow.cs` | System/Environment/Screen/Graphics/Input/Path/Scene/Time/Profiler/Resource/WebRequest/Download 信息窗口 |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.ObjectPoolInformationWindow.cs` | 对象池详情（逐池参数 + Release 按钮） |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.ReferencePoolInformationWindow.cs` | 引用池 7 列计数表 + 严格检查/全名开关 |
| `GodotGameFrameworkCore/Debugger/DebuggerComponent.SettingsWindow.cs` / `OperationsWindow.cs` | 设置（缩放/布局/行数）/ 运行时操作（GC/池释放/Shutdown） |
| `GodotGameFrameworkCore/Utility/DefaultLogHelper.cs` | `LogMessageReceived` 静态事件（控制台日志来源） |

---

## 3. 核心机制

### 3.1 窗口树与页签导航

窗口按 `'/'` 分级路径注册，纯层 `DebuggerWindowGroup` 自动建组：

```
Console                     ← 根级叶子
Information/System          ← 自动创建 Information 组
Profiler/Object Pool
Profiler/Resource           ← 资源加载代理计数
Profiler/WebRequest         ← Web 请求代理计数
Profiler/Download           ← 下载代理计数
Other/Operations
```

- 页签按行渲染：第一行根组（末尾附红色 `Close` 按钮收起窗口），选中项若仍是组则递归渲染下一行子页签
- 页签切换驱动 `SelectedWindow.OnLeave()` → 改 `SelectedIndex` → `OnEnter()`（与 UGF `DrawDebuggerWindowGroup` 一致）
- `ShowFullWindow` 翻转时对根组整链调用 `OnEnter/OnLeave`
- 页签按钮仅在"名称集合 + 各级选中索引"签名变化时重建，非逐帧重建

### 3.2 DebuggerDraw 绘制上下文（BBCode-IMGUI）

| API | 说明 |
|-----|------|
| `Label / Title / Separator / Space / NewLine` | 文本、小节标题、分隔线、空行、换行 |
| `BeginTable() / DrawItem(name, value) / EndTable()` | 两列信息表格（`[table=2]`） |
| `Button(label, onClick)` | 内联按钮（`[url]` 链接呈现） |
| `Toggle(value, label, onChanged)` | 内联开关（`[x]` / `[  ]`） |
| `Link(bbcode, onClick)` | 任意可点击文本（控制台日志行） |
| `AppendRaw(bbcode)` | 原始 BBCode（自定义表格等） |
| `ScrollFollowing` | 请求内容区自动滚动到底（控制台 Lock Scroll） |
| `DebuggerDraw.Esc(text)` | 转义 `[` → `[lb]`，**外部内容（日志等）必须转义** |

交互原理：每帧 `Clear()` 后回调按序注册（id 自增），点击到达时按上一次渲染帧的 id 表调用——内容稳定时 id 稳定，点击后主动置脏强制重绘。

### 3.3 控制台与日志捕获

控制台窗口采用**双源捕获**架构，将框架日志和 Godot 引擎原生输出合并为统一的日志流展示。

#### 源一：框架日志（`LogMessageReceived` 事件）

- `DefaultLogHelper.Log` 在写 GD 输出前触发静态事件 `LogMessageReceived(level, message, stackTrace)`；堆栈仅 Error/Fatal 级别捕获（`StackTrace(2, true)`）
- 日志可能来自任意线程：事件处理只把 `LogNode.Create(...)`（`ReferencePool` 池化，线程安全）压入 `lock` 保护的暂存队列 `m_PendingLogNodes`

#### 源二：Godot 引擎原生输出（文件尾部轮询）

- `Initialize()` 时调用 `EnableGodotFileLogging()`：
  - 创建 `user://logs/godot.log` 文件，记录当前文件大小为起始位置（跳过已有历史日志）
  - 设置 `debug/file_logging/enable_file_logging = true` 启用 Godot 内建文件日志
  - 设置 `debug/file_logging/log_path = "user://logs/godot.log"`
  - 设置 `debug/file_logging/flush_stdout_on_print = true` 确保立即刷盘，尾部轮询能及时读到新行
- 每帧 `OnUpdate()` 调用 `PollGodotLog()`：
  - 以 `FileShare.ReadWrite` 打开日志文件（兼容 Godot 引擎同时写入）
  - 从上次记录位置 `m_GodotLogPos` 读取新增行
  - 调用 `ParseGodotLogLine()` 解析每行的日志级别
- `ParseGodotLogLine()` 识别以下行前缀模式：
  | 前缀模式 | 映射级别 |
  |----------|----------|
  | `ERROR:` | `GameFrameworkLogLevel.Error` |
  | `WARNING:` | `GameFrameworkLogLevel.Warning` |
  | `E ` 或 `E\t`（脚本错误） | `GameFrameworkLogLevel.Error` |
  | `<C++ 错误>` / `<C# 错误>` | `GameFrameworkLogLevel.Error` |
  | 普通输出（非引擎横幅/模块加载等噪声） | `GameFrameworkLogLevel.Debug` |
  - `ExtractMeaningfulMessage()` 从 Godot 错误行中截取 `at:` 之前的摘要描述，并加 `[Godot]` 前缀
  - 引擎引导噪声（`Godot Engine` 版本横幅、模块加载、D3D12/Vulkan/OpenGL 初始化行）被跳过

#### 合并与展示

- 两个源写入**同一个** `lock` 保护的暂存队列 `m_PendingLogNodes`
- 主线程在 `OnUpdate` 和 `OnDraw` 中调用 `Pump()` 将暂存队列转入正式队列 `m_LogNodes`
- 超出 `MaxLine`（默认 100，Settings 窗口可调 50–1000）从队首裁剪并 `ReferencePool.Release`
- 行点击 → 选中态高亮 + 底部详情（完整消息 + 堆栈 + `Copy` 复制到系统剪贴板）；选中期间暂停自动滚动
- 过滤开关与 Lock Scroll 经 `GF.Setting` 持久化（键 `Debugger.Console.*`）

### 3.4 布局与设置持久化

| 设置键 | 内容 |
|--------|------|
| `Debugger.Icon.X/Y` | 图标位置（拖拽结束时保存） |
| `Debugger.Window.X/Y` | 窗口位置（拖拽结束时保存） |
| `Debugger.Window.Scale` | 窗口缩放（Settings 窗口修改时保存） |
| `Debugger.Console.LockScroll` / `DebugFilter` / `InfoFilter` / `WarningFilter` / `ErrorFilter` / `FatalFilter` | 控制台开关 |

启动时 `OnEnter` 读取；`ResetLayout()` 恢复默认并立即保存。设置组件不可用时静默降级为默认值。

### 3.5 激活策略（DebuggerActiveWindowType）

| 枚举值 | 行为 |
|--------|------|
| `AlwaysOpen` | 总是激活（当前 `GameFramework.tscn` 的配置） |
| `OnlyOpenWhenDevelopment` | 仅调试构建（`OS.IsDebugBuild()`） |
| `OnlyOpenInEditor` | 仅编辑器运行（`OS.HasFeature("editor")`） |
| `AlwaysClose` | 总是关闭（仍可运行时 `GF.Debugger.ActiveWindow = true` 打开） |

---

## 4. DebuggerComponent 与 API

### 4.1 Inspector 参数

| 参数 | 默认 | 说明 |
|------|------|------|
| `_ActiveWindowType` | `OnlyOpenInEditor` | 激活策略（见 §3.5；场景中当前配为 `AlwaysOpen`） |
| `_ShowFullWindow` | `false` | 启动即展开全窗口（否则先显示 FPS 图标） |

### 4.2 API 总览

```csharp
GF.Debugger.ActiveWindow = true;                  // 激活/关闭整个调试器
GF.Debugger.ShowFullWindow = true;                // 展开/收起全窗口
GF.Debugger.WindowScale = 1.5f;                   // 缩放（0.5 ~ 4）
GF.Debugger.SelectDebuggerWindow("Profiler/Reference Pool");  // 代码选中页签
GF.Debugger.ResetLayout();                        // 重置布局并保存
float fps = GF.Debugger.CurrentFps;               // 当前帧率（FpsCounter）

// 注册/反注册自定义窗口
GF.Debugger.RegisterDebuggerWindow("Game/Cheat", new CheatWindow());
GF.Debugger.UnregisterDebuggerWindow("Game/Cheat");
```

### 4.3 内置窗口路径

`Console`；`Information/System·Environment·Screen·Graphics·Input·Path·Scene·Time`；`Profiler/Summary·Object Pool·Reference Pool·Resource·WebRequest·Download`；`Other/Settings·Operations`。

### 4.4 自定义调试窗口

继承 `DebuggerComponent.ScrollableDebuggerWindowBase` 最简（自动获得 `Component` / `Draw`）：

```csharp
public sealed class CheatWindow : DebuggerComponent.ScrollableDebuggerWindowBase
{
    protected override void OnDrawScrollableWindow()
    {
        Draw.Title("Cheat");
        Draw.Button("加 100 分", () => GF.Event.Fire(this, ScoreChangedEventArgs.Create(100)));
        Draw.Toggle(m_God, "无敌", v => m_God = v);
        Draw.NewLine();
    }
    private bool m_God;
}
GF.Debugger.RegisterDebuggerWindow("Game/Cheat", new CheatWindow());
```

直接实现 `IDebuggerWindow` 也可以：注意组件会把**自身注入为 `Initialize` 的首个参数**（`args[0]` 为 `DebuggerComponent`，自定义 `args` 顺延），绘制上下文经 `GF.Debugger.Draw` 获取。

---

## 5. 注意事项 / FAQ

**Q：运行后看不到调试器？**
检查 `_ActiveWindowType`（导出模板包中 `OnlyOpenInEditor` 不会激活）与场景中 Debugger 节点是否存在；`AlwaysClose` 下可用 `GF.Debugger.ActiveWindow = true` 手动打开。

**Q：控制台为什么收不到 `GD.Print` 的输出？**
控制台**双源捕获**：框架日志（`Log.Debug/Info/Warning/Error/Fatal` → `DefaultLogHelper.LogMessageReceived`）直接入队；Godot 引擎原生输出（`GD.Print`、`GD.PrintErr`、C++ 引擎错误/警告）通过 Godot 文件日志的尾部轮询（`PollGodotLog()`）捕获。两层覆盖下，绝大多数引擎输出都能在控制台中看到。注意：`GD.Print` 输出经文件日志捕获后以 `Debug` 级别呈现（需开启 Debug 过滤才能看到）。

**Q：Reference Pool 页签里的 "Enable Strict Check" 是什么？**
即 `ReferencePool.EnableStrictCheck` 的运行时开关（与场景 `ReferencePool` 节点的策略同一个底层开关），用于抓双重 `Release` —— 参见 `ObjectPoolSystem.md` 与 `EventSystem.md` 的池化回收约定。

**Q：日志行/信息条目里的 `[` 去哪了？**
BBCode 内容必须经 `DebuggerDraw.Esc` 转义；自定义窗口用 `Label/DrawItem` 会自动转义，`AppendRaw/Link` 需自行转义。

**Q：性能开销？**
仅激活时有开销：图标模式每帧只更新一行文本；全窗口模式内容文本做"变化才赋值"，页签做签名比对增量重建。控制台 `MaxLine` 限制重排版规模。

---

## 6. 已知边界

- 窗口尺寸固定 `760×520`（仅整体缩放，无拖拽改尺寸/最大化）
- 内容区为纯文本渲染：表格列宽由 RichTextLabel 自动分配，无法逐列精确控宽
- 未适配触摸多点操作（拖拽/点击按鼠标事件处理，触屏单点可用）
- Godot 引擎侧错误/警告/脚本错误已通过文件尾部轮询捕获（`PollGodotLog`），但解析依赖行前缀模式（`ERROR:` / `WARNING:` / `E ` 等），非标准格式的引擎输出可能被漏掉或误判为 Debug 级别
- `UpdatableWhilePlaying` 等运行期逐资源内存归属分析无 Unity Profiler 等价 API，Profiler/Summary 以 `Performance` 监控项 + .NET GC 计数为准
