# GrandUMI 设计系统规范 v1.0

> 权威来源：`opcgpro-vue/docs/GrandUMI Redesign Offline.html`（Claude Design 生成）
> 本文档供所有页面 UI 生成、改造时统一参考。

---

## 主题体系

项目支持双主题切换，通过 `<div data-theme="pirate|navy">` 切换：

| 维度 | 海贼主题（pirate） | 海军主题（navy） |
|------|--------------------|------------------|
| 基调 | 深暖黑 + 琥珀金 | 深冷蓝 + 钢蓝 |
| 背景最暗 | `#0e0a06` | `#070d18` |
| 主强调色 | `#e8b04b`（琥珀金） | `#5b9bd5`（钢蓝） |
| 文字 | `#f3e9d8`（奶白） | `#eaf1fa`（冷白） |
| 红色 accent | `#b23b32` | `#cf4233` |

---

## 色彩 Token

所有组件通过 CSS 变量引用颜色，**不硬编码 hex**。

### 背景层级

| Token | 海贼 | 海军 | 用途 |
|-------|------|------|------|
| `--bg0` | `#0e0a06` | `#070d18` | 页面最深底色 |
| `--bg1` | `#160e08` | `#0a1322` | 输入框/嵌套区域背景 |
| `--surface` | `#1d1510` | `#0f1c30` | 面板/卡片底色 |
| `--surface2` | `#241a12` | `#143150` | 抬起层/悬浮元素底色 |

### 主题色

| Token | 海贼 | 海军 | 用途 |
|-------|------|------|------|
| `--primary` | `#e8b04b` | `#5b9bd5` | 主强调色（按钮、激活态、标签） |
| `--primary-bright` | `#f5cd72` | `#8fc3f2` | 按钮渐变顶色、highlight |
| `--primary-glow` | `rgba(232,176,75,0.45)` | `rgba(95,160,230,0.50)` | 发光阴影、focus ring |
| `--on-primary` | `#1a1206` | `#061018` | 主色背景上的文字（深色） |
| `--accent` | `#b23b32` | `#cf4233` | 红色提示/危险色 |

### 文字

| Token | 海贼 | 海军 | 用途 |
|-------|------|------|------|
| `--ink` | `#f3e9d8` | `#eaf1fa` | 主文字 |
| `--ink-dim` | `rgba(243,233,216,0.56)` | `rgba(234,241,250,0.58)` | 次要文字、icon |
| `--ink-faint` | `rgba(243,233,216,0.30)` | `rgba(234,241,250,0.30)` | 占位符、极弱提示 |

### 边框/分隔

| Token | 海贼 | 海军 | 用途 |
|-------|------|------|------|
| `--line` | `rgba(232,176,75,0.16)` | `rgba(120,175,235,0.18)` | 面板边框、细分隔线 |
| `--line-strong` | `rgba(232,176,75,0.34)` | `rgba(120,175,235,0.40)` | hover 边框、强调分隔 |

### 状态色（主题无关）

| Token | 值 | 用途 |
|-------|----|------|
| `--good` | `#6fcf97` | 成功/完整 |
| `--bad` | `#e26a6a` | 错误/超限 |

---

## 字体

```css
--font-head:  'Noto Serif SC', 'Cinzel', serif   /* 标题、品牌名 — weight 900 */
--font-ui:    'Space Grotesk', 'Noto Sans SC', sans-serif  /* UI 正文、按钮 */
--font-mono:  'JetBrains Mono', ui-monospace, monospace    /* 标签、代码、kicker */
```

**使用规则：**
- 大标题（品牌名/页面标题）：`font-head` weight 900，letter-spacing 0.01em
- 按钮/正文/标签文字：`font-ui` weight 400-600
- Kicker 标签/状态文字/编号：`font-mono`
- 字间距：kicker 用 0.26em，按钮 0.06em，标签 0.08em

---

## 圆角

```css
--radius:      10px   /* 默认：按钮、输入框、小卡片 */
--radius-lg:   16px   /* 面板、弹窗、大容器 */
--radius-pill: 999px  /* 药丸形 tag/toggle/状态点 */
```

---

## 层级模糊

```css
--panel-blur: 14px   /* 玻璃面板 backdrop-filter: blur() 值 */
```

---

## 组件模式规范

### Panel（玻璃面板）

用于卡片、弹窗、信息区域。

```css
background: color-mix(in srgb, var(--surface) 82%, transparent);
border: 1px solid var(--line);
border-radius: var(--radius-lg);
backdrop-filter: blur(var(--panel-blur));
transition: background-color 0.6s, border-color 0.6s;
```

**变体：**
- `.panel--solid`：`background: var(--surface)`（不透明，用于内容较多的区域）

---

### Kicker（区块标题标签）

用于标记每个功能区的标题（如"搜索卡牌"、"颜色"、"费用曲线"）。

```css
font-family: var(--font-mono);
font-size: 12px;
letter-spacing: 0.26em;
color: var(--primary);
text-transform: uppercase;
display: inline-flex;
align-items: center;
gap: 10px;

/* 前缀 // */
::before {
  content: "//";
  color: var(--ink-faint);
}
```

