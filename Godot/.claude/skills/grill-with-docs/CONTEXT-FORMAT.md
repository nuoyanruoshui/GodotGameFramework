# CONTEXT.md 格式

> **GGF 映射**：本项目中 "CONTEXT" 的角色由仓库根 `docs/*.md` 系统文档（`EventSystem.md`/`FsmSystem.md`/`UISystem.md`/`EntitySystem.md`/`ResourceSystem.md`/`FrameworkCore.md` 等）+ `CLAUDE.md` 承担。下方格式即"领域语言"章节的写法；在 GGF 中，将术语写入**对应的系统文档**（或 CLAUDE.md 的相关章节），而不是单独创建 `CONTEXT.md`。见 [grill-with-docs/SKILL.md](SKILL.md) 与 [improve-codebase-architecture/SKILL.md](../improve-codebase-architecture/SKILL.md)。

## 结构

```md
# {上下文名称}

{一两句话描述这个上下文是什么、为什么存在。}

## 语言

**Order（订单）**:
{一两句话描述该术语}
_Avoid_: Purchase, transaction

**Invoice（发票）**:
在交付后向客户发送的付款请求。
_Avoid_: Bill, payment request

**Customer（客户）**:
下单的个人或组织。
_Avoid_: Client, buyer, account
```

## 规则

- **要有主见。** 当同一概念存在多个词时，选最好的那个，将其余的列在 `_Avoid_` 下。
- **定义要紧凑。** 最多一两句话。定义它"是什么"，而不是它"做什么"。
- **只包含本项目上下文特有的术语。** 通用编程概念（超时、错误类型、工具模式）不属于这里，即使项目大量使用它们。在添加术语前先问：这是本上下文独有的概念，还是通用编程概念？只有前者才该出现在这里。
- **在自然分群出现时用子标题分组。** 如果所有术语属于一个紧密领域，平铺列表即可。

## 单上下文 vs 多上下文仓库

**单上下文（大多数仓库）：** 仓库根目录放一个 `CONTEXT.md`。

**多上下文：** 仓库根目录放一个 `CONTEXT-MAP.md`，列出各上下文、位置和它们之间的关系：

```md
# 上下文映射

## 上下文

- [Ordering](./src/ordering/CONTEXT.md) — 接收和跟踪客户订单
- [Billing](./src/billing/CONTEXT.md) — 生成发票和处理付款
- [Fulfillment](./src/fulfillment/CONTEXT.md) — 管理仓库拣货和发货

## 关系

- **Ordering → Fulfillment**：Ordering 发出 `OrderPlaced` 事件；Fulfillment 消费它们开始拣货
- **Fulfillment → Billing**：Fulfillment 发出 `ShipmentDispatched` 事件；Billing 消费它们生成发票
- **Ordering ↔ Billing**：共享 `CustomerId` 和 `Money` 类型
```

技能会推断适用哪种结构：

- 如果存在 `CONTEXT-MAP.md`，读取它来查找上下文
- 如果只有根目录的 `CONTEXT.md`，则为单上下文
- 如果两者都不存在，在第一个术语被解析时惰性创建根目录 `CONTEXT.md`

当存在多个上下文时，推断当前话题关联哪个上下文。如果不清楚，就问。
