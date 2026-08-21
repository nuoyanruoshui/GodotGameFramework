# 存档系统 (Archive System)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GodotGameFrameworkCore/Archive/ArchiveSystem.cs`、`Framework/GodotGameFrameworkCore/Archive/Rijindael.cs`、`Framework/GodotGameFrameworkCore/Json/EasySave.cs`、`TheGame/MainPack/Scripts/Resources/ArchiveSetting.cs`、`TheGame/GameScripts/Archive/GameData.cs`（含 `GameCatalogue` 和 `GameData` 两个类）
> 本文档描述 GGF 的通用存档系统：ArchiveSystem 泛型设计、Catalogue/Data 分离模式、CRUD API、AES 存档加密（Rijindael + ArchiveSetting）、EasySave 持久化底层与游戏侧定制示例。

---

## 1. 概述

GGF 的存档系统是一个**泛型通用存档框架**，提供"存档目录 + 存档数据"的分离式设计，支持创建、读取、保存、覆盖和删除存档。底层基于 `EasySave`（Newtonsoft.Json 序列化 + `user://` 文件系统）实现持久化，并支持通过 `Rijindael`（AES-256-CBC）对存档内容**可选加密**。

| 层 | 位置 | 职责 |
|----|------|------|
| 持久化底层 | `GodotGameFrameworkCore/Json/EasySave.cs` | JSON 序列化/反序列化、文件 CRUD（同步 + 异步）、加密/明文双通道、`user://` 路径封装 |
| 加密工具 | `GodotGameFrameworkCore/Archive/Rijindael.cs` | AES-256-CBC + PBKDF2 密钥派生、随机 IV，纯 .NET 实现 |
| 配置资源 | `TheGame/MainPack/Scripts/Resources/ArchiveSetting.cs` | `ArchiveSetting : Resource`：存档目录名、是否加密、KEY、Salt（`.tres` 配置） |
| 通用存档框架 | `GodotGameFrameworkCore/Archive/ArchiveSystem.cs` | `ArchiveSystem<T, U>` 泛型类：Catalogue 列表管理、Create/Load/Overwrite/Delete，驱动加密 |
| 游戏侧实现 | `TheGame/GameScripts/Archive/GameData.cs` | `GameData : ArchiveData` / `GameCatalogue : ArchiveCatalogue`：项目自定义数据字段 |

> 命名空间为 `GodotGameFramework.Archive`（Rijindael、ArchiveSystem、ArchiveCatalogue、ArchiveData、CryptoException 均在 `GodotGameFramework.Archive` 下，不再使用旧命名空间 `GodotGameFrameworkCore.Archive`）。

---

## 2. 架构与数据流

### 2.1 核心类型

```csharp
// 存档目录条目（基类）
public class ArchiveCatalogue
{
    public long UnitId;  // 存档唯一 ID（Unix 时间戳）
}

// 存档数据（基类）
public class ArchiveData
{
    public long UnitId;  // 与 Catalogue 关联的 ID
}

// 通用存档管理器
public sealed class ArchiveSystem<T, U>
    where T : ArchiveCatalogue, new()
    where U : ArchiveData, new()
```

### 2.2 配置资源 ArchiveSetting

`ArchiveSetting` 是存档系统的配置中心，由 `ArchiveSetting.tres`（`TheGame/MainPack/Resources/`）承载，`ArchiveSystem.Setting` 属性经 `ResourceLoader.Load<ArchiveSetting>(ResourcesCollectionConstant.Resources_ArchiveSetting)` 懒加载：

```csharp
[GlobalClass]
public partial class ArchiveSetting : Resource
{
    [Export] public string Folder { get; set; } = "GameData";            // 存档根目录名（相对 user://）
    [Export] public bool   EnableAesEncryption { get; set; }             // 是否 AES 加密
    [Export] public string KEY  { get; set; } = "GodotGameFramework";    // 口令（PBKDF2 派生密钥）
    [Export] public string Salt { get; set; } = "Rkb4jvUy/ye7Cd7k89QQgQ=="; // 盐值（≥8 字节）
}
```

