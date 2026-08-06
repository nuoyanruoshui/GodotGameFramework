# 设置系统 (Setting Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Setting/`、`Framework/GodotGameFrameworkCore/Setting/`
> 本文档描述 GGF 的游戏设置系统：键值持久化、存储介质与路径、GetXxx/SetXxx/Save API 与注意事项。

---

## 1. 概述

设置系统是 [Game Framework](https://gameframework.cn/) Setting 模块的 Godot 移植，提供**键值对形式的玩家数据持久化**（音量、语言、分数存档等），底层由可替换的辅助器（Helper）决定存储介质。

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Setting/` | `ISettingManager`/`SettingManager`：API 定义、参数校验、委托辅助器、关闭时自动保存 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Setting/` | `SettingComponent`（组件封装）+ `DefaultSettingHelper`（**Godot ConfigFile** 实现） | ✅ |

**存储介质（默认辅助器）**：Godot `ConfigFile`（INI 风格文本），固定写入 **`user://Settings.cfg`**，所有键放在固定 Section `[Settings]` 下。

`user://` 实际磁盘位置（Godot 约定）：

| 平台 | 路径 |
|------|------|
| Windows | `%APPDATA%/Godot/app_userdata/[项目名]/` |
| Linux | `~/.local/share/godot/app_userdata/[项目名]/` |
| macOS | `~/Library/Application Support/Godot/app_userdata/[项目名]/` |

### 能力清单

- ✅ `bool / int / float / string` 四种基础类型 + 任意对象（JSON 序列化）
- ✅ 全部 GetXxx 支持默认值重载
- ✅ 启动自动 Load，模块关闭自动 Save（进程正常退出不丢数据）
- ✅ 辅助器可替换（Inspector 配类型名，反射创建）
- ✅ 文件不存在视为空配置，不报错

---

## 2. 架构与数据流

```
调用方（业务代码）
    │  GF.Setting.SetInt("Score", 100); GF.Setting.Save();
    ▼
SettingComponent (Godot 桥接层，场景节点 "Setting")
    │  OnInit: 反射创建 Helper → SetSettingHelper → Load()
    ▼
SettingManager : GameFrameworkModule (纯 C# 层)
    │  参数校验 + 委托；Shutdown() 时自动 Save()
    ▼
ISettingHelper（存储抽象）
    ▲ 实现
DefaultSettingHelper (Godot 桥接层)
    └── Godot ConfigFile ──读写──► user://Settings.cfg
            [Settings]
            Score=100
            MusicVolume=0.8
```

写入路径：`SetXxx` 只改**内存中的 ConfigFile**；`Save()` 才落盘。读取路径：启动时 `Load()` 一次性读入，之后 `GetXxx` 全部走内存。

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Setting/ISettingManager.cs` | 管理器接口 |
| `GameFramework/Setting/ISettingHelper.cs` | 存储辅助器抽象（Load/Save/GetXxx/SetXxx/Remove…） |
| `GameFramework/Setting/SettingManager.cs` | 校验 + 委托辅助器；`Shutdown()` 自动 `Save()` |
| `GodotGameFrameworkCore/Setting/SettingComponent.cs` | 组件封装（`GF.Setting`），反射创建辅助器并 `Load()` |
| `GodotGameFrameworkCore/Setting/DefaultSettingHelper.cs` | ConfigFile 实现：`user://Settings.cfg`、Section `Settings`、对象走 JSON |

---

## 3. 核心机制

### 3.1 生命周期与自动保存

| 时机 | 行为 |
|------|------|
| 组件 `OnInit`（启动） | 反射创建辅助器 → `Load()` 读入 `user://Settings.cfg`；**文件不存在返回成功（空配置）**，其他错误仅 `Log.Warning` |
| 运行期 `SetXxx` | 只写内存，**不落盘** |
| 手动 `Save()` | 立即写盘（Godot 自动创建目录） |
| 模块 `Shutdown`（正常退出） | `SettingManager.Shutdown()` 自动调用一次 `Save()` |

> ⚠️ **崩溃/强杀不会触发 Shutdown 保存**。关键数据（如分数存档）修改后应立即手动 `Save()`——TheGame 的实际写法（`AngerEntity.cs`）：
> ```csharp
> GF.Setting.SetInt("Score", GF.Setting.GetInt("Score", 0) + 100);
> GF.Setting.Save();
> ```

### 3.2 对象存储（JSON）

`SetObject/GetObject` 将对象经 `Utility.Json`（Newtonsoft.Json）序列化为字符串后按 string 存储：

```csharp
GF.Setting.SetObject("PlayerProfile", new PlayerProfile { Level = 3 });
PlayerProfile p = GF.Setting.GetObject<PlayerProfile>("PlayerProfile", new PlayerProfile());
```

对象最终以 JSON 字符串形式存在 `Settings.cfg` 的对应键下。

### 3.3 辅助器替换

`SettingComponent` 的 Inspector 参数：

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `m_SettingHelperTypeName` | `GodotGameFramework.Setting.DefaultSettingHelper` | 辅助器类型全名，`Activator.CreateInstance` 反射创建 |

需要加密存档 / SQLite / 云存档时：实现 `ISettingHelper` 全部成员，在场景 `Setting` 节点上改类型名即可，业务代码零改动。

---

## 4. 组件与 API

场景节点：`Framework/GameFramework.tscn` 中的 `Setting` 节点，经 `GF.Setting`（`SettingComponent`）访问。

```csharp
// 元操作
GF.Setting.Count                          // 配置项数量
GF.Setting.Save()                         // 落盘
string[] names = GF.Setting.GetAllSettingNames();
GF.Setting.GetAllSettingNames(List<string> results);
bool has = GF.Setting.HasSetting("Score");
GF.Setting.RemoveSetting("Score");        // 仅内存，需 Save() 生效
GF.Setting.RemoveAllSettings();

// 基础类型（每种均有带默认值重载；无默认值版本在键不存在时
// 由 ConfigFile 返回默认 Variant，建议总是用带默认值的重载）
bool  b = GF.Setting.GetBool("Mute", false);        GF.Setting.SetBool("Mute", true);
int   i = GF.Setting.GetInt("Score", 0);            GF.Setting.SetInt("Score", 100);
float f = GF.Setting.GetFloat("Volume", 1f);        GF.Setting.SetFloat("Volume", 0.8f);
string s = GF.Setting.GetString("Name", "Guest");   GF.Setting.SetString("Name", "喵");

// 对象（JSON）
T obj = GF.Setting.GetObject<T>("Key", defaultObj);
object obj2 = GF.Setting.GetObject(typeof(T), "Key", defaultObj);
GF.Setting.SetObject("Key", obj);
```

---

## 5. 注意事项 / FAQ

**Q: 为什么改了设置重启后丢了？**
`SetXxx` 只写内存。正常退出（走模块 Shutdown）会自动保存，但调试中强停进程/崩溃不会。重要修改后手动 `GF.Setting.Save()`。

**Q: 存储文件长什么样？能手改吗？**
`user://Settings.cfg` 是 Godot ConfigFile 文本（INI 风格，单节 `[Settings]`），可用文本编辑器直接查看/修改，便于调试。**没有加密与防篡改**——敏感数据需自定义辅助器。

**Q: 和 Godot 原生 ConfigFile / user:// 是什么关系？**
默认辅助器**就是** ConfigFile + `user://` 的薄封装；框架价值在于统一 API、默认值重载、对象 JSON 存储、启动/关闭的自动 Load/Save，以及底层介质可整体替换。

**Q: 和 `EasySave`（`GodotGameFrameworkCore/Json/EasySave.cs`）什么区别？**
两者**无关**。EasySave 是独立的轻量 JSON 文件工具（`TrySave/LoadOrDefault` + `user://`/`res://` 路径便捷方法），供热更流程存取 `GameFrameworkVersion.dat` 清单及 Download 模块路径转换使用；Setting 是面向"玩家设置/小型存档"的键值系统。一个键值对走 Setting，一份完整 JSON 文档（如版本清单）走 EasySave。

**Q: 线程安全吗？**
不保证。Godot ConfigFile 与组件均按主线程使用设计，请勿在后台线程调用 `GF.Setting`。

**Q: `GetInt("Key")`（无默认值）在键不存在时会怎样？**
`DefaultSettingHelper` 直接 `ConfigFile.GetValue(...)`，键不存在时 Godot 返回空 Variant（转换为 0/false/空串），不会抛异常——但语义不明确，**建议永远使用带默认值的重载**。

---

## 6. 已知边界与后续计划

- [ ] 存档加密辅助器（当前明文）
- [ ] Web 导出平台的 `user://`（IndexedDB）写入时机验证
