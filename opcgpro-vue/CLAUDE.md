# GrandUMI opcgpro-vue 项目说明

## 语言

**所有对话、回复、注释均使用中文。**

## 项目概述

- Vue 3 + Vite + TypeScript 卡牌对战游戏客户端
- 后端：C# WebSocket 服务器（GrandUMIServer.exe）
- 端口：Vite dev server 5176

## 工作范围

- **只修改 `opcgpro-vue/`**，不主动改动 `opcgpro-web/`
- opcgpro-web（Next.js）仅作参考

## 技术栈

- 状态管理：Zustand vanilla store（非 Pinia）
- 路由：Vue Router，`requiresAuth` 需要 `loggedIn` 为 true
- 样式：Tailwind CSS，主题色 `#c8a04a`（金色）/ `#08070d`（深色背景）
- 字体：`font-hs-heading`（标题用）
- 图片：WebP 格式，位于 `/sprites/`

## 重要约定

- 登录逻辑以 Vue 代码为准，不与 web 版对齐
- 卡牌图片已全部转为 WebP（4341张，366MB）
- 使用 `npx vue-tsc --noEmit` 做类型检查
- 禁止在 Windows 环境使用会渲染为乱码的 emoji（⚔ 等），改用文字
- **全局不展示 `//` 前缀**：`.kicker` / `.gde-kicker` 的 `::before { content: "//" }` 已删除，禁止恢复

---

# 界面重设计：一比一还原任务（TODO）

> 源文件：`opcgpro-vue/docs/GrandUMI Redesign Offline.html`（claude design 导出的 原型）
> 目标：将下列每个页面完整迁移到对应 Vue 组件，样式/布局/动画一比一复刻。
> **执行原则：不改功能逻辑，只改视觉层（template + style）。**

---

## 一、设计稿结构总览

### 主题系统（2 套）

| key      | 名称 | 主色              | 背景                | 特效         |
| -------- | ---- | ----------------- | ------------------- | ------------ |
| `pirate` | 海贼 | `#e8b04b`（金色） | `#0e0a06`（深棕黑） | 上升火星粒子 |
| `navy`   | 海军 | `#5b9bd5`（蓝色） | `#070d18`（深海蓝） | 声呐扩散圆环 |

### 气质系统（3 种 Mood，影响字体和圆角）

| key | 名称         | 标题字体                      | UI字体         | 圆角 |
| --- | ------------ | ----------------------------- | -------------- | ---- |
| `a` | 终端         | Noto Sans SC / JetBrains Mono | JetBrains Mono | 3px  |
| `b` | 电影（默认） | Noto Serif SC / Cinzel        | Space Grotesk  | 10px |
| `c` | 游戏         | Noto Sans SC / Anton          | Space Grotesk  | 14px |

### 布局骨架（`App` shell）

```
div.app[data-theme][data-mood]
├── AnimatedBackground (canvas, z-index:0, 全屏铺底)
│   └── EmblemWatermark (SVG, 居中水印, opacity:0.06)
└── div (position:absolute inset:0, z-index:5)
    ├── TopBar (position:absolute top:0, h:56px, z-index:30)
    ├── Sidebar (position:absolute left:16 top:72 bottom:16 w:84, z-index:20) [仅登录后]
    └── Content区 (position:absolute top:0 right:0 bottom:0, left:112[登录后]/0[登录前])
        ├── LoginScreen
        ├── LobbyScreen
        ├── DeckScreen
        └── HistoryScreen
```

---

## 二、页面对应关系（设计稿 → 当前 Vue 文件）

