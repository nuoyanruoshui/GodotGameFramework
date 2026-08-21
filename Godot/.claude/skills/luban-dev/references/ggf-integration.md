# GGF 框架 Luban 集成指南

## 概述

GGF（Godot Game Framework，Godot 4.7 + C#/.NET 8）项目使用 Luban 作为配置表方案，生成格式为 **cs-bin**（C# 代码）+ **bin**（二进制数据），通过 Godot 的 `FileAccess` 直接读取 `res://` 下的 `.bytes` 数据，生成代码位于 `TheGame/GameScripts/GameProto/GameConfig/`。

## 项目目录结构

```
<仓库根>/                                    # 仓库根（Godot/ 的上一级）
├── Configs/GameConfig/                      # 配置工程根目录
│   ├── luban.conf                           # Luban 主配置文件
│   ├── gen_code_bin_to_project.bat/sh       # 客户端代码生成（标准模板）
│   ├── gen_code_bin_to_project_lazyload.bat/sh  # 客户端代码生成（懒加载模板，推荐）
│   ├── gen_code_bin_to_server.bat/sh        # 服务端代码生成
│   ├── Defines/                             # XML Schema 定义（内置类型 vector2/vector3 等）
│   ├── Datas/                               # Excel 数据源目录
│   │   ├── __tables__.xlsx                  # 表注册索引
│   │   ├── __beans__.xlsx                   # Bean 复合类型定义
│   │   ├── __enums__.xlsx                   # 枚举类型定义
│   │   └── 实体.xlsx / 角色.xlsx / ...      # 业务数据表
│   └── CustomTemplate/                      # 自定义代码生成模板
│       ├── ConfigSystem.cs                  # 配置加载器模板（非自动生成，复制到 GameProto/）
│       ├── ExternalTypeUtil.cs              # Godot 类型转换工具（非自动生成，复制到 GameProto/）
│       └── CustomTemplate_Client_LazyLoad/  # 懒加载表模板
├── Tools/Luban/                             # Luban 工具链
│   └── Luban.dll                            # Luban 主程序
└── Godot/                                   # Godot 工程
    └── GodotProject/
        └── TheGame/
            ├── GameScripts/GameProto/       # 生成代码
            │   ├── ConfigSystem.cs          # 配置加载器（由脚本从 CustomTemplate 复制）
            │   ├── ExternalTypeUtil.cs      # Godot 类型映射（由脚本从 CustomTemplate 复制）
            │   └── GameConfig/              # Luban 自动生成的代码（命名空间 GameConfig）
            │       ├── Tables.cs            # 表管理类
            │       ├── Entity/  UI/  Character/  Level/   # 按模块组织的生成代码
            │       │   ├── TbEntityConfig.cs / TbXxx.cs   # 表类
            │       │   ├── EntityConfig.cs / Xxx.cs       # 数据类
            │       │   └── EntityId.cs / UIFormId.cs      # 枚举/Id 类
            └── DataTables/GameConfigs/      # 二进制配置数据（.bytes）
                └── entity_tbentityconfig.bytes            # 各表二进制数据
```

> **注意路径关系**：`Configs/GameConfig/` 在**仓库根**（`Godot/` 的上级目录），生成脚本内部 `WORKSPACE=../..`、`cd` 到自身目录后输出到 `Godot/GodotProject/TheGame/...`。`GameFolderConstant.GameConfigs = "res://TheGame/DataTables/GameConfigs/{0}.bytes"`（`Framework/GodotGameFrameworkCore/Config/GameFolderConstant.cs`）。

## luban.conf 实际配置

项目使用 Excel 定义方式（`__tables__.xlsx`/`__beans__.xlsx`/`__enums__.xlsx`），搭配 XML Schema（`Defines/`）：

```json
{
    "groups": [
        {"names": ["c"], "default": true},
        {"names": ["s"], "default": true},
        {"names": ["e"], "default": true}
    ],
    "schemaFiles": [
        {"fileName": "Defines", "type": ""},
        {"fileName": "Datas/__tables__.xlsx", "type": "table"},
        {"fileName": "Datas/__beans__.xlsx", "type": "bean"},
        {"fileName": "Datas/__enums__.xlsx", "type": "enum"}
    ],
    "dataDir": "Datas",
    "targets": [
        {"name": "server", "manager": "Tables", "groups": ["s"], "topModule": "GameConfig"},
        {"name": "client", "manager": "Tables", "groups": ["c"], "topModule": "GameConfig"},
        {"name": "all", "manager": "Tables", "groups": ["c,s,e"], "topModule": "GameConfig"}
    ]
}
```

**关键约定**：
- `topModule` 为 `GameConfig`，生成代码命名空间为 `GameConfig.xxx`
- 分组 `c`（客户端）、`s`（服务端）、`e`（编辑器）
- 代码输出目标：`cs-bin`，数据输出目标：`bin`
- 客户端导出使用 `--customTemplateDir CustomTemplate_Client_LazyLoad` 启用懒加载模板

