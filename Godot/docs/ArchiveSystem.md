# 存档系统 (Archive System)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GodotGameFrameworkCore/Archive/ArchiveSystem.cs`、`Framework/GodotGameFrameworkCore/Json/EasySave.cs`、`TheGame/GameScripts/Archive/GameData.cs`（含 `GameCatalogue` 和 `GameData` 两个类）
> 本文档描述 GGF 的通用存档系统：ArchiveSystem 泛型设计、Catalogue/Data 分离模式、CRUD API、EasySave 持久化底层与游戏侧定制示例。

---

## 1. 概述

GGF 的存档系统是一个**泛型通用存档框架**，提供"存档目录 + 存档数据"的分离式设计，支持创建、读取、保存、覆盖和删除存档。底层基于 `EasySave`（Newtonsoft.Json 序列化 + `user://` 文件系统）实现持久化。

| 层 | 位置 | 职责 |
|----|------|------|
| 持久化底层 | `GodotGameFrameworkCore/Json/EasySave.cs` | JSON 序列化/反序列化、文件 CRUD（同步 + 异步）、`user://` 路径封装 |
| 通用存档框架 | `GodotGameFrameworkCore/Archive/ArchiveSystem.cs` | `ArchiveSystem<T, U>` 泛型类：Catalogue 列表管理、Create/Load/Overwrite/Delete |
| 游戏侧实现 | `TheGame/GameScripts/Archive/GameData.cs` | `GameData : ArchiveData` / `GameCatalogue : ArchiveCatalogue`：项目自定义数据字段 |

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

### 2.2 文件布局（`user://`）

```
user://
  └── GameData/
        ├── Catalogue.sav          ← 存档目录列表（List<T> JSON）
        └── Data/
              ├── 1711600000.sav   ← 每个存档的独立数据文件
              ├── 1711700000.sav
              └── ...
```

### 2.3 数据流

```
游戏逻辑
    │  archive.SaveAsync() / LoadAsync() / Delete(unitId)
    ▼
ArchiveSystem<T, U>
    ├── Catalogues : List<T>           ← 所有存档条目
    ├── CurrentCatalogue : T           ← 当前活跃存档条目
    ├── CurrentData : U                ← 当前活跃存档数据
    │
    └── EasySave
          ├── SaveInUserAsync(obj, path)  → Newtonsoft.Json → user:// 文件
          ├── LoadFromUserAsync<T>(path)  → 读文件 → 反序列化
          └── DeleteInUserAsync(path)     → 删除文件
```

---

## 3. 核心 API

### 3.1 ArchiveSystem<T, U>

| 方法 | 说明 |
|------|------|
| `SaveAsync()` | 创建新存档：生成新 `UnitId`（当前 Unix 时间戳），写入 Catalogue + Data 文件到 `user://GameData/` |
| `SaveAsync(long unitId)` | 将 `CurrentData` 保存到已有存档条目（覆盖该存档的 Data 文件） |
| `OverWriteAsync()` | 覆盖当前活跃存档（= `SaveAsync(CurrentCatalogue.UnitId)`） |
| `LoadAsync()` | 初始化/加载：读取 Catalogue 列表，不存在则自动创建新存档；默认加载最新（`Catalogues[^1]`） |
| `LoadAsync(long unitId)` | 按 `unitId` 加载指定存档数据到 `CurrentData` |
| `Delete(long unitId)` | 删除指定存档：从 Catalogue 列表移除条目，删除 Data 文件，重写 Catalogue 列表 |

**属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Catalogues` | `List<T>` | 所有存档目录条目 |
| `CurrentCatalogue` | `T` | 当前活跃的存档条目 |
| `CurrentData` | `U` | 当前活跃的存档数据 |

### 3.2 EasySave

| 方法 | 说明 |
|------|------|
| `SaveInUser<T>(data, fileName)` | 同步保存到 `user://` |
| `LoadFromUser<T>(fileName)` | 同步从 `user://` 加载（不存在返回 null） |
| `DeleteInUser(fileName)` | 同步删除 `user://` 文件 |
| `ExistsInUser(fileName)` | 检查 `user://` 文件是否存在 |
| `SaveInUserAsync<T>(data, fileName)` | 异步保存（`Task.Run` + `StreamWriter`） |
| `LoadFromUserAsync<T>(fileName)` | 异步加载（`Task.Run` + `StreamReader`） |
| `DeleteInUserAsync(fileName)` | 异步删除 |
| `SaveInProject<T>` / `LoadFromProject<T>` / `DeleteInProject` | `res://` 路径同步方法（仅编辑器/开发期使用） |

> `EasySave` 使用纯 .NET `StreamWriter/StreamReader` + `Task.Run` 实现异步 I/O（避免阻塞 Godot 主线程）。`user://` 路径经 `ProjectSettings.GlobalizePath` 解析为绝对路径。

---

## 4. 游戏侧定制

### 4.1 继承 ArchiveData / ArchiveCatalogue

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

### 4.2 使用示例

```csharp
// 初始化存档管理器
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

## 5. 设计决策

| 决策 | 理由 |
|------|------|
| **Catalogue + Data 分离** | 目录文件小（仅列表），加载存档列表时无需反序列化所有存档的完整数据；Data 文件独立，利于版本演进和增量修改 |
| **UnitId 用 Unix 时间戳** | 自然唯一且天然可排序，无需 UUID 依赖 |
| **泛型 `<T, U>` 而非接口约束** | 游戏侧通过继承 `ArchiveCatalogue`/`ArchiveData` 追加字段，无需框架层感知具体类型；`new()` 约束保证框架可自动创建实例 |
| **Newtonsoft.Json 序列化** | 复用项目已有的 JSON 库，比 Godot 原生序列化更灵活（支持复杂类型、列表等） |
| **异步 I/O 经 `Task.Run`** | Godot `FileAccess` 不是线程安全的，因此 EasySave 用纯 .NET Stream 绕开 |
| **存档路径硬编码 `user://GameData/`** | 框架层约定，游戏侧无需关心具体路径 |

---

## 6. 注意事项 / FAQ

**Q: 存档数据能升级吗（加字段/重构）？**
能。因为基于 JSON 序列化，加字段直接加 `public` 属性即可（旧档自动 `null` / `default`）。复杂迁移需在 `LoadAsync` 后手动检查并填充默认值。

**Q: 存档文件损坏怎么处理？**
`EasySave.LoadFromUserAsync` 反序列化失败返回 `null`，`ArchiveSystem.LoadAsync` 检测到 null 会打 Error 日志。业务代码应在访问 `CurrentData` 前判空。

**Q: 支持多存档槽位吗？**
支持。`Catalogues` 列表可存任意数量条目，`LoadAsync(long unitId)` 切换活跃存档。游戏侧通常基于 `GameCatalogue.Name` 展示给玩家。

**Q: EasySave 的 `res://` 方法什么时候用？**
仅编辑器/开发期（如导出默认配置）。运行时 `res://` 是只读的，`SaveInProject` 在打包后不可用。
