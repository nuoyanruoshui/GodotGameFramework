# C# 程序集热更方案 (Windows + Android)

> ⚠️ **搁置状态（2026-07-25）**：本文档描述的 ALC 程序集热更方案（Bootstrap / HotUpdateManager / 壳+逻辑分离）**已暂时搁置**，等待**华佗团队**完成对 Godot 的热更适配后再继续实施。以下为原设计方案，配套基础设施（下载通道、资源热更管线、崩溃安全守护）已先行落地，重启时可直接复用，详见下方"配套基础设施"。
>
> **配套基础设施（已落地，搁置期间不动）：**
> - 统一下载通道 `GF.Download`（`GodotGameFrameworkCore/Download/`，断点续传 + 大小/SHA256 校验，详见 `DownloadSystem.md`）
> - 资源热更管线（`ProcedureUpdateVersion` → `ProcedureCheckResources` → `ProcedureUpdateResources`：多包并发下载、版本比对、失败回退）
> - 崩溃安全守护 `HotUpdateSafetyGuard`（`GodotGameFrameworkCore/HotUpdate/`，命名空间 `GodotGameFramework.HotUpdate` 已建立）

## 前提

| 维度 | 决策 |
|------|------|
| 目标平台 | **仅 Windows + Android**（无 iOS/主机/Web AOT 限制） |
| 生效方式 | **下载后重启生效** |
| 核心技术 | `AssemblyLoadContext` — .NET 标准 API，CoreCLR 和 Mono 均支持 |

## 核心原理

```
首次启动:
  Godot 加载 GodotProject.dll（内置版本）→ 运行游戏

热更后重启:
  Godot 加载 Bootstrap.dll（不热更，永远不变）
    → HotUpdateManager 检测 user://hotupdate/GodotProject.Game.dll
    → 有新版 → AssemblyLoadContext 加载热更 DLL
    → 从热更 DLL 中创建 Procedure、逻辑类实例
    → Godot 脚本（Entity/UI）通过接口委托到热更 DLL 中的逻辑
```

关键约束：**只有纯 C# 类可以直接走 ALC 热更。Godot 脚本类型（继承 CharacterBody2D / Control 的 partial class）不能放进 ALC 程序集**——因为 Godot 的脚本解析/序列化系统不识别 ALC 加载的类型。

现有代码的热更能力分析：

| 类型 | 是否 Godot 脚本 | ALC 热更 |
|------|:--:|:--:|
| **ProcedureBase 子类** (ProcedureLaunch, ProcedureGame, ProcedureUpdateVersion...) | ❌ 纯 C# | ✅ 零改动直接热更 |
| **Entity** (CatEntity, AngerEntity...) | ✅ CharacterBody2D | ❌ 需提取逻辑到接口 |
| **UI Form** (MenuForm, MainForm...) | ✅ Control | ❌ 需提取逻辑到接口 |
| **Event 参数** (ScoreChangedEventArgs...) | ❌ 纯 C# | ✅ 零改动直接热更 |
| **Framework** (GameFramework, GodotGameFrameworkCore) | ❌ 纯 C# | ⚠️ 不建议热更（基础层改动风险大） |

---

## 1. 项目拆分

### 当前结构

```
GodotProject.sln
└── GodotProject.csproj        ← 全部代码在一个程序集
    ├── Framework/GameFramework/
    ├── Framework/GodotGameFrameworkCore/
    └── TheGame/GameScripts/
```

### 目标结构

```
GodotProject.sln
├── GodotProject.Bootstrap.csproj   ← 新增：引导程序集（永不热更，~20KB）
│   ├── IHotUpdateEntry.cs          ← 热更入口接口
│   ├── IEntityLogic.cs             ← 实体逻辑接口
│   ├── IUIFormLogic.cs             ← UI 逻辑接口
│   ├── HotUpdateLoadContext.cs     ← 自定义 ALC
│   └── HotUpdateManager.cs         ← 加载/缓存/安全调用
│
└── GodotProject.csproj             ← 现有代码（可全量热更）
    ├── [引用 GodotProject.Bootstrap]
    ├── Framework/GameFramework/
    ├── Framework/GodotGameFrameworkCore/
    └── TheGame/GameScripts/
        ├── Procedure/              ← 纯 C#，可直接用热更版本替换
        ├── Entity/                 ← Godot 脚本壳 + IEntityLogic 实现
        ├── UI/                     ← Godot 脚本壳 + IUIFormLogic 实现
        ├── Event/                  ← 纯 C#，可直接热更
        └── Logic/                  ← 新增：所有业务逻辑实现
```