在 Tailwind 中实现（Deck Editor 已有的组件）：
```html
<span class="gde-kicker">搜索卡牌</span>
```
或内联：
```html
<span class="font-mono text-[12px] tracking-[0.26em] text-[var(--primary)] uppercase before:content-['//'] before:text-[var(--ink-faint)] before:mr-2">颜色</span>
```

---

### Field（输入框）

用于所有文本输入（搜索框、卡组名称、导入导出框）。

```css
display: flex;
align-items: center;
gap: 10px;
height: 52px;
padding: 0 16px;
background: var(--bg1);
border: 1px solid var(--line);
border-radius: var(--radius);
transition: border-color 0.25s, box-shadow 0.25s;

:focus-within {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-glow);
}

input {
  flex: 1;
  background: transparent;
  border: none;
  outline: none;
  color: var(--ink);
  font-family: var(--font-ui);
  font-size: 15px;
}

input::placeholder {
  color: var(--ink-faint);
}

.ic {  /* 图标前缀 */
  color: var(--ink-faint);
  font-family: var(--font-mono);
}
```

---

### Btn（按钮体系）

**基础按钮：**
```css
font-family: var(--font-ui);
font-size: 15px;
font-weight: 600;
letter-spacing: 0.06em;
color: var(--ink);
background: transparent;
border: 1px solid var(--line-strong);
border-radius: var(--radius);
padding: 13px 22px;
transition: transform 0.18s, background-color 0.25s, border-color 0.25s, box-shadow 0.25s;

:hover { border-color: var(--primary); color: var(--primary); transform: translateY(-1px); }
:active { transform: translateY(0) scale(0.99); }
:disabled { opacity: 0.4; cursor: not-allowed; }
```

**Primary（金色渐变主按钮）：**
```css
background: linear-gradient(180deg, var(--primary-bright), var(--primary));
color: var(--on-primary);
border: none;
font-weight: 700;
box-shadow: 0 8px 26px -8px var(--primary-glow);

:hover { transform: translateY(-2px); box-shadow: 0 14px 34px -8px var(--primary-glow); }

/* shimmer 扫光效果 */
::after {
  content: "";
  position: absolute;
  top: 0; left: -60%; width: 40%; height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,0.45), transparent);
  transform: skewX(-18deg);
  transition: left 0.6s;
}
:hover::after { left: 120%; }
```

**Ghost（轮廓按钮）：**
```css
border-color: var(--line);
color: var(--ink-dim);
:hover { color: var(--ink); border-color: var(--line-strong); }
```

**尺寸变体：**
- Small: padding 8px 14px, font-size 13px
- Medium (default): padding 13px 22px, font-size 15px
- Large: padding 17px 34px, font-size 17px
- Block: width 100%

---

### Tag（筛选药丸标签）

用于颜色/类型/属性/稀有度等过滤器按钮。

```css
font-family: var(--font-mono);
font-size: 11px;
letter-spacing: 0.08em;
text-transform: uppercase;
color: var(--ink-dim);
border: 1px solid var(--line);
border-radius: var(--radius-pill);  /* 999px */
padding: 5px 12px;
cursor: pointer;
transition: 0.2s;
background: transparent;

:hover { color: var(--ink); border-color: var(--line-strong); }
.is-active {
  color: var(--on-primary);
  background: var(--primary);
  border-color: var(--primary);
}
```

---

### Seg（分段切换控件）

用于 tab 切换，如格式选择（自由/OP15/OP16）、登录/注册切换。

```css
/* 容器 */
.seg {
  display: inline-flex;
  padding: 4px;
  gap: 4px;
  background: var(--bg1);
  border: 1px solid var(--line);
  border-radius: calc(var(--radius) + 4px);
}

/* 选项 */
.seg__opt {
  font-family: var(--font-ui);
  font-size: 14px;
  font-weight: 600;
  color: var(--ink-dim);
  padding: 8px 18px;
  border-radius: var(--radius);
  border: none;
  background: transparent;
  transition: background-color 0.25s, color 0.25s;
}

.seg__opt.is-active {
  color: var(--on-primary);
  background: linear-gradient(180deg, var(--primary-bright), var(--primary));
}
```

---

### Rule（带文字分隔线）

用于区块之间的分隔，兼具装饰性。

```css
display: flex;
align-items: center;
gap: 14px;
color: var(--ink-faint);
font-family: var(--font-mono);
font-size: 11px;
letter-spacing: 0.2em;
text-transform: uppercase;

::before, ::after {
  content: "";
  height: 1px;
  flex: 1;
  background: var(--line);
}
```

---

### Nav-item（侧栏导航按钮）

用于大厅侧栏导航（厅/组/战）。

