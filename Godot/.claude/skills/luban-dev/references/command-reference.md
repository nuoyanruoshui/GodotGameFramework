# Luban 命令行完整参考

## 基本命令格式

```bash
dotnet <luban.dll路径> [参数]
```

## 核心参数

### 必需参数

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--conf` | - | 配置文件路径 | `--conf DataTables/luban.conf` |
| `-t` | `--target` | 生成目标 | `-t client` |

### 可选参数

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `-c` | `--codeTarget` | 代码生成目标 | `-c cs-bin` |
| `-d` | `--dataTarget` | 数据生成目标 | `-d json` |
| `-s` | `--schemaCollector` | Schema 收集器 | `-s default` |
| `-p` | `--pipeline` | 生成管线 | `-p default` |
| `-f` | `--forceLoadTableDatas` | 强制加载表数据 | `-f` |
| `-i` | `--includeTag` | 包含指定 tag | `-i dev` |
| `-e` | `--excludeTag` | 排除指定 tag | `-e test` |
| `-o` | `--outputTable` | 仅输出指定表 | `-o TbItem` |
| `-x` | `--xargs` | 扩展参数 | `-x key=value` |
| `-w` | `--watchDir` | 监视目录 | `-w DataTables` |
| `-v` | `--verbose` | 详细输出 | `-v` |
| `--variant` | - | 字段变体 | `--variant default=cn` |
| `--timeZone` | - | 目标时区 | `--timeZone Asia/Shanghai` |

## 代码目标 (codeTarget)

| 目标 | 说明 |
|------|------|
| `cs-bin` | C# + Binary 格式（ByteBuf 加载，最小体积，GGF 采用此格式） |
| `cs-simple-json` | C# + SimpleJSON（第三方轻量 JSON 库） |
| `cs-dotnet-json` | C# + System.Text.Json（.NET 6+ 内置，推荐 .NET 运行时） |
| `cs-newtonsoft-json` | C# + Newtonsoft.Json（兼容性最广） |
| `java-bin` | Java + Binary |
| `java-json` | Java + JSON |
| `go-bin` | Go + Binary |
| `go-json` | Go + JSON |
| `ts-json` | TypeScript + JSON |
| `lua` | Lua |
| `py-json` | Python + JSON |
| `cpp-bin` | C++ + Binary |
| `cpp-json` | C++ + JSON |

## 数据目标 (dataTarget)

| 目标 | 说明 |
|------|------|
| `bin` | Binary 格式 |
| `json` | JSON 格式 |
| `json2` | 紧凑 JSON |
| `lua` | Lua 格式 |
| `xml` | XML 格式 |
| `yaml` | YAML 格式 |

## xargs 扩展参数

### 输出目录

```bash
-x outputCodeDir=Assets/Scripts/Gen
-x outputDataDir=Assets/StreamingAssets/Data
```

### 多目标输出

```bash
-x cs-bin.outputCodeDir=Client/Gen
-x java-bin.outputCodeDir=Server/Gen
```

### 数据格式

```bash
-x json.compact=true        # 紧凑 JSON
-x json.indent=true         # 缩进
-x lua.useNamespace=false   # Lua 不使用命名空间
```

### 路径验证

```bash
-x pathValidator.rootDir=Assets/Resources
```

### 代码风格

```bash
-x codeStyle=upper_camel    # UpperCamelCase
-x codeStyle=lower_camel    # lowerCamelCase
-x codeStyle=snake_case     # snake_case
```

### 本地化

```bash
-x l10n.provider=default
-x l10n.textProviderName=MyTextProvider
```

### 其他

```bash
-x outputSaver=local         # 本地保存（默认）
-x outputSaver=null          # 不保存（仅校验）
-x code.lineEnding=LF        # 换行符: CR/LF/CRLF
-x data.lineEnding=LF
```

## 变体参数

```bash
# 指定字段变体
--variant item.ItemCfg.price=cn

# 全局默认变体
--variant default=en

# 多字段变体
--variant item.ItemCfg.price=cn --variant item.ItemCfg.name=cn
```

## 常用命令示例

### Godot 项目

```bash
# 生成 C# + JSON（GGF 实际使用 cs-bin + bin，见 ggf-integration.md 与项目脚本）
dotnet Luban.dll \
  -t all \
  -c cs-simple-json \
  -d json \
  --conf DataTables/luban.conf \
  -x cs-simple-json.outputCodeDir=Assets/Scripts/Gen \
  -x json.outputDataDir=Assets/StreamingAssets/Data
```

### 仅校验配置

```bash
dotnet Luban.dll \
  -t all \
  --conf DataTables/luban.conf \
  -f \
  -x outputSaver=null
```

### 生成多平台代码

```bash
dotnet Luban.dll \
  -t all \
  -c cs-bin \
  -c java-bin \
  -d bin \
  --conf DataTables/luban.conf \
  -x cs-bin.outputCodeDir=Client/Gen \
  -x java-bin.outputCodeDir=Server/Gen \
  -x bin.outputDataDir=Server/Data
```

### 排除测试数据

```bash
dotnet Luban.dll \
  -t all \
  -c cs-bin \
  -d bin \
  --conf DataTables/luban.conf \
  -e test,dev
```

### 仅生成指定表

```bash
dotnet Luban.dll \
  -t all \
  -c cs-bin \
  -d json \
  --conf DataTables/luban.conf \
  -o TbItem \
  -o TbEquip
```

### 监视模式

```bash
dotnet Luban.dll \
  -t all \
  -c cs-bin \
  -d json \
  --conf DataTables/luban.conf \
  -w DataTables
```

## 完整参数列表示例

```bash
dotnet Luban.dll \
  --conf DataTables/luban.conf \
  -t client \
  -c cs-simple-json \
  -d json \
  -s default \
  -p default \
  -f \
  -e test,dev \
  --variant item.ItemCfg.price=cn \
  -x cs-simple-json.outputCodeDir=Assets/Scripts/Gen \
  -x json.outputDataDir=Assets/StreamingAssets/Data \
  -x pathValidator.rootDir=Assets/Resources \
  -x json.compact=true \
  -v
```
