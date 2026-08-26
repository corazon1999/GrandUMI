import { create } from "zustand";
import {
  INITIAL_RANK_SNAPSHOT_REQUEST_STATE,
  acceptRankSnapshot,
  beginRankSnapshotRequest as beginRankSnapshotRequestState,
  failRankSnapshotRequest as failRankSnapshotRequestState,
  shouldReplaceRankProfile,
  transitionRankSnapshotSeason,
  type IncomingRankSnapshotMetadata,
  type RankSnapshotRequestState,
} from "@/lib/rankSnapshotState";
import type {
  PlayerInfo,
  FriendInfo,
  FriendRequestInfo,
  FriendSearchPlayer,
  BlockedPlayerInfo,
  FriendlyPlayer,
  MsgLeaderLeaderboard,
  MsgLeaderMatchupMatrix,
  MsgLeaderMatchups,
  MsgPlayerProfileStats,
  CardBackGalleryItem,
  CardBackReviewItem,
  DeckPlazaItem,
  FriendChatMessage,
  RankProfileSnapshot,
  RankLeaderboardItem,
  FactionStanding,
  RankPlayerSettlement,
  RankedMode,
  MatchQueueKind,
  SpectateMode,
  CardRulesetSummary,
  AdminDeploymentStatus,
  OnlinePlayerPeakPoint,
  DailyMatchCountPoint,
  DailyActivePlayerPoint,
  AdminStorageSnapshot,
  AdminPlayerSummary,
} from "@/types/net";

export function leaderMatchupKey(period: string, leaderNumber: string): string {
  return `${period}:${leaderNumber}`;
}

export type IncomingInvite = { inviteId: string; fromName: string };
export type FriendlyRoomState = {
  roomId: string;
  origin: "roomCode" | "invite";
  roomCode: string | null;
  players: FriendlyPlayer[];
  scores: number[];
  state: "lobby" | "starting" | "playing";
};

export type RoomOperation = "idle" | "creating" | "joining";

export type MaintenanceState = {
  enabled: boolean;
  activeRoomCount: number;
  startedAt: number | null;
  canManage: boolean;
};

export type RulesetAdminState = {
  activeRulesetId: string;
  availableRulesets: CardRulesetSummary[];
  activeRoomCounts: Record<string, number>;
};

export type AdminOperationsState = {
  currentCommit: string;
  deploymentAvailable: boolean;
  onlineCount: number | null;
  peaks7: OnlinePlayerPeakPoint[];
  peaks30: OnlinePlayerPeakPoint[];
  dailyActive7: DailyActivePlayerPoint[];
  dailyActive30: DailyActivePlayerPoint[];
  playerTrafficUpdatedAt: number | null;
  matches7: DailyMatchCountPoint[];
  matches30: DailyMatchCountPoint[];
  matchesUpdatedAt: number | null;
  storage: AdminStorageSnapshot | null;
  test: AdminDeploymentStatus;
  production: AdminDeploymentStatus;
};

export interface ChatMessage {
  Name: string;
  Msg: string;
  type: number;
  time: number;
}

export function friendAccountKey(account: string): string {
  return account.toLocaleLowerCase("zh-CN");
}

export interface SelectedDeck {
  name: string;
  leader: string;
  leaderName: string;
  leaderSprite?: string; // 领航卡面图 URL（大厅「已选卡组」显示头像用）
  cards: string; // 卡组字符串（DeckMapper.exportDeckString 格式）
}

type MatchState = "idle" | "matching" | "matched";
export type SpectateState = "idle" | "joining" | "watching";

