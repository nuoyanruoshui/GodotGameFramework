# Luban 其他数据源格式

## JSON 格式

### 基本对象

```json
{
    "id": "item_001",
    "name": "生命药水",
    "price": 50
}
```

### Map 表

```json
{
    "item_001": {
        "id": "item_001",
        "name": "生命药水",
        "price": 50
    },
    "item_002": {
        "id": "item_002",
        "name": "铁剑",
        "price": 100
    }
}
```

### List 表

```json
[
    {"id": "item_001", "name": "生命药水"},
    {"id": "item_002", "name": "铁剑"}
]
```

### 容器类型

```json
{
    "tags": ["tag1", "tag2", "tag3"],
    "attributes": [
        ["str", 10],
        ["agi", 15]
    ]
}
```

### 多态 bean

```json
{
    "effect": {
        "$type": "DamageEffect",
        "type": "DamageEffect",
        "damage": 100,
        "damageType": "Fire"
    }
}
```

使用 `$type` 属性指定具体类型。

### 多态鉴别符汇总

各数据源格式中多态 bean 的鉴别符字段名不同：

| 数据源 | 鉴别符 | 示例 |
|:---|:---|:---|
| JSON | `$type` | `{"$type": "AttackSkill", ...}` |
| XML | `type` | `<effect type="AttackSkill">` |
| YAML | `$type` | `$type: AttackSkill` |
| Lua | `_type_` | `{_type_ = "AttackSkill", ...}` |
| Lite | 位置式 | `{AttackSkill, ...}` |

## XML 格式

### 基本对象

```xml
<?xml version="1.0" encoding="utf-8"?>
<data>
    <record>
        <id>item_001</id>
        <name>生命药水</name>
        <price>50</price>
    </record>
</data>
```

### 多态 bean

```xml
<effect type="DamageEffect">
    <damage>100</damage>
</effect>
```

使用 `type` 属性指定具体类型。

### 容器类型

```xml
<data>
    <tags>
        <item>tag1</item>
        <item>tag2</item>
    </tags>
    <attributes>
        <item>
            <key>str</key>
            <value>10</value>
        </item>
    </attributes>
</data>
```

## YAML 格式

### 基本对象

```yaml
id: item_001
name: 生命药水
price: 50
tags:
  - tag1
  - tag2
```

### Map 表

```yaml
item_001:
  id: item_001
  name: 生命药水
  price: 50
item_002:
  id: item_002
  name: 铁剑
  price: 100
```

### 多态 bean

```yaml
effect:
  $type: DamageEffect
  damage: 100
```

使用 `$type` 指定具体类型。

## Lua 格式

数据文件需要以 `return` 开头：

### 基本对象

```lua
return {
    id = "item_001",
    name = "生命药水",
    price = 50,
    tags = {"tag1", "tag2"}
}
```

### Map 表

```lua
return {
    ["item_001"] = { id = "item_001", name = "生命药水" },
    ["item_002"] = { id = "item_002", name = "铁剑" }
}
```

### 多态 bean

```lua
return {
    effect = { _type_ = "DamageEffect", damage = 100 }
}
```

使用 `_type_` 指定具体类型。

## Lite 格式（.lit）

Luban 专有简洁格式，无字段名，按声明顺序：

### 基本对象

```
{1, item1, {100, 200}}
```

### 容器类型

```
{1, {tag1, tag2, tag3}, {{str, 10}, {agi, 15}}}
```

### 多态 bean

```
{1, {DamageEffect, 100}}
```

使用 `{TypeName, field1, field2, ...}` 格式。

### Null bean

```
null
```

注意不是 `{null}`。

## 子字段引用

### @语法

```xml
<var name="subField" type="SubBean" input="items@file"/>
<var name="jsonData" type="Bean" input="data.json"/>
```

## 标签（Tag）

### JSON

```json
{
    "__tag__": "dev",
    "id": "item_001"
}
```

### XML

```xml
<record>
    <__tag__>test</__tag__>
    <id>item_001</id>
</record>
```

### Lua

```lua
return {
    __tag__ = "dev",
    id = "item_001"
}
```

## 变体（Variants）

### JSON

```json
{
    "id": "item_001",
    "price": 100,
    "price@cn": 80,
    "price@en": 120
}
```

## 格式选择建议

| 场景 | 推荐格式 |
|:---|:---|
| 策划填表 | Excel/CSV |
| 复杂配置（技能/AI）| JSON |
| 需要注释 | YAML |
| Lua 项目 | Lua |
| 最小体积 | Lite |
| 编辑器数据 | JSON（单文件单记录）|
