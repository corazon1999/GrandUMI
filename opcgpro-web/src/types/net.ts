import type { SavedDeck } from "@/types/deck";

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
  MsgPlayerData = 32,
  MsgSaveDeck = 33,
  MsgDeleteDeck = 34,
  MsgSelectDeck = 35,
  MsgUpdateProfile = 36,
  MsgImportDecks = 37,
  MsgLeaderLeaderboard = 38,
  MsgPlayerProfileStats = 39,
  MsgUpdateCardBack = 40,
  MsgFriendList = 41,
  MsgFriendSearch = 42,
  MsgFriendRequest = 43,
  MsgFriendRespond = 44,
  MsgFriendRemove = 45,
  MsgFriendCancel = 46,
  MsgCardBackGallery = 47,
  MsgUploadCardBack = 48,
  MsgLikeCardBack = 49,
  MsgDeleteCardBack = 50,
  MsgFriendChat = 51,
  MsgDeckPlazaList = 52,
  MsgPublishDeckPlaza = 53,
  MsgLikeDeckPlaza = 54,
  MsgCopyDeckPlaza = 55,
  MsgDeleteDeckPlaza = 56,
  MsgCardBackReviewQueue = 57,
  MsgReviewCardBack = 58,
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
  supportsStateDelta?: boolean; // 客户端声明支持 MsgGameStateDelta
  Secret?: string;   // 服务器返回的 AES 密钥
  result?: boolean;  // 版本是否匹配
  stateDeltaEnabled?: boolean; // 服务端确认本连接启用增量快照
}

// ── 心跳 ──────────────────────────────────────────────────────────────
export interface MsgPing extends MsgBase {
  proto: "MsgPing";
  id?: string; // 客户端生成、服务端原样回显，用于计算 RTT
}

// ── 账户 ──────────────────────────────────────────────────────────────
// 字段名与 C# LobbyMsg.cs [ProtoMember] 完全一致
export interface MsgLogin extends MsgBase {
  proto: "MsgLogin";
  account: string;
  password?: string;
  authToken?: string;
  clientInstanceId?: string;
  resume?: boolean;
  needsPassword?: boolean;
  needsPasswordSetup?: boolean;
  authChallenge?: boolean;
  name?: string;     // 服务器返回的玩家昵称
  avatar?: string;
  cardBackId?: string;
  canChangeDisplayName?: boolean;
  selectedDeckName?: string | null;
  decks?: SavedDeck[];
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
  currentPassword: string;
  newPassword: string;
  authToken?: string;
  result?: boolean;
  logStr?: string;
}

export interface MsgPlayerData extends MsgBase {
  proto: "MsgPlayerData";
  result: boolean;
  logStr?: string | null;
  account?: string;
  displayName?: string;
  avatar?: string;
  cardBackId?: string;
  canChangeDisplayName?: boolean;
  selectedDeckName?: string | null;
  decks?: SavedDeck[];
}

export interface MsgSaveDeck extends MsgBase {
  proto: "MsgSaveDeck";
  deck: SavedDeck;
}

export interface MsgDeleteDeck extends MsgBase {
  proto: "MsgDeleteDeck";
  name: string;
}

export interface MsgSelectDeck extends MsgBase {
  proto: "MsgSelectDeck";
  name: string | null;
}

export interface MsgUpdateProfile extends MsgBase {
  proto: "MsgUpdateProfile";
  displayName: string;
  avatar: string;
}

export interface MsgUpdateCardBack extends MsgBase {
  proto: "MsgUpdateCardBack";
  cardBackId: string;
}

export interface CardBackGalleryItem {
  id: string;
  name: string;
  authorName: string;
  imageUrl: string;
  likes: number;
  liked: boolean;
  owned: boolean;
  publiclyListed: boolean;
  createdAt: number;
  reviewStatus: "pending" | "approved" | "rejected";
  reviewReason: string;
}

/** 卡背广场游标分页请求与响应；本人投稿单独返回，不占热门分页名额。 */
export interface MsgCardBackGallery extends MsgBase {
  proto: "MsgCardBackGallery";
  result?: boolean;
  logStr?: string;
  cursor?: string | null;
  pageSize?: number;
  items?: CardBackGalleryItem[];
  ownedItems?: CardBackGalleryItem[];
  total?: number;
  hasMore?: boolean;
  nextCursor?: string | null;
}