interface NetStore {
  // 连接状态（含重连状态）
  connState: "disconnected" | "connecting" | "handshaking" | "connected" | "reconnecting" | "recovering" | "failed";
  // 重连倒计时（秒），ReconnectOverlay 显示用
  reconnectCountdown: number;
  // 账户状态
  loggedIn: boolean;
  account: string;
  playerName: string;
  avatar: string;
  cardBackId: string;
  canChangeDisplayName: boolean;
  // 错误提示
  error: string | null;
  // 匹配
  matchState: MatchState;
  matchQueueKind: MatchQueueKind;
  selectedDeck: SelectedDeck | null;
  opponentName: string;
  rankProfile: RankProfileSnapshot | null;
  rankLeaderboard: RankLeaderboardItem[];
  rankProfiles: Record<RankedMode, RankProfileSnapshot | null>;
  rankLeaderboards: Record<RankedMode, RankLeaderboardItem[]>;
  factionStandingsByMode: Record<RankedMode, FactionStanding[]>;
  rankSnapshotRequests: Record<RankedMode, RankSnapshotRequestState>;
  lastRankResult: RankPlayerSettlement | null;
  // 房间码
  roomCode: string | null;
  roomOperation: RoomOperation;
  // 在线人数（服务器广播的已登录人数）
  onlineCount: number;
  maintenance: MaintenanceState;
  rulesets: RulesetAdminState;
  adminOperations: AdminOperationsState;
  adminPlayerSearchResults: AdminPlayerSummary[];
  adminTemporaryPassword: { account: string; password: string } | null;
  // 在线玩家列表（点击在线人数时拉取）
  playerList: PlayerInfo[];
  friends: FriendInfo[];
  incomingFriendRequests: FriendRequestInfo[];
  outgoingFriendRequests: FriendRequestInfo[];
  friendSearchResults: FriendSearchPlayer[];
  blockedPlayers: BlockedPlayerInfo[];
  // 最近一次 Leader 排行榜回包
  leaderLeaderboard: MsgLeaderLeaderboard | null;
  // 点击榜单项后按“周期:Leader”保存的对战前二十及起手留牌统计
  leaderMatchups: Record<string, MsgLeaderMatchups>;
  // 当前周期榜前十五的完整对阵矩阵
  leaderMatchupMatrix: MsgLeaderMatchupMatrix | null;
  // 当前个人详情页的周期统计
  playerProfileStats: MsgPlayerProfileStats | null;
  cardBackGallery: CardBackGalleryItem[] | null;
  cardBackGalleryOwned: CardBackGalleryItem[];
  cardBackGalleryTotal: number;
  cardBackGalleryNextCursor: string | null;
  cardBackGalleryHasMore: boolean;
  cardBackGalleryLoadingMore: boolean;
  cardBackReviewQueue: CardBackReviewItem[] | null;
  deckPlazaPage: { items: DeckPlazaItem[]; page: number; pageSize: number; total: number; hasMore: boolean } | null;
  deckPlazaRevision: number;
  // 收到的对战邀请（被邀请方弹窗用）
  incomingInvite: IncomingInvite | null;
  // 友谊战房间（非 null 时大厅显示房间界面）
  friendlyRoom: FriendlyRoomState | null;
  // 观战申请与当前观战房间
  spectateState: SpectateState;
  spectateRoomId: string | null;
  spectateMode: SpectateMode;
  spectatorHandsPublic: boolean;
  spectateCode: string | null;
  // 聊天
  chatMessages: ChatMessage[];
  friendChatMessages: FriendChatMessage[];
  friendChatUnreadByAccount: Record<string, number>;
  // 客户端路由导航（避免 window.location.href 导致整页刷新断开 WebSocket）
  navigateTo: string | null;

