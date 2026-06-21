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
  password: string;
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
}

export interface MsgEnterBotMatch extends MsgBase {
  proto: "MsgEnterBotMatch";
  deck: string;
  goFirst?: boolean;
  result?: boolean;
  logStr?: string;
}

export interface MsgCancelMatch extends MsgBase {
  proto: "MsgCancelMatch";
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
export interface MsgGameChat extends MsgBase {
  proto: "MsgGameChat";
  text: string;
  code?: string | null;
  fromSeat?: number;
  fromAccount?: string;
  fromName?: string;
  fromRole?: "player" | "spectator";
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
  | "PlayCounter"       // { handIndex: number } 或 { fieldCardId, useCounterIcon: true }
  | "PassCounter"       // {}
  | "UseEffect"         // { sourceId, effectKey, ... }
  | "EndTurn"           // {}
  | "ConfirmDamage"     // {}
  | "DebugAddCard"      // { cardNumber: string }
  | "DebugAddDon"       // { count: number }
  | "DebugRefreshDon"   // {}
  | "DebugSummon"       // { cardNumber: string; target: "self" | "opponent" }
  | "DebugKoAll"        // { target: "self" | "opponent" }
  | "DebugRestAll"      // { target: "self" | "opponent" }
  | "DebugLeaderAttack" // {}
  | "Surrender";        // {}

/** 服务器推送的场地卡快照 */
export interface FieldCardSnapshot {
  id: string;
  number: string;
  isTapped: boolean;
  powerCurrent: number;
  cost: number;
  attachedDon: number;
  gainedKeywords: string[];
  cannotActivateNextReset: boolean;
  cannotBeRested: boolean;
  activatedUsedThisTurn: boolean;
  turnPlayed: number;
  canAttack: boolean;
}

/** 服务器推送的单方玩家快照（已按视角脱敏） */
export interface PlayerSnapshot {
  name: string;
  handCardNumbers: string[];  // 仅自己有内容
  handCardCosts: number[];    // 每张手牌的有效费用（含静态减费），仅自己有内容
  handCount: number;
  fieldCards: FieldCardSnapshot[];
  stageNumber: string | null;
  stageId: string | null;
  stageTapped: boolean;
  trashNumbers: string[];
  deckCount: number;
  lifeCount: number;
  lifeNumbers: string[];      // 始终为空，由 Prompt 单独公开
  lifeFaceUp?: { faceUp: boolean; number: string | null }[];
  leaderId: string;
  leaderNumber: string;
  leaderTapped: boolean;
  leaderPower: number;
  leaderAttachedDon: number;
  leaderCanAttack: boolean;
  leaderActivatedUsedThisTurn: boolean;
  stageActivatedUsedThisTurn: boolean;
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

/** 检索/公开牌的瞬时展示数据 */
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

// ── 在线人数 ──────────────────────────────────────────────────────────────
export interface MsgOnlineCount extends MsgBase {
  proto: "MsgOnlineCount";
  count?: number;
}

// ── 在线玩家列表 + 邀请对战 ──────────────────────────────────────────────
export interface PlayerInfo {
  account: string;
  name: string;
  status: "idle" | "matching" | "playing";
  roomId?: string | null;
}

export interface MsgPlayerList extends MsgBase {
  proto: "MsgPlayerList";
  players?: PlayerInfo[];
}

export interface MsgInvitePlayer extends MsgBase {
  proto: "MsgInvitePlayer";
  toAccount?: string;
  deck?: string;
  result?: boolean;
  toName?: string;
  logStr?: string;
}

export interface MsgInviteNotify extends MsgBase {
  proto: "MsgInviteNotify";
  inviteId: string;
  fromName: string;
}

export interface MsgInviteResponse extends MsgBase {
  proto: "MsgInviteResponse";
  inviteId: string;
  accept: boolean;
  deck?: string;
}

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

export interface MsgFriendlyRoom extends MsgBase {
  proto: "MsgFriendlyRoom";
  roomId: string;
  players: FriendlyPlayer[];
  scores: number[];
  state: "lobby" | "playing";
  error?: string | null;
}

export interface MsgFriendlySelectDeck extends MsgBase {
  proto: "MsgFriendlySelectDeck";
  deck: string;
  deckName: string;
}

export interface MsgFriendlyReady extends MsgBase {
  proto: "MsgFriendlyReady";
  ready: boolean;
}

export interface MsgFriendlyLeave extends MsgBase {
  proto: "MsgFriendlyLeave";
}

export interface MsgFriendlyLeft extends MsgBase {
  proto: "MsgFriendlyLeft";
  logStr?: string;
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

/** 服务器 → 客户端：对手断线通知 */
export interface MsgPlayerDisconnected extends MsgBase {
  proto: "MsgPlayerDisconnected";
  gracePeriodSeconds: number;  // 宽限期剩余秒数
}

/** 服务器 → 客户端：对手重连通知 */
export interface MsgPlayerReconnected extends MsgBase {
  proto: "MsgPlayerReconnected";
}

/** 游戏内 F2 反馈 Bug（客户端 → 服务器；服务端回执带 result/path） */
export interface MsgBugReport extends MsgBase {
  proto: "MsgBugReport";
  description: string;
  clientInfo: string;
  result?: boolean;
  path?: string;
  error?: string;
}

/** 对手断线宽限期内主动请求即时结束对局 */
export interface MsgEndByDisconnect extends MsgBase {
  proto: "MsgEndByDisconnect";
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
  | MsgEndByDisconnect
  | MsgChatMsg
  | MsgGameChat
  | MsgEnterBotMatch
  | MsgOnlineCount
  | MsgPlayerList
  | MsgInvitePlayer
  | MsgInviteNotify
  | MsgInviteResponse
  | MsgInviteResult
  | MsgFriendlyRoom
  | MsgFriendlySelectDeck
  | MsgFriendlyReady
  | MsgFriendlyLeave
  | MsgFriendlyLeft;