> **改配置后需注意**：`KEY`/`Salt` 一旦用于加密写入存档，之后修改会导致旧档解密失败。见 §6 FAQ。

### 2.3 文件布局（`user://`，目录名由 `ArchiveSetting.Folder` 决定，默认 `GameData`）

```
user://
  └── GameData/                        ← ${Setting.Folder}
        ├── Catalogue.sav              ← 存档目录列表（List<T>，明文或 AES 密文 Base64）
        └── Data/
              ├── 1711600000.sav       ← 每个存档的独立数据文件（明文或 AES 密文 Base64）
              ├── 1711700000.sav
              └── ...
```

> 旧版存档目录硬编码为 `user://GameData/`；现在目录名、加密开关、密钥/盐全部由 `ArchiveSetting` 配置。

### 2.4 数据流

```
游戏逻辑
    │  archive.SaveAsync() / LoadAsync() / Delete(unitId)
    ▼
ArchiveSystem<T, U>
    ├── Setting : ArchiveSetting          ← 目录名 / EnableAesEncryption / KEY / Salt
    ├── Catalogues : List<T>              ← 所有存档条目
    ├── CurrentCatalogue : T              ← 当前活跃存档条目
    ├── CurrentData : U                   ← 当前活跃存档数据
    │
    └── EasySave
          ├── SaveInUserAsync(obj, path, encrypt, key, salt)
          │     └── encrypt ? Rijindael.Encrypt(json, key, salt) : json   → user:// 文件
          ├── LoadFromUserAsync<T>(path, encrypt, key, salt)
          │     └── 读文件 → encrypt ? Rijindael.Decrypt(...) : json → 反序列化
          └── DeleteInUserAsync(path)     → 删除文件
```

---

## 3. 核心 API

### 3.1 ArchiveSystem<T, U>

| 方法 | 说明 |
|------|------|
| `SaveAsync()` | 创建新存档：生成新 `UnitId`（当前 Unix 时间戳），按 `Setting`（目录/加密/密钥）写入 Catalogue + Data 文件 |
| `SaveAsync(long unitId)` | 将 `CurrentData` 保存到已有存档条目（覆盖该存档的 Data 文件） |
| `OverWriteAsync()` | 覆盖当前活跃存档（= `SaveAsync(CurrentCatalogue.UnitId)`） |
| `LoadAsync()` | 初始化/加载：**文件不存在 → 首次存档自动创建；文件存在但读取失败 → 拒绝覆盖**，仅打 Error 日志返回。成功则加载最新（`Catalogues[^1]`） |
| `LoadAsync(long unitId)` | 按 `unitId` 加载指定存档数据到 `CurrentData` |
| `Delete(long unitId)` | 删除指定存档：从 Catalogue 列表移除条目，删除 Data 文件，重写 Catalogue 列表（若删除的是当前活跃存档则重置 `CurrentCatalogue`/`CurrentData`） |

