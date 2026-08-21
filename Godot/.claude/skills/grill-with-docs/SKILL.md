---
name: grill-with-docs
description: 对代码变更进行深度审查，借助项目文档（docs/ 系统文档、CLAUDE.md、ADR）提供架构上下文。当用户说"审查这个"、"grill 这个 PR"、要求代码审查，或希望对变更进行深度而非表面的审查时使用。
---

# Grill With Docs

对代码变更进行深度审查，利用项目文档提供架构上下文。

## 何时使用

当用户要求代码审查，或当你需要对变更进行比表面审查更深入的分析时。

## 流程

### 1. 收集上下文

在审查任何代码之前，收集相关的项目文档：

- **系统文档（CONTEXT 的等价物）** — GGF 项目以 `docs/*.md`（仓库根 `docs/` 下 20+ 篇系统文档：`EventSystem.md`/`FsmSystem.md`/`UISystem.md`/`EntitySystem.md`/`ResourceSystem.md` 等）+ 根目录 `CLAUDE.md` 承担领域词汇与架构规范的职责。审查应使用其中的概念词汇来评估变更是否正确地建模了领域。若某模块没有对应文档，以 `CLAUDE.md` 为兜底。
- **ADR（架构决策记录）** — 查找 `docs/adr/` 或 `.adr/` 目录。这些记录了关键决策及其理由。**注意：GGF 目前尚无 ADR**——若本项目尚未建立，审查时应建议在重大架构决策处创建 ADR（用 `/grill-with-docs` 以外的 `/improve-codebase-architecture` 追问循环或单独记录）。

参见 [ADR-FORMAT.md](ADR-FORMAT.md) 和 [CONTEXT-FORMAT.md](CONTEXT-FORMAT.md) 了解这些文档的预期格式（当需要新建时使用）。

### 2. 审查变更

对于每个变更，评估：

1. **领域忠实度** — 变更是否正确建模了领域？是否与 `docs/*.md` / CLAUDE.md 中的概念（如 `GameFramework/` 纯 C# 层与 `GodotGameFrameworkCore/` Godot 桥的分层、`IEntity`/`IUIForm`/`Fsm<CatEntity>` 等模式）一致？
2. **架构一致性** — 变更是否与现有 ADR 一致？是否引入了需要新 ADR 的决策？（GGF 的双层架构规则是最重要的检查点：`GameFramework/` 不得依赖 Godot。）
3. **模块深度** — 变更是否改善了模块深度（简单接口，深层实现）？还是增加了浅层性？
4. **接缝放置** — 变更是否在正确的位置引入了接缝？接缝是否被至少两个适配器（adapter）证明是合理的？（对应 GGF 的 `IResourceLoadHelper`/`DefaultResourceLoadHelper` 这类模式。）
5. **泄漏** — 变更是否将知识泄漏到了不该去的模块？是否违反了封装？（在 GGF 语境：是否把 Godot 类型泄漏进 `GameFramework/` 纯层，或事件参数是否被二次释放。）

### 3. 产出审查结果

对每个发现：

- 引用支持该发现的系统文档或 ADR（`docs/XxxSystem.md`、CLAUDE.md、`docs/adr/`）
- 如果变更与 ADR 矛盾，建议要么修改变更，要么创建新的 ADR
- 如果变更引入了领域概念但 `docs/` 中没有，建议更新对应系统文档或 CLAUDE.md
- 使用 [LANGUAGE.md](../improve-codebase-architecture/LANGUAGE.md) 中的词汇来描述架构问题

## 原则

- **文档是规范，代码是实现。** 当代码与文档矛盾时，审查应标记这一点——并建议哪个应该改变。
- **审查应教学。** 引用文档不仅是为了证明审查观点，也是为了教作者为什么做出这些决策。
- **没有文档不是借口。** 如果项目某模块没有对应系统文档或 ADR，审查仍应进行——但应建议补齐它们，特别是对于架构性变更。
