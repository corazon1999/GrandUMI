# OPCGPRO-VUE HS-现代设计重构

**日期**：2026-06-20
**范围**：除 GamePage 战斗界面外的所有页面与组件
**基线**：Hearthstone 现代演绎（非完全复刻），UI 退到背景、卡牌 sprite 仍是主角

## 决策记录

| 维度 | 决策 | 理由 |
|------|------|------|
| 范围 | Login/Home/DeckEditor/Spectate/Replay/Loading + 共享 ui/composables | GamePage 战斗界面用户明确不动 |
| 参考 | Hearthstone | 暖色温 + 金色点缀契合 OPCG 当前 orange-500 调性 |
| 卡框 | 纯 CSS（border + inset shadow） | 零资源依赖、动态主题切换友好 |
| 动效 | 丰富（GSAP 弹性/弹跳/闪光） | HS 招牌「手感」，用户明确要求 |
| 路径 | 基座先稳（tokens → primitives → 页面） | 一致性 + 可维护 |
| 方向 | HS-现代（B） | 卡牌为主，UI 退到背景 |

## Design Tokens（`src/style.css` `@theme`）

```css
/* 色板 */
--color-surface-base: #0c0a09;       /* stone-950 全局底 */
--color-surface-panel: #1c1917;      /* stone-800 面板 */
--color-surface-raised: #292524;     /* stone-700 抬起层 */
--color-gold-500: #c8a04a;           /* 主金 */
--color-gold-300: #d4b876;           /* 浅金 hover */
--color-gold-700: #8a6d2e;           /* 深金 active */
--color-warm-glow: #3a2810;          /* 顶部暖光 */
--color-accent-orange: #f59e0b;      /* 保留 orange-500 hue */

/* 字体 */
--font-heading: 'Cinzel', 'Cinzel Decorative', Georgia, serif;
--font-body: 'Arial', 'Helvetica', sans-serif;
--font-number: 'Arial', monospace;   /* font-variant-numeric: tabular-nums */

/* 间距/字号（沿用现有放大方案） */
--text-xs: 0.8125rem;   /* 13px */
--text-sm: 0.9375rem;   /* 15px */
--text-base: 1.0625rem; /* 17px */
--text-lg: 1.1875rem;   /* 19px */

/* 缓动 */
--ease-hs-snap: cubic-bezier(0.34, 1.56, 0.64, 1);   /* 轻弹性 */
--ease-hs-bounce: cubic-bezier(0.68, -0.55, 0.27, 1.55); /* 强弹性 */
--ease-hs-ease: cubic-bezier(0.4, 0, 0.2, 1);

/* 时长 */
--dur-hover: 180ms;
--dur-card-flip: 320ms;
--dur-glow: 600ms;

/* 阴影 */
--shadow-card: 0 4px 12px rgba(0,0,0,.5), inset 0 0 0 1px #c8a04a;
--shadow-card-hover: 0 8px 24px rgba(200,160,74,.25), 0 0 0 1px #d4b876;
--shadow-panel-inset: inset 0 1px 0 rgba(255,255,255,.04), inset 0 -1px 0 rgba(0,0,0,.4);

/* 圆角 */
--radius-card: 0.5rem;   /* 8px */
--radius-panel: 0.75rem; /* 12px */
--radius-button: 0.375rem; /* 6px */
```

## 全局背景

```css
body {
  background:
    radial-gradient(ellipse 80% 50% at 50% 0%, rgba(58,40,16,0.4) 0%, transparent 60%),
    var(--color-surface-base);
  background-attachment: fixed;
  color: var(--foreground);
  font-family: var(--font-body);
  overflow: hidden;
}
```

## 新增文件

