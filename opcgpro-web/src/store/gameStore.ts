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
import type { MsgGameState, RevealSnapshot } from "@/types/net";

// ── 服务器快照中的字段（部分公开） ────────────────────────────────────────

export interface FieldCardView {
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
  cannotAttack: boolean;     // 是否存在明确的“无法攻击”状态（不含横置、新登场等普通条件）
}

export interface PlayerView {
  name: string;
  cardBackId?: string;         // 旧回放缺失时由卡背组件回退经典款
  handCardNumbers: string[];   // 仅己方有内容；对手为空数组
  handCardCosts: number[];     // 每张手牌的有效费用（含静态减费）；仅己方有内容
  handCardCounters: number[];  // 每张手牌的有效反击值（含静态光环）；仅己方有内容
  handCount: number;
  fieldCards: FieldCardView[];
  stageNumber: string | null;
  stageId: string | null;
  stageTapped: boolean;
  trashNumbers: string[];
  deckCount: number;
  lifeCount: number;
  lifeNumbers: string[];       // 始终为空（生命牌不公开），由触发流程单独 prompt
  // 生命区每张牌的正反朝向（后端权威，顶→底）：faceUp 时 number 为公开番号，否则 null（背面占位）
  lifeFaceUp?: { faceUp: boolean; number: string | null }[];
  leaderId: string;
  leaderNumber: string;
  leaderTapped: boolean;
  leaderPower: number;
  leaderAttachedDon: number;
  leaderCanAttack: boolean;   // 领袖当前是否可发起攻击（后端权威）
  leaderCannotAttack: boolean; // 领袖是否存在明确的“无法攻击”状态
  leaderActivatedUsedThisTurn: boolean;  // 领袖【启动主要】【每回合1次】本回合是否已用
  stageActivatedUsedThisTurn: boolean;   // 舞台【启动主要】【每回合1次】本回合是否已用
  costActive: number;
  costRest: number;
  costAttached: number;
  donDeckCount: number;
  hasReDraw: boolean;
  mulliganDone: boolean;
}

