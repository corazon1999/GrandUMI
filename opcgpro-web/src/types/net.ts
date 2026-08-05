// 协议枚举 — 与 C# ProtocolEnum.cs 完全一致
export enum ProtocolEnum {
  None = 0,
  MsgSecret = 1,
  MsgPing = 2,
  MsgGetRoomList = 4,
  MsgQuitRoom = 5,
  MsgEnemyQuit = 6,
  MsgEnterRoom = 7,
  MsgEnemyEnter = 8,
  MsgPrepare = 9,
  MsgGameStart = 10,
  MsgShuffle = 11,
  MsgSurrender = 12,
  MsgDuelOver = 13,
  MsgChatMsg = 14,
  MsgTransmit = 15,
  MsgDisconnect = 16,
  MsgDefeat = 17,
  MsgLogin = 18,
  MsgUpdatePs = 19,
  MsgAddAccount = 20,
  MsgEnterMatch = 21,
  MsgCancelMatch = 22,
  MsgMatchFound = 23,
  MsgCreateRoom = 24,
  MsgJoinRoom = 25,
  MsgCancelRoom = 26,
  // Sprint 3: 服务端结算协议
  MsgGameState = 27,       // 服务器 → 客户端：权威游戏快照
  MsgGameAction = 28,      // 客户端 → 服务器：游戏动作请求
  MsgRequestState = 29,    // 客户端 → 服务器：重连后请求完整快照
  MsgPlayerDisconnected = 30, // 服务器 → 客户端：对手断线通知
  MsgPlayerReconnected = 31, // 服务器 → 客户端：对手重连通知
}

// WebSocket JSON 消息基类
// 字段名 proto 使用枚举名字符串（如 "MsgLogin"），与 C# EncodeName 一致
export interface MsgBase {
  proto: string; // ProtocolEnum 枚举名
}

// ── 握手 ──────────────────────────────────────────────────────────────
// 连接后客户端第一个消息；服务器返回 Secret 密钥和版本校验结果
// C#: MsgSecret.vesion（原始拼写保留，与服务器匹配）
export interface MsgSecret extends MsgBase {
  proto: "MsgSecret";
  vesion?: string;   // 客户端版本号（发送）
  Secret?: string;   // 服务器返回的 AES 密钥
  result?: boolean;  // 版本是否匹配
}

// ── 心跳 ──────────────────────────────────────────────────────────────
export interface MsgPing extends MsgBase {
  proto: "MsgPing";
}

// ── 账户 ──────────────────────────────────────────────────────────────
// 字段名与 C# LobbyMsg.cs [ProtoMember] 完全一致
export interface MsgLogin extends MsgBase {
  proto: "MsgLogin";
  account: string;
  password?: string;  // 已不再校验密码，仅为兼容旧协议字段保留
  name?: string;     // 服务器返回的玩家昵称
  result?: boolean;  // true = 成功（C# 中是 bool 不是 int）
  logStr?: string;   // 服务器提示文本
}

export interface MsgAddAccount extends MsgBase {
  proto: "MsgAddAccount";
  id: string;
  password: string;
  name: string;
}

export interface MsgUpdatePs extends MsgBase {
  proto: "MsgUpdatePs";
  newPs: string;
  result?: boolean;
  logStr?: string;
}

// ── 匹配 ────────────────────────────────────────────────────────────────
export interface MsgEnterMatch extends MsgBase {
  proto: "MsgEnterMatch";
  deck: string;
  result?: boolean;
  logStr?: string;
}

export interface MsgCancelMatch extends MsgBase {
  proto: "MsgCancelMatch";
}

// 单人测试模式：与机器人对战
export interface MsgEnterBotMatch extends MsgBase {
  proto: "MsgEnterBotMatch";
  deck: string;
  goFirst?: boolean;   // 单人测试先后手：true=人类先手(默认)，false=后手
  result?: boolean;
  logStr?: string;
}

export interface MsgMatchFound extends MsgBase {
  proto: "MsgMatchFound";
  opponentName: string;
}

// ── 房间码对战 ──────────────────────────────────────────────────────────
export interface MsgCreateRoom extends MsgBase {
  proto: "MsgCreateRoom";
  deck: string;
  roomCode?: string;  // 服务器返回的房间码
  result?: boolean;
}

export interface MsgCancelRoom extends MsgBase {
  proto: "MsgCancelRoom";
}

export interface MsgJoinRoom extends MsgBase {
  proto: "MsgJoinRoom";
  roomCode: string;
  deck: string;
  result?: boolean;
  logStr?: string;
  opponentName?: string;
}

