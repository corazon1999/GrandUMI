/**
 * gameStore.ts — 游戏状态镜像（只读）
 *
 * 架构原则：
 *   store 是服务器权威状态的只读镜像
 *   syncFromServer 是唯一写入入口（immer 逐字段赋值）
 *   客户端不持有任何结算逻辑
 *
 * 历史方法（initFromDecks/executeAction/nextTurn 等）已删除。
 * 所有游戏动作通过 GameRequest 发到服务器，服务器结算后通过 MsgGameState 推回。
 */

import { create } from "zustand";
import { immer } from "zustand/middleware/immer";
import type { BattlePhase, GameMode } from "@/types/game";
import type { MsgGameState } from "@/types/net";

// ── 服务器快照中的字段（部分公开） ────────────────────────────────────────

export interface FieldCardView {
  id: string;
  number: string;
  isTapped: boolean;
  powerCurrent: number;
  attachedDon: number;
  gainedKeywords: string[];
  cannotActivateNextReset: boolean;
  turnPlayed: number;
}

export interface PlayerView {
  name: string;
  handCardNumbers: string[];   // 仅己方有内容；对手为空数组
  handCount: number;
  fieldCards: FieldCardView[];
  stageNumber: string | null;
  stageId: string | null;
  trashNumbers: string[];
  deckCount: number;
  lifeCount: number;
  lifeNumbers: string[];       // 始终为空（生命牌不公开），由触发流程单独 prompt
  leaderId: string;
  leaderNumber: string;
  leaderTapped: boolean;
  leaderPower: number;
  leaderAttachedDon: number;
  costActive: number;
  costRest: number;
  costAttached: number;
  donDeckCount: number;
  hasReDraw: boolean;
  mulliganDone: boolean;
}

export interface PromptView {
  promptId: string;
  kind: string;
  text: string;
  validChoices: string[];
  minChoose: number;
  maxChoose: number;
  extra: Record<string, unknown>;
}

export interface BattleView {
  attackerPlayer: number;
  attackerCardId: string;
  targetIsLeader: boolean;
  targetCardId: string | null;
  blockerCardId: string | null;
  attackerBonus: number;
  defenderBonus: number;
}

interface GameStore {
  // 元信息
  mode: GameMode;
  isStart: boolean;
  tick: number;
  currentTurn: boolean;
  turnCount: number;
  firstPlayer: number;
  mulliganBothDone: boolean;
  phase: BattlePhase;
  viewerKind: "player" | "spectator";

  // 双方
  my: PlayerView | null;
  opponent: PlayerView | null;

  // 当前 prompt / 战斗
  pendingPrompt: PromptView | null;
  battle: BattleView | null;

  // 动作驱动动画
  lastAction: string;
  lastActionPayloadObj: Record<string, unknown> | null;

  // UI 暂态
  isPending: boolean;
  isGameOver: boolean;
  winnerIsMe: boolean;
  gameOverReason: string;

  // 选中（纯本地）
  selectedHandIndex: number | null;
  selectedFieldId: string | null;
  selectedDonIndex: number | null;

  // 名字
  myName: string;
  opponentName: string;

  // ── 唯一写入路径 ─────────────────────────────────────────────────────
  syncFromServer: (msg: MsgGameState) => void;

  // 纯本地 UI 状态
  setPending: (v: boolean) => void;
  setSelectedHand: (idx: number | null) => void;
  setSelectedField: (id: string | null) => void;
  setSelectedDon: (idx: number | null) => void;
  setMode: (m: GameMode) => void;
  resetGame: () => void;
}

export const useGameStore = create<GameStore>()(
  immer((set) => ({
    mode: "Player",
    isStart: false,
    tick: 0,
    currentTurn: false,
    turnCount: 0,
    firstPlayer: 0,
    mulliganBothDone: false,
    phase: "Main",
    viewerKind: "player",
    my: null,
    opponent: null,
    pendingPrompt: null,
    battle: null,
    lastAction: "",
    lastActionPayloadObj: null,
    isPending: false,
    isGameOver: false,
    winnerIsMe: false,
    gameOverReason: "",
    selectedHandIndex: null,
    selectedFieldId: null,
    selectedDonIndex: null,
    myName: "",
    opponentName: "",

    syncFromServer: (msg) =>
      set((s) => {
        s.tick = msg.tick ?? s.tick + 1;
        s.phase = (msg.phase as BattlePhase) ?? "Main";
        s.currentTurn = msg.currentTurn;
        s.turnCount = msg.turnCount;
        s.firstPlayer = msg.firstPlayer ?? 0;
        s.mulliganBothDone = msg.mulliganBothDone ?? false;
        s.isGameOver = msg.isGameOver ?? false;
        s.winnerIsMe = msg.winnerIsMe ?? false;
        s.gameOverReason = msg.gameOverReason ?? "";
        s.viewerKind = (msg.viewerKind as "player" | "spectator") ?? "player";
        s.my = msg.my ?? null;
        s.opponent = msg.opponent ?? null;
        s.pendingPrompt = msg.pendingPrompt ?? null;
        s.battle = msg.battle ?? null;
        s.lastAction = msg.lastAction ?? "";
        s.lastActionPayloadObj = msg.actionPayload
          ? (() => {
              try { return JSON.parse(msg.actionPayload as string) as Record<string, unknown>; }
              catch { return null; }
            })()
          : null;
        s.isPending = false;
        s.myName = msg.my?.name ?? "";
        s.opponentName = msg.opponent?.name ?? "";
        // 收到新快照后清掉选中
        s.selectedHandIndex = null;
        s.selectedFieldId = null;
        s.selectedDonIndex = null;
      }),

    setPending: (v) => set((s) => { s.isPending = v; }),
    setSelectedHand: (idx) => set((s) => {
      s.selectedHandIndex = idx;
      s.selectedFieldId = null;
      s.selectedDonIndex = null;
    }),
    setSelectedField: (id) => set((s) => {
      s.selectedFieldId = id;
      s.selectedHandIndex = null;
      s.selectedDonIndex = null;
    }),
    setSelectedDon: (idx) => set((s) => {
      s.selectedDonIndex = idx;
      s.selectedHandIndex = null;
      s.selectedFieldId = null;
    }),
    setMode: (m) => set((s) => { s.mode = m; }),
    resetGame: () => set((s) => {
      s.tick = 0;
      s.currentTurn = false;
      s.turnCount = 0;
      s.firstPlayer = 0;
      s.mulliganBothDone = false;
      s.phase = "Main";
      s.my = null;
      s.opponent = null;
      s.pendingPrompt = null;
      s.battle = null;
      s.lastAction = "";
      s.lastActionPayloadObj = null;
      s.isPending = false;
      s.isGameOver = false;
      s.winnerIsMe = false;
      s.gameOverReason = "";
      s.selectedHandIndex = null;
      s.selectedFieldId = null;
      s.selectedDonIndex = null;
      s.myName = "";
      s.opponentName = "";
    }),
  })),
);
