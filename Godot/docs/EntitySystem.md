# 实体系统 (Entity Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Entity/`、`Framework/GodotGameFrameworkCore/Entity/`、`TheGame/GameScripts/Entity/`（含 CatEntity FSM 状态机示例）、`TheGame/MainPack/Scripts/Resources/`
> 本文档描述 GGF 的实体系统：生命周期、实体组与对象池、配置驱动的显示/隐藏 API、TheGame 实体继承树与新增实体步骤。

---

## 1. 概述

实体系统是 [Game Framework](https://gameframework.cn/) Entity 模块的 Godot 移植，管理游戏中所有动态对象（角色、敌人、子弹等）的创建、显示、隐藏、父子关系与池化复用。遵循框架**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Entity/` | `EntityManager`：实体组、状态机、对象池调度、父子关系、事件；`IEntity` / `IEntityHelper` / `IEntityGroupHelper` 抽象 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Entity/` | `EntityComponent` 组件封装 + 可 await API、`DefaultEntityHelper`（PackedScene 实例化 / QueueFree）、`EntityGroupHelperBase : Node` 容器节点、`EntityExtension` 配置驱动扩展 | ✅ |

**与原版 Game Framework 的关键差异**：Unity 版有 `Entity` MonoBehaviour 包装 + `EntityLogic` 分离；GGF 中**实体脚本直接继承 Godot 节点类型并实现 `IEntity`**（如 `ActorEntity : CharacterBody2D, IEntity`、`GanTanEntity : Area2D, IEntity`），没有中间包装层，也没有抽象实体基类库——每个游戏项目自行定义基类（TheGame 的 `ActorEntity`）。

### 能力清单

- ✅ 实体组（EntityGroup）+ 每组独立实例对象池（容量/过期/优先级/自动释放）
- ✅ 配置驱动显示：`EntityId` 枚举 → Luban `TbEntityConfig` → 场景路径 + 实体组
- ✅ 同步 `ShowEntity` / 可 await `ShowEntityAsync<T>`（TCS 桥接成功/失败事件）
- ✅ 完整生命周期：`OnInit → OnShow → OnUpdate(每帧) → OnHide → OnRecycle`
- ✅ 父子实体（Attach/Detach，同步 Godot 场景树父子关系）
- ✅ 全局事件转发（ShowEntitySuccess/Failure/Update、HideEntityComplete，Inspector 可开关。Godot 自动管理资源依赖，无 DependencyAsset 事件）
- ✅ 实体脚本 Ge/Logic 双文件生成（`ScriptGenerateInspector`，Node2D/Node3D 节点适用）

---

## 2. 架构与数据流

```
调用方（CatEntity.SpawnGanTan / ProcedureGame 等）
    │  GF.Entity.ShowEntityAsync<GanTanEntity>(EntityId.GanTan, userData)
    ▼
EntityExtension（配置解析 + 自增序列号）
    │  TbEntityConfig[EntityId] → AssetPath / EntityGroupName
    ▼
EntityComponent (Godot 桥接层，场景节点 "Entity")
    │  委托 + TaskCompletionSource(entityId → TCS)        ▲ C# 事件
    ▼                                                     │
EntityManager : GameFrameworkModule (纯 C# 层) ───────────┘
    │  ShowEntity(id, assetPath, groupName, priority, userData)
    ▼
EntityGroup.SpawnEntityInstanceObject(assetPath)   ← 查实体实例对象池
    ├─ 命中 → InternalShowEntity(isNewInstance:false)     [同帧完成]
    └─ 未命中 → IResourceManager.LoadAsset(assetPath)     [异步]
              → 成功: IEntityHelper.InstantiateEntity(PackedScene.Instantiate)
                      → 池 Register(spawned:true)
                      → InternalShowEntity(isNewInstance:true)

InternalShowEntity：
    CreateEntity(挂到组容器节点) → OnInit → 加入组链表 → OnShow → ShowEntitySuccess 事件

HideEntity：
    递归隐藏子实体 → Detach → OnHide → 移出组 → HideEntityComplete 事件 → 入回收队列
    └─ 下一帧 EntityManager.Update()：OnRecycle → 对象池 Unspawn（节点隐藏保留，待复用）
    └─ 池过期释放时：IEntityHelper.ReleaseEntity → Node.QueueFree()
```

每帧驱动：`EntityManager.Update()` 先清空回收队列，再遍历所有实体组，对组内每个已显示实体调用 `OnUpdate(elapseSeconds, realElapseSeconds)` —— **实体不需要 Godot 的 `_Process`，逻辑写在 `OnUpdate` 即可**。

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Entity/IEntity.cs` | 实体接口（9 个生命周期方法 + Id/EntityAssetName/Handle/EntityGroup） |
| `GameFramework/Entity/IEntityManager.cs` | 管理器接口（Show/Hide/查询/父子/实体组） |
| `GameFramework/Entity/EntityManager.cs` | 核心实现：状态机、回收队列、加载回调 |
| `GameFramework/Entity/EntityManager.EntityGroup.cs` | 实体组：实体链表 + 每组 `IObjectPool<EntityInstanceObject>` |
| `GameFramework/Entity/EntityManager.EntityInstanceObject.cs` | 池化对象（`ObjectBase`），`Release` 时调 Helper 销毁节点 |
| `GameFramework/Entity/EntityManager.EntityInfo.cs` / `.EntityStatus.cs` | 实体登记信息 + 状态枚举（WillInit→Inited→WillShow→Showed→WillHide→Hidden→WillRecycle→Recycled） |
| `GameFramework/Entity/Show/Hide*EventArgs.cs` | 管理器层事件参数（池化） |
| `GodotGameFrameworkCore/Entity/EntityComponent.cs` | 组件封装、事件转发、`ShowEntityAsync`、Attach/Detach 的 Node 重挂 |
| `GodotGameFrameworkCore/Entity/EntityExtension.cs` | `EntityId` 配置驱动扩展 + `HideEntitySafe` |
| `GodotGameFrameworkCore/Entity/DefaultEntityHelper.cs` | PackedScene 实例化 / 挂组容器 / QueueFree |
| `GodotGameFrameworkCore/Entity/EntityGroupHelperBase.cs` / `DefaultEntityGroupHelper.cs` | 实体组容器节点（每组一个 Node，挂在 EntityComponent 下） |
| `TheGame/GameScripts/Entity/*.cs` | 游戏实体：ActorEntity / CatEntity / AngerEntity / GanTanEntity.Logic |
| `TheGame/GameScripts/GameProto/EntityGe/*.cs` | 生成的实体 Ge 文件（会被覆盖，勿手改） |
| `TheGame/MainPack/Scripts/Resources/EntityGroup.cs` / `EntityGroupRes.cs` | 实体组配置资源（`[GlobalClass]`） |

---

## 3. 核心机制

### 3.1 IEntity 生命周期

```csharp
void OnInit(int entityId, string entityAssetName, IEntityGroup entityGroup,
            bool isNewInstance, object userData);   // 创建/复用时。isNewInstance=false 表示来自对象池
void OnShow(object userData);                       // 每次显示（含复用）。userData 透传自 ShowEntity
void OnUpdate(float elapseSeconds, float realElapseSeconds);  // 每帧（仅 Showed 状态）
void OnHide(bool isShutdown, object userData);      // 隐藏
void OnRecycle();                                   // 回收进池（下一帧执行）。重置状态，节点不销毁
void OnAttached/OnDetached/OnAttachTo/OnDetachFrom(IEntity other, object userData);  // 父子关系
```

编写约定（来自 `GanTanEntity` 实践）：

- **一次性初始化放 `isNewInstance` 分支**（如信号订阅 `BodyEntered += ...`），否则池复用会重复订阅
- **可变状态在 `OnShow` 里重置**（复用实例带着上次的脏状态回来）
- `OnRecycle` 后 `Id == 0` 但 Godot 信号仍可能触发——用 `m_IsDead` 之类标志防止 HideEntity 后再次操作
- ⚠️ **userData 必须原样透传**：纯层管理器把 `ShowEntity(..., userData)` 的 userData **原样**交给 `entity.OnShow(userData)`，中间没有任何解包层。`EntityExtension` 等封装**不得**把 userData 包进 `ShowEntityInfo` 之类的包装对象——否则实体侧 `userData is BulletData` 这类判断会静默失败（2026-07 曾因此导致子弹方向失效），且池化包装对象无人归还

### 3.2 实体组与对象池

每个实体组持有一个**单次获取对象池**（`CreateSingleSpawnObjectPool<EntityInstanceObject>`，池名 `Entity Instance Pool ({组名})`），以**资源路径**为对象名——同组内不同场景的实例互不混用。池参数（容量/过期秒数/优先级/自动释放间隔）来自组注册参数。详见 `ObjectPoolSystem.md`。

组注册发生在 `ProcedurePrelode.LoadEntityGroup()`：读取 `EntityComponent` Inspector 上挂的 `EntityGroupRes`（`[GlobalClass]` 资源）：

```csharp
public partial class EntityGroup : Resource {   // TheGame/MainPack/Scripts/Resources/EntityGroup.cs
    [Export] public string Name;              // 组名
    [Export] public float ReleaseInterval;    // 池自动释放间隔（秒）
    [Export] public int Capacity;             // 池容量
    [Export] public float ExpireTime;         // 对象过期秒数
    [Export] public int Priority;             // 池优先级
}
```

每组注册时创建一个 `DefaultEntityGroupHelper : Node` 容器节点（名为 `{HelperType}-{组名}`）挂在 EntityComponent 下——**组内所有实体节点都是它的子节点**，场景树层级即实体归属。

### 3.3 配置驱动（EntityId → 场景）

Luban 表 `TbEntityConfig`（Excel 源：`Configs/GameConfig/Datas/实体.xlsx`）行结构：

| 字段 | 类型 | 说明 |
|------|------|------|
| `Id` | int | 表内主键 |
| `EntityId` | enum `GameConfig.Entity.EntityId` | Cat=0 / GanTan=1 / Anger=2 / LightningBall=3 |
| `AssetPath` | string | `.tscn` 场景路径 |
| `EntityGroupName` | string | 所属实体组 |
| `Priority` | int | 加载优先级（⚠️ 扩展方法当前未传递此值，实际恒用默认 0） |

`EntityExtension` 用 `Interlocked.Increment` 维护**全局自增实体编号**（调用方拿返回值/实体引用即可，无需自己管 id）。

### 3.4 父子实体与 Godot 场景树同步

`EntityComponent.AttachEntity` 在委托纯 C# 层记账之外，还把子实体 Node **重挂到父实体 Node 下**；`DetachEntity` 把子节点**移回其实体组容器节点**。隐藏父实体会先递归隐藏全部子实体。

### 3.5 异步显示与事件

`ShowEntityAsync` 用 `Dictionary<int, TaskCompletionSource<IEntity>>`（按实体编号）桥接 `ShowEntitySuccess/Failure` 事件；失败时 Task 以 `GameFrameworkException(ErrorMessage)` 完结。管理器事件同时经 `EventComponent` 全局转发（Inspector 四个开关控制），事件参数为池化对象，**回调返回后即回收，不可持有**。✅（2026-07）转发时按事件约定**复制**参数（纯层全参 `Create(...)` 或 Godot 层包装 `Create(e)`），不再直接转发管理器实例（详见 `EventSystem.md` §3.4 双重归还说明）。

加载中途 `HideEntity`：不会取消资源加载，而是记入 `m_EntitiesToReleaseOnLoad`，资源到位后直接释放不显示。

---

## 4. EntityComponent

场景节点：`Framework/GameFramework.tscn` 中的 `Entity` 节点，经 `GF.Entity` 访问。

### 4.1 Inspector 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `m_EnableShowEntitySuccessEvent` 等 4 个开关（Success/Failure/HideComplete 开，Update 关） | [Export] 配置 | 是否向全局 EventComponent 转发对应事件。Godot 自动管理依赖，无 DependencyAsset。⚠️ Success 开关同时控制 `ShowEntitySuccess` 的订阅，而 `ShowEntityAsync` 的 TCS 结算在同一订阅回调里——**关闭 Success 开关会导致 `ShowEntityAsync` 永不完结**（Failure 恒订阅不受此影响） |
| `m_EntityHelperTypeName` | `GodotGameFramework.Entity.DefaultEntityHelper` | 实体辅助器类型（Inspector 下拉可选自定义实现） |
| `m_EntityGroupHelperTypeName` | `GodotGameFramework.Entity.DefaultEntityGroupHelper` | 实体组辅助器类型 |
| `EntityGroupRes` | — | 实体组定义资源（`ProcedurePrelode` 读取注册） |

### 4.2 API 总览

```csharp
// 显示（推荐：配置驱动扩展）
int id = GF.Entity.ShowEntity(EntityId.Cat, userData);            // 返回自增实体编号，-1 = 配置缺失
int id = GF.Entity.ShowEntity(assetPath, groupName, userData);    // 直接路径版
T e  = await GF.Entity.ShowEntityAsync<T>(EntityId.GanTan, userData);  // T : Node, IEntity
IEntity e = await GF.Entity.ShowEntityAsync(EntityId.Anger, userData);

// 显示（底层：自管 id）
GF.Entity.ShowEntity(entityId, assetPath, groupName, priority, userData);
await GF.Entity.ShowEntityAsync(entityId, assetPath, groupName, userData);

// 隐藏
GF.Entity.HideEntity(entityId / entity, userData);   // 实体不存在会抛异常
GF.Entity.HideEntitySafe(entityId / entity);         // 先查存在性，适合"可能已死"的场景
GF.Entity.HideAllLoadedEntities();
GF.Entity.HideAllLoadingEntities();

// 查询
GF.Entity.HasEntity(id | assetPath);
GF.Entity.GetEntity(id | assetPath);   GF.Entity.GetEntities(assetPath);
GF.Entity.GetAllLoadedEntities();      GF.Entity.IsValidEntity(entity);
GF.Entity.IsLoadingEntity(id);         GF.Entity.GetAllLoadingEntityIds();

// 实体组
GF.Entity.AddEntityGroup(name, releaseInterval, capacity, expireTime, priority);
GF.Entity.HasEntityGroup(name);  GF.Entity.GetEntityGroup(name);  GF.Entity.GetAllEntityGroups();

// 父子
GF.Entity.AttachEntity(childId, parentId, userData);   // 同步 Node 父子关系
GF.Entity.DetachEntity(childId);                       // 子节点移回组容器
GF.Entity.GetParentEntity(childId);  GF.Entity.GetChildEntities(parentId);
```

### 4.3 使用示例（TheGame 实战）

```csharp
// CatEntity 发射子弹：配置驱动 + userData 传参 + await 拿实例
var entity = await GF.Entity.ShowEntityAsync<GanTanEntity>(EntityId.GanTan,
    new BulletData { Direction = dir, IsPlayerBullet = true, Speed = 300f });
if (entity != null)
    entity.Position = Position;    // Success 后可直接操作节点

// GanTanEntity 命中后自杀（实体内部）
GF.Entity.HideEntity(this);        // → OnHide → OnRecycle → 回池，节点不销毁
```

---

## 5. TheGame 实体继承树

```
Godot CharacterBody2D                     Godot Area2D                             Godot Node2D
    └── ActorEntity (IEntity, IActor)         ├── GanTanEntity (IEntity)  ← 子弹     └── DropItem (IPoolable) ← 掉落物
         ├── CatEntity    ← 玩家猫            └── LightningBall (IEntity) ← 电球
         └── AngerEntity  ← 敌人
                （CatEntity 内置 Fsm<CatEntity>：IdleState/MoveState）
```

### ActorEntity（角色基类，手写）

`TheGame/GameScripts/Entity/ActorEntity.cs` —— 战斗角色的公共职责：

- **IEntity 样板**：Id/EntityAssetName/Handle/EntityGroup + 全部生命周期虚方法（`OnRecycle` 重置 Id/位置/速度并隐藏）
- **Anim 属性**：`public AnimatedSprite2D Anim { get; private set; }`，在 `OnInit(isNewInstance)` 中通过 `this.GetChild<AnimatedSprite2D>()` 自动查找子节点；子类直接使用 `Anim.Play("Idle")` 等，无需各自声明 `[Export]` 字段
- **ActorData**（struct）：`Hp / MaxHp`，`IsDead => Hp <= 0`；`Hurt(attackerId, damage)` / `Heal(heal)`
- **EntityTeam**：`Player / Enemy` 阵营，子弹据此判定敌我
- **CharacterConfig m_Config**：Luban `TbCharacterConfig`（移速/攻速/索敌半径），子类在 `isNewInstance` 时按自身 `EntityId` 查表
- **PhysicsCheck2D m_Check**：池化的物理区域检测（`ReferencePool`），`_ExitTree` 时归还；`_Draw` 画调试圈（仅 TOOLS）
- **`Die()`**：默认 `GF.Entity.HideEntity(this)`，子类扩展（AngerEntity 死亡加积分）
- 引入了 `GameFramework.Fsm` 命名空间（供子类 CatEntity 的 FSM 使用）

### 各实体职责

| 实体 | 基类 | 要点 |
|------|------|------|
| `CatEntity` | ActorEntity | 键盘移动（`ui_left/right/up/down`）、`m_IsMoving`（`public bool`）驱动 FSM 状态切换（`IdleState` ↔ `MoveState`，OnEnter 播放 Idle/Walk 动画并 FlipH）；`PhysicsCheck2D` 圆形索敌取最近敌人方向、按 `AtkSpeed` 冷却发射子弹（`GanTan` / `LightningBall`）；`IActor` 接口定义于此文件 |
| `AngerEntity` | ActorEntity | 追击目标玩家（范围外靠近/范围内射击）、`HSlider` 血条；不再持有私有 `m_Anim` 字段，改用基类 `Anim` 属性；`OnShow` 中 `Anim.Play("Idle")`；`Die()` 经 `GF.Archive` 加 100 分 + 掉落 `DropItem`（NodePool） |
| `GanTanEntity` | Area2D | `BulletData`（方向/速度/敌我）经 userData 注入；`BodyEntered` 命中 `ActorEntity` 按阵营伤害后自隐藏；8 秒超时自毁；`m_IsDead` 防复用期重复触发 |
| `LightningBall` | Area2D | 电球子弹（与 GanTanEntity 并行，不同外观/速度），BodyEntered 命中后自销毁、穿透多个目标 |
| `DropItem` | Node2D | 掉落物（实现 `IPoolable`），经 NodePool 管理；`DOMove` + GTween 完成回调自动归还池 |

---

## 6. 新增实体完整步骤

1. **建场景**：`TheGame/Entitys/` 下创建 `XxxEntity.tscn`，根节点选 Godot 类型（`CharacterBody2D` / `Area2D` / `Node2D` 等）
2. **生成脚本**：选中根节点，Inspector 顶部点 **Generate Script**（`ScriptGenerateInspector` 对 Node2D/Node3D 生成实体模板）：
   - Ge 文件 → `ScriptGenerateRes.EntityOutPutPathGe`（默认 `GameProto/EntityGe/`）：IEntity 属性样板 + `[Export]` 子节点字段（`m_` 前缀自动收集），**每次生成覆盖**
   - Logic 文件 → `ScriptGenerateRes.EntityOutPutPathLogic`：生命周期方法骨架，**仅首次生成**
   - 模板：`Framework/GodotGameFrameworkCore/Templet/EntityTemplet.txt` / `EntityLogicTemplet.txt`
   - 若要继承 `ActorEntity` 之类基类，可跳过生成直接手写（CatEntity/AngerEntity 即手写）
3. **构建**：`cd GodotProject && dotnet build`（新脚本编译后 Inspector 才显示 `[Export]` 字段）
4. **配表**：`Configs/GameConfig/Datas/实体.xlsx` 加行（EntityId 枚举、AssetPath、EntityGroupName），运行 `gen_code_bin_to_project.bat` 重新生成 `EntityId.cs` / 二进制数据
5. **实体组**：确认 `EntityGroupRes` 里已有对应组（无则加组条目）
6. **（可选）路径常量**：编辑器菜单 `CollectionRes` 重新生成 `ResourcesCollectionConstant.cs`
7. **调用**：`await GF.Entity.ShowEntityAsync<XxxEntity>(EntityId.Xxx, userData)`

---

## 7. 注意事项 / FAQ

**Q: 实体逻辑该写在 `_Process` 还是 `OnUpdate`？**
`OnUpdate`。它由 `EntityManager` 统一驱动，只在 Showed 状态执行，隐藏/回收后自动停。`_Process` 会在池内隐藏期间继续跑。

**Q: 同一个 `entityId` 重复 Show 会怎样？**
抛 `GameFrameworkException`（已存在或加载中）。用扩展方法（自增 id）可完全避免。

**Q: 池复用时哪些回调会走？**
复用命中：`OnInit(isNewInstance:false)` → `OnShow`，**同帧完成**；新实例：异步加载后 `OnInit(true)` → `OnShow`。信号订阅等一次性逻辑必须放 `isNewInstance` 分支。

**Q: `HideEntity` 后节点去哪了？**
仍在实体组容器节点下（`Visible=false` 由 `OnHide`/`OnRecycle` 自行设置），作为池内对象等待复用；超过 `ExpireTime` 且池自动释放时才 `QueueFree`。

**Q: 加载中的实体能取消吗？**
`HideEntity(id)` 即可——资源加载不中断，但到位后直接释放，不会触发 OnShow。✅（2026-07 修复）对应的 `ShowEntityAsync` 的 Task 会以 `GameFrameworkException`（"Load entity '...' cancelled by HideEntity."）完结，调用方可通过 try/catch 获知取消。

**Q: `ShowEntityFailure` 时 userData 里的引用会泄漏吗？**
事件参数本体是池化对象会回收；userData 原样透传，生命周期由调用方负责。

**Q: `EntityConfig.Priority` 列有什么用？**
预留。`EntityExtension` 当前未把它传给 `ShowEntity`（恒用优先级 0），且资源层加载队列目前也不按优先级调度（见 `ResourceSystem.md` §3.1）。

**Q: CLAUDE.md 里的 `AbstractCharacterBody2DEntity` 等基类在哪？**
不存在（文档滞后）。当前模式是实体类直接 `: Godot类型, IEntity`，公共逻辑放游戏侧基类（如 `ActorEntity`）。

---

## 8. 已知边界与后续计划

- [x] 加载中隐藏实体时完结对应 `ShowEntityAsync` 的 TCS（当前该 Task 悬挂，靠调用方超时兜底）✅ 2026-07
- [x] `ActorEntity._ExitTree()` ReferencePool.Release null 安全（`m_Check` 可能未初始化）✅ 2026-07
- [x] `CatEntity.OnInit` 实体池复用时重建 `PhysicsCheck2D`（旧实例已在上次 `_ExitTree` 中释放）✅ 2026-07
- [x] `GanTanEntity.OnBodyEntered` 集成 NodePool 伤害数字（`DamagePop`）✅ 2026-07
- [ ] `EntityConfig.Priority` 接入 ShowEntity 扩展与资源加载调度
- [ ] 抽象实体基类库（若多项目复用 ActorEntity 模式，可下沉到 `GodotGameFrameworkCore/Base/Node/`）