| 设计稿 Screen               | 当前 Vue 文件                              | 子组件                                              | 状态                         |
| --------------------------- | ------------------------------------------ | --------------------------------------------------- | ---------------------------- |
| `LoginScreen`               | `src/pages/LoginPage.vue`                  | -                                                   | 需重写 template+style        |
| `LobbyScreen`（中间内容区） | `src/components/home/MainPanel.vue`        | `LobbyPanel.vue`, `DeckChoosePanel.vue`             | 需重写                       |
| `CommsPanel`（右侧聊天）    | `src/components/home/ChatPanel.vue`        | -                                                   | 需重写                       |
| `DeckScreen`                | `src/pages/DeckEditorPage.vue`             | `SearchPanel`, `SearchResultPanel`, `DeckInfoPanel` | 需重写                       |
| `HistoryScreen`             | `src/components/home/HistoryPanel.vue`     | -                                                   | 现为子组件，需确认是否独立页 |
| `TopBar`（顶部状态栏）      | 目前无独立组件                             | `ThemeSwitcher.vue` 部分逻辑                        | 需新建或整合                 |
| `Sidebar`（左侧导航）       | 目前无独立组件，逻辑在 `HomePage` 路由     | -                                                   | 需新建                       |
| `AnimatedBackground`        | `src/components/ui/AnimatedBackground.vue` | -                                                   | 需按设计稿重写 canvas 逻辑   |

**新增 CSS 变量系统：** 设计稿完全基于 CSS 变量，需在 `src/assets/` 或 `App.vue` 加入全套 token，替换当前 Tailwind 硬编码颜色。

---

## 三、CSS 设计系统（需全部落地）

### 3.1 CSS 变量（写入全局 :root + [data-theme]）

```css
/* Pirate 主题（同 :root 默认） */
[data-theme="pirate"] {
  --bg0: #0e0a06;
  --bg1: #160e08;
  --surface: #1d1510;
  --surface2: #241a12;
  --line: rgba(232, 176, 75, 0.16);
  --line-strong: rgba(232, 176, 75, 0.34);
  --ink: #f3e9d8;
  --ink-dim: rgba(243, 233, 216, 0.56);
  --ink-faint: rgba(243, 233, 216, 0.3);
  --primary: #e8b04b;
  --primary-bright: #f5cd72;
  --accent: #b23b32;
  --primary-glow: rgba(232, 176, 75, 0.45);
  --on-primary: #1a1206;
  --good: #6fcf97;
  --bad: #e26a6a;
}
/* Navy 主题 */
[data-theme="navy"] {
  --bg0: #070d18;
  --bg1: #0a1322;
  --surface: #0f1c30;
  --surface2: #143150;
  --line: rgba(120, 175, 235, 0.18);
  --line-strong: rgba(120, 175, 235, 0.4);
  --ink: #eaf1fa;
  --ink-dim: rgba(234, 241, 250, 0.58);
  --ink-faint: rgba(234, 241, 250, 0.3);
  --primary: #5b9bd5;
  --primary-bright: #8fc3f2;
  --accent: #cf4233;
  --primary-glow: rgba(95, 160, 230, 0.5);
  --on-primary: #061018;
}
/* Mood A 终端 */
[data-mood="a"] {
  --font-head: "Noto Sans SC", "JetBrains Mono", monospace;
  --head-weight: 900;
  --font-ui: "JetBrains Mono", "Noto Sans SC", monospace;
  --radius: 3px;
  --radius-lg: 5px;
  --radius-pill: 5px;
  --btn-upper: uppercase;
  --btn-spacing: 0.16em;
  --panel-blur: 8px;
}
/* Mood B 电影（默认） */
[data-mood="b"] {
  --font-head: "Noto Serif SC", "Cinzel", serif;
  --head-weight: 900;
  --font-ui: "Space Grotesk", "Noto Sans SC", sans-serif;
  --radius: 10px;
  --radius-lg: 16px;
  --radius-pill: 999px;
  --btn-upper: none;
  --btn-spacing: 0.06em;
  --panel-blur: 14px;
}
/* Mood C 游戏 */
[data-mood="c"] {
  --font-head: "Noto Sans SC", "Anton", sans-serif;
  --head-weight: 900;
  --font-ui: "Space Grotesk", "Noto Sans SC", sans-serif;
  --radius: 14px;
  --radius-lg: 22px;
  --radius-pill: 999px;
  --btn-upper: none;
  --btn-spacing: 0.04em;
  --panel-blur: 16px;
}
```