function clonePlayerView(player: PlayerView | null): PlayerView | null {
  if (!player) return null;
  return {
    ...player,
    // IndexedDB 中的旧回放可能缺少后来新增的手牌费用、反击值等字段。
    // 在同步入口统一补齐，避免旧快照中断回放或让手牌区域无法渲染。
    handCardNumbers: [...(player.handCardNumbers ?? [])],
    handCardCosts: [...(player.handCardCosts ?? [])],
    handCardCounters: [...(player.handCardCounters ?? [])],
    fieldCards: (player.fieldCards ?? []).map((card) => ({
      ...card,
      gainedKeywords: [...(card.gainedKeywords ?? [])],
    })),
    trashNumbers: [...(player.trashNumbers ?? [])],
    lifeNumbers: [...(player.lifeNumbers ?? [])],
    lifeFaceUp: player.lifeFaceUp?.map((life) => ({ ...life })),
  };
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
  firstPlayerChosen: boolean;
  isFirstPlayer: boolean;
  canChooseFirstPlayer: boolean;
  diceWinnerIsMe: boolean;
  startingDiceRolls: Array<{ my: number; opponent: number; tie: boolean }>;
  mulliganBothDone: boolean;
  mulliganDeadlineUtc: string | null;
  phase: BattlePhase;
  viewerKind: "player" | "spectator";

  // 双方
  my: PlayerView | null;
  opponent: PlayerView | null;
  // 最近一份服务端权威副本，供乐观更新被拒绝时回滚。
  authoritativeMy: PlayerView | null;
  authoritativeBattle: BattleView | null;

  // 当前 prompt / 战斗
  pendingPrompt: PromptView | null;
  // 普通手牌满场出牌时的本地腾位选择；确认后与 PlayCard 合并为一次请求
  localOverflowHandIndex: number | null;
  battle: BattleView | null;

  // 动作驱动动画
  lastAction: string;
  lastActionPayloadObj: Record<string, unknown> | null;

  // 操作日志（按 tick 去重累积）
  logLines: { id: string; text: string }[];
  lastLogTick: number;

  // 检索/公开牌瞬时展示（nonce 递增以便重复触发；由 RevealOverlay 计时清除）
  reveal: (RevealSnapshot & { nonce: number }) | null;

  // #241 选择/确认成功的瞬时提示（nonce 递增触发；由 PromptSuccessFlash 计时清除）
  promptFlash: number;

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
  spectatorNames: string[];

  // ── 唯一写入路径 ─────────────────────────────────────────────────────
  syncFromServer: (msg: MsgGameState) => void;
  clearReveal: () => void;
  flashPromptSuccess: () => void;
  openLocalOverflow: (handIndex: number) => void;
  clearLocalOverflow: () => void;
  optimisticTrashFieldCard: (cardId: string) => void;
  optimisticPlayCard: (handIndex: number) => void;
  optimisticAttachDon: (targetId: string, count: number) => void;
  optimisticAttack: (attackerId: string) => void;
  rollbackOptimistic: () => void;

  // 纯本地 UI 状态
  setPending: (v: boolean) => void;
  setSelectedHand: (idx: number | null) => void;
  setSelectedField: (id: string | null) => void;
  setSelectedDon: (idx: number | null) => void;
  setSpectatorNames: (names: string[]) => void;
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
    firstPlayer: -1,
    firstPlayerChosen: false,
    isFirstPlayer: false,
    canChooseFirstPlayer: false,
    diceWinnerIsMe: false,
    startingDiceRolls: [],
    mulliganBothDone: false,
    mulliganDeadlineUtc: null,
    phase: "Main",
    viewerKind: "player",
    my: null,
    opponent: null,
    authoritativeMy: null,
    authoritativeBattle: null,
    pendingPrompt: null,
    localOverflowHandIndex: null,
    battle: null,
    lastAction: "",
    lastActionPayloadObj: null,
    logLines: [],
    lastLogTick: -1,
    reveal: null,
    promptFlash: 0,
    isPending: false,
    isGameOver: false,
    winnerIsMe: false,
    gameOverReason: "",
    selectedHandIndex: null,
    selectedFieldId: null,
    selectedDonIndex: null,
    myName: "",
    opponentName: "",
    spectatorNames: [],

    syncFromServer: (msg) =>
      set((s) => {
        const firstPlayer = msg.firstPlayer ?? -1;
        const my = clonePlayerView(msg.my ?? null);
        const opponent = clonePlayerView(msg.opponent ?? null);
        s.tick = msg.tick ?? s.tick + 1;
        s.phase = (msg.phase as BattlePhase) ?? "Main";
        s.currentTurn = msg.currentTurn;
        s.turnCount = msg.turnCount;
        s.firstPlayer = firstPlayer;
        // firstPlayerChosen 是骰点流程上线后新增的字段。旧回放虽没有该字段，
        // 但 firstPlayer 已是 0/1；据此兼容推断，避免 HandArea 把整局手牌隐藏。
        s.firstPlayerChosen = msg.firstPlayerChosen ?? (firstPlayer === 0 || firstPlayer === 1);
        s.isFirstPlayer = msg.isFirstPlayer ?? false;
        s.canChooseFirstPlayer = msg.canChooseFirstPlayer ?? false;
        s.diceWinnerIsMe = msg.diceWinnerIsMe ?? false;
        s.startingDiceRolls = msg.startingDiceRolls ?? [];
        s.mulliganBothDone = msg.mulliganBothDone ?? false;
        s.mulliganDeadlineUtc = msg.mulliganDeadlineUtc ?? null;
        s.isGameOver = msg.isGameOver ?? false;
        s.winnerIsMe = msg.winnerIsMe ?? false;
        s.gameOverReason = msg.gameOverReason ?? "";
        s.viewerKind = (msg.viewerKind as "player" | "spectator") ?? "player";
        s.my = my;
        s.opponent = opponent;
        s.authoritativeMy = clonePlayerView(my);
        s.pendingPrompt = msg.pendingPrompt ?? null;
        s.localOverflowHandIndex = null;
        s.battle = msg.battle ?? null;
        s.authoritativeBattle = msg.battle ? { ...msg.battle } : null;
        s.lastAction = msg.lastAction ?? "";
        s.lastActionPayloadObj = msg.actionPayload
          ? (() => {
              try { return JSON.parse(msg.actionPayload as string) as Record<string, unknown>; }
              catch { return null; }
            })()
          : null;
        // 操作日志：同一快照可携带多条效果选择记录；仍按 tick 去重，避免重连重复追加。
        {
          const lines = msg.logLines?.length
            ? msg.logLines
            : (msg.logLine ? [msg.logLine] : []);
          if (s.tick > s.lastLogTick) {
            lines.forEach((text, index) => {
              if (text) s.logLines.push({ id: `${s.tick}:${index}`, text });
            });
            if (s.logLines.length > 200) {
              s.logLines.splice(0, s.logLines.length - 200);
            }
          }
          if (s.tick > s.lastLogTick) s.lastLogTick = s.tick;
        }
        // 检索公开：仅在快照携带 reveal 时写入（递增 nonce 触发展示）；
        // 不携带时保持原值不动，交由 RevealOverlay 的计时器清除，避免被紧随的普通快照瞬间抹掉
        if (msg.reveal) {
          s.reveal = { ...msg.reveal, nonce: (s.reveal?.nonce ?? 0) + 1 };
        }
        s.isPending = false;
        s.myName = my?.name ?? "";
        s.opponentName = opponent?.name ?? "";
        // 收到新快照后清掉选中
        s.selectedHandIndex = null;
        s.selectedFieldId = null;
        s.selectedDonIndex = null;
      }),

    clearReveal: () => set((s) => { s.reveal = null; }),
    flashPromptSuccess: () => set((s) => { s.promptFlash = s.promptFlash + 1; }),
    openLocalOverflow: (handIndex) => set((s) => { s.localOverflowHandIndex = handIndex; }),
    clearLocalOverflow: () => set((s) => { s.localOverflowHandIndex = null; }),
    // 只做即时视觉反馈；下一份服务端快照会覆盖为权威状态。
    optimisticTrashFieldCard: (cardId) => set((s) => {
      if (!s.my) return;
      const index = s.my.fieldCards.findIndex((c) => c.id === cardId);
      if (index < 0) return;
      const [card] = s.my.fieldCards.splice(index, 1);
      s.my.trashNumbers.push(card.number);
    }),
    optimisticPlayCard: (handIndex) => set((s) => {
      if (!s.my || handIndex < 0 || handIndex >= s.my.handCardNumbers.length) return;
      s.my.handCardNumbers.splice(handIndex, 1);
      s.my.handCardCosts.splice(handIndex, 1);
      s.my.handCardCounters.splice(handIndex, 1);
      s.my.handCount = Math.max(0, s.my.handCount - 1);
    }),
    optimisticAttachDon: (targetId, count) => set((s) => {
      if (!s.my) return;
      const actual = Math.max(0, Math.min(count, s.my.costActive));
      if (actual === 0) return;
      s.my.costActive -= actual;
      s.my.costAttached += actual;
      if (targetId === "leader" || targetId === s.my.leaderId) {
        s.my.leaderAttachedDon += actual;
      } else {
        const target = s.my.fieldCards.find((c) => c.id === targetId);
        if (target) target.attachedDon += actual;
      }
    }),
    optimisticAttack: (attackerId) => set((s) => {
      if (!s.my) return;
      if (attackerId === s.my.leaderId) s.my.leaderTapped = true;
      else {
        const attacker = s.my.fieldCards.find((c) => c.id === attackerId);
        if (attacker) attacker.isTapped = true;
      }
    }),
    rollbackOptimistic: () => set((s) => {
      s.my = clonePlayerView(s.authoritativeMy);
      s.battle = s.authoritativeBattle ? { ...s.authoritativeBattle } : null;
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
    setSpectatorNames: (names) => set((s) => { s.spectatorNames = names; }),
    setMode: (m) => set((s) => { s.mode = m; }),
    resetGame: () => set((s) => {
      s.tick = 0;
      s.currentTurn = false;
      s.turnCount = 0;
      s.firstPlayer = -1;
      s.firstPlayerChosen = false;
      s.isFirstPlayer = false;
      s.canChooseFirstPlayer = false;
      s.diceWinnerIsMe = false;
      s.startingDiceRolls = [];
      s.mulliganBothDone = false;
      s.mulliganDeadlineUtc = null;
      s.phase = "Main";
      s.my = null;
      s.opponent = null;
      s.authoritativeMy = null;
      s.authoritativeBattle = null;
      s.pendingPrompt = null;
      s.localOverflowHandIndex = null;
      s.battle = null;
      s.lastAction = "";
      s.lastActionPayloadObj = null;
      s.logLines = [];
      s.lastLogTick = -1;
      s.reveal = null;
      s.isPending = false;
      s.isGameOver = false;
      s.winnerIsMe = false;
      s.gameOverReason = "";
      s.selectedHandIndex = null;
      s.selectedFieldId = null;
      s.selectedDonIndex = null;
      s.myName = "";
      s.opponentName = "";
      s.spectatorNames = [];
    }),
  })),
);