// ── 准备 / 开始 ─────────────────────────────────────────────────────────
// 发送：自己的准备状态和卡组字符串
// 接收：对手的准备状态和卡组（服务器转发）
export interface MsgPrepare extends MsgBase {
  proto: "MsgPrepare";
  IsPrepare?: boolean;
  deck?: string; // DeckMapper.ListToString 格式：卡号以逗号分隔
}

export interface MsgGameStart extends MsgBase {
  proto: "MsgGameStart";
  MainDeck?: string;  // 自己的卡组（1张领航+50张）
  EnemyDeck?: string; // 对手的卡组
  IsFirst?: boolean;  // 是否先手
}

// ── 游戏中 ──────────────────────────────────────────────────────────────
export interface MsgShuffle extends MsgBase {
  proto: "MsgShuffle";
  MainDeck?: string; // 服务器生成的随机卡组顺序
  Camp?: string;
}

// 服务器发送游戏操作（透传字符串，AcceptManager 解析）
export interface MsgTransmit extends MsgBase {
  proto: "MsgTransmit";
  Msg: string; // RequestManager 序列化的操作字符串
}

export interface MsgSurrender extends MsgBase {
  proto: "MsgSurrender";
}

export interface MsgDefeat extends MsgBase {
  proto: "MsgDefeat";
}

export interface MsgDisconnect extends MsgBase {
  proto: "MsgDisconnect";
}

export interface MsgDuelOver extends MsgBase {
  proto: "MsgDuelOver";
  IsWin: boolean;
  Description: string;
}

// ── 聊天 ────────────────────────────────────────────────────────────────
// 字段名与 C# 完全一致：Name / Msg / type
export interface MsgChatMsg extends MsgBase {
  proto: "MsgChatMsg";
  type?: number; // 0=大厅, 1=房间, 2=游戏中
  Name?: string;
  Msg?: string;
}

// 局内聊天（房间内：对战双方 + 观战者）。区别于大厅全局 MsgChatMsg。
// 客户端→服务器只需带 Text（+可选 Code 预设短语编号）；服务器→客户端回带发送者信息。
export interface MsgGameChat extends MsgBase {
  proto: "MsgGameChat";
  text: string;
  code?: string | null;       // 预设短语编号（自由文字为 null）
  fromSeat?: number;          // 0/1=玩家座位, -1=观战者
  fromAccount?: string;       // 发送者账号（客户端据此判断是否为自己）
  fromName?: string;          // 发送者显示名
  fromRole?: "player" | "spectator";
}

// ── 在线人数 ──────────────────────────────────────────────────────────────
// 服务器 → 客户端：当前在线（已登录）人数，登录/断开时广播
export interface MsgOnlineCount extends MsgBase {
  proto: "MsgOnlineCount";
  count?: number;
}

// ── 在线玩家列表 + 邀请对战 ──────────────────────────────────────────────
export interface PlayerInfo {
  account: string;
  name: string;
  status: "idle" | "matching" | "playing";
  roomId?: string | null;   // 对战中玩家所在的对局房间ID，供一键观战；无对局房间时为 null
}

// 客户端 → 服务器:请求在线玩家列表;服务器 → 客户端:返回列表
export interface MsgPlayerList extends MsgBase {
  proto: "MsgPlayerList";
  players?: PlayerInfo[];
}

// 客户端 → 服务器:邀请某玩家对战(带自己卡组);服务器 → 发起方:回执
export interface MsgInvitePlayer extends MsgBase {
  proto: "MsgInvitePlayer";
  toAccount?: string;
  deck?: string;
  result?: boolean;
  toName?: string;
  logStr?: string;
}

// 服务器 → 被邀请方:收到邀请
export interface MsgInviteNotify extends MsgBase {
  proto: "MsgInviteNotify";
  inviteId: string;
  fromName: string;
}

// 客户端 → 服务器:应答邀请(接受时带自己卡组)
export interface MsgInviteResponse extends MsgBase {
  proto: "MsgInviteResponse";
  inviteId: string;
  accept: boolean;
  deck?: string;
}

// 服务器 → 发起方:邀请被拒/失效(接受成功则走 MsgGameStart,不发此协议)
export interface MsgInviteResult extends MsgBase {
  proto: "MsgInviteResult";
  accepted: boolean;
  byName?: string;
  logStr?: string;
}

// ── 友谊战房间 ──────────────────────────────────────────────────────────
export interface FriendlyPlayer {
  account: string;
  name: string;
  deckName: string | null;
  ready: boolean;
}

// 服务器 → 客户端:友谊战房间完整状态
export interface MsgFriendlyRoom extends MsgBase {
  proto: "MsgFriendlyRoom";
  roomId: string;
  players: FriendlyPlayer[];
  scores: number[];
  state: "lobby" | "playing";
  error?: string | null;
}

