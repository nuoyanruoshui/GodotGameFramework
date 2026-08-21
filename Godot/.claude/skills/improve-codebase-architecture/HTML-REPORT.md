# HTML 报告格式

架构审查以单个自包含 HTML 文件渲染，放在操作系统临时目录中。Tailwind 和 Mermaid 都从 CDN 加载。Mermaid 可靠地处理图状图表；手工构建的 div 和内联 SVG 处理更偏编辑性质的视觉（质量图、剖面图）。两者混合使用——不要什么都依赖 Mermaid，那会看起来千篇一律。

## 脚手架

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <title>Architecture review — {{repo name}}</title>
    <script src="https://cdn.tailwindcss.com"></script>
    <script type="module">
      import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
      mermaid.initialize({ startOnLoad: true, theme: "neutral", securityLevel: "loose" });
    </script>
    <style>
      /* 小型自定义层，处理 Tailwind 不能干净覆盖的东西：
         虚线接缝线、手绘感觉的箭头头部等 */
      .seam { stroke-dasharray: 4 4; }
      .leak { stroke: #dc2626; }
      .deep { background: linear-gradient(135deg, #0f172a, #1e293b); }
    </style>
  </head>
  <body class="bg-stone-50 text-slate-900 font-sans">
    <main class="max-w-5xl mx-auto px-6 py-12 space-y-12">
      <header>...</header>
      <section id="candidates" class="space-y-10">...</section>
      <section id="top-recommendation">...</section>
    </main>
  </body>
</html>
```

## 页头

仓库名、日期和紧凑图例：实线框 = 模块，虚线 = 接缝，红色箭头 = 泄漏，粗深色框 = 深模块。不要导言段落——直接进入候选。

## 候选卡片

图表承载主要信息。文字精简、朴素，使用术语表术语（[LANGUAGE.md](LANGUAGE.md)）而不加修饰。

每个候选是一个 `<article>`：

- **标题** — 简短，命名加深操作（例如"合并订单接入管道"）。
- **徽章行** — 建议强度（`Strong` = 翡翠色，`Worth exploring` = 琥珀色，`Speculative` = 石板灰），加上依赖类别标签（`in-process`、`local-substitutable`、`ports & adapters`、`mock`）。
- **文件** — 等宽字体列表，`font-mono text-sm`。
- **Before / After 图** — 核心。两列并排。参见下方模式。
- **问题** — 一句话。什么在造成痛苦。
- **方案** — 一句话。什么会改变。
- **收益** — 要点，每条不超过 6 个词。例如"测试只需命中一个接口"、"定价逻辑不再泄漏"、"删除 4 个浅层包装"。
- **ADR 标注**（如适用）— 琥珀色底框中的一行。

不要解释段落。如果图表需要段落才能理解，就重画图表。

## 图表模式

选择适合候选的模式。混合使用。不要让每个图表看起来一样——多样性本身就是意义的一部分。

### Mermaid 图（依赖/调用流的主力工具）

当要点是"X 调用 Y 调用 Z，看看这团乱"时，使用 Mermaid `flowchart` 或 `graph`。包裹在 Tailwind 样式的卡片中，这样不会感觉突兀。用 classDef 将泄漏边染红，深模块染深色。时序图适合"之前：6 次往返；之后：1 次。"

```html
<div class="rounded-lg border border-slate-200 bg-white p-4">
  <pre class="mermaid">
    flowchart LR
      A[OrderHandler] --> B[OrderValidator]
      B --> C[OrderRepo]
      C -.leak.-> D[PricingClient]
      classDef leak stroke:#dc2626,stroke-width:2px;
      class C,D leak
  </pre>
</div>
```

### 手工构建的框与箭头（当 Mermaid 布局与你作对时）

模块用带边框和标签的 `<div>`。箭头用绝对定位在相对容器上的内联 SVG `<line>` 或 `<path>`。当你希望"after"图看起来像一个粗边框的深模块、内部灰化时使用——Mermaid 渲染不出那种分量感。

### 剖面图（适合分层浅性）

堆叠水平条带（`h-12 border-l-4`）来展示调用穿过的层。之前：6 个薄层各做不了什么。之后：1 个厚条带标注合并后的职责。

### 质量图（适合"接口与实现一样宽"）

每个模块两个矩形——一个代表接口表面积，一个代表实现。之前：接口矩形几乎和实现矩形一样高（浅）。之后：接口矩形矮，实现矩形高（深）。

### 调用图折叠

之前：渲染为嵌套框的函数调用树。之后：同一棵树折叠为一个框，现在内部的调用在其中淡显。

## 风格指导

- 偏编辑风格，不是企业仪表盘。充裕留白。标题可选衬线体（`font-serif` 与 stone/slate 搭配良好）。
- 节制用色：一个主色（翡翠或靛蓝）加上红色表示泄漏、琥珀色表示警告。
- 图表保持约 320px 高，使 before/after 并排时无需滚动即可舒适查看。
- 模块标签在图表内使用 `text-xs uppercase tracking-wider`——应读起来像示意图，不像 UI。
- 唯一的脚本是 Tailwind CDN 和 Mermaid ESM 导入。报告其余部分是静态的——没有应用代码，没有 Mermaid 自身渲染之外的交互性。

## 顶部建议区

一个较大的卡片。候选名称，一句话原因，锚链接到其卡片。仅此而已。

## 语气

朴素中文，简洁——但架构名词和动词直接来自 [LANGUAGE.md](LANGUAGE.md)。简洁不是偏离术语的借口。

**使用精确术语：** module（模块）、interface（接口）、implementation（实现）、depth（深度）、deep（深）、shallow（浅）、seam（接缝）、adapter（适配器）、leverage（杠杆）、locality（局部性）。

**绝不替换为：** component、service、unit（代替 module）· API、signature（代替 interface）· boundary（代替 seam）· layer、wrapper（代替 module，当你指 module 时）。

**符合风格的措辞：**

- "订单接入模块是浅层的——接口几乎与实现一致。"
- "定价跨接缝泄漏。"
- "加深：一个接口，一个测试点。"
- "两个适配器证明接缝合理：生产用 HTTP，测试用内存。"

**收益要点**用术语表术语命名收益：*"局部性：bug 集中在一个模块"*，*"杠杆：一个接口，N 个调用点"*，*"接口缩小；实现吸收包装层"*。不要写*"更容易维护"*或*"代码更清晰"*——这些术语不在术语表中，不配占位。

不模棱两可，不铺垫，不"值得注意的是……"。如果一句话可以是要点，就写成要点。如果一个要点可以删掉，就删掉。如果一个术语不在 [LANGUAGE.md](LANGUAGE.md) 中，在发明新术语之前先找一个已有的替代。