| 路径 | 用途 |
|------|------|
| `src/composables/useGsap.ts` | GSAP 单例 + `prefers-reduced-motion` 守卫 |
| `src/composables/useCardEntrance.ts` | 卡牌入场：scale 0.5→1.1→1.0 + 弹性 + 金光闪 |
| `src/composables/useCardHover.ts` | hover 抬升 + shadow + gold ring |
| `src/components/ui/CardFrame.vue` | 卡框包装（外金边/内描边/稀有度 ring/无障碍标签） |
| `src/components/ui/HearthButton.vue` | 厚重金边按钮（primary/secondary/ghost） |
| `src/components/ui/PanelChrome.vue` | 面板包边（HS 风格 inset 阴影 + 可选 title 插槽） |
| `src/components/ui/GoldDivider.vue` | 金色装饰分隔条（带 1px 高光 + 1px 暗边） |
| `src/components/ui/RarityRing.vue` | 稀有度环（5 档颜色：UC/C/R/SR/L/SEC/P） |

## 改动文件

| 路径 | 变更 |
|------|------|
| `src/style.css` | 重写 `@theme` + body 背景 + 滚动条沿用上次结果 |
| `src/components/deck-editor/CardGridItem.vue` | 用 `CardFrame` + `useCardEntrance/useCardHover` |
| `src/components/deck-editor/CardHoverPreview.vue` | 用 `CardFrame` + GSAP 弹性入场 |
| `src/components/deck-editor/DeckEntryRow.vue` | 改用 `RarityRing` + 弹性 hover |
| `src/components/deck-editor/CostCurve.vue` | 用新色板（gold-500 高亮命中曲线） |
| `src/components/deck-editor/SearchPanel.vue` | 按钮 → `HearthButton`，面板用 `PanelChrome` |
| `src/components/deck-editor/SearchResultPanel.vue` | 上次已改，header 加 `GoldDivider` |
| `src/components/deck-editor/DeckInfoPanel.vue` | 面板/输入/按钮统一替换 |
| `src/pages/LoginPage.vue` | 标题改 Cinzel + 金色描边 + 弹性入场 |
| `src/pages/HomePage.vue` | 房间按钮用 HearthButton，房间卡片用 PanelChrome |
| `src/pages/DeckEditorPage.vue` | 上次已改（栏宽），loading 屏加金边框 |
| `src/pages/LoadingPage.vue` | 加金边框 spinner + 暖光 |
| `src/pages/SpectatePage.vue` | 标题栏用 PanelChrome + 按钮 HearthButton |
| `src/pages/ReplayPage.vue` | 同 Spectate |

## 动效预设

```ts
// useCardEntrance
{ duration: 0.32, ease: 'back.out(1.7)' }            // 入场
{ boxShadow: '0 0 24px #c8a04a', duration: 0.6 }     // 金光闪
{ y: 0, rotate: 0 }                                   // 静止态

// useCardHover
{ y: -4, scale: 1.05, boxShadow: shadow-hover, 0.18, 'power2.out' }

// HearthButton click
{ scale: 0.96, duration: 0.05 } then { scale: 1, duration: 0.15, ease: 'back.out(2)' }
```

## 可访问性

- 所有动效受 `prefers-reduced-motion` 守护：reduced 时跳过 GSAP，仅保留 50ms transition
- 颜色对比度：gold-500 (#c8a04a) on stone-950 (#0c0a09) = 7.2:1（WCAG AAA）
- 卡牌按钮带 `aria-label="<name>, 费用 <cost>, <rarity>"`

## 不在本设计范围

- GamePage 战斗界面（用户明确排除）
- 卡牌 sprite 资源（已 WebP 化）
- 国际化/多语言
- 音效（未在用户要求内）

## 验收

- [ ] 视觉：登录/首页/卡组编辑器均能体现暖色温 + 金色点缀
- [ ] 卡牌：边框 1.5px gold + 内描边 + hover 抬升
- [ ] 动效：所有 hover 180ms、有 5-10% 弹性 overshoot
- [ ] 性能：滚动 100+ 卡牌维持 60fps
- [ ] 可访问性：reduced-motion 友好、颜色对比 AAA
- [ ] 回归：现有 vue-tsc + vite build 通过