## 代码生成脚本（导出数据的唯一入口）

**导出数据时，始终使用项目提供的脚本，不要手动拼接 dotnet 命令。**

脚本位于 `Configs/GameConfig/` 目录下，脚本内部会 `cd` 到自身目录，因此可从任意目录以**完整或相对仓库根的路径**调用：

| 脚本 | 用途 | 说明 |
|:---|:---|:---|
| `gen_code_bin_to_project_lazyload` | 客户端代码+数据（懒加载模板，**推荐**） | AI 调用此脚本 |
| `gen_code_bin_to_project` | 客户端代码+数据（标准模板） | 非懒加载 |
| `gen_code_bin_to_server` | 服务端代码+数据 | - |

### AI 调用导表命令

**Windows（从 `Godot/` 目录）：**
```bash
cmd //c "set AI_MODE=1 && ..\\Configs\\GameConfig\\gen_code_bin_to_project_lazyload.bat"
```
（或从仓库根：`cmd //c "set AI_MODE=1 && Configs\GameConfig\gen_code_bin_to_project_lazyload.bat"`）

**macOS/Linux：**
```bash
bash ../Configs/GameConfig/gen_code_bin_to_project_lazyload.sh
```

`AI_MODE=1` 会让 `.bat` 跳过结尾的 `pause`（`if not defined AI_MODE pause`），适合 AI/CI 调用。

### 客户端生成脚本流程

1. 复制 `CustomTemplate/ConfigSystem.cs` → `TheGame/GameScripts/GameProto/ConfigSystem.cs`
2. 复制 `CustomTemplate/ExternalTypeUtil.cs` → `TheGame/GameScripts/GameProto/ExternalTypeUtil.cs`
3. 执行 Luban 代码生成：`-t client -c cs-bin -d bin`，`--customTemplateDir` 启用懒加载模板，输出代码到 `TheGame/GameScripts/GameProto/GameConfig/`、数据到 `TheGame/DataTables/GameConfigs/`

**注意事项**：
- `ConfigSystem.cs` 和 `ExternalTypeUtil.cs` 是手动维护的桥接文件，不在 `GameConfig/` 下、不被 Luban 覆盖
- 生成前确保 `.NET SDK 8.0+` 已安装
- 生成后需在 Godot 中重新扫描（`--build-solutions`）或重新构建才可编译通过

## ConfigSystem 配置加载器

`ConfigSystem.cs` 桥接 Luban 生成代码与 Godot 资源系统（懒加载，首次访问才触发加载）：

```csharp
using System;
using Luban;
using GameConfig;
using Godot;
using GameFramework;
using GameConfig.Constant;

public class ConfigSystem
{
    private static ConfigSystem _instance;
    public static ConfigSystem Instance => _instance ??= new ConfigSystem();

    private bool _init = false;
    private Tables _tables;

    /// <summary>懒加载访问所有配置表</summary>
    public Tables Tables
    {
        get
        {
            if (!_init) Load();
            return _tables;
        }
    }

    public void Load()
    {
        _tables = new Tables(LoadByteBuf);
        _init = true;
    }

    private ByteBuf LoadByteBuf(string fileName)
    {
        string path = Utility.Text.Format(GameFolderConstant.GameConfigs, fileName);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        byte[] bytes = file?.GetBuffer((long)file.GetLength());
        if (bytes == null || bytes.Length == 0)
        {
            throw new Exception($"Failed to load config file: res://DataTables/{file}");
        }
        return new ByteBuf(bytes);
    }
}
```

### 初始化时机

配置表在 `ProcedurePrelode` 阶段由 `GF.Resource` 预加载（`res://TheGame/DataTables/GameConfigs/` 下的 `.bytes` 资源）。首次访问 `ConfigSystem.Instance.Tables` 时自动触发加载。

## ExternalTypeUtil Godot 类型映射

通过 `Defines/` 的 `mapper` 定义，将 Luban 配置类型映射到 Godot 内置类型（`vector2` 等由 Luban 内置定义，`ExternalTypeUtil` 提供构造函数）：

```csharp
using GameConfig;
using Godot;

public static class ExternalTypeUtil
{
    public static Vector2 NewVector2(GameConfig.vector2 v) => new Vector2(v.X, v.Y);
    public static Vector3 NewVector3(GameConfig.vector3 v) => new Vector3(v.X, v.Y, v.Z);
    public static Vector4 NewVector4(GameConfig.vector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);
    public static Vector2I NewVector2Int(GameConfig.vector2int v) => new Vector2I(v.X, v.Y);
    public static Vector3I NewVector3Int(GameConfig.vector3int v) => new Vector3I(v.X, v.Y, v.Z);
}
```

对应的 XML 定义（`Defines/builtin.xml`）：