### 3.2 字体引入

需 Google Fonts 引入：`Anton`、`Cinzel 700/900`、`JetBrains Mono 400`、`Space Grotesk`、`Noto Serif SC 900`、`Noto Sans SC 900`。

### 3.3 通用 class（需提取为全局样式或 Tailwind plugin）

| class                                    | 作用                                                               |
| ---------------------------------------- | ------------------------------------------------------------------ |
| `.kicker`                                | mono 上标标签，`//` 前缀，`letter-spacing: var(--kick-spacing)`    |
| `.head`                                  | 标题字体，`font-family: var(--font-head)`                          |
| `.mono`                                  | `font-family: var(--font-mono)`                                    |
| `.dim` / `.faint`                        | `--ink-dim` / `--ink-faint` 颜色                                   |
| `.glow-title`                            | `text-shadow: 0 0 40px var(--primary-glow); color: var(--primary)` |
| `.panel`                                 | 面板：82%透明surface + border + backdrop-blur                      |
| `.ticks`                                 | 四角装饰：4个`<i>`绝对定位，各占一角，`var(--primary)` 2px border  |
| `.btn` / `.btn--primary` / `.btn--ghost` | 按钮系统，primary有shimmer扫光动画                                 |
| `.seg` / `.seg__opt`                     | 分段选择控件                                                       |
| `.field`                                 | 输入框容器，focus时 primary border + glow                          |
| `.tag`                                   | pill标签，`.is-active`变primary填充                                |
| `.dot` / `.dot--live`                    | 状态圆点，live版带pulse动画                                        |
| `.rule`                                  | 两端线条中间文字的分隔线                                           |
| `.nav-item`                              | 侧边栏导航按钮，active左侧3px glow线                               |
| `.enter > *`                             | 子元素 fadeUp 0.6s 入场动画                                        |
| `.enter-fade`                            | fadeIn 0.6s                                                        |
| `.enter-scale`                           | scaleIn 0.5s                                                       |
| `.cardslot`                              | 卡牌槽，5:7比例，hover上浮+发光                                    |

---

## 四、动画特效清单（不能遗漏）

### 4.1 AnimatedBackground（canvas 全屏）

**实现位置：** `AnimatedBackground.vue` - 完全重写 canvas 逻辑

每帧绘制顺序：

1. **填充背景色** `--bg0`
2. **飘移雾气 blob**（`globalCompositeOperation: lighter`）
   - 3个径向渐变椭圆，位置随 sin/cos 慢速漂移（周期约20s）
   - pirate: 琥珀/棕色系；navy: 蓝色系
3. **声呐环**（仅 navy 主题）
   - 每帧随机 ~0.55×dt 概率生成新环
   - 从随机位置向外扩散，life 0→1 约4.5s，透明度(1-life)×0.35
   - stroke: `var(--spark)` 蓝色，lineWidth 1.4
4. **上升粒子/火星**（46个）
   - pirate：上升速度较快(×0.9)，navy：较慢(×0.6)
   - 水平drift随sin漂移，到达顶部重置到底部
   - 透明度随sin呼吸：0.35±0.3
5. **水波纹线条**（底部38%区域）
   - 渐变填充底部背景（spark色，opacity 0.05）
   - 3条sin波浪线，y轴80%/86%/92%位置，不同频率和振幅
6. **暗角 vignette**
   - 径向渐变，中心透明→边缘 bg0 色 85%不透明

**Pirate vs Navy 差异：**

- pirate spark: `#f0c463`（金黄），sonar: false
- navy spark: `#7fc0f5`（蓝白），sonar: true

### 4.2 EmblemWatermark（SVG 水印）

- 全屏居中，`pointer-events: none`，`opacity: 0.06`
- **animation: `breathe 9s ease-in-out infinite`** opacity在0.05~0.085之间缓动
- SVG 640×640，`stroke: var(--primary)`，strokeWidth 1.4
- pirate：外圈虚线圆 + 内圆 + 交叉剑（strokeWidth 3）+ 护手圆
- navy：外圈虚线圆 + 内圆 + 锚图标（圆+竖线+横线+弧形+小竖线）