#### Bootstrap.csproj

```xml
<Project Sdk="Godot.NET.Sdk/4.7.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>GodotProject.Bootstrap</AssemblyName>
    <RootNamespace>GodotGameFramework.HotUpdate</RootNamespace>
    <!-- 此程序集永远随 App 发布，不做热更 -->
  </PropertyGroup>
</Project>
```

#### 主 csproj（修改后）

在原 `GodotProject.csproj` 中增加：

```xml
<ItemGroup>
  <ProjectReference Include="..\GodotProject.Bootstrap.csproj" />
</ItemGroup>
```

> 注意：`<EnableDynamicLoading>true</EnableDynamicLoading>` 应该**保留**在主 csproj 上——它告诉 .NET SDK"此程序集可能被动态加载"，防止裁剪掉 ALC 所需的元数据。

---

## 2. 核心代码

### 2.1 接口定义（Bootstrap 程序集）

```csharp
// GodotProject.Bootstrap/IHotUpdateEntry.cs
namespace GodotGameFramework.HotUpdate
{
    /// <summary>
    /// 热更程序集的入口点。
    /// 热更 DLL 中必须有一个实现了此接口的类。
    /// </summary>
    public interface IHotUpdateEntry
    {
        /// <summary>热更包版本号</summary>
        string Version { get; }

        /// <summary>获取所有热更 Procedure 类型</summary>
        Type[] GetProcedureTypes();

        /// <summary>创建实体逻辑实例</summary>
        IEntityLogic CreateEntityLogic(string entityName);

        /// <summary>创建 UI 逻辑实例</summary>
        IUIFormLogic CreateUIFormLogic(string uiFormName);
    }
}

// GodotProject.Bootstrap/IEntityLogic.cs
namespace GodotGameFramework.HotUpdate
{
    public interface IEntityLogic
    {
        void Bind(GodotObject entity);
        void OnInit();
        void OnRecycle();
        void OnShow();
        void OnHide();
        void OnUpdate(float delta);
        void OnPhysicsProcess(float delta);
    }
}

// GodotProject.Bootstrap/IUIFormLogic.cs
namespace GodotGameFramework.HotUpdate
{
    public interface IUIFormLogic
    {
        void Bind(Control form);
        void OnOpen(object userData);
        void OnClose(bool isShutdown, object userData);
        void OnPause();
        void OnResume();
        void OnUpdate(float delta);
    }
}
```

### 2.2 HotUpdateLoadContext（Bootstrap 程序集）

```csharp
// GodotProject.Bootstrap/HotUpdateLoadContext.cs
using System.Reflection;
using System.Runtime.Loader;

namespace GodotGameFramework.HotUpdate
{
    /// <summary>
    /// 自定义 AssemblyLoadContext，用于加载热更 DLL。
    /// 关键：对 Bootstrap 和 Framework 程序集的引用回退到 Default ALC，
    /// 避免同一个类型在 Default 和 Hot ALC 中各有一份（类型不等价问题）。
    /// </summary>
    internal sealed class HotUpdateLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver m_Resolver;
        private readonly string m_HotUpdateDir;

        public HotUpdateLoadContext(string dllPath) : base("HotUpdate", isCollectible: true)
        {
            m_HotUpdateDir = Path.GetDirectoryName(dllPath);
            m_Resolver = new AssemblyDependencyResolver(dllPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // 1. 尝试从热更目录加载
            string dllPath = Path.Combine(m_HotUpdateDir, assemblyName.Name + ".dll");
            if (File.Exists(dllPath))
            {
                return LoadFromAssemblyPath(dllPath);
            }

            // 2. 使用 resolver 查找依赖
            string resolvedPath = m_Resolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                return LoadFromAssemblyPath(resolvedPath);
            }

            // 3. 回退到 Default ALC（已加载的 Bootstrap / Framework 程序集）
            return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            // 透传给 Default —— Godot 的原生库已经由 Default 加载
            return IntPtr.Zero;
        }
    }
}
```

### 2.3 HotUpdateManager（Bootstrap 程序集）

