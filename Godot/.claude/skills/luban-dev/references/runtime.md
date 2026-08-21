# Luban 运行时加载

## GGF + C# + Binary

GGF 项目使用 `cs-bin` + `bin`，通过 `ConfigSystem.Instance.Tables` 懒加载，底层用 Godot `FileAccess` 读取 `res://` 下的 `.bytes`：

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

`GameFolderConstant.GameConfigs = "res://TheGame/DataTables/GameConfigs/{0}.bytes"`。

## C# + JSON（非本项目默认）

如需 JSON 数据源（`cs-simple-json`），加载器返回 `JSONNode`：

```csharp
using SimpleJSON;

// Loader(string file) 返回 JSONNode，读取 {file}.json
```

## 自动判断格式

通过反射检测 `cfg.Tables` 构造函数的 Loader 返回类型，自动适配 JSON 或 Binary：

```csharp
var loader = loaderReturnType == typeof(ByteBuf) ?
    new System.Func<string, ByteBuf>(LoadByteBuf) :
    (System.Delegate)new System.Func<string, JSONNode>(LoadJson);

var tables = (cfg.Tables)tablesCtor.Invoke(new object[] {loader});
```

## 类型映射（TypeMapper）

### 将配置类型映射到外部类型

**仅 C# 代码支持**

### Enum 映射

```xml
<enum name="AudioType">
    <var name="UNKNOWN" value="0"/>
    <mapper target="client" codeTarget="cs-bin">
        <option name="type" value="Godot.AudioStreamPlayer"/>
    </mapper>
</enum>
```

**注意（GGF）**：本项目 `ExternalTypeUtil.cs` 已映射 `Godot.Vector2/Vector3/Vector4/Vector2I/Vector3I`（见 [ggf-integration.md](ggf-integration.md)）。新增类型映射需同时改 `Defines/` 中的 mapper 和 `Configs/GameConfig/CustomTemplate/ExternalTypeUtil.cs` 模板。

**注意：** 必须确保枚举项的值与映射的枚举类型的枚举项的值完全一致。

### Bean 映射

```xml
<bean name="vector3" valueType="1" sep=",">
    <var name="x" type="float"/>
    <var name="y" type="float"/>
    <var name="z" type="float"/>
    <mapper target="client" codeTarget="cs-bin">
        <option name="type" value="Godot.Vector3"/>
        <option name="constructor" value="ExternalTypeUtil.NewVector3"/>
    </mapper>
</bean>
```

Bean 需额外提供 `constructor` 指定转换函数。

### 匹配规则

通过命令行 `-t $target` 和 `-c $codeTarget` 匹配 mapper 的对应属性。

```bash
-t client -c cs-bin
```

`target`/`codeTarget` 支持多值：

```xml
<mapper target="client,server" codeTarget="cs-bin">
```

## 代码风格

### 命名风格

| 风格 | 说明 | 示例 |
|------|------|------|
| `none` | 保持原样 | `aa_bb_cc` → `aa_bb_cc` |
| `camel` | 小驼峰 | `aa_bb_cc` → `aaBbCc` |
| `pascal` | 大驼峰 | `aa_bb_cc` → `AaBbCc` |
| `upper` | 全大写 | `aa_bb_cc` → `AA_BB_CC` |
| `snake` | 下划线 | `aa_bb_cc` → `aa_bb_cc` |

### 命名位置

- `namespace` — 命名空间
- `type` — 类型名（enum, bean, table, manager）
- `method` — 函数名
- `property` — 属性名
- `field` — 字段名
- `enumItem` — 枚举项名

### 默认风格（按语言）

| 语言 | namespace | type | method | property | field | enumItem |
|------|-----------|------|--------|----------|-------|----------|
| C# | pascal | pascal | pascal | pascal | camel | none |
| Java | pascal | pascal | pascal | camel | camel | none |
| Go | snake | pascal | camel | camel | pascal | none |
| TypeScript | pascal | pascal | camel | camel | camel | none |

### 命令行设置

```bash
-x codeStyle=your_style
-x namingConvention.{codeTarget}.{location}=style
```

## 本地化系统

### 配置方法

通过命令行参数启用：

```bash
-x l10n.provider=default
-x l10n.textFile.path=*@path/to/texts.json
-x l10n.textFile.keyFieldName=key
-x l10n.textFile.languageFieldName=zh
-x l10n.convertTextKeyToValue=1
```

### text.json 格式

多语言文本文件，每条记录包含 key 和各语言翻译：

```json
[
  {"key": "item_sword_001", "zh": "倚天剑", "en": "Heaven Sword", "ja": "倚天の剣"},
  {"key": "item_sword_001_desc", "zh": "传说中的神剑", "en": "A legendary sword", "ja": "伝説の神剣"}
]
```

- `key`：文本 key，配置表中引用此值
- 其余字段：语言代码 → 翻译文本（语言字段名由 `l10n.textFile.languageFieldName` 指定）

### 字段标记方式

- **`text` 类型**（等价 `string#text=1`）：标记为本地化 key，导出时触发本地化替换
- **`string` 类型**：不触发本地化替换，原样输出

```xml
<var name="name" type="text" comment="名称（本地化）"/>   <!-- 触发本地化 -->
<var name="internalId" type="string" comment="内部ID"/>  <!-- 不触发 -->
```

### 导出效果

| 阶段 | 字段值 |
|:---|:---|
| 源数据（Excel/JSON） | key：`item_sword_001` |
| 导出后（生成数据） | 实际文本：`Heaven Sword`（由 `languageFieldName` 指定的语言） |

### 两种策略

| 策略 | 说明 | 适用场景 |
|:---|:---|:---|
| **导出时替换**（`convertTextKeyToValue=1`） | 导出数据中直接包含目标语言文本 | 单语言包发布，无需运行时切换 |
| **运行时查找** | 导出数据保留 key，运行时通过 LocalizationManager 按 key+语言查找 | 多语言动态切换 |

### text 类型

`text` 是特殊的语法糖，等价 `string#text=1`，语义上表示本地化字符串的 key。

### 时间本地化

datetime 按目标时区转 UTC 秒数：

```bash
-t "Asia/Shanghai"
```