### 4.3 按钮动画

- `.btn` hover: `translateY(-1px)`，active: `scale(0.99)`，颜色transition 0.25s
- `.btn--primary` hover: shimmer扫光（`::after` 白色渐变从 left:-60% 移到 left:120%，0.6s ease）
- `.btn--primary` hover: box-shadow增强至 `0 14px 34px -8px var(--primary-glow)`

### 4.4 入场动画（页面切换时触发）

```css
@keyframes fadeUp {
  from {
    opacity: 0.5;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
@keyframes fadeIn {
  from {
    opacity: 0.45;
  }
  to {
    opacity: 1;
  }
}
@keyframes scaleIn {
  from {
    opacity: 0.55;
    transform: scale(0.96);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
```

- `.enter > *`：子元素依次 fadeUp 0.6s（Login 右侧面板用 enter-scale）
- 页面切换时通过 Vue `key` 触发重新挂载，即可复现入场动画
- 尊重 `prefers-reduced-motion`：所有动画设 `animation: none !important`

### 4.5 其他动画

- `.dot--live`：`pulse 1.8s ease-in-out infinite`（opacity 1→0.35→1）
- `.caret`：terminal光标闪烁，`blink 1s step-end infinite`
- CardSlot hover: `translateY(-6px) scale(1.02)` + `box-shadow: 0 0 26px -6px var(--cc)`（卡牌颜色光晕）
- `.field` focus-within: `border-color: var(--primary)` + `box-shadow: 0 0 0 3px var(--primary-glow)`

---

## 五、逐页详细规格

### 5.1 [ TODO ] LoginPage.vue → 对标 LoginScreen

**布局：** `position:absolute inset:0; display:flex`（水平两栏）

**左侧英雄区**（`flex: 1.15`，居中纵列，padding 40px，gap 22px，text-center）

- `.kicker`（fontSize 13）：`ONE PIECE TCG`
- `h1.head.glow-title`（`font-size: clamp(56px, 8vw, 132px)`，letterSpacing 0.06em）：`GRANDUMI`
- `.rule`（width 320）：`ONLINE · BATTLE · TERMINAL`
- 主题文案（`color: var(--accent)`，fontSize 16，letterSpacing 0.04em）：pirate="准备好了吗，航海王？" / navy="为了绝对的正义，起锚。"
- 状态行（`.mono.dim`，fontSize 12，flex，gap 8）：`.dot.dot--live` + "服务器已连接 · [faction]阵营"

**右侧终端面板**（width 520px，居中，padding 40px）

- `.panel.panel-pad.enter-scale`（width 100%，maxWidth 420px）
- `<Ticks />`（四角装饰）
- `.rule`：`LOGIN TERMINAL`
- `.seg`（width 100%，mb 20）：登录/注册 两个 `.seg__opt`
- 账号 `.field`：`@` 图标 + input placeholder="账号"
- 密码 `.field`：`·` 图标 + input type=password + 眼睛图标按钮（切换明文）
- 注册时额外显示确认密码 `.field`（`.enter-fade` 入场动画）
- `.btn.btn--primary.btn--lg.btn--block`（mt 6）：pirate="启 航" / navy="起 锚"
- 分隔线（height 1，var(--line)，margin 20px 0 14px）
- 状态行（`.mono.dim`，fontSize 12，居中）：`.dot` + "服务器已连接"

**注意：** 保留现有网络请求逻辑，只替换 template 和 style。

---

### 5.2 [ TODO ] MainPanel.vue + ChatPanel.vue → 对标 LobbyScreen + CommsPanel

**整体布局：** `position:absolute inset:0; display:flex`

**中间内容区**（`flex:1`，class="enter scroll"，纵列居中，padding: 80px 40px 40px，gap 26px，overflow:auto）