```csharp
// GodotProject.Bootstrap/HotUpdateManager.cs
using System.Reflection;

namespace GodotGameFramework.HotUpdate
{
    /// <summary>
    /// 热更管理器。全局单例，在 GameEntry 初始化时调用。
    /// </summary>
    public static class HotUpdateManager
    {
        private static HotUpdateLoadContext s_LoadContext;
        private static Assembly s_HotAssembly;
        private static IHotUpdateEntry s_Entry;
        private static string s_HotVersion;

        public static bool IsHotUpdateLoaded => s_HotAssembly != null;
        public static string HotVersion => s_HotVersion ?? "built-in";

        /// <summary>
        /// 检测并加载热更程序集。
        /// 在 Bootstrap 启动流程中调用（越早越好）。
        /// </summary>
        /// <param name="useHotUpdate">如果为 false，直接使用内置程序集</param>
        public static bool TryLoadHotUpdate(string hotUpdateDir)
        {
            string dllPath = Path.Combine(hotUpdateDir, "GodotProject.dll");
            if (!File.Exists(dllPath))
            {
                Log.Info("[HotUpdate] 未找到热更 DLL，使用内置版本。");
                return false;
            }

            try
            {
                // 创建独立的 ALC 加载热更 DLL
                s_LoadContext = new HotUpdateLoadContext(dllPath);
                s_HotAssembly = s_LoadContext.LoadFromAssemblyPath(dllPath);

                // 找到 IHotUpdateEntry 实现
                var entryType = s_HotAssembly.GetTypes()
                    .FirstOrDefault(t => typeof(IHotUpdateEntry).IsAssignableFrom(t) && !t.IsInterface);

                if (entryType == null)
                {
                    Log.Error("[HotUpdate] 热更 DLL 中未找到 IHotUpdateEntry 实现！回退到内置版本。");
                    Unload();
                    return false;
                }

                s_Entry = (IHotUpdateEntry)Activator.CreateInstance(entryType);
                s_HotVersion = s_Entry.Version;

                Log.Info("[HotUpdate] 热更程序集加载成功，版本: {0}", s_HotVersion);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[HotUpdate] 加载热更 DLL 失败: {0}", ex);
                Unload();
                return false;
            }
        }

        /// <summary>
        /// 获取热更程序集中的 Procedure 类型列表。
        /// 调用方用这些类型替换内置的 Procedure。
        /// </summary>
        public static Type[] GetHotProcedureTypes()
        {
            if (s_Entry == null) return Array.Empty<Type>();
            return s_Entry.GetProcedureTypes();
        }

        /// <summary>
        /// 创建实体逻辑（热更版本）。
        /// 如果热更 DLL 未加载，返回 null → 调用方使用内置逻辑。
        /// </summary>
        public static IEntityLogic CreateEntityLogic(string entityName)
        {
            return s_Entry?.CreateEntityLogic(entityName);
        }

        /// <summary>
        /// 创建 UI 逻辑（热更版本）。
        /// </summary>
        public static IUIFormLogic CreateUIFormLogic(string uiFormName)
        {
            return s_Entry?.CreateUIFormLogic(uiFormName);
        }

        /// <summary>
        /// 卸载热更程序集（仅在退出/回滚时调用）。
        /// </summary>
        public static void Unload()
        {
            s_Entry = null;
            s_HotAssembly = null;
            if (s_LoadContext != null)
            {
                s_LoadContext.Unload();
                s_LoadContext = null;
            }
            s_HotVersion = null;
        }
    }
}
```

### 2.4 热更程序集中的入口实现

```csharp
// GodotProject / TheGame/HotUpdateEntry.cs
// 此类在热更 DLL 中，实现 IHotUpdateEntry
namespace GodotGameFramework.HotUpdate
{
    public class HotUpdateEntry : IHotUpdateEntry
    {
        public string Version => "2.3.0";

        public Type[] GetProcedureTypes()
        {
            return new Type[]
            {
                typeof(ProcedureLaunch),
                typeof(ProcedureUpdateVersion),
                typeof(ProcedureCheckResources),
                typeof(ProcedureUpdateResources),
                typeof(ProcedurePrelode),
                typeof(ProcedureGame),
            };
        }

        public IEntityLogic CreateEntityLogic(string entityName)
        {
            return entityName switch
            {
                "Cat" => new CatLogic(),
                "Anger" => new AngerLogic(),
                "GanTan" => new GanTanLogic(),
                _ => null
            };
        }

        public IUIFormLogic CreateUIFormLogic(string uiFormName)
        {
            return uiFormName switch
            {
                "MenuForm" => new MenuFormLogic(),
                "MainForm" => new MainFormLogic(),
                _ => null
            };
        }
    }
}
```