export interface MsgUploadCardBack extends MsgBase {
  proto: "MsgUploadCardBack";
  name: string;
  mimeType: "image/png" | "image/jpeg" | "image/webp";
  imageBase64: string;
}

export interface MsgLikeCardBack extends MsgBase {
  proto: "MsgLikeCardBack";
  cardBackId?: string;
  result?: boolean;
  logStr?: string;
  item?: CardBackGalleryItem;
}

export interface MsgDeleteCardBack extends MsgBase {
  proto: "MsgDeleteCardBack";
  cardBackId: string;
}

export interface CardBackReviewItem {
  id: string;
  name: string;
  authorName: string;
  imageUrl: string;
  createdAt: number;
}

export interface MsgCardBackReviewQueue extends MsgBase {
  proto: "MsgCardBackReviewQueue";
  result?: boolean;
  canReview?: boolean;
  logStr?: string;
  items?: CardBackReviewItem[];
}

export interface MsgReviewCardBack extends MsgBase {
  proto: "MsgReviewCardBack";
  cardBackId: string;
  approved: boolean;
  reason?: string;
}

export interface MsgImportDecks extends MsgBase {
  proto: "MsgImportDecks";
  decks: SavedDeck[];
}

export type DeckPlazaSort = "popular" | "newest" | "copies";

export interface DeckPlazaItem {
  id: string;
  title: string;
  authorName: string;
  leader: string;
  leaderName: string;
  leaderSprite: string;
  leaderColor: string;
  charCount: number;
  eventCount: number;
  stageCount: number;
  cards: string[];
  spriteMap: Record<string, string>;
  likes: number;
  liked: boolean;
  owned: boolean;
  copies: number;
  createdAt: number;
  updatedAt: number;
}

export interface MsgDeckPlazaList extends MsgBase {
  proto: "MsgDeckPlazaList";
  result?: boolean;
  logStr?: string;
  page?: number;
  pageSize?: number;
  sort?: DeckPlazaSort;
  query?: string;
  color?: string;
  mineOnly?: boolean;
  total?: number;
  hasMore?: boolean;
  items?: DeckPlazaItem[];
}

export interface MsgPublishDeckPlaza extends MsgBase {
  proto: "MsgPublishDeckPlaza";
  sourceDeckName: string;
  title: string;
  publicationId?: string;
  result?: boolean;
  logStr?: string;
}

export interface MsgLikeDeckPlaza extends MsgBase {
  proto: "MsgLikeDeckPlaza";
  publicationId: string;
  result?: boolean;
  logStr?: string;
}

export interface MsgCopyDeckPlaza extends MsgBase {
  proto: "MsgCopyDeckPlaza";
  publicationId: string;
  result?: boolean;
  logStr?: string;
  deckName?: string;
}

export interface MsgDeleteDeckPlaza extends MsgBase {
  proto: "MsgDeleteDeckPlaza";
  publicationId: string;
  result?: boolean;
  logStr?: string;
}

// ── 匹配 ────────────────────────────────────────────────────────────────
export interface MsgEnterMatch extends MsgBase {
  proto: "MsgEnterMatch";
  deck: string;
  deckName?: string;
  queueKind?: MatchQueueKind;
  result?: boolean;
  logStr?: string;
}

export interface MsgCancelMatch extends MsgBase {
  proto: "MsgCancelMatch";
}

export type RankFaction = "pirate" | "marine" | "government";
export type RankedMode = "standard" | "wild";
export type MatchQueueKind = "ranked" | "rankedWild" | "casual";

export interface MsgSelectRankFaction extends MsgBase {
  proto: "MsgSelectRankFaction";
  faction: RankFaction;
  /** 更换已选阵营时，明确确认清空本赛季排位进度。 */
  resetRankProgress?: boolean;
  mode?: RankedMode;
  result?: boolean;
  logStr?: string;
  profile?: RankProfileSnapshot;
  leaderboard?: RankLeaderboardItem[];
  factionStandings?: FactionStanding[];
}