**属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Setting` | `ArchiveSetting` | 存档配置（目录名 / 加密开关 / KEY / Salt），懒加载 |
| `Catalogues` | `List<T>` | 所有存档目录条目 |
| `CurrentCatalogue` | `T` | 当前活跃的存档条目 |
| `CurrentData` | `U` | 当前活跃的存档数据 |

> **`LoadAsync()` 防吞档行为**：旧逻辑"读不到就当空档自动新建"会静默覆盖玩家数据。现在仅当 `Catalogue.sav` **不存在**时才新建；存在但反序列化失败（如密钥/盐变更、文件损坏）时**拒绝覆盖**，保证玩家存档不被误删。此时应先修正配置或让玩家手动处理，再重新加载。

### 3.2 EasySave

| 方法 | 说明 |
|------|------|
| `SaveInUser<T>(data, fileName)` | 同步保存到 `user://`（明文） |
| `LoadFromUser<T>(fileName)` | 同步从 `user://` 加载（明文，不存在返回 null） |
| `DeleteInUser(fileName)` | 同步删除 `user://` 文件 |
| `ExistsInUser(fileName)` | 检查 `user://` 文件是否存在 |
| `SaveInUserAsync<T>(data, fileName)` | 异步保存（明文） |
| `LoadFromUserAsync<T>(fileName)` | 异步加载（明文） |
| `SaveInUserAsync<T>(data, fileName, encrypt, key, salt)` | 异步保存；`encrypt=true` 时经 `Rijindael.Encrypt` 加密 |
| `LoadFromUserAsync<T>(fileName, encrypt, key, salt)` | 异步加载；`encrypt=true` 时经 `Rijindael.Decrypt` 解密 |
| `DeleteInUserAsync(fileName)` | 异步删除 |
| `SaveInProject<T>` / `LoadFromProject<T>` / `DeleteInProject` / `ExistsInProject` | `res://` 路径同步方法（仅编辑器/开发期使用） |

> `EasySave` 使用纯 .NET `StreamWriter/StreamReader` + `Task.Run` 实现异步 I/O（避免阻塞 Godot 主线程）。`user://` 路径经 `ProjectSettings.GlobalizePath` 解析为绝对路径。

---

## 4. 加密实现（Rijindael）

存档加密由 `Rijindael` 静态类提供，**纯 .NET 实现、不依赖 Godot**，可在后台线程安全调用。

- **算法**：AES-256-CBC，PKCS7 填充
- **密钥派生**：PBKDF2（SHA256，10000 次迭代），以 `KEY` 为口令、`Salt` 为盐
- **IV**：每次加密随机生成（16 字节），**前置写入密文**；解密时按前 16 字节还原
- **密文格式**：`Base64( 16 字节随机 IV ‖ AES 密文 )`
- **失败处理**：解密失败抛 `CryptoException`，由 `EasySave` 统一捕获后返回 `null`

```csharp
string cipher = Rijindael.Encrypt(json, key, salt);  // → Base64(IV‖密文)
string plain  = Rijindael.Decrypt(cipher, key, salt); // 失败抛 CryptoException
string newSalt = Rijindael.GenerateIV();              // 随机 16 字节 Base64（编辑器"随机生成盐"按钮）
```

### 编辑器配置（ArchiveSettingInspectorPlugin）

`addons/ComponentInsoector/ArchiveSettingInspectorPlugin.cs` 为 `ArchiveSetting` 提供自定义 Inspector：

- **EnableAesEncryption 开关** — 勾选后 KEY 字段显示、Salt 字段显示并附"随机生成"按钮（调用 `Rijindael.GenerateIV()`）；未勾选时 KEY/Salt 隐藏（保持 .tres 简洁）
- 用途：在 Godot 编辑器里可视化地开启加密、设置/生成密钥盐值，免手改 `.tres`

---

## 5. 游戏侧定制

### 5.1 继承 ArchiveData / ArchiveCatalogue

游戏项目在 `TheGame/GameScripts/Archive/GameData.cs` 中扩展基类：

```csharp
[Serializable]
public class GameData : ArchiveData
{
    public int Score;
    public List<ActorData> Actors;

    public GameData()
    {
        Actors = new List<ActorData>();
    }
}

[Serializable]
public class GameCatalogue : ArchiveCatalogue
{
    public string Name;   // 存档显示名（如 "冒险存档 1"）
}
```

`[Serializable]` 特性用于 Newtonsoft.Json 序列化。`ArchiveCatalogue` 和 `ArchiveData` 基类只提供 `UnitId` 关联，游戏侧按需追加字段。

### 5.2 使用示例