---

## 3. Procedure 热更（零改动）

Procedure 是纯 C# 类（继承 `ProcedureBase`，`ProcedureBase` 继承 `FsmState<IProcedureManager>`），不依赖 Godot 脚本系统。**只要通过 ALC 加载热更 DLL 中的 Procedure 类型，并在 ProcedureManager 中使用它们即可。**

### 3.1 ProcedureComponent 增强

```csharp
// 修改 GodotGameFrameworkCore/Procedure/ProcedureComponent.cs

public void StartProcedure<T>() where T : ProcedureBase
{
    Type targetType = typeof(T);

    // 如果加载了热更 DLL，尝试从中获取同名 Procedure
    if (HotUpdateManager.IsHotUpdateLoaded)
    {
        var hotTypes = HotUpdateManager.GetHotProcedureTypes();
        var hotType = hotTypes.FirstOrDefault(t => t.Name == targetType.Name);
        if (hotType != null)
        {
            // 使用热更版本的 Procedure 类型
            m_ProcedureManager.StartProcedure(hotType);
            return;
        }
    }

    // 回退：使用内置类型
    m_ProcedureManager.StartProcedure(targetType);
}

// 同理修改 ChangeState<T>
```

或者在 ProcedureManager 中统一处理，对调用方透明——只需改一处。

### 3.2 启动流程调整

```
原流程:  ProcedureLaunch → ProcedureUpdateVersion → ProcedureCheckResources → ProcedureUpdateResources → ProcedurePrelode → ProcedureGame

新流程:
  Bootstrap 启动
    → HotUpdateManager.TryLoadHotUpdate()
    → 获取热更 Procedure 类型
    → 启动热更版 ProcedureLaunch（如果有）
    → 后续所有 ChangeState<T> 都从热更 DLL 中解析类型
```

---

## 4. Entity 逻辑热更（需要轻量重构）

### 4.1 重构：Entity 壳 + 逻辑分离

**重构前（当前代码）：**

```csharp
// CatEntity.cs — 所有逻辑都在 Godot 脚本中
public partial class CatEntity : CharacterBody2D, IEntity, IActor
{
    public override void _Ready()
    {
        // 初始化逻辑直接写在这里...
    }

    public override void _Process(double delta)
    {
        // 每帧逻辑直接写在这里...
    }
}
```

**重构后：**

```csharp
// CatEntity.cs — 变成薄壳（在 GodotProject.dll 中）
public partial class CatEntity : CharacterBody2D, IEntity, IActor
{
    private IEntityLogic m_Logic;

    public override void _Ready()
    {
        base._Ready();
        // 尝试获取热更版本逻辑，失败则用内置
        m_Logic = HotUpdateManager.CreateEntityLogic("Cat") ?? new CatLogic_BuiltIn();
        m_Logic.Bind(this);
        m_Logic.OnInit();
    }

    public override void _Process(double delta)
    {
        m_Logic?.OnUpdate((float)delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        m_Logic?.OnPhysicsProcess((float)delta);
    }
}

// CatLogic.cs — 纯 C# 类，可热更（在 GodotProject.dll 中，通过 ALC 加载为新版本）
public class CatLogic : IEntityLogic
{
    private CatEntity m_Entity;

    public void Bind(GodotObject entity)
    {
        m_Entity = (CatEntity)entity;
    }

    public void OnInit() { /* 原 _Ready 逻辑 */ }
    public void OnUpdate(float delta)
    {
        // 键盘输入处理 → 走热更版本
        // 自动瞄准 → 走热更版本
        // 子弹生成 → 走热更版本
    }
    public void OnPhysicsProcess(float delta) { }
    public void OnRecycle() { }
    public void OnShow() { }
    public void OnHide() { }
}
```

**重构代价评估：** 每个 Entity 约 10-20 行壳代码 + 将现有 `_Ready/_Process` 逻辑移到 Logic 类中。Entity 的 Godot 节点结构、信号连接不受影响（仍然在壳中）。