// 客户端 → 服务器:在房间内选卡组
export interface MsgFriendlySelectDeck extends MsgBase {
  proto: "MsgFriendlySelectDeck";
  deck: string;
  deckName: string;
}

// 客户端 → 服务器:切换准备状态
export interface MsgFriendlyReady extends MsgBase {
  proto: "MsgFriendlyReady";
  ready: boolean;
}

// 客户端 → 服务器:退出房间
export interface MsgFriendlyLeave extends MsgBase {
  proto: "MsgFriendlyLeave";
}

// 服务器 → 客户端:房间已解散/已退出
export interface MsgFriendlyLeft extends MsgBase {
  proto: "MsgFriendlyLeft";
  logStr?: string;
}

// ── Sprint 3: 服务端结算游戏协议 ─────────────────────────────────────────

/** 客户端可发送的游戏动作类型（服务端权威结算） */
export type GameActionType =
  | "Mulligan"          // { redraw: boolean }
  | "PlayCard"          // { handIndex: number, freeCost?: boolean }
  | "AttachDon"         // { targetId: "leader" | cardId, count: number }
  | "Attack"            // { attackerId: cardId | "leader", targetIsLeader: boolean, targetId?: cardId }
  | "DeclareBlocker"    // { blockerId: cardId }
  | "PassBlock"         // {}
  | "PlayCounter"       // 反击值: { handIndex, useCounterIcon: true }；反击事件: { handIndex }
  | "PassCounter"       // {}
  | "UseEffect"         // { sourceId, effectKey, ... }
  | "EndTurn"           // {}
  | "ConfirmDamage"     // {}
  | "DebugAddCard"      // { cardNumber: string }  ← GM 调试：加牌到手牌
  | "DebugAddLife"      // { cardNumber: string; target: "self" | "opponent" } ← GM 调试：置于生命区顶端
  | "DebugAddDon"       // { count: number }       ← GM 调试：加咚
  | "DebugRefreshDon"   // {}                       ← GM 调试：刷新所有咚
  | "DebugSummon"       // { cardNumber: string; target: "self" | "opponent" }  ← GM 调试：召唤到场上
  | "DebugKoAll"        // { target: "self" | "opponent" }                       ← GM 调试：KO 一方全部角色
  | "DebugRestAll"      // { target: "self" | "opponent" }                       ← GM 调试：横置一方全部角色
  | "DebugLeaderAttack" // {}                       ← GM 调试：对手领袖攻击我方领袖
  | "DebugRunOP17Coverage" // {}                    ← GM 调试：巡检当前领航颜色的全部 OP17 卡牌
  | "Surrender";        // {}

/** 服务器推送的场地卡快照 */
export interface FieldCardSnapshot {
  id: string;
  number: string;
  isTapped: boolean;
  powerCurrent: number;
  cost: number;              // 当前费用（含持续光环，如 OP16-080 对方回合 +1）
  attachedDon: number;
  gainedKeywords: string[];
  cannotActivateNextReset: boolean;
  cannotBeRested: boolean;   // 无法被效果转为休息状态
  activatedUsedThisTurn: boolean;  // 本回合【启动主要】【每回合1次】是否已用（已用则隐藏启动按钮）
  turnPlayed: number;
  canAttack: boolean;        // 该角色当前是否可发起攻击（后端权威，对手/非我方回合恒 false）
}

/** 服务器推送的单方玩家快照（已按视角脱敏） */
export interface PlayerSnapshot {
  name: string;
  handCardNumbers: string[];  // 仅自己有内容
  handCardCosts: number[];    // 每张手牌的有效费用（含静态减费），仅自己有内容；对手为空
  handCardCounters: number[]; // 每张手牌的有效反击值（含静态光环），仅自己有内容；对手为空
  handCount: number;
  fieldCards: FieldCardSnapshot[];
  stageNumber: string | null;
  stageId: string | null;
  stageTapped: boolean;
  trashNumbers: string[];
  deckCount: number;
  lifeCount: number;
  lifeNumbers: string[];      // 始终为空，由 Prompt 单独公开
  leaderId: string;
  leaderNumber: string;
  leaderTapped: boolean;
  leaderPower: number;
  leaderAttachedDon: number;
  leaderCanAttack: boolean;   // 领袖当前是否可发起攻击（后端权威）
  leaderActivatedUsedThisTurn: boolean;  // 领袖【启动主要】【每回合1次】本回合是否已用
  stageActivatedUsedThisTurn: boolean;   // 舞台【启动主要】【每回合1次】本回合是否已用
  costActive: number;
  costRest: number;
  costAttached: number;
  donDeckCount: number;
  hasReDraw: boolean;
  mulliganDone: boolean;
}

