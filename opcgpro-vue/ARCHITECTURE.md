# opcgpro-vue — 项目架构说明

## 项目定位

航海王卡牌对战（One Piece Card Game）纯前端客户端，基于 **Vue 3 + Vite**。  
所有游戏逻辑由 C# 服务端权威执行，前端只做**读取状态 + 发送指令**，零游戏结算代码。

---

## 目录结构

```
src/
├── main.ts                   # 入口：createApp → use(router) → mount
├── App.vue                   # 全局壳：启动 WS、监听路由跳转信号
├── style.css                 # 全局 Tailwind 基础样式
│
├── router/
│   └── index.ts              # Vue Router（History 模式，懒加载页面）
│
├── pages/                    # 路由级页面组件（一路由一文件）
│   ├── HomePage.vue          # 登录 / 大厅 / 匹配 / 房间
│   ├── DeckEditorPage.vue    # 卡组编辑器
│   ├── GamePage.vue          # 对战主界面
│   ├── SpectatePage.vue      # 观战
│   ├── ReplayPage.vue        # 回放
│   └── LoadingPage.vue       # 加载过渡
│
├── components/               # 功能组件（按页面分包）
│   ├── home/                 # LoginPanel / LobbyPanel / DeckChoosePanel ...
│   ├── deck-editor/          # SearchPanel / DeckInfoPanel / CostCurve ...
│   ├── game/                 # PlayerMat / HandArea / FieldArea / AnimationLayer ...
│   └── ui/                   # 通用：CardItem / Modal / MessageBox / NetStatePanel
│
├── composables/              # Vue 组合式 API（逻辑复用）
│   ├── useNet.ts             # 启动 WS 连接、注册协议处理器（App.vue 调用一次）
│   ├── useStore.ts           # vanilla store → Vue Ref 桥接（订阅 store 切片）
│   ├── useGameInit.ts        # GamePage 挂载时初始化游戏状态
│   ├── useGameAnimation.ts   # 动作驱动动画（监听 lastAction）
│   ├── useGameAudio.ts       # 游戏内音效触发
│   ├── useAudio.ts           # 全局背景音乐 / 音效控制
│   ├── usePlayback.ts        # 回放模式播放控制
│   ├── useResponsive.ts      # 窗口尺寸响应式适配
│   └── useVirtualList.ts     # 大列表虚拟滚动
│
├── store/                    # 全局状态（zustand/vanilla，无 React 依赖）
│   ├── gameStore.ts          # 对战状态镜像（syncFromServer 是唯一写入口）
│   ├── netStore.ts           # 连接状态、登录信息、路由跳转信号
│   ├── battleStore.ts        # 战斗流程暂态（攻击/防御/计算阶段）
│   ├── deckStore.ts          # 卡组编辑器状态
│   ├── audioStore.ts         # 音频开关 / 音量
│   └── settingsStore.ts      # 用户设置持久化
│
├── net/                      # 网络层（与 C# 服务端通信）
│   ├── NetManager.ts         # WebSocket 单例（连接/重连/心跳/消息队列）
│   ├── eventBus.ts           # 内部事件总线（解耦 NetManager ↔ 协议处理器）
│   ├── HomeProtocol.ts       # 大厅协议注册 + HomeRequest 发送方法
│   ├── GameProtocol.ts       # 对战协议注册（syncFromServer / prompt / battle）
│   └── GameRequest.ts        # 对战指令发送（出牌/攻击/DON分配/投降 ...）
│
├── types/                    # TypeScript 类型定义
│   ├── net.ts                # 所有 Msg* 消息类型（字段名与 C# 完全一致）
│   ├── game.ts               # BattlePhase / GameMode / 游戏内枚举
│   ├── card.ts               # 卡牌数据结构（CardData / DeckCard ...）
│   └── playback.ts           # 回放数据结构
│
├── data/                     # 静态数据 / 资源加载
│   ├── CardLoader.ts         # 从 /data/*.json 加载卡牌数据 + 图片路径解析
│   ├── DeckMapper.ts         # 卡组字符串 ↔ 卡牌列表 互转
│   ├── MockPlayback.ts       # 开发用模拟回放数据
│   ├── cardSets.ts           # 所有卡集路径常量（CARD_SET_PATHS）
│   └── gameLabels.ts         # 阶段显示文本（PHASE_LABELS）
│
└── lib/                      # 纯工具函数
    └── colorMap.ts           # 颜色数据值 ↔ 显示名互转 + Tailwind 样式映射
```

---

## 数据 / 资产（public/）

