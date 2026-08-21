# Available Skills (Slash Commands)

16 project skills organized by purpose. Type `/` in Claude Code to access any of them.

> 游戏生产流水线技能（GDD/故事/冲刺/QA/发布/团队编排，共 61 个）已归档到 `.claude/skills-archived/`（2026-08）——GGF 项目当前为 Technical Setup 阶段、无对应生产工件。需要时移回 `.claude/skills/` 即可恢复，见 `.claude/skills-archived/README.md`。
> 另外 Claude Code 内置了若干通用命令，始终可用且不在下方列表：`/code-review`（审查当前 diff）、`/security-review`、`/verify`、`/simplify`、`/init`、`/review`、`/loop`、`/deep-research` 等。

## 入门与导航

| Command | Purpose |
|---------|---------|
| `/start` | First-time onboarding — asks where you are, then guides you to the right workflow |
| `/help` | Context-aware "what do I do next?" — reads current stage and surfaces the required next step |
| `/project-stage-detect` | Full project audit — detect phase, identify existence gaps, recommend next steps |

## 思考与审查

| Command | Purpose |
|---------|---------|
| `/caveman` | 原始人思维 — strip all abstraction and jargon, explain from zero in concrete terms |
| `/grill-me` | 深度追问 — Socratic stress-test of an idea/proposal: find holes, weak links, unverified assumptions |
| `/grill-with-docs` | Deep code review backed by project docs (docs/, CLAUDE.md, ADR) — domain fidelity, architecture consistency, seams, leaks |
| `/improve-codebase-architecture` | Deepen analysis — find shallow modules, propose seam/depth refactors, HTML report + Q&A loop |
| `/security-audit` | 安全审计 — save tampering, cheat vectors, network exploits, data exposure, input validation |
| `/perf-profile` | Structured performance profiling with bottleneck identification |
| `/tech-debt` | Scan, track, prioritize, and report on technical debt |
| `/reverse-document` | Generate design/architecture docs from existing implementation (works backwards from code) |

## 配置与数据管线

| Command | Purpose |
|---------|---------|
| `/luban-dev` | Luban 配置全栈工具 — Excel 表/枚举/Bean CRUD（luban_helper.py）、导表生成代码与二进制、GGF 集成（ConfigSystem 懒加载）、Schema/校验器 |
| `/localize` | Localization workflow: string extraction, validation, translation readiness |

## 工程环境与元技能

| Command | Purpose |
|---------|---------|
| `/setup-engine` | Configure engine + version, detect knowledge gaps, populate version-aware reference docs（GGF 已完成 Godot 4.7 锁定） |
| `/skill-test` | Validate skill files for structural compliance and behavioral correctness |
| `/skill-improve` | Improve a skill using a test-fix-retest loop — diagnose, propose fix, rewrite, verify |