  // actions
  setConnState: (s: NetStore["connState"]) => void;
  setReconnectCountdown: (n: number) => void;
  setLoggedIn: (v: boolean, name?: string, account?: string) => void;
  setPlayerName: (name: string) => void;
  setProfile: (name: string, avatar: string, cardBackId?: string, canChangeDisplayName?: boolean) => void;
  setError: (msg: string | null) => void;
  setMatchState: (s: MatchState) => void;
  setMatchQueueKind: (kind: MatchQueueKind) => void;
  setSelectedDeck: (deck: SelectedDeck | null) => void;
  setOpponentName: (name: string) => void;
  beginRankSnapshotRequest: (mode: RankedMode, requestId: string) => void;
  failRankSnapshotRequest: (mode: RankedMode, requestId: string | null, error: string, retryable?: boolean) => void;
  setRankProfile: (mode: RankedMode, profile: RankProfileSnapshot) => void;
  setRankSnapshot: (
    mode: RankedMode,
    profile: RankProfileSnapshot,
    leaderboard: RankLeaderboardItem[],
    factionStandings: FactionStanding[] | undefined,
    metadata?: Omit<IncomingRankSnapshotMetadata, "seasonId"> & {
      allowSameSeasonProfileRegression?: boolean;
    },
  ) => void;
  setLastRankResult: (result: RankPlayerSettlement | null) => void;
  setRoomCode: (code: string | null) => void;
  setRoomOperation: (operation: RoomOperation) => void;
  setOnlineCount: (n: number) => void;
  setMaintenance: (maintenance: MaintenanceState) => void;
  setRulesets: (rulesets: RulesetAdminState) => void;
  setAdminOperations: (operations: AdminOperationsState) => void;
  setAdminPlayerSearchResults: (players: AdminPlayerSummary[]) => void;
  setAdminTemporaryPassword: (value: NetStore["adminTemporaryPassword"]) => void;
  setPlayerList: (list: PlayerInfo[]) => void;
  setFriendData: (friends: FriendInfo[], incoming: FriendRequestInfo[], outgoing: FriendRequestInfo[]) => void;
  setFriendSearchResults: (players: FriendSearchPlayer[]) => void;
  setBlockedPlayers: (players: BlockedPlayerInfo[]) => void;
  setLeaderLeaderboard: (data: MsgLeaderLeaderboard | null) => void;
  setLeaderMatchups: (data: MsgLeaderMatchups) => void;
  clearLeaderMatchups: () => void;
  setLeaderMatchupMatrix: (data: MsgLeaderMatchupMatrix | null) => void;
  setPlayerProfileStats: (data: MsgPlayerProfileStats | null) => void;
  setCardBackGallery: (items: CardBackGalleryItem[] | null) => void;
  setCardBackGalleryPage: (page: {
    items: CardBackGalleryItem[];
    ownedItems: CardBackGalleryItem[];
    total: number;
    nextCursor: string | null;
    hasMore: boolean;
    append: boolean;
  }) => void;
  setCardBackGalleryLoadingMore: (loading: boolean) => void;
  updateCardBackGalleryItem: (item: CardBackGalleryItem) => void;
  setCardBackReviewQueue: (items: CardBackReviewItem[] | null) => void;
  setDeckPlazaPage: (page: NetStore["deckPlazaPage"]) => void;
  refreshDeckPlaza: () => void;
  setIncomingInvite: (inv: IncomingInvite | null) => void;
  setFriendlyRoom: (room: FriendlyRoomState | null) => void;
  setSpectate: (state: SpectateState, roomId?: string | null) => void;
  setSpectateSettings: (mode: SpectateMode, handsPublic: boolean, code?: string | null) => void;
  setNavigateTo: (path: string | null) => void;
  addChatMessage: (msg: ChatMessage) => void;
  addFriendChatMessage: (msg: FriendChatMessage) => void;
  markFriendChatRead: (account: string) => void;
  clearChat: () => void;
  reset: () => void;
}