```
public/
├── data/
│   ├── imageManifest.json    # 卡图路径清单（CardLoader 读取）
│   ├── OP01.json ~ OP16.json # 卡牌数据（各卡集）
│   ├── ST01.json ~ ST30.json
│   ├── EB01.json ~ EB04.json
│   └── P.json / PRB01.json ...
├── cards/
│   ├── op01/                 # 卡牌图片（小写目录名）
│   │   ├── OP01-001.png
│   │   └── ...
│   ├── st01/ op02/ eb01/ ... # 共约 4500+ 张（由 download_cards.mjs 下载）
│   └── ...
└── audio/                    # 背景音乐 / 音效
```

> 卡图不入 git（版权原因）。初次部署或图片丢失时运行：  
> `node D:/code/GrandUMI/download_cards.mjs`

---

## 本地运行

### 1. 启动 C# 后端

```bash
cd D:/code/GrandUMI/服务端WebSocket
dotnet run
# WebSocket 监听 ws://localhost:8080/ws
```

需要 .NET 10 SDK（`dotnet --version` 应输出 `10.x.x`）。  
安装：`winget install Microsoft.DotNet.SDK.10`

### 2. 启动前端开发服务器

```bash
npm --prefix D:/code/GrandUMI/opcgpro-vue run dev
# http://localhost:5173
```

### 3. （可选）下载卡牌图片

```bash
node D:/code/GrandUMI/download_cards.mjs
# 断点续传，已有的自动跳过
```

---

## 全链路请求流程

```
用户操作（点击按钮）
    │
    ▼
组件调用 HomeRequest.xxx() 或 GameRequest.xxx()
    │  （net/HomeProtocol.ts / net/GameRequest.ts）
    │
    ▼
NetManager.send(msg)        ← 序列化为 JSON
    │
    ▼  WebSocket
C# 服务端处理（游戏逻辑、结算、广播）
    │
    ▼  WebSocket（JSON）
NetManager.onmessage
    │
    ▼
eventBus.emit("message", msg)
    │
    ├─ HomeProtocol 监听器 → 更新 netStore（登录/匹配/房间状态）
    └─ GameProtocol 监听器 → gameStore.syncFromServer(msg)
                                        │
                                        ▼
                             组件通过 useStore(useGameStore, s => s.xxx)
                             自动获得响应式 Ref，模板重渲染
```

---

## WebSocket 连接生命周期

```
App.vue 挂载
    └─ useNet()
         ├─ registerHomeProtocols()   // 仅首次调用
         ├─ registerGameProtocols()   // 仅首次调用
         └─ NetManager.connect("ws://localhost:8080/ws")
                  │
                  ├─ onopen  → 发送 { proto:"MsgSecret", vesion:"0.998" }（握手）
                  ├─ onmessage MsgSecret 回包 → state="connected" → eventBus.emit("connectSucc")
                  ├─ onclose（非主动）→ 指数退避重连（最多 6 次，2s/4s/8s/16s/32s/64s）
                  └─ onclose（主动 disconnect()）→ 不重连
```

连接状态同步到 `netStore.connState`，`NetStatePanel` 组件负责展示。

---

## 路由跳转机制

前端**禁止**直接调用 `router.push`，所有跳转通过 `netStore.navigateTo` 信号：

```
协议处理器（HomeProtocol / GameProtocol）
    └─ useNetStore.getState().setNavigateTo("/game")
            │
            ▼
App.vue watch(navigateTo)
    └─ router.push(path)   // SPA 内导航，不断 WebSocket
```

这样确保页面切换时 WebSocket 单例保持连接。

---

## Store 使用规范

所有 store 用 **zustand/vanilla**（`createStore`），不依赖 React。

- 在组件外（协议处理器、composable）：直接 `useXxxStore.getState().method()`
- 在组件内（响应式）：`const val = useStore(useXxxStore, s => s.field)` 返回 `Ref<T>`

```ts
// composables/useStore.ts 原理
import { shallowRef, onScopeDispose } from "vue";
store.subscribe(state => {
  const next = selector(state);
  if (!Object.is(next, ref.value)) ref.value = next; // shallowRef + Object.is 去重
});
onScopeDispose(unsubscribe); // 组件卸载自动退订
```

---

## 协议字段名约定

`types/net.ts` 中的消息字段名**严格对齐 C# ProtoMember 命名**，含已知拼写怪癖：

| 字段 | 说明 |
|------|------|
| `vesion` | 握手版本号（非 version，C# 原始拼写） |
| `IsWin` | 胜负标志（大写，C# PascalCase） |
| `MainDeck` | 主卡组字符串（PascalCase） |
| `EnemyDeck` | 对手卡组字符串（PascalCase） |
| `IsFirst` | 是否先手（PascalCase） |

**不要"修正"这些字段名**，否则与服务端协议不匹配。

---

## 构建验证

```bash
npm --prefix D:/code/GrandUMI/opcgpro-vue run build
# vue-tsc -b（类型检查）+ vite build（打包）
# 正常输出：✓ built in ~4s，无 error
```