- `<ScreenHead>`（共享组件）：kicker="GRAND UMI · LOBBY"，title="对战大厅"
- **当前卡组卡片**（`.panel`，width `min(560px, 80%)`，padding 22，flex，gap 20，align-items center）
  - `<Ticks />`
  - 卡组颜色方块（64×64，`border-radius: var(--radius)`，`background: linear-gradient(160deg, var(--accent), var(--bg1))`，flex居中，font-head 22px）
  - 卡组信息（flex:1）：`.mono.faint`（10px，letterSpacing 0.16em）+ `.head`（22px）+ `.mono.dim`（12px）
  - `.btn.btn--ghost`：更换 →
- **操作区**（flex，gap 16，wrap，justify-center）
  - `.btn.btn--primary.btn--lg`（minWidth 220，fontSize 18）：▶ 开始匹配
  - `.btn`：单人测试
  - `.seg`：先手/后手
- `.rule`（width 320，margin 4px 0）：或与好友对战
- 好友房间按钮（flex，gap 16）：＋ 创建房间 / → 加入房间

**右侧通讯面板**（`.panel`，width 320，margin: 72px 16px 16px 0，flex column）

- `<Ticks />`
- 头部（padding 16px 18px，border-bottom，flex space-between）：`.kicker`="COMMS" + `.mono.dim`（.dot.dot--live + "在线 1,284"）
- 聊天列表（flex:1，overflow:auto，padding 16，flex column，gap 12）
  - 每条消息：`alignSelf: me?flex-end:flex-start`，maxWidth 85%
  - 用户名：`.mono.faint`（9px，mb 3，textAlign对齐方向）
  - 气泡：fontSize 13，padding 9px 12px，`border-radius: var(--radius)`，me用primary色/对方用surface2+border
- 输入区（padding 14，border-top，flex，gap 8）：`.field`（flex:1，h 44）+ `.btn.btn--primary`（padding 0 18px）："发"

---

### 5.3 [ TODO ] DeckEditorPage.vue → 对标 DeckScreen

**整体布局：** `position:absolute inset:0; padding: 64px 16px 16px; display:flex; gap:14px`

**左侧筛选栏**（`.panel.scroll.enter`，width 230，padding 18，overflow:auto，flex column，gap 18）

- `<Ticks />`
- `.kicker`（11px）：搜索卡牌
- `.field`（h 44）：搜索输入
- `.btn.btn--block`：选择领航卡
- `FilterGroup` 类型：全部/角色/场地/事件
- 颜色 chips：6色（红/绿/蓝/紫/黑/黄），对应颜色 borderColor + color
- `FilterGroup` 稀有度：全部/L/SR/R/UC/C/SEC（small尺寸）
- 弹数：OP主弹/ST起始/EB-PRB/P-其他，每项为下拉触发器样式的 `.field`

**中间卡牌格**（`flex:1`，`.scroll.enter`，overflow:auto，minWidth 0）

- 标题行（flex，space-between，mb 14）：结果数 `.kicker` + 列数 `.mono.faint`
- CSS Grid：`grid-template-columns: repeat(auto-fill, minmax(118px, 1fr))`，gap 12
- 每个 `.cardslot`（`--cc:卡牌颜色`）：
  - `aspect-ratio: 5/7`，border 1.5px solid `var(--cc)`，background var(--surface2)
  - `.cost`：左上角圆形徽章，background `var(--cc)`，deep dark text
  - `.pw`：右上角战力，白色mono加粗
  - `.art`：斜纹填充的占位区，中间 `.mono` 灰色"卡图"文字
  - `.nm`：底部名称，黑色半透明背景，border-top
  - hover：`translateY(-6px) scale(1.02)` + 颜色光晕 `box-shadow: 0 0 26px -6px var(--cc)`

**右侧卡组面板**（`.panel.scroll.enter`，width 290，padding 18，overflow:auto，flex column，gap 14）

