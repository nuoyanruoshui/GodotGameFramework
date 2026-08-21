---
name: luban-dev
description: Luban 游戏配置全栈工具，支持枚举/Bean/数据表的增删改查、代码生成、GGF 集成。触发场景：(1) 编辑游戏配置数据（配置表/数据表/道具表/技能表/奖励表/活动表），(2) 新增/修改/删除配置表结构，(3) 定义枚举/Bean/字段，(4) 导表/生成配置代码，(5) 编写 luban.conf 或 Schema 定义，(6) Luban 类型系统/校验器问题。即使用户未明确说"Luban"，只要是编辑游戏配置数据，也应使用此技能。
---

# Luban 数据配置工具

## GGF 项目核心约定

- **生成格式**：`cs-bin`（C# 代码）+ `bin`（二进制数据）
- **命名空间**：`GameConfig`（非默认 `cfg`）
- **数据加载**：`ConfigSystem.Instance.Tables` 懒加载，底层走 Godot `FileAccess` 读取 `res://TheGame/DataTables/GameConfigs/{0}.bytes`
- **代码位置**：`Godot/GodotProject/TheGame/GameScripts/GameProto/GameConfig/`（Luban 自动生成，勿手改）
- **数据位置**：`Godot/GodotProject/TheGame/DataTables/GameConfigs/`（`.bytes`）
- **配置工程**：`Configs/GameConfig/`（位于仓库根，即 `Godot/` 的上一级）
- **表定义**：Excel（`__tables__.xlsx`/`__beans__.xlsx`/`__enums__.xlsx`）+ XML Schema（`Defines/`）
- **导出数据 → 使用导出脚本，不要手动拼 dotnet 命令**

### 导出脚本

所有脚本位于 `Configs/GameConfig/`，脚本内部会 `cd` 到自身目录，可用完整路径或相对仓库根的路径调用。

| 脚本 | 用途 | 说明 |
|:---|:---|:---|
| `gen_code_bin_to_project_lazyload` | 客户端（**推荐**，懒加载模板） | AI 调用此脚本 |
| `gen_code_bin_to_project` | 客户端（标准模板） | 非懒加载 |
| `gen_code_bin_to_server` | 服务端 | - |

### AI 调用导表命令

根据操作系统选择对应扩展名（`AI_MODE=1` 跳过 `.bat` 结尾的 `pause`）：

**Windows（从 Godot/ 目录）：**
```bash
cmd //c "set AI_MODE=1 && ..\\Configs\\GameConfig\\gen_code_bin_to_project_lazyload.bat"
```

**macOS/Linux：**
```bash
bash ../Configs/GameConfig/gen_code_bin_to_project_lazyload.sh
```

### 新增配置表流程

1. 在 `Datas/__tables__.xlsx` 注册新表
2. 创建 `Datas/xxx.xlsx` 数据表
3. 在 `__beans__.xlsx` / `__enums__.xlsx` 定义复合类型/枚举（可选）
4. 运行导出脚本生成代码和数据
5. 在 `TheGame/GameScripts/Manager/` 封装 `XxxConfigMgr`

### 配置管理红线

- **不要直接修改** `TheGame/GameScripts/GameProto/GameConfig/` 下的生成代码
- 复杂模块封装 `XxxConfigMgr`，业务代码通过管理器访问
- `ConfigSystem.cs` 和 `ExternalTypeUtil.cs` 是桥接文件，改逻辑需同步 `Configs/GameConfig/CustomTemplate/` 模板
- 新增字段前向兼容，删除/改名字段不兼容

---

## 配置表操作工具（luban_helper.py）

通过 Python 脚本直接 CRUD Excel 配置表，无需手动编辑 xlsx。

### 前置条件

Python 3.8+，`pip install openpyxl`

### 执行方式

```bash
python scripts/luban_helper.py --data-dir ../Configs/GameConfig/Datas <command>
```

`--data-dir` 必须放在子命令之前。PowerShell 中用 `;` 分隔命令，JSON 参数推荐用 `--file` 从文件读取。

**参数类型**：位置参数（大写，如 `NAME`、`TABLE`）直接传值，不加 `--` 前缀；可选参数带 `--` 前缀。例如 `table get test.TbItem`（正确）而非 `table get --name test.TbItem`（错误）。

### 命令速查

| 分类 | 命令 | 功能 |
|------|------|------|
| 枚举 | `enum list/get/add/update/delete` | 枚举 CRUD |
| 结构 | `bean list/get/add/update/delete` | Bean CRUD |
| 表 | `table list/get/add/update/delete` | 表 CRUD |
| 字段 | `field list/add/update/delete/disable/enable` | 字段操作 |
| 数据 | `row list/get/query/add/update/delete` | 数据行操作 |
| 批量 | `batch fields/rows` | 批量操作 |
| 导入导出 | `export/import` | JSON 导入导出 |
| 验证 | `validate` / `ref` | 数据验证 / 引用检查 |
| 类型 | `type list/validate/suggest/search/guide/info` | 类型系统 |
| 自动 | `auto list/create` | 自动导入表（`#` 前缀，不推荐） |
| 管理 | `rename/copy/diff/template` | 表管理工具 |

### 操作规范

- **只读操作**（list/get/query/search）：直接执行
- **写入操作**（add/update/delete）：**必须确认**后再执行
- **删除操作**：先 `ref` 检查引用，提醒风险，二次确认
- **修改前**：先 `table get` / `field list` 确认结构，`row get` 避免主键冲突
- **修改后**：用 `validate` 验证

### 分组自动推断

添加字段时不指定 `--group`，自动推断：
- `c`（客户端）：name, desc, icon, image, model, effect, sound, ui 等
- `s`（服务器）：server, logic, damage, hp, mp, exp, level, rate 等
- `cs`（两端）：id, 其他无法判断的字段

---

## 参考文档（按需加载）

| 场景 | 文档 | 内容 |
|------|------|------|
| GGF 集成 / ConfigSystem / 生成脚本 / Excel规范 | [ggf-integration.md](references/ggf-integration.md) | 项目结构、加载器、导出脚本、路径约定 |
| 操作工具命令详解 / Excel结构 / 数据填写格式 | [operating-guide.md](references/operating-guide.md) | luban_helper.py 完整命令参考 |
| 类型系统和语法 | [type-system.md](references/type-system.md) | 基础/容器/自定义/可空类型、Mapper、constalias |
| Schema 定义（XML/Excel）/ 多态规范 | [schema.md](references/schema.md) | enum/bean/table 定义、字段属性、多态bean、flags约束 |
| luban.conf 配置 | [luban-conf.md](references/luban-conf.md) | 完整配置项、分组策略、级联选项、topModule |
| 校验器类型和用法 / ref行为 | [validators.md](references/validators.md) | ref/range/path/size/set/index/!、Ref字段命名、ResolveRef |
| Excel 数据格式 | [excel-format.md](references/excel-format.md) | 标题行、容器格式、多态、标签 |
| CLI 命令行参数 | [command-reference.md](references/command-reference.md) | 完整参数列表、代码/数据目标、--variant/--timeZone |
| JSON/XML/YAML/Lua 数据源 | [data-sources.md](references/data-sources.md) | 非Excel数据源格式、多态鉴别符 |
| 运行时加载 / 类型映射 / 本地化工作流 | [runtime.md](references/runtime.md) | Godot 加载、代码风格、本地化完整流程 |

示例：`examples/item-system/`（CSV+XML）、`examples/skill-system/`（JSON+多态）

脚本：`scripts/luban_helper.py`（操作工具）、`scripts/requirements.txt`（依赖）

官方文档：https://www.datable.cn/docs/intro