// 单人测试模式：与机器人对战
export interface MsgEnterBotMatch extends MsgBase {
  proto: "MsgEnterBotMatch";
  deck: string;
  deckName?: string;
  goFirst?: boolean;   // 单人测试先后手：true=人类先手(默认)，false=后手
  result?: boolean;
  logStr?: string;
}

export interface MsgMatchFound extends MsgBase {
  proto: "MsgMatchFound";
  opponentName: string;
  queueKind?: MatchQueueKind;
}

export interface RankProfileSnapshot {
  seasonId: string;
  seasonStartsAtUtc: string;
  seasonEndsAtUtc: string;
  placementGames: number;
  placementRequired: number;
  rankPoints: number;
  faction: RankFaction | null;
  tier: string;
  division: number | null;
  games: number;
  wins: number;
  losses: number;
  highestRankPoints: number;
  championLeaderNumbers?: string[];
}

export interface RankLeaderboardItem {
  rank: number;
  factionRank: number;
  displayName: string;
  rankPoints: number;
  faction: RankFaction;
  tier: string;
  division: number | null;
  games: number;
  wins: number;
  winRate: number;
  favoriteLeader?: string | null;
  championLeaderNumbers?: string[];
  isCurrentPlayer?: boolean;
}

export interface FactionStanding {
  rank: number;
  faction: RankFaction;
  totalRankPoints: number;
  playerCount: number;
  games: number;
  wins: number;
}

export interface RankPlayerSettlement {
  account: string;
  rankPointsBefore: number;
  rankPointsAfter: number;
  rankPointDelta: number;
  baseRankPointDelta: number;
  streakAdjustment: number;
  winStreakEndedBounty: number;
  endedWinStreak: number;
  rankDifference: number;
  rankDifferenceAdjustment: number;
  rankProtectionAdjustment: number;
  resultStreak: number;
  won: boolean;
  rankPointFormulaApplied: boolean;
  faction: RankFaction;
  tier: string;
  division: number | null;
  placementGames: number;
  placementRequired: number;
  placementCompleted: boolean;
  winStreak: number;
}

export interface MsgRankSnapshot extends MsgBase {
  proto: "MsgRankSnapshot";
  mode?: RankedMode;
  profile?: RankProfileSnapshot;
  leaderboard?: RankLeaderboardItem[];
  factionStandings?: FactionStanding[];
}

export interface MsgRankResult extends MsgBase {
  proto: "MsgRankResult";
  mode?: RankedMode;
  result?: RankPlayerSettlement;
  profile?: RankProfileSnapshot;
  leaderboard?: RankLeaderboardItem[];
  factionStandings?: FactionStanding[];
  error?: string;
}

// ── 房间码对战 ──────────────────────────────────────────────────────────
export interface MsgCreateRoom extends MsgBase {
  proto: "MsgCreateRoom";
  deck: string;
  deckName?: string;
  roomCode?: string;  // 服务器返回的房间码
  result?: boolean;
  logStr?: string;
}

export interface MsgCancelRoom extends MsgBase {
  proto: "MsgCancelRoom";
}

