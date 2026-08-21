# Excel 数据格式详解

## 标题行格式

Excel 数据表使用特殊标记行定义结构：

| 标记 | 用途 | 必需 |
|:---|:---|:---|
| `##var` 或 `##` | 字段定义行 | 是 |
| `##type` | 类型定义行 | 是 |
| `##group` | 导出分组行 | 否 |
| `##comment` | 字段注释行 | 否 |
| `##` 开头 | 注释行 | 否 |

## 标准表格式

### 示例

| | A | B | C | D |
|:---|:---|:---|:---|:---|
| 1 | ##var | id | name | reward |
| 2 | ##type | int | string | Reward |
| 3 | ##group | c,s | c,s | c,s |
| 4 | ##comment | ID | 名称 | 奖励 |
| 5 | | 1001 | 金币 | 1,100 |
| 6 | | 1002 | 钻石 | 2,50 |

### 多行数据

字段名前加 `*` 表示该字段数据跨多行：

```
##var | id | *items
##type| int| list,int
     | 1  | 100
     |    | 200
     |    | 300
     | 2  | 400
     |    | 500
```

## 列限定格式

使用多级标题头表示嵌套结构：`a.b.c`

```
##var | id | pos.x | pos.y | pos.z
##type| int| float | float | float
     | 1  | 1.0   | 2.0   | 3.0
```

## 流式格式（单单元格）

使用 `sep` 分隔符拆分单元格内容：

```xml
<bean name="vec3" sep=",">
    <var name="x" type="float"/>
    <var name="y" type="float"/>
    <var name="z" type="float"/>
</bean>
```

Excel 数据：`1,2,3` 表示 `x=1, y=2, z=3`

## 紧凑格式标记

| 标记 | 说明 |
|:---|:---|
| `#format=lite` | Luban 专有格式 |
| `#format=json` | JSON 格式 |
| `#format=lua` | Lua 格式 |

```
##var | id | data#format=json
##type| int| string
     | 1  | {"a":1,"b":2}
```

## 数据类型格式

### bool

支持：`true`, `false`, `0`, `1`, `是`, `否`（大小写不敏感）

### string

- 留空 = 空字符串
- 流式格式用 `""` 表示空串
- 加 `#escape=1` 处理转义字符

### datetime

- 支持 Excel 内置日期格式
- 支持字符串格式：`yyyy-mm-dd hh:mm:ss`

### 可空类型

- 除 datetime 外的基础类型可留空取默认值
- `int?` 类型留空或填 `null` 表示 null

## 容器类型格式

### array/list/set

**多单元格：**
```
##var | id | tags
##type| int| list,string
     | 1  | tag1  | tag2  | tag3
```

**单单元格（sep）：**
```
##var | id | tags
##type| int| list,string
     | 1  | tag1,tag2,tag3
```

**多行：**
```
##var | id | *tags
##type| int| list,string
     | 1  | tag1
     |    | tag2
     |    | tag3
```

### map

**单单元格（需两个分隔符）：**
```
##var | id | attrs
##type| int| map,int,int
     | 1  | 1:100;2:200
```

**多行：**
```
##var | id | *attrs
##type| int| map,int,int
     | 1  | 1 | 100
     |    | 2 | 200
```

## 多态类型格式

**流式格式：**
第一值指定类型名：`Circle,5` 或 `矩形,3,4`

**列限定格式：**
```
##var | id | shape.$type | shape.radius | shape.width
##type| int| string      | float        | float
     | 1  | Circle      | 5            |
     | 2  | Rectangle   |              | 3
```

## 特殊表类型

### 纵表（Vertical Table）

A1 为 `##column`：

```
##column
name    string
value   int
```

### 单例表

```xml
<table name="TbGlobalConfig" mode="one" valueType="GlobalConfig"/>
```

Excel 只需一行数据。

## 标签系统

| Tag | 作用 |
|:---|:---|
| `##` | 注释记录，永不导出 |
| `dev` | 开发数据 |
| `test` | 测试数据 |
| `unchecked` | 跳过校验器验证 |

在数据的第一列填写 tag：

```
##var | tag | id   | name
##type|     | int  | string
     |     | 1    | 正式数据
     | dev | 2    | 开发数据
     | ##  | 3    | 注释数据
```

## 变体（Variants）

```xml
<var name="price" type="int" variants="cn,en,jp"/>
```

Excel 标题：
```
##var | id | price | price@cn | price@en | price@jp
```