```xml
<module name="">
    <bean name="vector3" valueType="1" sep=",">
        <var name="x" type="float"/>
        <var name="y" type="float"/>
        <var name="z" type="float"/>
        <mapper target="client" codeTarget="cs-bin">
            <option name="type" value="Godot.Vector3"/>
            <option name="constructor" value="ExternalTypeUtil.NewVector3"/>
        </mapper>
    </bean>
</module>
```

## 配置数据访问

### 基础访问

```csharp
// 获取表实例
var tables = ConfigSystem.Instance.Tables;

// Map 表：按主键查询
var entityCfg = tables.TbEntityConfig.Get((int)EntityId.Cat);

// 遍历所有数据
foreach (var item in tables.TbEntityConfig.DataList)
{
    GD.Print($"{item.Id} - {item.Path}");
}
```

### 推荐封装 ConfigManager

复杂模块应封装配置管理器，避免业务代码直接散落 `ConfigSystem.Instance.Tables.TbXxx`：

```csharp
// GameScripts/Manager/EntityConfigMgr.cs
public class EntityConfigMgr
{
    private static EntityConfigMgr _instance;
    public static EntityConfigMgr Instance => _instance ??= new EntityConfigMgr();

    /// <summary>获取实体配置</summary>
    public EntityConfig GetEntityConfig(EntityId id)
    {
        return ConfigSystem.Instance.Tables.TbEntityConfig.Get((int)id);
    }
}
```

**使用**：
```csharp
// 推荐
var cfg = EntityConfigMgr.Instance.GetEntityConfig(EntityId.Cat);

// 简单场景可直接用
var cfg = ConfigSystem.Instance.Tables.TbEntityConfig.Get((int)EntityId.Cat);
```

## Excel 表定义规范

### __tables__.xlsx 注册表

| full_name | value_type | read_mode | comment |
|-----------|-----------|-----------|---------|
| GameConfig.TbEntityConfig | EntityConfig | map | 实体配置表 |
| GameConfig.TbUINormalForm | UINormalForm | map | UI 表 |

- `full_name`：`GameConfig.表名`，生成 `GameConfig.Tables.TbXxx`
- `value_type`：对应的数据类名
- `read_mode`：`map`（按键索引）/ `list`（列表）

### __beans__.xlsx 复合类型

| full_name | fields.name | fields.type | fields.comment |
|-----------|-------------|-------------|----------------|
| ItemDrop | itemId | int | 道具ID |
| ItemDrop | count | int | 数量 |
| ItemDrop | probability | float | 概率 |

在数据表中使用 `ItemDrop` 或 `ItemDrop[]`。

### __enums__.xlsx 枚举

| enum_name | item_name | item_value | item_alias |
|-----------|-----------|------------|------------|
| EQuality | White | 0 | 白 |
| EQuality | Green | 1 | 绿 |

### 业务数据表格式

```
第1行（字段名）: id    | name   | hp   | atk  | desc
第2行（类型）:   int   | string | int  | int  | string
第3行（分组）:   c     | c      | c    | c    | c
第4行（注释）:   道具ID| 名称   | 血量 | 攻击 | 描述
第5行+（数据）: 1001  | 木剑   | 0    | 10   | 新手木剑
```

## 添加新配置表完整流程

```
1. 在 Configs/GameConfig/Datas/__tables__.xlsx 中注册新表
   full_name: GameConfig.TbNewTable   value_type: NewTableRow   read_mode: map

2. 创建 Datas/new_table.xlsx，按规范添加列定义和数据

3. （如需复合类型）在 __beans__.xlsx 中定义 Bean
   （如需枚举）在 __enums__.xlsx 中定义枚举
   （如需 Godot 类型）在 Defines/ 中添加 mapper

4. 运行 gen_code_bin_to_project_lazyload.bat（推荐）生成代码和数据

5. 验证生成结果：
   - TheGame/GameScripts/GameProto/GameConfig/ 下新增 TbNewTable.cs 和 NewTableRow.cs
   - TheGame/DataTables/GameConfigs/ 下新增 new_table_tbnewtable.bytes

6. 重新构建（dotnet build），在 GameScripts/Manager/ 下创建 NewTableConfigMgr.cs 封装查询方法
```

**注意**：不要使用 `#` 前缀自动导入方式（如 `#Item-道具表.xlsx`），所有表必须在 `__tables__.xlsx` 中正式注册，确保表结构统一管理。

## 兼容性注意事项

- **新增字段**：前向兼容，旧客户端自动忽略新字段
- **删除字段**：不兼容，旧客户端会报错
- **修改字段类型**：不兼容，需同步更新代码
- **重命名字段**：不兼容，需同步更新代码
- 生成代码（`GameConfig/` 目录）**不要手动修改**，下次生成会覆盖
- `ConfigSystem.cs` 和 `ExternalTypeUtil.cs` 是手动维护的桥接文件，位于 `CustomTemplate/` 模板中，改桥接逻辑时同步更新模板
