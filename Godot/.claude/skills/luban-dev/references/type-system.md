# Luban 类型系统完整参考

## 基础类型

| 类型 | C# 映射 | 取值范围 | 示例 |
|------|---------|----------|------|
| `bool` | bool | true/false | `true`, `1`, `0` |
| `byte` | byte | 0-255 | `255` |
| `short` | short | -32768~32767 | `-1000` |
| `int` | int | -2^31~2^31-1 | `100000` |
| `long` | long | -2^63~2^63-1 | `10000000000` |
| `float` | float | IEEE 754 | `1.5`, `3.14` |
| `double` | double | IEEE 754 | `3.14159` |
| `string` | string | Unicode 字符串 | `"hello"` |
| `text` | string | 支持换行的长文本 | `"多行\n文本"` |
| `datetime` | DateTime | 日期时间 | `2024-01-01 12:00:00` |

## 容器类型

### array

固定大小的数组，在 JSON 中使用数组表示，CSV 中使用分隔符。

```xml
<var name="values" type="array,int"/>
```

### list

可变大小的列表，最常用。

```xml
<var name="tags" type="list,string"/>
<var name="items" type="list,ItemData"/>
```

### set

元素唯一的集合。

```xml
<var name="flags" type="set,string"/>
```

### map

键值对映射，key 只能是基本类型或 enum。

```xml
<var name="attributes" type="map,string,int"/>
```

**JSON 格式：** `[["key1", value1], ["key2", value2]]`

**Lua 格式：** `{["key1"] = value1, ["key2"] = value2}`

## 自定义类型

### enum

枚举类型，支持 int 值。

```xml
<enum name="EQuality">
    <var name="White" value="0"/>
    <var name="Green" value="1"/>
</enum>
```

### bean

复合结构类型，支持继承和多态。

```xml
<bean name="Vector3">
    <var name="x" type="float"/>
    <var name="y" type="float"/>
    <var name="z" type="float"/>
</bean>
```

### table

数据表管理类型，不是普通类型，不能用于字段 type。

## 可空类型

所有非容器类型都支持可空版本。

| 语法 | 说明 |
|------|------|
| `int?` | 可空整数 |
| `string?` | 可空字符串 |
| `ItemCfg?` | 可空 bean |

## 类型组合

### 嵌套容器

```xml
<var name="matrix" type="list,list,int"/>
<var name="groups" type="map,string,list,int"/>
```

### 容器+验证器

```xml
<var name="levels" type="(list#size=[1,10]),int"/>
<var name="ids" type="(list#index=id),ItemData"/>
```

## 类型映射 (Mapper)

将配置类型映射到外部现成类型。

### Enum 类型映射

```xml
<enum name="AudioType">
    <mapper target="client" codeTarget="cs-bin">
        <option name="type" value="Godot.AudioStream"/>
    </mapper>
    <var name="Unknown" value="0"/>
    <var name="ACC" value="1"/>
</enum>
```

### Bean 类型映射

```xml
<bean name="Vector3">
    <mapper target="client" codeTarget="cs-bin">
        <option name="type" value="Godot.Vector3"/>
        <option name="constructor" value="ExternalTypeUtil.NewVector3"/>
    </mapper>
    <var name="x" type="float"/>
    <var name="y" type="float"/>
    <var name="z" type="float"/>
</bean>
```

## 特殊类型用法

### constalias

为数值类型提供字符串别名（仅 Excel/lite 数据源）。

在 XML Schema 中定义为 `<module>` 的直接子元素：

```xml
<module name="">
    <constalias name="MAX_LEVEL" value="99"/>
    <constalias name="BOSS_TAG" value="1001"/>
</module>
```

数据文件中使用别名代替数值：

```csv
id,maxLevel
item_001,MAX_LEVEL
```

### refgroup

定义可复用的表引用组。

```xml
<refgroup name="EquipTables">
    item.TbWeapon,item.TbArmor,item.TbAccessory
</refgroup>
```

使用：

```xml
<var name="equipId" type="string#ref=EquipTables"/>
```

## 数据格式对照

### array/list/set

| 格式 | 示例 |
|------|------|
| CSV | `1,2,3` |
| JSON | `[1,2,3]` |
| XML | `<item>1</item><item>2</item>` |
| Lua | `{1,2,3}` |
| YAML | `[1, 2, 3]` |

### map

| 格式 | 示例 |
|------|------|
| JSON | `[["a",1],["b",2]]` |
| Lua | `{["a"]=1, ["b"]=2}` |
| YAML | `a: 1\nb: 2` |

### bean

| 格式 | 示例 |
|------|------|
| JSON | `{"x":1,"y":2}` |
| XML | `<x>1</x><y>2</y>` |
| Lua | `{x=1, y=2}` |
