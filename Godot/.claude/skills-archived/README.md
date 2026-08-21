# 归档技能（Archived Skills）

这些技能曾属于游戏工作室生产流水线（概念 → 设计 → 架构 → 故事 → 冲刺 → QA → 发布），
因 GGF 项目当前处于 **Technical Setup** 阶段、没有 GDD/Epic/Story/冲刺/ADR 等生产工件而休眠。

**归档原因**（2026-08）：
- 项目是 Godot 框架移植，不运行游戏生产流水线；
- 其中 `code-review` 与内置 `/code-review` 同名冲突；
- `dev-story` / `setup-engine` / `team-ui` 曾引用已于 2026-07 移除的引擎特化 agent（已修复后再归档）。

**如何恢复**：把技能目录移回 `../skills/` 即可重新注册：

```bash
mv .claude/skills-archived/<skill-name> .claude/skills/
```

**归档清单**（62）：adopt, architecture-decision, architecture-review, art-bible, asset-audit, asset-spec,
balance-check, brainstorm, bug-report, bug-triage, changelog, code-review, consistency-check, content-audit,
create-architecture, create-control-manifest, create-epics, create-stories, day-one-patch, design-review,
design-system, dev-story, estimate, gate-check, hotfix, launch-checklist, map-systems, milestone-review,
onboard, patch-notes, playtest-report, propagate-design-change, prototype, qa-plan, quick-design,
regression-suite, release-checklist, retrospective, review-all-gdds, scope-check, smoke-check, soak-test,
sprint-plan, sprint-status, story-done, story-readiness, team-audio, team-combat, team-level, team-live-ops,
team-narrative, team-polish, team-qa, team-release, team-ui, test-evidence-review, test-flakiness,
test-helpers, test-setup, ux-design, ux-review, vertical-slice
