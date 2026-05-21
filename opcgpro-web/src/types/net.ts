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

// ── Sprint 3: 服务端结算游戏协议 ─────────────────────────────────────────

/** 客户端可发送的游戏动作类型 */
export type GameActionType =
  | "DrawCard"
  | "PlayCard"
  | "Attack"
  | "Block"
  | "Counter"
  | "UseEffect"
  | "EndTurn"
  | "ConfirmDamage"
  | "Surrender";

/** 服务器推送的场地卡快照（仅含必要字段，客户端用 number 查完整数据） */
export interface FieldCardSnapshot {
  number: string;       // 卡号，客户端 getCard(number) 查完整数据
  isTapped: boolean;    // 是否横置
  powerBuff: number;    // 威力修正
}

/** 服务器推送的单方玩家快照 */
export interface PlayerSnapshot {
  handCardNumbers: string[];  // 己方手牌卡号列表（对手只有 handCount）
  handCount: number;          // 对手手牌数量（只显示牌背）
  fieldCards: FieldCardSnapshot[];
  deckCount: number;
  lifeCount: number;
  costActive: number;
  costMax: number;
  leaderNumber: string | null;
  stageNumber: string | null;
}

/** 服务器 → 双方：权威游戏状态快照 */
export interface MsgGameState extends MsgBase {
  proto: "MsgGameState";
  my: PlayerSnapshot;
  opponent: PlayerSnapshot;
  phase: string;            // BattlePhase 字符串
  currentTurn: boolean;     // 是否当前玩家的回合
  turnCount: number;
  lastAction: string;       // 触发动画标识 "PlayCard" / "Attack" / "Damage"
  actionPayload: string;    // JSON 字符串，动画参数（目标位置、卡号等）
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
  | MsgRequestState
  | MsgPlayerDisconnected
  | MsgPlayerReconnected
  | MsgChatMsg;