- `<Ticks />`
- 顶部：`.kicker`="卡组" + 操作 tags（新建/读取/清空）
- 卡组名称 `.field`（h 44）
- 格式 `.seg`（100%宽）：自由/OP15/OP16，等宽排列
- **领航卡槽**：虚线 border（1.5px dashed `var(--line-strong)`），居中 `?`（font-head 28px primary色）
- **费用曲线**：`.mono.faint`标题 + flex等宽柱状图（height 56，10根柱，`border-radius:3px 3px 0 0`，var(--primary) opacity渐变）
- **张数计数**：X/50 + 状态文字 + 进度条（h 6，rounded，var(--primary)，transition width 0.3s）
- `.btn.btn--primary.btn--block`（disabled时 total<1）：保存卡组

---

### 5.4 [ TODO ] HistoryPanel.vue（或新建 HistoryPage.vue）→ 对标 HistoryScreen

**布局：** `.scroll.enter`，`position:absolute inset:0`，`padding: 76px 40px 24px`，overflow:auto

- 内容区 maxWidth 880，margin 0 auto

**顶部标题**

- `.kicker`（12px）：BATTLE LOG
- `h1.head`（fontSize 40，margin 10px 0 4px）：对局记录
- `.dim`（13px，mb 24）：提示仅保留本设备最近30局

**统计卡片**（3列 grid，gap 14，mb 26）

- 每格 `.panel.panel-pad`（padding 20，text-center）：
  - `<Ticks />`
  - `.head.glow-title`（fontSize 38）：数值
  - `.mono.faint`（11px，letterSpacing 0.14em，mt 6）：标签
- 三项：总场次 / 胜率 / 当前连胜

**对局列表**（flex column，gap 10）

- 每行 `.panel`（flex，gap 18，padding 14px 20px，align-items center）：
  - W/L 徽章（44×44，`border-radius: var(--radius)`，font-head 900 20px）：W=primary渐变，L=var(--accent)
  - 领航颜色圆点（10×10，对应颜色+颜色光晕）
  - 对局信息（flex:1，minWidth 0）：对手名（overflow ellipsis）+ `.mono.faint`（先/后手 · 用时）
  - 时间（`.mono.dim`，12px，flex-shrink 0）
  - 回放按钮（`.btn.btn--ghost`，padding 8px 14px，13px，flex-shrink 0）

---

### 5.5 [ TODO ] 新建 TopBar.vue 共享组件

**位置规格：** `position:absolute; top:0; left:0; right:0; height:56px; z-index:30`
**布局：** flex，space-between，padding 0 20px，`pointer-events:none`

**左侧**（`pointer-events:auto`，cursor:pointer，点击返回 login）

- `.kicker`（fontSize 11）：`.dot.dot--live` + `SYS://ONLINE · V2.0 · CYP_2026`

**右侧**（flex，gap 12，`pointer-events:auto`）

- `.mono.faint`（10px，letterSpacing 0.12em）：字型
- `<MoodToggle>`：A/B/C 三个按钮，pill形，active=primary填充
- `.mono.faint`（10px）：阵营
- `<ThemeToggle>`：帽子(pirate)/锚(navy) 两个按钮，pill形

---

### 5.6 [ TODO ] 新建 Sidebar.vue 共享组件

**位置规格：** `position:absolute; left:16px; top:72px; bottom:16px; width:84px; z-index:20`
**样式：** `.panel`，flex column，align-items center，`border-radius: var(--radius-lg)`

**内部结构（从上到下）：**

- `<Ticks />`
- **头像区**（padding 18px 0 10px，flex column，gap 6）
  - 头像圆（44×44，border 2px `var(--primary)`，background `var(--surface2)`，font-head 900，内显等级数字，`box-shadow: 0 0 18px -4px var(--primary-glow)`）
  - 用户名（`.mono.faint`，10px）