```csharp
// 初始化存档管理器（加密开关/密钥来自 ArchiveSetting.tres）
var archive = new ArchiveSystem<GameCatalogue, GameData>();

// 启动时加载（自动加载最新存档，不存在则创建）
await archive.LoadAsync();

// 修改数据并覆盖保存
archive.CurrentData.Score += 100;
archive.CurrentData.Actors.Add(new ActorData { Name = "Player", Hp = 80 });
await archive.OverWriteAsync();

// 创建新存档（新 UnitId）
await archive.SaveAsync();

// 按 UnitId 加载特定存档
await archive.LoadAsync(someUnitId);

// 删除存档
await archive.Delete(someUnitId);
```

---

## 6. 设计决策

| 决策 | 理由 |
|------|------|
| **Catalogue + Data 分离** | 目录文件小（仅列表），加载存档列表时无需反序列化所有存档的完整数据；Data 文件独立，利于版本演进和增量修改 |
| **UnitId 用 Unix 时间戳** | 自然唯一且天然可排序，无需 UUID 依赖 |
| **泛型 `<T, U>` 而非接口约束** | 游戏侧通过继承 `ArchiveCatalogue`/`ArchiveData` 追加字段，无需框架层感知具体类型；`new()` 约束保证框架可自动创建实例 |
| **AES-256-CBC + PBKDF2** | 存档为敏感数据时加密；PBKDF2 从口令派生密钥（可更换口令/盐），每次随机 IV 避免相同明文产生相同密文 |
| **随机 IV 前置密文** | 无需额外存储 IV，单文件自包含（`Base64(IV‖密文)`） |
| **纯 .NET 加密实现** | `Rijindael` 不依赖 Godot，可在 `Task.Run` 后台线程安全调用 |
| **配置集中到 ArchiveSetting** | 目录名、加密开关、密钥/盐统一由 `ArchiveSetting.tres` 管理，编辑器可视化配置，避免硬编码 |
| **Newtonsoft.Json 序列化** | 复用项目已有的 JSON 库，比 Godot 原生序列化更灵活（支持复杂类型、列表等） |
| **异步 I/O 经 `Task.Run`** | Godot `FileAccess` 不是线程安全的，因此 EasySave 用纯 .NET Stream 绕开 |
| **LoadAsync 拒绝覆盖** | 文件存在但读取失败时（密钥变更/损坏）不新建空档覆盖，保护玩家数据 |

---

## 7. 注意事项 / FAQ

**Q: 存档数据能升级吗（加字段/重构）？**
能。因为基于 JSON 序列化，加字段直接加 `public` 属性即可（旧档自动 `null` / `default`）。复杂迁移需在 `LoadAsync` 后手动检查并填充默认值。

**Q: 开启加密后改 KEY/Salt 会怎样？**
旧档会解密失败：`Rijindael.Decrypt` 抛 `CryptoException` → `EasySave` 捕获返回 `null` → `ArchiveSystem.LoadAsync` 检测到读取失败 → **拒绝覆盖并打 Error 日志**。如需改密钥，应视为"旧档作废"，让玩家新建存档，或先解密迁移再改配置。

**Q: 存档文件损坏怎么处理？**
`EasySave.LoadFromUserAsync` 反序列化/解密失败返回 `null`。`ArchiveSystem.LoadAsync` 现在区分两种情形：`Catalogue.sav` **不存在** → 自动新建（首次）；**存在但读失败** → 拒绝覆盖、打 Error 日志返回，避免吞掉玩家数据。业务代码应在访问 `CurrentData` 前判空。

**Q: 支持多存档槽位吗？**
支持。`Catalogues` 列表可存任意数量条目，`LoadAsync(long unitId)` 切换活跃存档。游戏侧通常基于 `GameCatalogue.Name` 展示给玩家。

**Q: EasySave 的 `res://` 方法什么时候用？**
仅编辑器/开发期（如导出默认配置）。运行时 `res://` 是只读的，`SaveInProject` 在打包后不可用。