/** 服务器推送的 prompt 信息 */
export interface PromptSnapshot {
  promptId: string;
  kind: string;
  text: string;
  validChoices: string[];
  minChoose: number;
  maxChoose: number;
  extra: Record<string, unknown>;
}

/** 服务器推送的战斗信息 */
export interface BattleSnapshot {
  attackerPlayer: number;
  attackerCardId: string;
  targetIsLeader: boolean;
  targetCardId: string | null;
  blockerCardId: string | null;
  attackerBonus: number;
  defenderBonus: number;
}

/** 服务器 → 双方：权威游戏状态快照 */
export interface MsgGameState extends MsgBase {
  proto: "MsgGameState";
  tick: number;
  my: PlayerSnapshot;
  opponent: PlayerSnapshot;
  phase: string;
  currentTurn: boolean;
  turnCount: number;
  firstPlayer: number;
  mulliganBothDone: boolean;
  isGameOver: boolean;
  winnerIsMe: boolean;
  gameOverReason: string;
  viewerKind: "player" | "spectator";
  lastAction: string;
  actionPayload: string;
  /** 操作日志：按观看者视角生成的一行中文（不可记录的动作为空串） */
  logLine?: string;
  pendingPrompt: PromptSnapshot | null;
  battle: BattleSnapshot | null;
  /** 检索/公开牌的瞬时展示（side 已按视角换算），仅在公开那一刻的快照里非空 */
  reveal?: RevealSnapshot | null;
}

/** 检索/公开牌的瞬时展示信息 */
export interface RevealSnapshot {
  side: "my" | "opponent";
  cardNumbers: string[];
}

/** 客户端 → 服务器：响应 Prompt */
export interface MsgPromptResponse extends MsgBase {
  proto: "MsgPromptResponse";
  promptId: string;
  chosen: string[];   // 卡 ID 列表，长度 ∈ [minChoose, maxChoose]
}

/** 客户端 → 服务器：申请观战 */
export interface MsgSpectateRoom extends MsgBase {
  proto: "MsgSpectateRoom";
  roomId: string;
  result?: boolean;
  logStr?: string;
}

/** 服务端 → 客户端：动作被拒绝（不发对手） */
export interface MsgActionRejected extends MsgBase {
  proto: "MsgActionRejected";
  reason: string;
}

/** 客户端 → 服务器：游戏动作请求 */
export interface MsgGameAction extends MsgBase {
  proto: "MsgGameAction";
  action: GameActionType;
  data: Record<string, unknown>;  // 按 action 类型不同
}

/** 客户端 → 服务器：重连后请求完整快照 */
export interface MsgRequestState extends MsgBase {
  proto: "MsgRequestState";
}

export type FeedbackCategory = "bug" | "suggestion";

/** 游戏内反馈（客户端 → 服务器；服务端回执带 result/path） */
export interface MsgBugReport extends MsgBase {
  proto: "MsgBugReport";
  category: FeedbackCategory; // bug 或优化建议
  description: string;   // 问题描述
  clientInfo: string;    // 客户端全量信息（JSON 字符串）
  result?: boolean;      // 服务端回执：是否保存成功
  path?: string;         // 服务端回执：保存路径
  error?: string;        // 服务端回执：失败原因
}

/** 服务器 → 客户端：对手断线通知 */
export interface MsgPlayerDisconnected extends MsgBase {
  proto: "MsgPlayerDisconnected";
  gracePeriodSeconds: number;  // 宽限期剩余秒数
}

/** 服务器 → 客户端：对手重连通知 */
export interface MsgPlayerReconnected extends MsgBase {
  proto: "MsgPlayerReconnected";
}

// ── 联合类型（用于分发时的类型收窄）──────────────────────────────────────
export type AnyMsg =
  | MsgSecret
  | MsgPing
  | MsgLogin
  | MsgAddAccount
  | MsgUpdatePs
  | MsgEnterMatch
  | MsgCancelMatch
  | MsgMatchFound
  | MsgCreateRoom
  | MsgJoinRoom
  | MsgCancelRoom
  | MsgGameStart
  | MsgShuffle
  | MsgTransmit
  | MsgSurrender
  | MsgDefeat
  | MsgDisconnect
  | MsgDuelOver
  | MsgGameState
  | MsgGameAction
  | MsgPromptResponse
  | MsgSpectateRoom
  | MsgActionRejected
  | MsgRequestState
  | MsgPlayerDisconnected
  | MsgPlayerReconnected
  | MsgBugReport
  | MsgChatMsg
  | MsgGameChat
  | MsgOnlineCount;