```css
display: flex;
flex-direction: column;
align-items: center;
gap: 4px;
color: var(--ink-faint);
transition: color 0.25s;
width: 100%;
padding: 14px 0;
position: relative;

.glyph { font-family: var(--font-head); font-weight: 900; font-size: 24px; }
.lbl   { font-family: var(--font-mono); font-size: 10px; letter-spacing: 0.1em; }

:hover { color: var(--ink-dim); }

.is-active {
  color: var(--primary);

  /* 左侧激活指示条 */
  ::before {
    content: "";
    position: absolute;
    left: 0; top: 18%; bottom: 18%;
    width: 3px;
    background: var(--primary);
    border-radius: 0 3px 3px 0;
    box-shadow: 0 0 14px var(--primary-glow);
  }
}
```

---

### Dot（状态指示点）

```css
.dot {
  width: 8px; height: 8px;
  border-radius: 50%;
  background: var(--primary);
  box-shadow: 0 0 10px var(--primary);
  display: inline-block;
}

/* 实时/活跃状态 */
.dot--live { animation: pulse 1.8s ease-in-out infinite; }

@keyframes pulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50%       { opacity: 0.6; transform: scale(0.85); }
}
```

---

### Card Slot（卡牌占位符）

用于展示卡组内的卡牌缩略图槽。

```css
.cardslot {
  border-radius: var(--radius);
  overflow: hidden;
  cursor: pointer;
  border: 1.5px solid var(--line-strong);
  background: var(--surface2);
  aspect-ratio: 5/7;
  transition: transform 0.2s, box-shadow 0.2s;
}

.cardslot:hover {
  transform: translateY(-6px) scale(1.02);
  box-shadow: 0 18px 40px -12px rgba(0,0,0,.7), 0 0 26px -6px var(--primary-glow);
}
```

---

## 动效规范

### 缓动函数

```css
--ease-out:     cubic-bezier(0.2, 0.7, 0.2, 1)   /* 标准出场，轻微弹性 */
--ease-snap:    cubic-bezier(0.34, 1.56, 0.64, 1) /* 强弹性（用于卡片抬起） */
--ease-linear:  linear
```

### 时长

| 场景 | 时长 |
|------|------|
| hover 微动（位移/颜色） | 180-250ms |
| 面板/弹窗出场 | 500-600ms |
| shimmer 扫光 | 600ms |
| 状态指示脉冲 | 1800ms |
| 主题切换 | 600ms |

### 入场动画

```css
/* fadeUp — 列表项/区块入场 */
@keyframes fadeUp {
  from { opacity: 0; transform: translateY(16px); }
  to   { opacity: 1; transform: translateY(0); }
}
animation: 0.6s var(--ease-out) forwards fadeUp;

/* scaleIn — 面板/弹窗入场 */
@keyframes scaleIn {
  from { opacity: 0; transform: scale(0.96); }
  to   { opacity: 1; transform: scale(1); }
}
animation: 0.5s var(--ease-out) forwards scaleIn;
```

### Reduced Motion

```css
@media (prefers-reduced-motion: reduce) {
  .enter > *, .enter-fade, .enter-scale {
    animation: none !important;
    opacity: 1 !important;
    transform: none !important;
  }
}
```

---

## 字型气质（Mood）变体

HTML 提供了三种 mood 可切换，通过 `data-mood="a|b|c"` 激活：

| Mood | 风格 | 字体 | 圆角 | 适用场景 |
|------|------|------|------|---------|
| A | 终端/极简 | JetBrains Mono 主导 | 3px（极锐） | 系统日志、开发风 |
| **B（默认）** | **电影/古典** | **Noto Serif SC + Space Grotesk** | **10px（圆润）** | **主 UI，当前选用** |
| C | 游戏/粗体 | Anton + Space Grotesk | 14-22px（大圆角） | 游戏化风格 |

**当前项目选用 Mood B。**

---

## 布局规范

### 全局布局

```
body {
  background: var(--bg0);
  font-family: var(--font-ui);
  color: var(--ink);
  overflow: hidden;  /* 单页应用，不出现页面滚动条 */
}
```

### Deck Editor 三栏布局

```
┌──────────────────────────────────────────────────────────┐
│  左栏 (256px固定)  │  中栏 (flex-1)  │  右栏 (448px固定) │
│  SearchPanel       │  SearchResult   │  DeckInfoPanel    │
│  (筛选条件)        │  (卡牌网格)     │  (卡组信息)       │
└──────────────────────────────────────────────────────────┘

- 背景色：bg0
- 栏间分隔：border: 1px solid var(--line)
- 各栏高度：h-screen，overflow-hidden
```

---

## 实现注意事项

1. **不硬编码颜色**：所有颜色通过 `var(--xxx)` 引用，确保双主题切换正确
2. **Tailwind 任意值**：用 `bg-[var(--bg1)]`、`border-[var(--line)]`、`text-[var(--primary)]` 等形式
3. **字体已在 index.html 加载**：Space Grotesk + JetBrains Mono via Google Fonts
4. **backdrop-blur 性能**：仅在面板/弹窗上使用，不在列表行上使用
5. **on-primary 文字**：主按钮（金色/蓝色背景）上的文字必须用 `--on-primary`（深色），保证对比度
6. **功能色保留**：卡牌六色（红/绿/蓝/紫/黑/黄）是游戏机制色，与 UI 主题色独立，不替换
