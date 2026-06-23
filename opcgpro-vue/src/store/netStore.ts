import { createStore } from "zustand/vanilla";
import type { PlayerInfo, FriendlyPlayer } from "@/types/net";

export type IncomingInvite = { inviteId: string; fromName: string };
export type FriendlyRoomState = {
  roomId: string;
  players: FriendlyPlayer[];
  scores: number[];
  state: "lobby" | "playing";
};

export interface ChatMessage {
  Name: string;
  Msg: string;
  type: number;
  time: number;
}

export interface SelectedDeck {
  name: string;
  leader: string;
  leaderName: string;
  cards: string; // 卡组字符串（DeckMapper.exportDeckString 格式）
}

type MatchState = "idle" | "matching" | "matched" | "bot-matching";

interface NetStore {
  // 连接状态（含重连状态）
  connState: "disconnected" | "connecting" | "handshaking" | "connected" | "reconnecting" | "recovering" | "failed";
  // 重连倒计时（秒），ReconnectOverlay 显示用
  reconnectCountdown: number;
  // 账户状态
  loggedIn: boolean;
  account: string;
  playerName: string;
  // 错误提示
  error: string | null;
  // 匹配
  matchState: MatchState;
  selectedDeck: SelectedDeck | null;
  opponentName: string;
  // 房间码
  roomCode: string | null;
  // 在线人数
  onlineCount: number;
  // 在线玩家列表
  playerList: PlayerInfo[];
  // 收到的对战邀请
  incomingInvite: IncomingInvite | null;
  // 友谊战房间
  friendlyRoom: FriendlyRoomState | null;
  // 聊天
  chatMessages: ChatMessage[];
  // 客户端路由导航（避免 window.location.href 导致整页刷新断开 WebSocket）
  navigateTo: string | null;

  // actions
  setConnState: (s: NetStore["connState"]) => void;
  setReconnectCountdown: (n: number) => void;
  setLoggedIn: (v: boolean, name?: string, account?: string) => void;
  setPlayerName: (name: string) => void;
  setError: (msg: string | null) => void;
  setMatchState: (s: MatchState) => void;
  setSelectedDeck: (deck: SelectedDeck | null) => void;
  setOpponentName: (name: string) => void;
  setRoomCode: (code: string | null) => void;
  setOnlineCount: (n: number) => void;
  setPlayerList: (list: PlayerInfo[]) => void;
  setIncomingInvite: (inv: IncomingInvite | null) => void;
  setFriendlyRoom: (room: FriendlyRoomState | null) => void;
  setNavigateTo: (path: string | null) => void;
  addChatMessage: (msg: ChatMessage) => void;
  clearChat: () => void;
  reset: () => void;
}

const initialState = {
  connState: "disconnected" as const,
  reconnectCountdown: 0,
  loggedIn: false,
  account: "",
  playerName: "",
  error: null as string | null,
  matchState: "idle" as MatchState,
  selectedDeck: null as SelectedDeck | null,
  opponentName: "",
  roomCode: null as string | null,
  onlineCount: 0,
  playerList: [] as PlayerInfo[],
  incomingInvite: null as IncomingInvite | null,
  friendlyRoom: null as FriendlyRoomState | null,
  chatMessages: [] as ChatMessage[],
  navigateTo: null as string | null,
};

export const useNetStore = createStore<NetStore>()((set) => ({
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

  setError: (msg) => set({ error: msg }),

  setMatchState: (s) => set({ matchState: s }),

  setSelectedDeck: (deck) => set({ selectedDeck: deck }),

  setOpponentName: (name) => set({ opponentName: name }),

  setRoomCode: (code) => set({ roomCode: code }),

  setOnlineCount: (n) => set({ onlineCount: n }),

  setPlayerList: (list) => set({ playerList: list }),

  setIncomingInvite: (inv) => set({ incomingInvite: inv }),

  setFriendlyRoom: (room) => set({ friendlyRoom: room }),

  setNavigateTo: (path) => set({ navigateTo: path }),

  addChatMessage: (msg) =>
    set((s) => ({
      chatMessages: [...s.chatMessages.slice(-99), msg],
    })),

  clearChat: () => set({ chatMessages: [] }),

  reset: () => set({ ...initialState }),
}));