---

## 5. UI Form 逻辑热更

同理：

```csharp
// MenuForm.cs — 薄壳
public partial class MenuForm : Control, IUIForm
{
    private IUIFormLogic m_Logic;

    public void OnInit()
    {
        m_Logic = HotUpdateManager.CreateUIFormLogic("MenuForm") ?? new MenuFormLogic_BuiltIn();
        m_Logic.Bind(this);
    }

    public void OnOpen(object userData) => m_Logic?.OnOpen(userData);
    public void OnClose(bool isShutdown, object userData) => m_Logic?.OnClose(isShutdown, userData);
}

// MenuFormLogic.cs — 纯 C#，可热更
public class MenuFormLogic : IUIFormLogic
{
    private MenuForm m_Form;
    public void Bind(Control form) { m_Form = (MenuForm)form; }
    public void OnOpen(object userData)
    {
        // 按钮绑定的响应逻辑 → 可热更
    }
}
```

---

## 6. 更新管线集成

### 6.1 热更包内容

```
hotupdate_2.3.0/
├── GodotProject.dll              ← 热更程序集（必须）
├── GodotProject.pdb              ← 调试符号（可选，方便线上调试）
├── subpackages/                  ← .pck 资源子包（可选）
│   ├── scripts.pck
│   └── assets_v2.pck
├── GameConfig/                   ← Luban 配置更新（可选）
│   └── *.bytes
└── version.json                  ← 版本清单
```

### 6.2 增强 PackVersionList

```csharp
[Serializable]
public class PackVersionList
{
    public string Version { get; set; }
    public string DllHash { get; set; }          // 新增：程序集 SHA256
    public long DllSize { get; set; }            // 新增：程序集大小
    public string DllUrl { get; set; }           // 新增：程序集下载 URL
    public string MinBootstrapVersion { get; set; } // 新增：最低 Bootstrap 版本
    public Pack[] Packs { get; set; }            // 资源子包（已有）
}
```

> **现状（2026-07）**：`PackVersionList`（`Framework/GodotGameFrameworkCore/Resource/PackVersionList.cs`）已实现 `Version / Packs / MinAppVersion / ForceUpdate`（后两者已在热更链中接入）；`DllHash / DllSize / DllUrl / MinBootstrapVersion` 等程序集相关字段仍为规划中。

### 6.3 热更流程链增强

在现有热更流程链（`ProcedureUpdateResources`）中增加 DLL 下载逻辑：

```
ProcedureUpdateResources.OnEnter:
  1. 请求服务器 version.json
  2. 解析 DllHash / DllSize / DllUrl
  3. 对比本地 DLL 版本（从 user://hotupdate/version.json 读取）
  4. 有更新 → 下载 GodotProject.dll → user://hotupdate/
  5. 保存 version.json → user://hotupdate/
  6. 下载 .pck 子包（已实现：经 GF.Download 并发下载）
  7. 下载完成 → 弹窗"更新已就绪，重启生效" → 用户点确定 → 重启
```

> **现状（2026-07）**：热更链（`ProcedureUpdateVersion` → `ProcedureCheckResources` → `ProcedureUpdateResources`）的 .pck 资源热更部分已落地——版本清单经 `GF.WebRequest` 请求（3 次指数退避重试），子包经 `GF.Download.DownloadFileAsync` 多包并发下载（`Task.WhenAll`，进度按字节加权聚合，每包 3 次指数退避重试，断点续传 + 大小/SHA256 校验自动生效，详见 `DownloadSystem.md`）。DLL 下载逻辑（步骤 2~5、7）尚未实现，可直接复用同一下载通道。

---

## 7. 启动时序