export interface MsgJoinRoom extends MsgBase {
  proto: "MsgJoinRoom";
  roomCode: string;
  deck: string;
  deckName?: string;
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

export interface MsgGlobalAnnouncement extends MsgBase {
  proto: "MsgGlobalAnnouncement";
  content?: string;
  kind?: "rankedStreak";
  issuedAt?: number;
  result?: boolean;
  logStr?: string;
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

/** 客户端 → 服务器：离开结算页时退出上一场对局的赛后聊天组。 */
export interface MsgLeaveGameChat extends MsgBase {
  proto: "MsgLeaveGameChat";
}

// 好友实时私聊。客户端只发送 toAccount/text；服务端校验好友关系后向双方回显完整消息。
export interface MsgFriendChat extends MsgBase {
  proto: "MsgFriendChat";
  toAccount?: string;
  text?: string;
  result?: boolean;
  logStr?: string;
  id?: string;
  fromAccount?: string;
  fromName?: string;
  toName?: string;
  sentAt?: number;
}

export interface FriendChatMessage {
  id: string;
  text: string;
  fromAccount: string;
  fromName: string;
  toAccount: string;
  toName: string;
  sentAt: number;
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
  championLeaderNumbers?: string[];
  status: "idle" | "matching" | "playing" | "spectating";
  roomId?: string | null;   // 对战中玩家所在的对局房间ID，供一键观战；无对局房间时为 null
  seatIndex?: 0 | 1 | null; // 对战中的座位，用于让一键观战保持被点击玩家为主视角
  spectateMode?: SpectateMode | null;
}

// 客户端 → 服务器:请求在线玩家列表;服务器 → 客户端:返回列表
export interface MsgPlayerList extends MsgBase {
  proto: "MsgPlayerList";
  players?: PlayerInfo[];
  offset?: number;
  limit?: number;
  total?: number;
  hasMore?: boolean;
}

// ── 好友系统 ──────────────────────────────────────────────────────────────
export type FriendPresenceStatus = PlayerInfo["status"] | "offline";

export interface FriendInfo {
  account: string;
  name: string;
  championLeaderNumbers?: string[];
  avatar: string;
  friendsSince: number;
  online: boolean;
  status: FriendPresenceStatus;
  roomId?: string | null;
  seatIndex?: 0 | 1 | null;
  spectateMode?: SpectateMode | null;
}

export interface FriendRequestInfo {
  id: number;
  account: string;
  name: string;
  avatar: string;
  createdAt: number;
  online: boolean;
}

export type FriendRelationship = "none" | "friend" | "incoming" | "outgoing";

export interface FriendSearchPlayer {
  account: string;
  name: string;
  avatar: string;
  championLeaderNumbers?: string[];
  relationship: FriendRelationship;
  online: boolean;
  status: FriendPresenceStatus;
}

export interface MsgFriendList extends MsgBase {
  proto: "MsgFriendList";
  result?: boolean;
  logStr?: string;
  friends?: FriendInfo[];
  incomingRequests?: FriendRequestInfo[];
  outgoingRequests?: FriendRequestInfo[];
}

export interface MsgFriendSearch extends MsgBase {
  proto: "MsgFriendSearch";
  query?: string;
  result?: boolean;
  logStr?: string;
  players?: FriendSearchPlayer[];
}

export interface MsgFriendRequest extends MsgBase {
  proto: "MsgFriendRequest";
  toAccount?: string;
  /** 对局内快捷申请：由服务端按当前会话解析交战对手，不向客户端公开账号。 */
  currentOpponent?: boolean;
  result?: boolean;
  autoAccepted?: boolean;
  logStr?: string;
}

export interface MsgFriendRespond extends MsgBase {
  proto: "MsgFriendRespond";
  requestId?: number;
  accept?: boolean;
  accepted?: boolean;
  result?: boolean;
  logStr?: string;
}

export interface MsgFriendRemove extends MsgBase {
  proto: "MsgFriendRemove";
  account?: string;
  result?: boolean;
  logStr?: string;
}

export interface MsgFriendCancel extends MsgBase {
  proto: "MsgFriendCancel";
  requestId?: number;
  result?: boolean;
  logStr?: string;
}

export interface BlockedPlayerInfo {
  account: string;
  name: string;
  createdAt: number;
}

export type PlayerReportCategory = "harassment" | "stalling" | "cheating" | "spam" | "other";

export interface MsgPlayerSafety extends MsgBase {
  proto: "MsgPlayerSafety";
  action?: "list" | "block" | "unblock" | "report";
  targetAccount?: string;
  currentOpponent?: boolean;
  category?: PlayerReportCategory;
  description?: string;
  blockedPlayers?: BlockedPlayerInfo[];
  result?: boolean;
  logStr?: string;
}

// ── Leader 排行榜 ─────────────────────────────────────────────────────
export type LeaderboardPeriod = "7d" | "30d" | "all";

export interface LeaderChampionInfo {
  displayName: string;
}

export interface LeaderLeaderboardItem {
  rank: number | null;
  leaderNumber: string;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
  usageRate: number;
  firstGames: number;
  firstWinRate: number | null;
  secondGames: number;
  secondWinRate: number | null;
  insufficientSample: boolean;
  /** 当前全服最强使用者；按统一的近 30 日称号规则计算。 */
  champion?: LeaderChampionInfo | null;
}

/** 客户端发送时只带 period；服务端回包附带聚合结果。 */
export interface MsgLeaderLeaderboard extends MsgBase {
  proto: "MsgLeaderLeaderboard";
  period: LeaderboardPeriod;
  result?: boolean;
  error?: string;
  generatedAtUtc?: string;
  sinceUtc?: string | null;
  totalMatches?: number;
  minimumGames?: number;
  items?: LeaderLeaderboardItem[];
}

export interface LeaderMatchupItem {
  rank: number;
  leaderNumber: string;
  games: number;
  wins: number | null;
  losses: number | null;
  winRate: number | null;
  firstGames: number;
  firstWinRate: number | null;
  secondGames: number;
  secondWinRate: number | null;
  isMirror: boolean;
}

export interface LeaderStartingHandItem {
  cardNumber: string;
  games: number;
  percentage: number;
}

/** 点击榜单项后，查询该 Leader 对阵当前周期榜前二十的统计及起手留牌数据。 */
export interface MsgLeaderMatchups extends MsgBase {
  proto: "MsgLeaderMatchups";
  period: LeaderboardPeriod;
  leaderNumber: string;
  result?: boolean;
  error?: string;
  generatedAtUtc?: string;
  sinceUtc?: string | null;
  items?: LeaderMatchupItem[];
  startingHandSampleGames?: number;
  startingHandItems?: LeaderStartingHandItem[];
}

export interface LeaderMatchupMatrixRow {
  leaderNumber: string;
  items: LeaderMatchupItem[];
}

/** 当前周期胜率榜前十五名的完整对阵矩阵。 */
export interface MsgLeaderMatchupMatrix extends MsgBase {
  proto: "MsgLeaderMatchupMatrix";
  period: LeaderboardPeriod;
  result?: boolean;
  error?: string;
  generatedAtUtc?: string;
  sinceUtc?: string | null;
  rows?: LeaderMatchupMatrixRow[];
}

export interface PlayerLeaderStatsItem {
  leaderNumber: string;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
  usageRate: number;
  firstGames: number;
  firstWinRate: number | null;
  secondGames: number;
  secondWinRate: number | null;
}

export interface PlayerStatsTrendPoint {
  label: string;
  games: number;
  wins: number;
  winRate: number | null;
}

/** 当前登录账号的私有聚合战绩；客户端不能指定其他账号。 */
export interface MsgPlayerProfileStats extends MsgBase {
  proto: "MsgPlayerProfileStats";
  period: LeaderboardPeriod;
  result?: boolean;
  error?: string;
  generatedAtUtc?: string;
  sinceUtc?: string | null;
  games?: number;
  wins?: number;
  losses?: number;
  winRate?: number;
  firstGames?: number;
  firstWinRate?: number | null;
  secondGames?: number;
  secondWinRate?: number | null;
  topLeaders?: PlayerLeaderStatsItem[];
  trend?: PlayerStatsTrendPoint[];
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
  connected?: boolean;
}

// 服务器 → 客户端:友谊战房间完整状态
export interface MsgFriendlyRoom extends MsgBase {
  proto: "MsgFriendlyRoom";
  roomId: string;
  origin?: "roomCode" | "invite";
  roomCode?: string | null;
  players: FriendlyPlayer[];
  scores: number[];
  state: "lobby" | "starting" | "playing";
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
  | "ChooseFirstPlayer" // { goFirst: boolean }
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
  | "RequestDraw"       // {}
  | "RespondDraw"       // { accept: boolean }
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
  effectsNullified: boolean; // 当前角色效果是否处于无效状态
  cannotActivateNextReset: boolean;
  cannotBeRested: boolean;   // 无法被效果转为休息状态
  activatedUsedThisTurn: boolean;  // 本回合【启动主要】【每回合1次】是否已用（已用则隐藏启动按钮）
  oncePerTurnEffectAvailable: boolean; // 至少一项【每回合1次】效果本回合仍可发动
  turnPlayed: number;
  canAttack: boolean;        // 该角色当前是否可发起攻击（后端权威，对手/非我方回合恒 false）
  cannotAttack: boolean;     // 该角色是否存在明确的“无法攻击”状态（不含横置、新登场等普通条件）
}

/** 服务器推送的单方玩家快照（已按视角脱敏） */
export interface PlayerRankIdentitySnapshot {
  faction: RankFaction;
  tier: string;
  division: number | null;
  placementGames: number;
  placementRequired: number;
}

export interface PlayerSnapshot {
  name: string;
  /** 仅排位对局携带；旧回放及其他对局类型缺失时不展示。 */
  rankIdentity?: PlayerRankIdentitySnapshot | null;
  /** 旧回放没有该字段时回退经典卡背。 */
  cardBackId?: string;
  /** 该玩家卡组公开的卡面选择；旧回放缺失时使用正画。 */
  spriteMap?: Record<string, string>;
  handCardIds?: string[];     // 仅自己有内容；用于本地拖动排序时维持同名牌身份
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
  lifeFaceUp?: { faceUp: boolean; number: string | null }[];
  leaderId: string;
  leaderNumber: string;
  /** 当且仅当该玩家正使用自己全服最强的 Leader 时返回对应卡号。 */
  championLeaderNumber?: string | null;
  leaderTapped: boolean;
  leaderPower: number;
  leaderAttachedDon: number;
  leaderGainedKeywords: string[]; // 领袖动态获得的关键词（含持续效果）
  leaderCanAttack: boolean;   // 领袖当前是否可发起攻击（后端权威）
  leaderCannotAttack: boolean; // 领袖是否存在明确的“无法攻击”状态
  leaderEnterEffectNullified: boolean; // 【登场时】效果当前被无效
  leaderActivatedUsedThisTurn: boolean;  // 领袖【启动主要】【每回合1次】本回合是否已用
  stageActivatedUsedThisTurn: boolean;   // 舞台【启动主要】【每回合1次】本回合是否已用
  leaderOncePerTurnEffectAvailable: boolean; // 领袖的【每回合1次】效果本回合仍可发动
  stageOncePerTurnEffectAvailable: boolean;  // 舞台的【每回合1次】效果本回合仍可发动
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

/** 对局结束后附带的回放隐藏区变化帧；实时对局与观战不会收到内容。 */
export interface ReplayHandFrameSnapshot {
  tick: number;
  myCardNumbers: string[];
  opponentCardNumbers: string[];
  /** 旧回放帧不含生命区字段，客户端保持卡背显示。 */
  myLifeCardNumbers?: string[];
  opponentLifeCardNumbers?: string[];
}

/** 服务器 → 双方：权威游戏状态快照 */
export interface MsgGameState extends MsgBase {
  proto: "MsgGameState";
  /** 生成该快照时的服务端 UTC 时间，供客户端校准权威倒计时。 */
  serverNowUtc?: string;
  /** 本局锁定的卡牌效果规则版本；断线恢复和回放期间保持不变。 */
  rulesetId?: string;
  tick: number;
  my: PlayerSnapshot;
  opponent: PlayerSnapshot;
  phase: string;
  currentTurn: boolean;
  turnCount: number;
  firstPlayer: number;
  firstPlayerChosen: boolean;
  openingStage?: "NotStarted" | "ResolvingOpeningEffects" | "WaitingOpeningPrompt" | "RollingDice" | "WaitingFirstPlayerChoice" | "Mulligan" | "Playing";
  isFirstPlayer: boolean;
  canChooseFirstPlayer: boolean;
  diceWinnerIsMe: boolean;
  startingPlayerChoiceDeadlineUtc?: string | null;
  startingDiceRolls: Array<{ my: number; opponent: number; tie: boolean }>;
  mulliganBothDone: boolean;
  mulliganDeadlineUtc?: string | null;
  operationClockEnabled?: boolean;
  myOperationTimeMs?: number;
  opponentOperationTimeMs?: number;
  myTurnOperationTimeMs?: number;
  opponentTurnOperationTimeMs?: number;
  operationClockActive?: "my" | "opponent" | null;
  operationClockSyncUtc?: string | null;
  operationClockPaused?: boolean;
  matchKind?: "Ranked" | "RankedWild" | "Casual" | "Matchmaking" | "RoomCode" | "Friendly" | "Bot" | "UnknownHuman";
  isGameOver: boolean;
  isDraw?: boolean;
  winnerIsMe: boolean;
  gameOverReason: string;
  drawRequestPendingFromMe?: boolean;
  drawRequestPendingFromOpponent?: boolean;
  drawRequestRejectionCount?: number;
  drawRequestRejectionLimit?: number;
  viewerKind: "player" | "spectator";
  spectatorHandVisible?: boolean;
  /** 对应触发本次状态变化的客户端请求；无客户端请求时为空。 */
  requestId?: string | null;
  lastAction: string;
  actionPayload: string;
  /** 操作日志：按观看者视角生成的一行中文（不可记录的动作为空串） */
  logLine?: string;
  /** 本次快照附带的全部操作日志；用于一次效果结算中连续记录多个选择。 */
  logLines?: string[];
  /** 自上一份快照以来进入解析的卡牌效果；按实际结算顺序排列。 */
  effectActivations?: EffectActivationSnapshot[];
  /** 仅终局玩家快照携带，用于回放时明示双方手牌与生命区。 */
  replayHands?: ReplayHandFrameSnapshot[] | null;
  pendingPrompt: PromptSnapshot | null;
  battle: BattleSnapshot | null;
  /** 检索/公开牌的瞬时展示（side 已按视角换算），仅在公开那一刻的快照里非空 */
  reveal?: RevealSnapshot | null;
}

export type GameStateDeltaChanges = Partial<
  Omit<MsgGameState, "proto" | "tick" | "my" | "opponent">
> & {
  my?: Partial<PlayerSnapshot>;
  opponent?: Partial<PlayerSnapshot>;
};

/** 服务端 → 客户端：相对上一份已确认 Tick 的浅层增量快照。 */
export interface MsgGameStateDelta extends MsgBase {
  proto: "MsgGameStateDelta";
  baseTick: number;
  tick: number;
  changes: GameStateDeltaChanges;
}

/** 检索/公开牌的瞬时展示信息 */
export interface RevealSnapshot {
  side: "my" | "opponent";
  cardNumbers: string[];
}

/** 卡牌进入效果解析时下发的瞬时表现事件。 */
export interface EffectActivationSnapshot {
  sourceId: string;
  cardNumber: string;
  trigger: string;
  side: "my" | "opponent";
}

/** 客户端 → 服务器：响应 Prompt */
export interface MsgPromptResponse extends MsgBase {
  proto: "MsgPromptResponse";
  promptId: string;
  chosen: string[];   // 卡 ID 列表，长度 ∈ [minChoose, maxChoose]
  requestId?: string;
}

/** 客户端 → 服务器：申请观战 */
export type SpectateMode = "open" | "closed" | "friends" | "password";

export interface MsgSpectateRoom extends MsgBase {
  proto: "MsgSpectateRoom";
  roomId: string;
  viewPlayerIndex?: 0 | 1;
  spectateCode?: string;
  spectatorHandVisible?: boolean;
  result?: boolean;
  logStr?: string;
}

/** 客户端 → 服务器：主动退出观战；服务端以同协议回执 */
export interface MsgLeaveSpectate extends MsgBase {
  proto: "MsgLeaveSpectate";
  result?: boolean;
  logStr?: string;
}

/** 服务端 → 对战双方：当前观战者名称列表 */
export interface MsgSpectatorList extends MsgBase {
  proto: "MsgSpectatorList";
  spectators: string[];
  details?: Array<{
    account: string;
    name: string;
    viewingYou: boolean;
    handVisible: boolean;
  }>;
}

export interface MsgUpdateSpectateSettings extends MsgBase {
  proto: "MsgUpdateSpectateSettings";
  mode: SpectateMode;
  handsPublic: boolean;
  regenerateCode?: boolean;
  spectateCode?: string | null;
  result?: boolean;
  logStr?: string;
}

export interface MsgSpectatorHandRequest extends MsgBase {
  proto: "MsgSpectatorHandRequest";
  requestId: string;
  spectatorAccount: string;
  spectatorName: string;
}

export interface MsgSpectatorHandStatus extends MsgBase {
  proto: "MsgSpectatorHandStatus";
  status: "pending" | "approved" | "denied";
  logStr?: string;
  retryAfterMs?: number;
}

export interface MsgSpectatorHandResponse extends MsgBase {
  proto: "MsgSpectatorHandResponse";
  requestId?: string;
  result?: boolean;
  accepted?: boolean;
  logStr?: string;
}

export interface MsgKickSpectator extends MsgBase {
  proto: "MsgKickSpectator";
  spectatorAccount: string;
  result?: boolean;
  logStr?: string;
}

export interface MsgSpectatorKicked extends MsgBase {
  proto: "MsgSpectatorKicked";
  logStr?: string;
}

/** 服务端 → 客户端：动作被拒绝（不发对手） */
export interface MsgActionRejected extends MsgBase {
  proto: "MsgActionRejected";
  reason: string;
  requestId?: string | null;
}

/** 客户端 → 服务器：游戏动作请求 */
export interface MsgGameAction extends MsgBase {
  proto: "MsgGameAction";
  action: GameActionType;
  data: Record<string, unknown>;  // 按 action 类型不同
  requestId?: string;
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

/** 旧规则对局结束后通知玩家：后续对局已切换至新版卡效。 */
export interface MsgRulesetUpdated extends MsgBase {
  proto: "MsgRulesetUpdated";
  previousRulesetId: string;
  currentRulesetId: string;
  description?: string;
  changedCards?: string[];
  logStr?: string;
}

export interface CardRulesetSummary {
  id: string;
  baseRulesetId?: string | null;
  description?: string;
  changedCards: string[];
  active: boolean;
}

/** 管理员查询/激活卡效规则后的状态。 */
export interface MsgRulesetState extends MsgBase {
  proto: "MsgRulesetState";
  activeRulesetId?: string;
  availableRulesets?: CardRulesetSummary[];
  activeRoomCounts?: Record<string, number>;
  result?: boolean;
  logStr?: string;
}

export interface MsgActivateRuleset extends MsgBase {
  proto: "MsgActivateRuleset";
  rulesetId: string;
}

// ── 联合类型（用于分发时的类型收窄）──────────────────────────────────────
export type AnyMsg =
  | MsgSecret
  | MsgPing
  | MsgLogin
  | MsgAddAccount
  | MsgUpdatePs
  | MsgPlayerData
  | MsgSaveDeck
  | MsgDeleteDeck
  | MsgSelectDeck
  | MsgUpdateProfile
  | MsgUpdateCardBack
  | MsgCardBackGallery
  | MsgUploadCardBack
  | MsgLikeCardBack
  | MsgDeleteCardBack
  | MsgCardBackReviewQueue
  | MsgReviewCardBack
  | MsgDeckPlazaList
  | MsgPublishDeckPlaza
  | MsgLikeDeckPlaza
  | MsgCopyDeckPlaza
  | MsgDeleteDeckPlaza
  | MsgImportDecks
  | MsgEnterMatch
  | MsgCancelMatch
  | MsgSelectRankFaction
  | MsgMatchFound
  | MsgRankSnapshot
  | MsgRankResult
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
  | MsgLeaveSpectate
  | MsgSpectatorList
  | MsgUpdateSpectateSettings
  | MsgSpectatorHandRequest
  | MsgSpectatorHandStatus
  | MsgSpectatorHandResponse
  | MsgKickSpectator
  | MsgSpectatorKicked
  | MsgActionRejected
  | MsgRequestState
  | MsgPlayerDisconnected
  | MsgPlayerReconnected
  | MsgRulesetUpdated
  | MsgRulesetState
  | MsgBugReport
  | MsgChatMsg
  | MsgGlobalAnnouncement
  | MsgGameChat
  | MsgFriendChat
  | MsgLeaveGameChat
  | MsgLeaderLeaderboard
  | MsgLeaderMatchups
  | MsgLeaderMatchupMatrix
  | MsgPlayerProfileStats
  | MsgOnlineCount
  | MsgPlayerList
  | MsgFriendList
  | MsgFriendSearch
  | MsgFriendRequest
  | MsgFriendRespond
  | MsgFriendRemove
  | MsgFriendCancel
  | MsgPlayerSafety
  | MsgInvitePlayer
  | MsgInviteNotify
  | MsgInviteResponse
  | MsgInviteResult;
