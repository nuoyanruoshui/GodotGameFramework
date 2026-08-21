---
name: improve-codebase-architecture
description: 发现代码库中的加深机会，依据 docs/ 系统文档与 CLAUDE.md 中的领域语言和 ADR 中的决策。当用户想要改善架构、寻找重构机会、合并紧耦合模块，或使代码库更可测试、更易于 AI 导航时使用。
---

# Improve Codebase Architecture

暴露架构摩擦并提出**加深机会**——将浅模块变为深模块的重构。目标是可测试性和 AI 可导航性。

## 术语表

在每个建议中精确使用这些术语。一致的语言是关键——不要滑向"component"、"service"、"API"或"boundary"。完整定义见 [LANGUAGE.md](LANGUAGE.md)。

- **Module（模块）** — 任何具有接口和实现的东西（函数、类、包、切片）。
- **Interface（接口）** — 调用者使用模块所需知道的一切：类型、不变量、错误模式、顺序、配置。不仅仅是类型签名。
- **Implementation（实现）** — 内部的代码。
- **Depth（深度）** — 接口处的杠杆：少量接口背后的大量行为。**Deep（深）** = 高杠杆。**Shallow（浅）** = 接口几乎与实现一样复杂。
- **Seam（接缝）** — 接口所在的位置；一个可以在不原地编辑的情况下改变行为的地方。（使用这个词，不是"boundary"。）
- **Adapter（适配器）** — 在接缝处满足接口的具体实现。
- **Leverage（杠杆）** — 调用者从深度中获得的收益。
- **Locality（局部性）** — 维护者从深度中获得的收益：变更、bug、知识集中在一个地方。

关键原则（完整列表见 [LANGUAGE.md](LANGUAGE.md)）：

- **删除测试**：想象删除该模块。如果复杂性消失了，那它就是传递模块。如果复杂性重新出现在 N 个调用者身上，那它就在发挥价值。
- **接口就是测试面。**
- **一个适配器 = 假设的接缝。两个适配器 = 真正的接缝。**

本技能*依据*项目的领域模型。领域语言为好的接缝命名；ADR 记录本技能不应重新争论的决策。

**GGF 的领域语言来源**：项目根 `docs/*.md` 系统文档（`FrameworkCore.md`/`EventSystem.md`/`FsmSystem.md`/`ProcedureSystem.md`/`EntitySystem.md`/`UISystem.md`/`ResourceSystem.md`/`ObjectPoolSystem.md`/`SceneSystem.md` 等 20+ 篇）+ `CLAUDE.md`。这些定义了 GGF 的分层词汇（`GameFramework/` 纯 C# 层 vs `GodotGameFrameworkCore/` Godot 桥、`GF` 门面、`IEntity`/`IUIForm`/`IResourceLoadHelper` 等）。发现文档没有覆盖的领域概念时，应在此处补齐（见"追问循环"）。

## 流程

### 1. 探索

首先阅读项目根 `docs/` 下与目标区域对应的系统文档、`CLAUDE.md` 中相关章节，以及任何 ADR（`docs/adr/`，若已建立）。

然后使用 Agent 工具配合 `subagent_type=Explore` 来遍历代码库。不要遵循死板的经验法则——有机地探索，并记录你感受到摩擦的地方：

- 哪里理解一个概念需要在许多小模块之间来回跳转？
- 哪里模块是**浅的**——接口几乎与实现一样复杂？
- 哪里提取了纯函数仅仅为了可测试性，但真正的 bug 隐藏在调用方式中（没有**局部性**）？
- 哪里紧耦合的模块跨接缝泄漏？
- 代码库的哪些部分未被测试，或难以通过当前接口进行测试？

对你怀疑是浅模块的任何东西应用**删除测试**：删除它会让复杂性集中，还是仅仅移动它？"是的，集中了"就是你要的信号。

### 2. 以 HTML 报告呈现候选

将一个自包含的 HTML 文件写入操作系统临时目录，这样不会在仓库中留下任何东西。从 `$TMPDIR` 解析临时目录，回退到 `/tmp`（Windows 上是 `%TEMP%`），写入 `<tmpdir>/architecture-review-<timestamp>.html`，这样每次运行都会得到一个新文件。为用户打开它——Linux 上用 `xdg-open <path>`，macOS 上用 `open <path>`，Windows 上用 `start <path>`——并告诉他们绝对路径。

报告使用 **Tailwind via CDN** 进行布局和样式，使用 **Mermaid via CDN** 绘制图表，当图/流程/时序能可靠传达结构时使用。将 Mermaid 与手工 CSS/SVG 视觉混合使用——当关系呈图状（调用图、依赖、序列）时使用 Mermaid，当你想要更偏编辑性质的视觉（质量图、剖面图、折叠动画）时使用手工 div/SVG。每个候选都有一个 **before/after 可视化**。要视觉化。

对于每个候选，使用与之前相同的模板，但渲染为卡片：

- **文件** — 涉及哪些文件/模块
- **问题** — 当前架构为什么造成摩擦
- **方案** — 什么会改变的通俗描述
- **收益** — 用局部性和杠杆解释，以及测试将如何改善
- **Before / After 图** — 并排，自定义绘制，展示浅性和加深
- **建议强度** — `Strong`、`Worth exploring`、`Speculative` 之一，渲染为徽章

报告末尾以 **顶部建议** 章节结束：你建议先处理哪个候选以及原因。

**领域使用 `docs/` 系统文档与 CLAUDE.md 的词汇，架构使用 [LANGUAGE.md](LANGUAGE.md) 词汇。** 例如 GGF 定义了"ObjectPool"，就说"the ObjectPool module"——不是"the PoolContainer"，也不是"the object pool service"。

**ADR 冲突**：如果某个候选与现有 ADR 矛盾，只有当摩擦足够真实值得重新审视 ADR 时才提出。在卡片中明确标记（例如警告标注：_"与 ADR-0007 矛盾——但值得重新开启，因为……"_）。不要列出 ADR 禁止的每一个理论上的重构。

完整 HTML 脚手架、图表模式和样式指导见 [HTML-REPORT.md](HTML-REPORT.md)。

先不要提出接口。文件写好后，问用户："你想探索其中哪一个？"

### 3. 追问循环

用户选择候选后，进入追问对话。与他们一起走设计树——约束、依赖、加深模块的形状、接缝后面是什么、哪些测试保留。

随着决策成型，副作用在线发生：

- **用 `docs/` 中没有的概念命名加深模块？** 将术语添加到对应的 `docs/*.md` 系统文档（或 `CLAUDE.md`）——与 `/grill-with-docs` 相同的纪律（见 [CONTEXT-FORMAT.md](../grill-with-docs/CONTEXT-FORMAT.md)）。如果该文档不存在则惰性创建。
- **在对话中锐化了一个模糊术语？** 就地更新对应系统文档。
- **用户以一个承重理由拒绝了候选？** 提供一个 ADR，框架为：_"要不要我把这个记录为 ADR，这样未来的架构审查不会再建议它？"_ 只有当理由确实会被未来的探索者需要以避免重复建议时才提供——跳过临时理由（"现在不值得"）和显而易见的理由。ADR 写入 `docs/adr/`，见 [ADR-FORMAT.md](../grill-with-docs/ADR-FORMAT.md)。
- **想探索加深模块的替代接口？** 见 [INTERFACE-DESIGN.md](INTERFACE-DESIGN.md)。