- 分隔线（width 40，height 1，`var(--line)`，margin 4px 0 8px）
- **导航列表**（flex:1，width 100%，flex column，gap 2）
  - 三项：`{id:'lobby', glyph:'厅', lbl:'大厅'}` / `{id:'deck', glyph:'组', lbl:'卡组'}` / `{id:'history', glyph:'战', lbl:'战绩'}`
  - 每项 `.nav-item`：flex column center，padding 14px 0
    - `.glyph`（font-head 900，24px）
    - `.lbl`（mono，10px，letterSpacing 0.1em）
    - active状态：color `var(--primary)` + 左侧3px glow线（`::before`，top/bottom 18%，`box-shadow:0 0 14px var(--primary-glow)`）
- **底部状态**（paddingBottom 16，flex column center，gap 6）
  - `.dot.dot--live`
  - `.mono.faint`（9px，`writing-mode:vertical-rl`，letterSpacing 0.1em）：已连接

---

## 六、实现步骤（执行顺序）

### Step 1：建立 CSS 设计系统

- [ ] 在 `src/assets/design-system.css`（或 `App.vue` style）写入所有 CSS 变量（:root / [data-theme] / [data-mood]）
- [ ] 引入 Google Fonts（Anton/Cinzel/JetBrains Mono/Space Grotesk/Noto Serif SC/Noto Sans SC）
- [ ] 写入所有通用 class（.kicker/.head/.mono/.panel/.ticks/.btn/.seg/.field/.tag/.dot/.rule/.enter 等）
- [ ] 写入所有 @keyframes（pulse/breathe/fadeUp/fadeIn/scaleIn/blink/spin）
- [ ] 在 `App.vue` 的根 div 上绑定 `data-theme` 和 `data-mood` 属性，从状态管理读取

### Step 2：重写 AnimatedBackground.vue

- [ ] canvas 全屏，useResizeObserver
- [ ] 实现 5 层绘制：背景色 → 雾气blob → 声呐环(navy) → 粒子 → 水波纹 → 暗角
- [ ] props 接受 themeKey，themeKey 变化时 watch 重新初始化

### Step 3：新建 TopBar.vue + Sidebar.vue

- [ ] TopBar：左侧状态 + 右侧气质/主题切换
- [ ] Sidebar：头像 + 导航 + 连接状态
- [ ] 在 `HomePage.vue` 或 layout 层组合使用

### Step 4：重写 LoginPage.vue template+style

- [ ] 左侧英雄区 + 右侧 enter-scale 面板
- [ ] 保留现有 ref/网络逻辑，仅替换 template 结构和样式

### Step 5：重写 MainPanel.vue + ChatPanel.vue

- [ ] LobbyScreen 中间区（ScreenHead + 卡组卡片 + 操作按钮 + 好友房间）
- [ ] CommsPanel（聊天区）
- [ ] ScreenHead 抽出为独立组件

### Step 6：重写 DeckEditorPage.vue template

- [ ] 三栏布局（筛选 230 / 网格 flex:1 / 卡组 290）
- [ ] cardslot 样式（颜色边框 + hover浮起 + 费用/战力徽章）
- [ ] 费用曲线组件

### Step 7：补全 HistoryPanel.vue / HistoryPage.vue

- [ ] 三列统计 + 对局列表行

### Step 8：联调

- [ ] 切换主题时 canvas + 所有 CSS 变量同步更新
- [ ] 切换气质时字体/圆角即时响应
- [ ] 路由切换时 `.enter` 入场动画触发（通过 key 强制重渲染）
- [ ] prefers-reduced-motion 媒体查询

---

## 七、共享子组件清单（需新建或复用）

| 组件名            | 说明                                              |
| ----------------- | ------------------------------------------------- |
| `Ticks.vue`       | 四角金色装饰线，position:relative 的 panel 内使用 |
| `ScreenHead.vue`  | kicker + 大标题 + 菱形分隔线 + 副标题             |
| `TopBar.vue`      | 顶部 56px 状态/切换栏                             |
| `Sidebar.vue`     | 左侧 84px 导航栏                                  |
| `MoodToggle.vue`  | A/B/C 气质切换按钮组                              |
| `ThemeToggle.vue` | 帽子/锚 主题切换（可复用现有 ThemeSwitcher）      |