const initialState = {
  connState: "disconnected" as const,
  reconnectCountdown: 0,
  loggedIn: false,
  account: "",
  playerName: "",
  avatar: "",
  cardBackId: "classic",
  canChangeDisplayName: false,
  error: null as string | null,
  matchState: "idle" as MatchState,
  matchQueueKind: "casualStandard" as const,
  selectedDeck: null as SelectedDeck | null,
  opponentName: "",
  rankProfile: null as RankProfileSnapshot | null,
  rankLeaderboard: [] as RankLeaderboardItem[],
  rankProfiles: { standard: null, wild: null } as Record<RankedMode, RankProfileSnapshot | null>,
  rankLeaderboards: { standard: [], wild: [] } as Record<RankedMode, RankLeaderboardItem[]>,
  factionStandingsByMode: { standard: [], wild: [] } as Record<RankedMode, FactionStanding[]>,
  rankSnapshotRequests: {
    standard: { ...INITIAL_RANK_SNAPSHOT_REQUEST_STATE },
    wild: { ...INITIAL_RANK_SNAPSHOT_REQUEST_STATE },
  } as Record<RankedMode, RankSnapshotRequestState>,
  lastRankResult: null as RankPlayerSettlement | null,
  roomCode: null as string | null,
  roomOperation: "idle" as RoomOperation,
  onlineCount: 0,
  maintenance: { enabled: false, activeRoomCount: 0, startedAt: null, canManage: false },
  rulesets: { activeRulesetId: "", availableRulesets: [], activeRoomCounts: {} } as RulesetAdminState,
  adminOperations: {
    currentCommit: "",
    deploymentAvailable: false,
    onlineCount: null,
    peaks7: [],
    peaks30: [],
    dailyActive7: [],
    dailyActive30: [],
    playerTrafficUpdatedAt: null,
    matches7: [],
    matches30: [],
    matchesUpdatedAt: null,
    storage: null,
    test: { environment: "test", state: "unavailable", message: "等待服务器状态" },
    production: { environment: "production", state: "unavailable", message: "等待服务器状态" },
  } as AdminOperationsState,
  adminPlayerSearchResults: [] as AdminPlayerSummary[],
  adminTemporaryPassword: null as NetStore["adminTemporaryPassword"],
  playerList: [] as PlayerInfo[],
  friends: [] as FriendInfo[],
  incomingFriendRequests: [] as FriendRequestInfo[],
  outgoingFriendRequests: [] as FriendRequestInfo[],
  friendSearchResults: [] as FriendSearchPlayer[],
  blockedPlayers: [] as BlockedPlayerInfo[],
  leaderLeaderboard: null as MsgLeaderLeaderboard | null,
  leaderMatchups: {} as Record<string, MsgLeaderMatchups>,
  leaderMatchupMatrix: null as MsgLeaderMatchupMatrix | null,
  playerProfileStats: null as MsgPlayerProfileStats | null,
  cardBackGallery: null as CardBackGalleryItem[] | null,
  cardBackGalleryOwned: [] as CardBackGalleryItem[],
  cardBackGalleryTotal: 0,
  cardBackGalleryNextCursor: null as string | null,
  cardBackGalleryHasMore: false,
  cardBackGalleryLoadingMore: false,
  cardBackReviewQueue: null as CardBackReviewItem[] | null,
  deckPlazaPage: null as NetStore["deckPlazaPage"],
  deckPlazaRevision: 0,
  incomingInvite: null as IncomingInvite | null,
  friendlyRoom: null as FriendlyRoomState | null,
  spectateState: "idle" as SpectateState,
  spectateRoomId: null as string | null,
  spectateMode: "open" as SpectateMode,
  spectatorHandsPublic: false,
  spectateCode: null as string | null,
  chatMessages: [] as ChatMessage[],
  friendChatMessages: [] as FriendChatMessage[],
  friendChatUnreadByAccount: {} as Record<string, number>,
  navigateTo: null as string | null,
};