```
App 启动
  │
  ├── Godot 加载 GameFramework.tscn
  │     └── GameEntry.OnInit() → 注册各 Component
  │
  ├── [Bootstrap] HotUpdateManager.TryLoadHotUpdate("user://hotupdate/")
  │     │
  │     ├── 找到 GodotProject.dll → 创建 ALC 加载
  │     │   ├── 成功 → IsHotUpdateLoaded = true
  │     │   │   ├── 读取版本号 → 记录日志
  │     │   │   └── 后续所有 Procedure/Logic 从热更 DLL 加载
  │     │   │
  │     │   └── 失败 → IsHotUpdateLoaded = false
  │     │       ├── 删除损坏的 DLL
  │     │       ├── 回退 version.json → 上一版本
  │     │       └── 使用内置程序集
  │     │
  │     └── 未找到 DLL → IsHotUpdateLoaded = false → 使用内置程序集
  │
  ├── 正常启动流程
  │   ProcedureLaunch → ProcedureUpdateVersion → ProcedureCheckResources → ProcedureUpdateResources → ProcedurePrelode → ProcedureGame
  │   （每个 Procedure 类型从 HotUpdateManager 解析）
  │
  └── Entity/UI 创建时 → HotUpdateManager.CreateEntityLogic/CreateUIFormLogic
```

> **现状（2026-07）**：崩溃检测/安全模式已由 `HotUpdateSafetyGuard` 落地并接入资源热更侧——`ProcedureUpdateVersion` 开头检测 `WasLastSessionCrashed()`（命中则 `EnterSafeMode()` 回退版本文件并跳过全部热更补丁），加载子包前 `MarkStartupBegin()` 写启动锁，`ProcedureGame.OnEnter` 调用 `MarkStartupSuccess()`。上图中"DLL 加载失败 → 回退"分支实施时应复用该机制。

---

## 8. 回滚机制

```
user://hotupdate/
├── GodotProject.dll          ← 当前热更 DLL
├── version.json              ← 当前版本清单
└── .backup/                  ← 备份
    ├── GodotProject.dll      ← 上一版本 DLL
    └── version.json          ← 上一版本清单
```

- 新 DLL 加载失败 → 自动回退到 `.backup/` 中的上一版本
- 备份也失败 → 删除全部补丁，使用内置程序集
- 用户可在设置中手动"清除热更补丁，恢复出厂版本"

> **现状（2026-07）**：资源热更侧的回滚已实现——保存新版本清单前先备份 `GameFrameworkVersion.dat.bak`；任一子包加载失败自动回退版本文件（`ProcedureUpdateBase.RollbackVersionFile`）；上次启动崩溃时 `HotUpdateSafetyGuard.EnterSafeMode` 回退到 `.bak`（无备份则清除版本文件用内置版本）。DLL 侧 `.backup/` 机制待随 ALC 方案实施。

---

## 9. 构建管线

### 9.1 CI 构建流程

```bash
# 1. 构建热更程序集
dotnet build -c Release

# 2. 计算 SHA256
sha256sum GodotProject.dll > hotupdate/GodotProject.dll.sha256

# 3. 打包
mkdir -p hotupdate_2.3.0
cp GodotProject.dll hotupdate_2.3.0/

# 4. 生成 version.json
cat > hotupdate_2.3.0/version.json << EOF
{
  "Version": "2.3.0",
  "DllHash": "$(cat hotupdate/GodotProject.dll.sha256)",
  "DllSize": $(stat -f%z GodotProject.dll),
  "DllUrl": "https://cdn.example.com/hotupdate_2.3.0/GodotProject.dll",
  "MinBootstrapVersion": "1.0.0"
}
EOF

# 5. 上传到 CDN
```

### 9.2 Godot 编辑器内工作流

- 开发时正常编写代码、运行游戏
- `HotUpdateManager.TryLoadHotUpdate()` 在编辑器内返回 false（`user://` 下没有热更 DLL）
- 所有 Entity/UI 使用内置 Logic（fallback）
- 构建热更包时才需要 CI 步骤

---

## 10. 风险和注意事项

| 风险 | 缓解措施 |
|------|----------|
| **类型冲突**（Default ALC 和 Hot ALC 中有同名类型） | `HotUpdateLoadContext` 第 3 步回退到 Default ALC，防止重复加载 Framework 程序集 |
| **ALC 内存泄漏** | 设置 `isCollectible: true`，退出时调用 `Unload()` |
| **热更 DLL 引用了新版 Framework API** | `MinBootstrapVersion` 检查 + 不热更 Framework 核心代码 |
| **Godot API 版本不兼容** | 热更 DLL 必须用与 App 相同的 Godot SDK 版本编译 |
| **Android 文件路径差异** | 使用 `ProjectSettings.GlobalizePath("user://")` 统一路径 |
| **DLL 下载不完整/损坏** | 统一下载通道 `GF.Download.DownloadFileAsync` 已具备（2026-07）：大小 + SHA256 校验、`.download` 临时文件 + 完成后原子重命名、断点续传、失败返回 false——直接复用即可，详见 `DownloadSystem.md` |

