# 声音系统 (Sound Module)

> 适用版本：Godot 4.6.2 + .NET 8 ｜ 对应代码：`Framework/GameFramework/Sound/`、`Framework/GodotGameFrameworkCore/Sound/`
> 本文档描述 GGF 的声音系统：声音组与代理调度、PlaySoundParams、与 Godot `AudioStreamPlayer` 的桥接方式（音量/静音/暂停/淡入淡出的手动实现）、API 用法与注意事项。

---

## 1. 概述

声音系统是 [Game Framework](https://gameframework.cn/) Sound 模块的 Godot 移植，遵循框架的**双层架构**：

| 层 | 位置 | 职责 | Godot 依赖 |
|----|------|------|:--:|
| 纯 C# 层 | `GameFramework/Sound/` | SoundManager：声音组管理、代理抢占调度、异步资源加载、事件定义 | ❌ |
| Godot 桥接层 | `GodotGameFrameworkCore/Sound/` | SoundComponent 组件封装、AudioStreamPlayer 代理实现、Tween 淡入淡出、BGM/SFX 便捷扩展 | ✅ |

**核心概念**：每个**声音组（SoundGroup）** 持有固定数量的**声音代理（SoundAgent）**，每个代理桥接一个 Godot `AudioStreamPlayer` 节点。同组内同时发声数 = 代理数，超出时按**优先级抢占**。

### 能力清单

- ✅ 声音组：独立音量/静音/代理数/同优先级保护，映射到 Godot Audio Bus
- ✅ 优先级抢占调度（空闲优先 → 抢低优先级 → 抢同优先级中最旧的）
- ✅ 异步加载（经 `IResourceManager.LoadAsset`，serialId 贯穿全程）
- ✅ 停止 / 暂停 / 恢复，均支持淡入淡出（Godot `Tween` 实现）
- ✅ `PlaySoundParams`：起播位置 / 音量 / 音调 / 优先级 / 淡入等（池化）
- ✅ 播放完成自动回收代理（`Finished` 信号 → `ResetSoundAgent`）
- ✅ `PlayBGM / PlaySFX / PlayUISound / StopBGM` 便捷扩展

---

## 2. 架构与数据流

```
调用方（界面/实体逻辑）
    │  GF.Sound.PlaySFX(path) / PlaySound(path, group, priority, params, userData)
    ▼
SoundComponent (Godot 桥接层，场景节点 "Sound")
    │  委托                                        ▲ C# 事件
    ▼                                              │
SoundManager : GameFrameworkModule (纯 C# 层)      │
    │  serialId = ++m_Serial                       │
    │  IResourceManager.LoadAsset(异步加载 AudioStream)
    │        │ 成功
    │        ▼
    ├── SoundGroup.PlaySound → 按优先级挑选 SoundAgent
    │        │ 设置 Time/Loop/Priority/Volume/Pitch... → Play(fadeIn)
    │        ▼
    └── SoundAgent ──委托──▶ ISoundAgentHelper（桥接抽象）
                                  ▲ 实现
DefaultSoundAgentHelper : SoundAgentHelperBase : GodotComponent
    └── AudioStreamPlayer（音量 dB 换算 / 手动暂停 / Tween 淡变 / Finished 信号）
```

场景树运行时结构（`AddSoundGroup` 动态创建）：

```
GameFramework
└── Sound (SoundComponent)
    ├── DefaultSoundHelper
    └── "Sound Group - Music" (DefaultSoundGroupHelper)
        ├── "Agent 0" (AudioStreamPlayer, Bus="Music", ProcessMode=Always)
        │   └── "Agent Helper 0" (DefaultSoundAgentHelper)
        └── "Agent 1" (AudioStreamPlayer)
            └── "Agent Helper 1"
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `GameFramework/Sound/ISoundManager.cs` / `SoundManager.cs` | 管理器接口 / 实现（组管理、播放调度、加载回调） |
| `GameFramework/Sound/SoundManager.SoundGroup.cs` | 声音组：代理挑选（抢占算法）、组音量/静音广播 |
| `GameFramework/Sound/SoundManager.SoundAgent.cs` | 声音代理：参数中转、`RefreshMute/RefreshVolume`、Reset 释放资源 |
| `GameFramework/Sound/PlaySoundParams.cs` / `Constant.cs` | 播放参数（池化）/ 默认值 |
| `GameFramework/Sound/PlaySoundErrorCode.cs` | 错误码（组不存在 / 无代理 / 低优先级被忽略 / 设置资源失败 / 加载失败） |
| `GameFramework/Sound/ISoundGroup.cs` / `ISoundAgent.cs` / `ISound*Helper.cs` | 组 / 代理 / 三类辅助器抽象 |
| `GameFramework/Sound/PlaySound*EventArgs.cs` / `ResetSoundAgentEventArgs.cs` | 事件参数（池化） |
| `GodotGameFrameworkCore/Sound/SoundComponent.cs` | 组件封装：建组建代理、播放/停止/暂停 API、事件转发 |
| `GodotGameFrameworkCore/Sound/DefaultSoundAgentHelper.cs` | **核心桥接**：AudioStreamPlayer 封装（见 §3.3） |
| `GodotGameFrameworkCore/Sound/DefaultSoundGroupHelper.cs` | 组容器节点（空实现，仅承载 Agent） |
| `GodotGameFrameworkCore/Sound/DefaultSoundHelper.cs` | `ReleaseSoundAsset` 空实现（Godot 资源由引擎引用计数管理） |
| `GodotGameFrameworkCore/Sound/SoundExtension.cs` | `PlayBGM / PlaySFX / PlayUISound / StopBGM` |
| `TheGame/MainPack/Scripts/Resources/SoundGroup.cs` / `SoundGroupRes.cs` | 声音组定义资源（Name / AgentCounts / AvoidBeingReplacedBySamePriority） |

---

## 3. 核心机制

### 3.1 声音组与启动注册

`TheGame/MainPack/Resources/SoundGroupRes.tres` 定义三个组，由 `ProcedurePrelode.LoadSoundGroup()` 启动时注册：

| 组名 | AgentCounts | 用途 | 特殊处理（按组名硬编码于 `SoundComponent.AddSoundGroup`） |
|------|:--:|------|------|
| `Music` | 2 | 背景音乐 | `Bus="Music"`，`ProcessMode=Always`（游戏暂停时继续播放） |
| `SFX` | 8 | 游戏音效 | `Bus="SFX"` |
| `UI` | 4 | 界面音效 | `Bus="UI"` |

`GF.Sound.AddSoundGroup(name, agentCount, avoidBeingReplacedBySamePriority)` 会：创建组容器节点 → 注册纯 C# SoundGroup（`Mute=false, Volume=1`）→ 创建 `agentCount` 个 `AudioStreamPlayer` + `DefaultSoundAgentHelper` 并逐个 `AddSoundAgentHelper`。自定义组名不会设置 Bus（走 Master）。

### 3.2 代理抢占算法（`SoundGroup.PlaySound`）

按顺序遍历组内代理，选出 candidate：

1. **有空闲代理**（`!IsPlaying`）→ 立即选用
2. 否则找**优先级低于新声音**的代理中优先级最低者
3. 否则若组未开启 `AvoidBeingReplacedBySamePriority`，找**同优先级**中 `SetSoundAssetTime` 最旧者
4. 都没有 → `PlaySoundErrorCode.IgnoredDueToLowPriority`，播放失败

选中后代理先 `Reset()`（释放旧资源、恢复默认参数），再依次写入 `PlaySoundParams` 全部字段并 `Play(FadeInSeconds)`。

> 优先级语义：数值越大优先级越高（0 = 默认 = 最低档）。GGF 未做 UGF 的 128-x 反转。

### 3.3 与 AudioStreamPlayer 的桥接（DefaultSoundAgentHelper）

Godot 的 `AudioStreamPlayer` 缺少 Unity `AudioSource` 的若干能力，桥接层逐一补齐：

| 框架属性/操作 | Godot 实现 |
|------|------|
| `Volume`（线性 0-1） | `VolumeDb = Mathf.LinearToDb(v)`（分贝换算） |
| `Mute` | 无原生属性；静音时 `VolumeDb = -80f`（人耳不可闻） |
| `Pause/Resume` | 无原生 Pause；暂停 = 记录 `GetPlaybackPosition()` + `Stop()`，恢复 = `Play(保存位置)` |
| 淡入/淡出 | `SceneTree.CreateTween()` 渐变 `volume_db`；新的 Play/Stop/Pause/Resume 会 Kill 旧 Tween |
| `Time` | `GetPlaybackPosition()` / `Seek()` |
| `Length` | 按流类型转型取 `GetLength()`（Wav/Ogg/MP3，其余返回 0） |
| `Pitch` | `PitchScale`（clamp 0.01~4） |
| `Loop` | **仅存储，无实际效果**——Godot 循环由音频资源的导入设置决定（见 FAQ） |
| `PanStereo / SpatialBlend / MaxDistance / DopplerLevel` | AudioStreamPlayer 不支持，仅存储（预留给 2D/3D 播放器实现） |
| 播放完成回收 | `Finished` 信号 → 跳过（淡出中/暂停中/流自带循环 `Stream._HasLoop()`）→ 触发 `ResetSoundAgent` 事件 → `SoundAgent.Reset()` 释放代理 |

最终生效值由两级合成（`SoundAgent.RefreshMute/RefreshVolume`）：

```
实际静音 = 组.Mute || params.MuteInSoundGroup
实际音量 = 组.Volume × params.VolumeInSoundGroup
```

组的 `Mute`/`Volume` 属性写入时会广播刷新组内所有代理。

### 3.4 PlaySoundParams（池化）

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `Time` | 0 | 起播位置（秒） |
| `MuteInSoundGroup` | false | 在组内是否静音 |
| `Loop` | false | 循环（Godot 下无效，见 FAQ） |
| `Priority` | 0 | 抢占优先级（越大越高） |
| `VolumeInSoundGroup` | 1 | 组内音量（与组音量相乘） |
| `FadeInSeconds` | 0 | 淡入时长 |
| `Pitch` | 1 | 音调 / 播放速率 |
| `PanStereo` | 0 | 立体声声相（当前无效） |
| `SpatialBlend` | 0 | 2D/3D 混合（当前无效） |
| `MaxDistance` | 100 | 最大距离（当前无效） |
| `DopplerLevel` | 1 | 多普勒（当前无效） |

用 `PlaySoundParams.Create()` 从 `ReferencePool` 获取（`Referenced=true`，播放流程结束后框架自动 `Release`）；`new PlaySoundParams()` 也合法但不入池。**不要复用同一个 Create() 出来的实例发起多次播放**——第一次播放完成即被回收。

### 3.5 PlaySound 全流程与错误处理

```
PlaySound(...) → serialId（立即返回，无论成败）
    ├─ 组不存在 / 组无代理 → PlaySoundFailure（同步触发）
    ├─ LoadAsset 异步 → 失败 → PlaySoundFailure(LoadAssetFailure)
    │                → 成功 → SoundGroup.PlaySound
    │                        ├─ 无可抢占代理 → PlaySoundFailure(IgnoredDueToLowPriority)
    │                        ├─ 资源非 AudioStream → PlaySoundFailure(SetSoundAssetFailure)
    │                        └─ 成功 → PlaySoundSuccess
    └─ 加载途中 StopSound(serialId) → 静默取消（m_SoundsToReleaseOnLoad）
```

`SoundComponent` 对失败仅打 `Log.Warning`；`PlaySoundSuccess` 的全局事件转发**当前被注释未启用**（3D 绑定实体逻辑预留），如需订阅成功事件请直接使用 `ISoundManager.PlaySoundSuccess` C# 事件或自行恢复该转发。

---

## 4. SoundComponent 与 API

场景节点：`Framework/GameFramework.tscn` 中的 `Sound` 节点，经 `GF.Sound` 访问。

### 4.1 Inspector 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `m_EnablePlaySoundSuccessEvent` | true | 订阅管理器 Success 事件（转发部分被注释） |
| `m_EnablePlaySoundUpdateEvent` | false | 加载进度事件转发 |
| — | — | DependencyAsset 相关 Export 字段已于 2026-07 移除（Godot 自动管理依赖） |
| `m_SoundHelperTypeName` | `GodotGameFramework.Sound.DefaultSoundHelper` | 声音辅助器 |
| `m_SoundGroupHelperTypeName` | `GodotGameFramework.Sound.DefaultSoundGroupHelper` | 组辅助器 |
| `m_SoundAgentHelperTypeName` | `GodotGameFramework.Sound.DefaultSoundAgentHelper` | 代理辅助器（可替换为 2D/3D 实现） |
| `SoundGroupRes` | `TheGame/MainPack/Resources/SoundGroupRes.tres` | 声音组定义 |

### 4.2 方法总览

```csharp
// 组管理
GF.Sound.AddSoundGroup(name, agentCount, avoidBeingReplacedBySamePriority = false);
GF.Sound.HasSoundGroup(name);  GF.Sound.GetSoundGroup(name);  GF.Sound.GetAllSoundGroups();
GF.Sound.SetSoundGroupVolume(name, volume);   // 组音量（0-1）
GF.Sound.SetSoundGroupMute(name, mute);       // 组静音

// 播放（8 个重载：priority / playSoundParams / userData 任意组合）
int serialId = GF.Sound.PlaySound(assetName, groupName, priority, playSoundParams, userData);

// 便捷扩展（SoundExtension）
GF.Sound.PlayBGM(assetName);        // Music 组；先 StopAllLoadedSounds 停掉旧 BGM
GF.Sound.PlaySFX(assetName);        // SFX 组
GF.Sound.PlayUISound(assetName);    // UI 组
GF.Sound.StopBGM();                 // 停止 Music 组
GF.Sound.StopBGM(fadeOutSeconds);   // 淡出停止 Music 组
GF.Sound.StopSFX();                 // 停止 SFX 组
GF.Sound.SetVolume("Music", 0.5f);  // 设置指定 Bus 音量（0~1）

// 停止 / 暂停 / 恢复（均有带淡变秒数的重载）
GF.Sound.StopSound(serialId);              // 返回 bool；加载中→取消
GF.Sound.StopSound(serialId, fadeOut);
GF.Sound.StopAllLoadedSounds();  GF.Sound.StopAllLoadedSounds(fadeOut);
GF.Sound.StopAllLoadingSounds();
GF.Sound.PauseSound(serialId);             // ⚠️ 找不到 serialId 抛异常
GF.Sound.ResumeSound(serialId, fadeIn);    // ⚠️ 同上

// 查询
GF.Sound.IsLoadingSound(serialId);
GF.Sound.GetAllLoadingSoundSerialIds();
```

### 4.3 使用示例

```csharp
// BGM（TheGame/MenuForm、MainForm 实际用法）
GF.Sound.PlayBGM(ResourcesCollectionConstant.Music_Menu);

// 简单音效（CatEntity 射击）
GF.Sound.PlaySFX(ResourcesCollectionConstant.SFX_Shoot);

// 自定义参数：半音量、2 秒淡入、高优先级
var p = PlaySoundParams.Create();
p.VolumeInSoundGroup = 0.5f;
p.FadeInSeconds = 2f;
p.Priority = 10;
int id = GF.Sound.PlaySound("res://TheGame/Audio/boss_theme.ogg", "Music", p);

// 稍后淡出停止
GF.Sound.StopSound(id, fadeOutSeconds: 1.5f);

// 设置页：全局音量控制
GF.Sound.SetSoundGroupVolume("Music", musicSlider.Value);
GF.Sound.SetSoundGroupMute("SFX", sfxMuteCheck.ButtonPressed);
```

---

## 5. 注意事项 / FAQ

**Q: `PlaySoundParams.Loop = true` 为什么不循环？**
`DefaultSoundAgentHelper.Loop` 只是存储值，不作用于 AudioStreamPlayer。Godot 中循环由**音频资源的导入设置**决定（WAV 的 Loop Mode、OGG/MP3 的 Loop 勾选）。✅（2026-07）设置 `Loop=true` 时框架会打印 Warning 提醒。BGM 请在导入面板开循环。

**Q: `StopBGM()` 把音效也停了？**
✅（2026-07 修复）已改为只停 Music 组（`GetSoundGroup("Music").StopAllLoadedSounds()`），不再影响 SFX/UI 组。

**Q: `PauseSound/ResumeSound` 抛 `GameFrameworkException`？**
serialId 对应的声音已不在任何代理上（播完被回收/被抢占/从未成功）时会抛（原版 GF 行为）。`StopSound` 则返回 false 不抛。

**Q: 播放同组声音过多时旧声音被顶掉？**
这是抢占调度的预期行为。增大该组 `AgentCounts`（`SoundGroupRes.tres`），或给不希望被顶的声音更高 `Priority`，或开启组的 `AvoidBeingReplacedBySamePriority`。

**Q: 为什么订阅不到全局 PlaySoundSuccess 事件？**
✅（2026-07 修复）`SoundComponent.OnPlaySoundSuccess` 已恢复 `m_EventComponent.Fire` 转发，经 `PlaySoundSuccessEventArgs`（Godot 层）全局分发。订阅 `PlaySoundSuccessEventArgs.EventId` 即可。

**Q: 暂停游戏（SceneTree.Paused）时 BGM 会停吗？**
不会。Music 组的 AudioStreamPlayer 被设置为 `ProcessMode = Always`；SFX/UI 组会随树暂停。

**Q: 如何做 2D/3D 空间音效？**
`PanStereo/SpatialBlend/MaxDistance/DopplerLevel` 当前仅存储。可仿照 `DefaultSoundAgentHelper` 写一个基于 `AudioStreamPlayer2D/3D` 的 AgentHelper，把 `m_SoundAgentHelperTypeName` 改为其类型全名（注意 `SoundAgentHelperBase` 当前暴露的是 `AudioStreamPlayer` 类型属性，需要一并扩展）。

**Q: 声音资源何时释放？**
`DefaultSoundHelper.ReleaseSoundAsset` 为空实现——Godot `Resource` 由引擎引用计数管理，代理 `Reset` 时置 `Stream = null` 即解除引用。