export const useNetStore = create<NetStore>((set) => ({
  ...initialState,

  setConnState: (s) => set({ connState: s }),
  setReconnectCountdown: (n) => set({ reconnectCountdown: n }),

  setLoggedIn: (v, name, account) =>
    set((prev) => ({
      loggedIn: v,
      playerName: name ?? prev.playerName,
      account: account ?? prev.account,
    })),

  setPlayerName: (name) => set({ playerName: name }),

  setProfile: (playerName, avatar, cardBackId, canChangeDisplayName) => set((state) => ({
    playerName,
    avatar,
    cardBackId: cardBackId ?? state.cardBackId,
    canChangeDisplayName: canChangeDisplayName ?? state.canChangeDisplayName,
  })),

  setError: (msg) => set({ error: msg }),

  setMatchState: (s) => set({ matchState: s }),
  setMatchQueueKind: (matchQueueKind) => set({ matchQueueKind }),

  setSelectedDeck: (deck) => set({ selectedDeck: deck }),

  setOpponentName: (name) => set({ opponentName: name }),
  beginRankSnapshotRequest: (mode, requestId) => set((state) => ({
    rankSnapshotRequests: {
      ...state.rankSnapshotRequests,
      [mode]: beginRankSnapshotRequestState(state.rankSnapshotRequests[mode], requestId),
    },
  })),
  failRankSnapshotRequest: (mode, requestId, error, retryable = true) => set((state) => ({
    rankSnapshotRequests: {
      ...state.rankSnapshotRequests,
      [mode]: failRankSnapshotRequestState(state.rankSnapshotRequests[mode], requestId, error, retryable),
    },
  })),
  setRankProfile: (mode, rankProfile) => set((state) => {
    const current = state.rankProfiles[mode];
    if (!shouldReplaceRankProfile(current, rankProfile)) return {};
    const seasonTransition = transitionRankSnapshotSeason(
      state.rankSnapshotRequests[mode],
      rankProfile.seasonId,
    );
    const rankProfiles = { ...state.rankProfiles, [mode]: rankProfile };
    return {
      rankProfiles,
      ...(seasonTransition.clearPublicSnapshot ? {
        rankLeaderboards: { ...state.rankLeaderboards, [mode]: [] },
        factionStandingsByMode: { ...state.factionStandingsByMode, [mode]: [] },
        rankSnapshotRequests: {
          ...state.rankSnapshotRequests,
          [mode]: seasonTransition.state,
        },
      } : {}),
      ...(mode === "standard" ? {
        rankProfile,
        ...(seasonTransition.clearPublicSnapshot ? { rankLeaderboard: [] } : {}),
      } : {}),
    };
  }),
  setRankSnapshot: (mode, rankProfile, rankLeaderboard, factionStandings = [], metadata = {}) => set((state) => {
    const accepted = acceptRankSnapshot(state.rankSnapshotRequests[mode], {
      ...metadata,
      seasonId: rankProfile.seasonId,
    });
    const currentProfile = state.rankProfiles[mode];
    const replaceProfile = shouldReplaceRankProfile(
      currentProfile,
      rankProfile,
      metadata.allowSameSeasonProfileRegression,
    );
    const rankProfiles = replaceProfile
      ? { ...state.rankProfiles, [mode]: rankProfile }
      : state.rankProfiles;
    const rankLeaderboards = accepted.replacePublicSnapshot
      ? { ...state.rankLeaderboards, [mode]: rankLeaderboard }
      : state.rankLeaderboards;
    const factionStandingsByMode = accepted.replacePublicSnapshot
      ? { ...state.factionStandingsByMode, [mode]: factionStandings }
      : state.factionStandingsByMode;
    return {
      rankProfiles,
      rankLeaderboards,
      factionStandingsByMode,
      rankSnapshotRequests: { ...state.rankSnapshotRequests, [mode]: accepted.state },
      ...(mode === "standard" ? {
        ...(replaceProfile ? { rankProfile } : {}),
        ...(accepted.replacePublicSnapshot ? { rankLeaderboard } : {}),
      } : {}),
    };
  }),
  setLastRankResult: (lastRankResult) => set({ lastRankResult }),

  setRoomCode: (code) => set({ roomCode: code }),

  setRoomOperation: (roomOperation) => set({ roomOperation }),

  setOnlineCount: (n) => set({ onlineCount: n }),
  setMaintenance: (maintenance) => set({ maintenance }),
  setRulesets: (rulesets) => set({ rulesets }),
  setAdminOperations: (adminOperations) => set({ adminOperations }),
  setAdminPlayerSearchResults: (adminPlayerSearchResults) => set({ adminPlayerSearchResults }),
  setAdminTemporaryPassword: (adminTemporaryPassword) => set({ adminTemporaryPassword }),

  setPlayerList: (list) => set({ playerList: list }),

  setFriendData: (friends, incomingFriendRequests, outgoingFriendRequests) => set((state) => {
    const friendAccounts = new Set(friends.map((friend) => friendAccountKey(friend.account)));
    const friendChatUnreadByAccount = Object.fromEntries(
      Object.entries(state.friendChatUnreadByAccount).filter(([account]) => friendAccounts.has(account)),
    );
    return {
      friends,
      incomingFriendRequests,
      outgoingFriendRequests,
      friendChatUnreadByAccount,
    };
  }),

  setFriendSearchResults: (friendSearchResults) => set({ friendSearchResults }),
  setBlockedPlayers: (blockedPlayers) => set({ blockedPlayers }),

  setLeaderLeaderboard: (leaderLeaderboard) => set({ leaderLeaderboard }),

  setLeaderMatchups: (data) => set((state) => ({
    leaderMatchups: {
      ...state.leaderMatchups,
      [leaderMatchupKey(data.period, data.leaderNumber)]: data,
    },
  })),

  clearLeaderMatchups: () => set({ leaderMatchups: {} }),

  setLeaderMatchupMatrix: (leaderMatchupMatrix) => set({ leaderMatchupMatrix }),

  setPlayerProfileStats: (playerProfileStats) => set({ playerProfileStats }),

  setCardBackGallery: (cardBackGallery) => set({
    cardBackGallery,
    ...(cardBackGallery === null ? {
      cardBackGalleryOwned: [],
      cardBackGalleryTotal: 0,
      cardBackGalleryNextCursor: null,
      cardBackGalleryHasMore: false,
      cardBackGalleryLoadingMore: false,
    } : {}),
  }),
  setCardBackGalleryPage: ({ items, ownedItems, total, nextCursor, hasMore, append }) => set((state) => {
    const merged = append && state.cardBackGallery ? [...state.cardBackGallery, ...items] : items;
    const seen = new Set<string>();
    return {
      cardBackGallery: merged.filter((item) => {
        if (seen.has(item.id)) return false;
        seen.add(item.id);
        return true;
      }),
      cardBackGalleryOwned: ownedItems,
      cardBackGalleryTotal: total,
      cardBackGalleryNextCursor: nextCursor,
      cardBackGalleryHasMore: hasMore,
      cardBackGalleryLoadingMore: false,
    };
  }),
  setCardBackGalleryLoadingMore: (cardBackGalleryLoadingMore) => set({ cardBackGalleryLoadingMore }),
  updateCardBackGalleryItem: (item) => set((state) => ({
    cardBackGallery: state.cardBackGallery?.map((current) => current.id === item.id ? item : current) ?? null,
    cardBackGalleryOwned: state.cardBackGalleryOwned.map((current) => current.id === item.id ? item : current),
  })),
  setCardBackReviewQueue: (cardBackReviewQueue) => set({ cardBackReviewQueue }),
  setDeckPlazaPage: (deckPlazaPage) => set({ deckPlazaPage }),
  refreshDeckPlaza: () => set((state) => ({ deckPlazaRevision: state.deckPlazaRevision + 1 })),

  setIncomingInvite: (inv) => set({ incomingInvite: inv }),

  setFriendlyRoom: (room) => set({ friendlyRoom: room }),

  setSpectate: (spectateState, roomId = null) => set({ spectateState, spectateRoomId: roomId }),
  setSpectateSettings: (spectateMode, spectatorHandsPublic, spectateCode = null) => set({
    spectateMode,
    spectatorHandsPublic,
    spectateCode,
  }),

  setNavigateTo: (path) => set({ navigateTo: path }),

  addChatMessage: (msg) =>
    set((s) => ({
      chatMessages: [...s.chatMessages.slice(-99), msg],
    })),

  addFriendChatMessage: (msg) => set((state) => {
    const friendChatMessages = [...state.friendChatMessages.slice(-199), msg];
    const senderKey = friendAccountKey(msg.fromAccount);
    if (senderKey === friendAccountKey(state.account)) return { friendChatMessages };
    return {
      friendChatMessages,
      friendChatUnreadByAccount: {
        ...state.friendChatUnreadByAccount,
        [senderKey]: (state.friendChatUnreadByAccount[senderKey] ?? 0) + 1,
      },
    };
  }),

  markFriendChatRead: (account) => set((state) => {
    const key = friendAccountKey(account);
    if (!state.friendChatUnreadByAccount[key]) return state;
    const friendChatUnreadByAccount = { ...state.friendChatUnreadByAccount };
    delete friendChatUnreadByAccount[key];
    return { friendChatUnreadByAccount };
  }),

  clearChat: () => set({ chatMessages: [] }),

  reset: () => set({ ...initialState }),
}));