---

## 11. 实施路线图

> ⚠️ **全路线图暂缓**（2026-07-25）：以下所有 Phase 等待华佗团队 Godot 热更适配完成后重启。配套基础设施（下载通道、资源热更管线、崩溃安全守护）已先行落地并持续维护。

### Phase 1: 基础 ALC 加载（2-3 天）（搁置中）

- [ ] 创建 `GodotProject.Bootstrap.csproj`（2026-07 核实：仓库中不存在该工程，此前勾选无效）
- [ ] 实现 `HotUpdateLoadContext`
- [ ] 实现 `HotUpdateManager.TryLoadHotUpdate()`
- [ ] 实现 `HotUpdateEntry`（在热更 DLL 中）
- [ ] 修改 `ProcedureComponent` 支持从 ALC 获取 Procedure 类型
- [ ] **验证：手动放一个热更 DLL 到 `user://hotupdate/`，确认 Procedure 走热更版本**

### Phase 2: Procedure 热更打通（1 天）

> 前置能力已就绪（2026-07）：`GF.Download.DownloadFileAsync` 提供大小/SHA256 校验 + 断点续传；热更链已实现 .pck 的并发下载/重试/校验/回退，DLL 下载可按同一模式接入（详见 `DownloadSystem.md`）。

- [ ] 热更流程链增加 DLL 下载逻辑
- [ ] 版本比对（DllHash）
- [ ] 重启提示
- [ ] **验证：从 CDN 下载新版 DLL → 重启 → Procedure 行为变化**

### Phase 3: Entity/UI 逻辑提取（3-5 天，按需）

- [ ] 定义 `IEntityLogic` / `IUIFormLogic` 接口
- [ ] 重构 CatEntity → CatEntity(壳) + CatLogic(逻辑)
- [ ] 重构 AngerEntity、GanTanEntity
- [ ] 重构 MenuForm、MainForm（按需）
- [ ] **验证：修改 CatLogic → 打包热更 DLL → 下载重启 → 猫的行为变了**

### Phase 4: 健壮性（1-2 天）

- [ ] 回滚机制（备份/恢复）——资源热更侧已实现（2026-07：版本 `.bak` + 加载失败自动回退 + `HotUpdateSafetyGuard` 崩溃安全模式）；DLL 侧待实施
- [ ] 强制更新弹窗（`ForceUpdate` 字段已在热更链读取，强制拦截逻辑待完善）
- [ ] 下载进度 UI——资源热更侧已实现（2026-07：`LoadingForm` 进度 + `LoadSceneUpdate` 真实进度转发）；DLL 侧待实施
- [ ] 完整性校验（SHA256）——下载通道已内置（2026-07：`GF.Download.DownloadFileAsync`）
- [ ] 错误日志收集

---

## 12. 与 HybridCLR 的对比

| | HybridCLR (Unity) | 本方案 (Godot) |
|---|---|---|
| 原理 | IL2CPP 运行时内嵌 CLR 解释器 | AssemblyLoadContext 加载新版 DLL |
| 热更语言 | C#（IL 字节码解释执行） | C#（JIT 编译执行） |
| Entity 需要重构？ | ✅ 不需要（同类型系统） | ⚠️ 需要（Godot 脚本 vs 纯 C# 分离） |
| Procedure 需要重构？ | ✅ 不需要 | ✅ 不需要 |
| 平台限制 | Unity IL2CPP only | Windows (.NET CoreCLR) + Android (.NET Mono) |
| 性能 | 解释执行（慢） | JIT 编译（快） |
| 维护成本 | 极高（需维护 IL2CPP 分支） | 低（标准 .NET API） |

---

## 总结

**不需要 Lua，不需要 GDScript，不需要翻译器。纯 C# 方案：**

1. 拆出 20KB 的 Bootstrap 程序集（永不热更）
2. `AssemblyLoadContext` 加载热更版主程序集
3. Procedure（纯 C#）→ 零改动热更
4. Entity/UI（Godot 脚本）→ 轻量重构为壳+逻辑，逻辑可热更
5. 现有热更流程链增加 DLL 下载 → 校验 → 重启流程
6. 下载后重启生效，健壮可靠
