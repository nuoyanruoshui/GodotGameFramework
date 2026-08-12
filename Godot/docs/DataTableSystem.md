# 数据表系统 (Luban 配置管线)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`TheGame/GameScripts/GameProto/GameConfig/`（生成代码）、`TheGame/GameScripts/GameProto/ConfigSystem.cs`（加载器）、`Framework/GodotGameFrameworkCore/Lib/LubanLib/`（运行时库）｜ 配置源：仓库根 `Configs/GameConfig/`
> 本文档描述 GGF 的数据表系统：Excel → Luban → C# + 二进制的完整管线、运行时 `ConfigSystem` 懒加载机制、Tb 表访问 API 与新增表的完整步骤。

---

## 1. 概述

数据表系统采用 **[Luban](https://github.com/focus-creative-games/luban) 驱动**的强类型配置方案：策划编辑 Excel → Luban 一键生成 C# 代码 + 二进制数据 → 运行时 `ConfigSystem` 懒加载。

**当前不含框架层模块**（2026-07）：原版 Game Framework 的 `DataTableManager` / `DataTableComponent` 及 `GameFramework/DataTable/` 目录已移除。配置加载由 `ConfigSystem`（`TheGame/GameScripts/GameProto/ConfigSystem.cs`）直接负责，这是一个普通 C# 单例，不是 `GameFrameworkModule`，不注册到组件系统。

| 组件 | 位置 | 职责 | Godot 依赖 |
|------|------|------|:--:|
| ConfigSystem | `TheGame/GameScripts/GameProto/ConfigSystem.cs` | 单例持有 `Tables` 实例、懒加载、`FileAccess` 读 `.bytes` | ✅ |
| Luban 运行时 | `Framework/GodotGameFrameworkCore/Lib/LubanLib/` | `ByteBuf`（二进制读写）、`BeanBase`（Bean 基类）、`StringUtil` | ❌ |
| 生成代码 | `TheGame/GameScripts/GameProto/GameConfig/` | `Tables` / `TbXxx` / `XxxConfig` / 枚举，由 Luban 生成，**勿手改** | ❌ |

### 能力清单

- ✅ Excel 配置 → Luban 一键生成强类型 C# 代码 + 二进制数据
- ✅ 强类型访问：`ConfigSystem.Instance.Tables.TbEntityConfig.Get(id)`，字段全部 `readonly`
- ✅ 懒加载：首次访问 `Tables` 时才读取所有 `.bytes` 文件
- ✅ 表间引用解析（Luban `ResolveRef`）
- ✅ 枚举生成（如 `EntityId.Cat`），配置驱动实体/UI 创建
- ✅ 二进制数据可打入 Config 类型子包热更（`ProcedureUpdate` 优先加载 Config 包）

---

## 2. 架构与数据流

### 2.1 编辑期（Luban 管线）

```
Configs/GameConfig/Datas/*.xlsx（策划编辑）
    │  __tables__.xlsx / __beans__.xlsx / __enums__.xlsx（结构定义）
    │  实体.xlsx / 界面UI.xlsx / 角色.xlsx（数据）
    ▼  gen_code_bin_to_project.bat（dotnet Tools/Luban/Luban.dll -t client -c cs-bin -d bin）
    ├──► C# 代码 → Godot/GodotProject/TheGame/GameScripts/GameProto/GameConfig/
    │        Tables.cs / TbEntityConfig.cs / EntityConfig.cs / EntityId.cs ...
    └──► 二进制数据 → Godot/GodotProject/TheGame/DataTables/GameConfigs/
             entity_tbentityconfig.bytes / ui_tbuiformconfig.bytes / character_tbcharacterconfig.bytes / level_tblevelconfig.bytes
```

### 2.2 运行期

```
调用方（EntityExtension / UIExtension / 业务代码）
    │  ConfigSystem.Instance.Tables.TbXxx...
    ▼
ConfigSystem (普通 C# 单例，TheGame/GameScripts/GameProto/)
    │  Tables 属性首次访问 → Load() → new Tables(LoadByteBuf)
    │      LoadByteBuf(file):
    │        path = "res://TheGame/DataTables/GameConfigs/{file}.bytes"   ← GameFolderConstant.GameConfigs
    │        FileAccess.Open → GetBuffer → new ByteBuf(bytes)
    ▼
Tables 构造函数（Luban 生成）
    ├── TbUIFormConfig    = new(loader("ui_tbuiformconfig"))
    ├── TbCharacterConfig = new(loader("character_tbcharacterconfig"))
    ├── TbEntityConfig    = new(loader("entity_tbentityconfig"))
    ├── TbLevelConfig     = new(loader("level_tblevelconfig"))
    └── ResolveRef()      ← 解析表间引用
```

> **初始化时机**：`ProcedureLaunch.OnEnter` 中调用 `ConfigSystem.Instance.Load()` 显式触发初始化（在所有组件检查通过后）。由于 `Tables` 属性也有懒加载兜底，即使不显式调用，首次业务访问也会自动加载。

### 文件清单

| 文件 | 职责 |
|------|------|
| `TheGame/GameScripts/GameProto/ConfigSystem.cs` | 单例加载器：`Load()` / `Tables` 属性（懒加载兜底） |
| `TheGame/GameScripts/GameProto/GameConfig/Tables.cs` | 总入口（生成），聚合所有 TbXxx |
| `TheGame/GameScripts/GameProto/GameConfig/TbEntityConfig.cs` | 实体配置表（生成） |
| `TheGame/GameScripts/GameProto/GameConfig/TbCharacterConfig.cs` | 角色配置表（生成） |
| `TheGame/GameScripts/GameProto/GameConfig/TbUIFormConfig.cs` | UI 配置表（生成） |
| `TheGame/GameScripts/GameProto/GameConfig/TbLevelConfig.cs` | 关卡配置表（生成） |
| `TheGame/GameScripts/GameProto/GameConfig/EntityConfig.cs` | 实体行数据（生成） |
| `TheGame/GameScripts/GameProto/GameConfig/EntityId.cs` | 实体 ID 枚举（生成） |
| `TheGame/GameScripts/GameProto/GameConfig/vector2.cs` 等 | Luban 内建数学类型 |
| `TheGame/GameScripts/GameProto/ExternalTypeUtil.cs` | 外部类型转换（生成时从 `CustomTemplate/` 拷贝） |
| `Framework/GodotGameFrameworkCore/Config/GameFolderConstant.cs` | 路径常量：`GameConfigs = "res://TheGame/DataTables/GameConfigs/{0}.bytes"` |
| `Framework/GodotGameFrameworkCore/Lib/LubanLib/ByteBuf.cs` | Luban 二进制缓冲（ReadInt/ReadString/ReadSize…） |
| `Framework/GodotGameFrameworkCore/Lib/LubanLib/BeanBase.cs` | 生成 Bean 的基类（`ITypeId`） |

---

## 3. Luban 配置管线

### 3.1 目录结构（仓库根 `Configs/GameConfig/`）

```
Configs/GameConfig/
  Datas/
    __tables__.xlsx        ← 表定义（表名、模式、索引、数据源文件）
    __beans__.xlsx         ← Bean（结构体）字段定义
    __enums__.xlsx         ← 枚举定义（如 EntityId）
    实体.xlsx / 界面UI.xlsx / 角色.xlsx   ← 实际数据
  Defines/builtin.xml      ← Luban 内建类型定义
  CustomTemplate/          ← ExternalTypeUtil.cs 模板（生成时拷入项目）
  luban.conf               ← Luban 主配置
  gen_code_bin_to_project.bat/.sh          ← 客户端生成（常用）
  gen_code_bin_to_project_lazyload.bat/.sh ← 懒加载模式变体
  gen_code_bin_to_server.bat/.sh           ← 服务端生成
```

### 3.2 luban.conf 关键内容

```json
{
  "groups": [ {"names":["c"]}, {"names":["s"]}, {"names":["e"]} ],   // 客户端/服务端/编辑器分组
  "schemaFiles": [
    {"fileName":"Defines", "type":""},
    {"fileName":"Datas/__tables__.xlsx", "type":"table"},
    {"fileName":"Datas/__beans__.xlsx",  "type":"bean"},
    {"fileName":"Datas/__enums__.xlsx",  "type":"enum"}
  ],
  "dataDir": "Datas",
  "targets": [
    {"name":"client", "manager":"Tables", "groups":["c"], "topModule":"GameConfig"},
    ...
  ]
}
```

### 3.3 生成命令（gen_code_bin_to_project.bat 实际内容）

```bat
set WORKSPACE=../..
set LUBAN_DLL=%WORKSPACE%\Tools\Luban\Luban.dll
set DATA_OUTPATH=%WORKSPACE%/Godot/GodotProject/TheGame/DataTables/GameConfigs
set CODE_OUTPATH=%WORKSPACE%/Godot/GodotProject/TheGame/GameScripts/GameProto/GameConfig/

copy /y "CustomTemplate\ExternalTypeUtil.cs" "...\GameScripts\GameProto\ExternalTypeUtil.cs"

dotnet %LUBAN_DLL% ^
    -t client ^          ← 目标：客户端（组 c）
    -c cs-bin ^          ← 代码：C# 二进制反序列化
    -d bin ^             ← 数据：二进制
    --conf luban.conf ^
    -x code.lineEnding=crlf ^
    -x outputCodeDir=%CODE_OUTPATH% ^
    -x outputDataDir=%DATA_OUTPATH%
if not defined AI_MODE pause      ← CI/脚本环境设置 AI_MODE 可跳过暂停
```

生成产物文件名规则：`<模块小写>_<表名小写>.bytes`（如 `entity_tbentityconfig.bytes`），与 `Tables.cs` 构造函数中 `loader("entity_tbentityconfig")` 的参数一一对应。

---

## 4. 运行时机制

### 4.1 初始化与懒加载

- `ProcedureLaunch.OnEnter`（组件检查通过后）：调用 `ConfigSystem.Instance.Load()` → `new Tables(LoadByteBuf)` → 一次性同步读入所有表的 `.bytes` 并反序列化。
- 此外，`ConfigSystem.Tables` 属性 getter 内部有懒加载兜底（`if (!_init) Load()`），即使流程层未显式调用，首次业务访问也会触发加载。
- `LoadByteBuf` 使用 Godot `FileAccess` 读取文件，读不到会 `throw Exception`（快速失败）。

### 4.2 生成代码结构（以 EntityConfig 为例）

```csharp
// Tables.cs — 总入口（当前 4 张表）
public partial class Tables {
    public UI.TbUIFormConfig TbUIFormConfig { get; }
    public Character.TbCharacterConfig TbCharacterConfig { get; }
    public Entity.TbEntityConfig TbEntityConfig { get; }
    public Level.TbLevelConfig TbLevelConfig { get; }
    public Tables(System.Func<string, ByteBuf> loader) { ...; ResolveRef(); }
}

// TbEntityConfig.cs — 表：Dictionary + List 双容器
public partial class TbEntityConfig {
    public Dictionary<int, EntityConfig> DataMap { get; }
    public List<EntityConfig> DataList { get; }
    public EntityConfig Get(int key);              // 不存在抛异常
    public EntityConfig GetOrDefault(int key);     // 不存在返回 null
    public EntityConfig this[int key] { get; }
}

// EntityConfig.cs — 行：全 readonly 字段 + XML 注释（来自 Excel 表头）
public sealed partial class EntityConfig : Luban.BeanBase {
    public readonly int Id;
    public readonly Entity.EntityId EntityId;   // 枚举，来自 __enums__.xlsx
    public readonly string AssetPath;           // 场景路径
    public readonly string EntityGroupName;
    public readonly int Priority;
}

// EntityId.cs — 枚举
public enum EntityId { Cat = 0, GanTan = 1, Anger = 2, LightningBall = 3 }
```

### 4.3 访问方式

```csharp
// 主键访问
EntityConfig cfg = ConfigSystem.Instance.Tables.TbEntityConfig.Get(1);
EntityConfig cfgOrNull = ConfigSystem.Instance.Tables.TbEntityConfig.GetOrDefault(999);

// 条件查找（框架内 EntityExtension 的实际写法）
EntityConfig cfg = ConfigSystem.Instance.Tables.TbEntityConfig.DataList
    .FirstOrDefault(x => x.EntityId == entityId);

// 游戏侧（CatEntity 的实际写法）
m_Config = ConfigSystem.Instance.Tables.TbCharacterConfig.DataList
    .FirstOrDefault(x => x.EntityId == EntityId.Cat);
```

配置驱动链路示例：`GF.Entity.ShowEntity<CatEntity>(EntityId.Cat)` → `EntityExtension` 查 `TbEntityConfig` 取 `AssetPath` / `EntityGroupName` / `Priority` → 加载场景并显示实体。

---

## 5. 新增一张表的完整步骤

1. **定义结构**：在 `Configs/GameConfig/Datas/__beans__.xlsx` 中定义 Bean 字段；如需枚举，在 `__enums__.xlsx` 中定义。
2. **注册表**：在 `__tables__.xlsx` 中登记表名（如 `TbItemConfig`）、模块、索引字段、数据源文件名。
3. **填数据**：新建/编辑数据 Excel（如 `道具.xlsx`），表头注释即生成代码的 XML 注释。
4. **生成**：双击 `Configs/GameConfig/gen_code_bin_to_project.bat`（命令行/CI 先 `set AI_MODE=1` 免暂停）。
   - C# 代码落至 `TheGame/GameScripts/GameProto/GameConfig/`
   - `.bytes` 数据落至 `TheGame/DataTables/GameConfigs/`
5. **编译**：`cd GodotProject && dotnet build`（新文件首次生成后建议再执行 `--build-solutions` 刷新解决方案）。
6. **使用**：`ConfigSystem.Instance.Tables.TbItemConfig.Get(id)`。`Tables.cs` 中的新表属性和加载调用由 Luban 自动补齐，无需手写注册代码。

热更说明：`.bytes` 属于 `PackType.Config` 类型资源，可打入 Config 子包；`ProcedureUpdate.LoadDownloadedPacks` 会**先加载 Config 包再加载 Resource 包**，保证场景实例化时新配置已生效。注意：由于 `Tables` 是懒加载 + 一次性加载，子包必须在**首次访问 `GetTables()` 之前**完成 `LoadResourcePack`（当前流程顺序 ProcedureUpdate → ProcedurePrelode → 业务访问，天然满足）。

---

## 6. 注意事项 / FAQ

**Q: 修改 Excel 后运行时数据没变？**
必须重新执行生成脚本——运行时读的是 `.bytes` 二进制，不是 Excel。生成后无需重启 Godot 编辑器，但需要 `dotnet build`（若结构变化产生了新代码）。

**Q: 表加载是什么时机？会卡顿吗？**
`ProcedureLaunch.OnEnter` 中显式调用 `ConfigSystem.Instance.Load()`，同步加载**全部**表。当前表量小无感知；表规模变大后可考虑使用 `gen_code_bin_to_project_lazyload.bat` 生成懒加载版本代码（按表首次访问再读文件）。

**Q: 为什么没有 DataTableManager / DataTableComponent？**
原版 Game Framework 的 DataTable 模块（`GameFramework/DataTable/`、`GodotGameFrameworkCore/DataTable/`）已于 2026-07 移除。当前 Luban 配置管线由 `ConfigSystem` 单例直接驱动，不再需要框架模块封装。Config 子包热更仍然支持（`ProcedureUpdate` 优先加载 Config 包），无需框架模块中介。

**Q: 能在运行时增删行吗？**
不能。生成代码所有字段 `readonly`，容器只读暴露。运行时可变数据请使用 DataNode（见 `DataNodeSystem.md`）或 Setting。

**Q: `ExternalTypeUtil.cs` 是干什么的？**
Luban 内建 `vector2/vector3` 等数学类型与 Godot `Vector2/Vector3` 之间的转换工具，每次生成时从 `Configs/GameConfig/CustomTemplate/` 覆盖拷贝到 `GameScripts/GameProto/`，勿手改。

---

## 7. 已知边界与后续计划

- [x] `ConfigSystem` 已替代原版 DataTable 框架模块（2026-07）
- [ ] 表规模增长后切换 lazyload 生成模式
- [ ] 当前仅 4 张表（UIForm / Character / Entity / Level），新增表按 §5 步骤添加
