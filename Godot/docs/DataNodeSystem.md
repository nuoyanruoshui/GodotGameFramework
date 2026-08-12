# 数据结点系统 (DataNode Module)

> 适用版本：Godot 4.7 + .NET 8 ｜ 对应代码：`Framework/GameFramework/DataNode/`、`Framework/GodotGameFrameworkCore/DataNode/`、`Framework/GodotGameFrameworkCore/Variable/`
> 本文档描述 GGF 的数据结点系统：树形运行时数据结构、路径式读写 API、Variable 包装类型与典型用法。

---

## 1. 概述

数据结点系统是 [Game Framework](https://gameframework.cn/) DataNode 模块的 Godot 移植，提供一棵**全局的树形键值存储**，用于在系统间共享运行时数据（玩家临时状态、流程间传值等），避免到处散落静态变量。

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/DataNode/` | 树结构、路径解析、结点池化、数据读写 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/DataNode/` | `DataNodeComponent`：组件封装，全量透传 API | ✅（仅组件基类） |
| Variable 类型 | `GameFramework/Base/Variable/` + `GodotGameFrameworkCore/Variable/` | `Variable`/`Variable<T>` 基类 + `VarInt32/VarString/VarBoolean/VarSingle` 包装 | ❌ |

### 能力清单

- ✅ 树形结构，根结点 `<Root>`，任意深度
- ✅ 路径访问：`.`、`/`、`\` 三种分隔符等价（`"Player.Score"` ≡ `"Player/Score"`）
- ✅ `SetData` 自动逐级创建缺失结点（`GetOrAddNode` 语义）
- ✅ 数据统一为 `Variable` 包装，结点与数据均走 `ReferencePool` 池化
- ✅ 隐式转换：`VarInt32 v = 100;` / `int i = v;`

---

## 2. 架构与数据流

```
调用方（业务代码）
    │  GF.DataNode.SetData("Player.Score", (VarInt32)100)
    ▼
DataNodeComponent (Godot 桥接层，场景节点 "DataNode")
    │  全量透传（无附加逻辑）
    ▼
DataNodeManager : GameFrameworkModule (纯 C# 层)
    │  路径切分（'.' '/' '\'，RemoveEmptyEntries）
    ▼
DataNode（内部类，IDataNode + IReference）
    <Root>
      └── Player            ← GetOrAddChild 自动创建
            └── Score       ← m_Data = VarInt32(100)
```

- 结点创建：`DataNode.Create(name, parent)` 从 `ReferencePool.Acquire<DataNode>()` 取用
- 结点销毁（`RemoveNode`/`Clear`/模块 `Shutdown`）：递归将子结点与挂载的 `Variable` 数据一并 `ReferencePool.Release`
- `SetData` 覆盖旧数据时，旧 `Variable` 自动归还引用池

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/DataNode/IDataNodeManager.cs` | 管理器接口 |
| `GameFramework/DataNode/DataNodeManager.cs` | 路径解析、GetData/SetData/GetNode/GetOrAddNode/RemoveNode/Clear |
| `GameFramework/DataNode/DataNodeManager.DataNode.cs` | 结点实现：子结点字典、数据挂载、名称校验、池化回收 |
| `GameFramework/DataNode/IDataNode.cs` | 结点接口（见 §4.2） |
| `GameFramework/Base/Variable/Variable.cs` | 抽象基类（`IReference`，`Type`/`GetValue`/`SetValue`/`Clear`） |
| `GameFramework/Base/Variable/GenericVariable.cs` | `Variable<T>`：强类型 `Value` 属性 |
| `GodotGameFrameworkCore/Variable/VarInt32.cs` 等 | `int/float/bool/string` 四个包装类型（隐式转换 + 池化） |
| `GodotGameFrameworkCore/DataNode/DataNodeComponent.cs` | 组件封装（`GF.DataNode`） |

---

## 3. 核心机制

### 3.1 路径解析

- 分隔符：`.`、`/`、`\` 均可混用，空段自动忽略（`StringSplitOptions.RemoveEmptyEntries`）
- 所有路径 API 都有 `(path)` 与 `(path, IDataNode node)` 两种重载：后者以 `node` 为起点做**相对查找**；前者从根结点开始
- 结点名称合法性（`IsValidName`）：非空且**不含任何分隔符字符**，非法名称创建结点会抛 `GameFrameworkException`

### 3.2 读写语义差异（重要）

| API | 结点不存在时的行为 |
|-----|-------------------|
| `GetData / GetData<T>` | **抛 `GameFrameworkException`** |
| `GetNode` | 返回 `null` |
| `SetData` | 自动逐级创建结点（`GetOrAddNode`） |
| `GetOrAddNode` | 自动逐级创建结点 |
| `RemoveNode` | 静默返回 |

安全读取模式：先 `GetNode(path)` 判空，或确保写在前读在后。

### 3.3 Variable 池化与隐式转换

四个包装类型实现相同模式（以 `VarInt32` 为例）：

```csharp
public static implicit operator VarInt32(int value)   // int → VarInt32：内部 ReferencePool.Acquire
public static implicit operator int(VarInt32 value)   // VarInt32 → int：取 Value
```

- **写入**：`GF.DataNode.SetData("X", (VarInt32)100)` —— 隐式转换已从引用池取对象，交给结点后由结点负责生命周期
- **释放**：结点被移除 / 数据被覆盖 / `Clear()` 时，框架自动 `ReferencePool.Release`。**调用方不要手动 Release 已交给 DataNode 的 Variable**
- 可用类型：`VarInt32`(int)、`VarSingle`(float)、`VarString`(string)、`VarBoolean`(bool)；需要其他类型时按同一模式继承 `Variable<T>` 自定义

---

## 4. 组件与 API

场景节点：`Framework/GameFramework.tscn` 中的 `DataNode` 节点，经 `GF.DataNode`（`DataNodeComponent`）访问。无 Inspector 参数。

### 4.1 DataNodeComponent 方法总览

```csharp
GF.DataNode.Root                                  // IDataNode 根结点

// 数据读写（Variable）
T    GF.DataNode.GetData<T>(path)                 // T : Variable，结点不存在抛异常
Variable GF.DataNode.GetData(path)
void GF.DataNode.SetData<T>(path, T data)         // 自动创建路径
void GF.DataNode.SetData(path, Variable data)
// 以上均有 (path, IDataNode node) 相对查找重载

// 结点操作
IDataNode GF.DataNode.GetNode(path)               // 不存在返回 null
IDataNode GF.DataNode.GetOrAddNode(path)          // 不存在则创建
void      GF.DataNode.RemoveNode(path)
void      GF.DataNode.Clear()                     // 清空整棵树
```

### 4.2 IDataNode 结点接口

```csharp
string Name / string FullName / IDataNode Parent / int ChildCount

T GetData<T>() / Variable GetData()
void SetData<T>(T data) / void SetData(Variable data)

bool HasChild(int index) / bool HasChild(string name)
IDataNode GetChild(int index) / IDataNode GetChild(string name)
IDataNode GetOrAddChild(string name)
IDataNode[] GetAllChild() / void GetAllChild(List<IDataNode> results)
void RemoveChild(int index) / void RemoveChild(string name)
void Clear()                      // 清除本结点数据与所有子结点
string ToDataString()             // "[类型名] 值"，无数据时 "<Null>"
```

### 4.3 使用示例

```csharp
// 写入（路径自动创建）
GF.DataNode.SetData("Player.Name", (VarString)"喵喵");
GF.DataNode.SetData("Player.Score", (VarInt32)100);
GF.DataNode.SetData("Player.IsAlive", (VarBoolean)true);

// 读取
int score = GF.DataNode.GetData<VarInt32>("Player.Score");     // 隐式转换取值
string name = GF.DataNode.GetData<VarString>("Player.Name").Value;

// 安全读取（可能未写入过）
IDataNode node = GF.DataNode.GetNode("Player.Score");
int safeScore = node != null ? (VarInt32)node.GetData<VarInt32>() : 0;

// 相对查找：以 Player 结点为起点批量操作
IDataNode player = GF.DataNode.GetOrAddNode("Player");
GF.DataNode.SetData("Hp", (VarInt32)50, player);               // 实际路径 Player.Hp

// 清理
GF.DataNode.RemoveNode("Player");    // 递归回收 Player 及其子树、所有 Variable
```

---

## 5. 注意事项 / FAQ

**Q: DataNode 的数据会持久化吗？**
不会。DataNode 是**纯内存**运行时共享数据，进程退出即消失；模块 `Shutdown` 时整棵树归还引用池。需要持久化用 `GF.Setting`（见 `SettingSystem.md`）。

**Q: 和 GF.Setting 怎么分工？**
DataNode = 运行期跨系统共享的易变数据（无 IO）；Setting = 需要落盘的玩家设置/存档类数据（`user://settings.cfg`）。

**Q: `GetData` 为什么抛异常而不是返回 null？**
原版 GF 语义：读取不存在的路径视为逻辑错误。不确定是否写入过时先 `GetNode` 判空。

**Q: 从 DataNode 取出的 Variable 能长期持有吗？**
不建议。该对象生命周期归结点管理，`SetData` 覆盖或 `RemoveNode` 时会被归还引用池并复用，持有者会读到脏数据。取出后立即拆箱为原生类型（利用隐式转换）。

**Q: 结点名能用中文吗？路径大小写敏感吗？**
名称只要非空且不含 `.`/`/`/`\` 即合法（中文可用）；内部为 `Dictionary<string, DataNode>`，**大小写敏感**。

**Q: TheGame 里为什么搜不到实际用例？**
当前示例游戏仅在 `ProcedureLaunch` 中校验组件存在（`GF.DataNode != null`），尚无业务使用——模块处于"可用但未消费"状态，本文示例为按 API 编写的推荐用法。
