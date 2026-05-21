/**
 * gameStore.ts — 游戏状态镜像
 * 对应 C# GameManager.cs
 *
 * 架构原则（重构方案 §5.3）：
 *   store 是服务器权威状态的只读镜像
 *   syncFromServer 是唯一写入入口（immer 逐字段赋值）
 *   客户端不持有结算逻辑，禁止在组件中直接修改游戏状态字段
 *
 * 过渡期说明：
 *   当前服务器仍使用 MsgTransmit 字符串协议，本地仍需处理游戏逻辑
 *   以下方法标记 @deprecated 的将在服务器升级到 MsgGameState 后移除：
 *     addToHand, removeFromHand, playToField, tapCard, applyBuff,
 *     removeFromField, takeDamage, nextTurn
 */

import { create } from "zustand";
import { immer } from "zustand/middleware/immer";
import type { GameState, GameMode, BattlePhase, PlayerState, FieldCardState, DonCard, DonState } from "@/types/game";
import type { CardData } from "@/types/card";
import type { MsgGameState, PlayerSnapshot, FieldCardSnapshot } from "@/types/net";
import { getCard } from "@/data/CardLoader";

// ── 工具函数 ──────────────────────────────────────────────────────────────

function shuffle<T>(arr: T[]): T[] {
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]];
  }
  return arr;
}

/** 生成 10 张初始咚卡（全部在 deck 状态） */
function createDonCards(): DonCard[] {
  return Array.from({ length: 10 }, (_, i) => ({
    id: `don-${i}`,
    state: "deck" as DonState,
  }));
}

/** 根据 active/max 数量重建咚卡数组（用于服务器快照同步时近似还原） */
function makeDonCardsFromCounts(active: number, max: number): DonCard[] {
  const cards: DonCard[] = [];
  for (let i = 0; i < 10; i++) {
    if (i < active) cards.push({ id: `don-${i}`, state: "active" });
    else if (i < max) cards.push({ id: `don-${i}`, state: "rest" });
    else cards.push({ id: `don-${i}`, state: "deck" });
  }
  return cards;
}

function emptyPlayerState(): PlayerState {
  return {
    cost: { active: 0, rest: 0, max: 0, donCards: createDonCards() },
    hand: { cards: [], canChoose: false },
    field: { characterCards: [], stageCard: null, isMyTurn: false },
    deckCount: 50,
    life: [],
    leader: null,
  };
}

/** 将服务器 PlayerSnapshot 转为本地 PlayerState */
function snapshotToPlayerState(snap: PlayerSnapshot, isMe: boolean): PlayerState {
  return {
    cost: {
      active: snap.costActive,
      rest: 0, // 服务器暂不追踪 rest
      max: snap.costMax,
      donCards: makeDonCardsFromCounts(snap.costActive, snap.costMax),
    },
    hand: {
      cards: isMe
        ? (snap.handCardNumbers.map((n) => getCard(n)).filter(Boolean) as CardData[])
        : Array<CardData>(snap.handCount).fill(null as unknown as CardData),
      canChoose: false,
    },
    field: {
      characterCards: snap.fieldCards.map(
        (fc: FieldCardSnapshot): FieldCardState => ({
          card: getCard(fc.number)!,
          isTapped: fc.isTapped,
          powerBuff: fc.powerBuff,
          isSelected: false,
        }),
      ),
      stageCard: snap.stageNumber ? (getCard(snap.stageNumber) ?? null) : null,
      isMyTurn: false,
    },
    deckCount: snap.deckCount,
    life: Array<CardData>(snap.lifeCount).fill(null as unknown as CardData),
    leader: snap.leaderNumber ? (getCard(snap.leaderNumber) ?? null) : null,
  };
}

/** 从 donCards 中取 count 张 stateA 的咚卡改为 stateB，返回改动的索引 */
function transitionDonCards(
  cards: DonCard[],
  fromState: DonState,
  toState: DonState,
  count: number,
  attachedTo?: number,
): void {
  let changed = 0;
  for (const c of cards) {
    if (changed >= count) break;
    if (c.state === fromState) {
      c.state = toState;
      if (toState === "attached") c.attachedTo = attachedTo;
      else delete c.attachedTo;
      changed++;
    }
  }
}

// ── Store 类型 ────────────────────────────────────────────────────────────

type MulliganState = "none" | "choosing" | "waiting" | "done";

interface GameStore extends GameState {
  isPending: boolean;
  isGameOver: boolean;
  selectedHandIndex: number | null;
  selectedFieldIndex: number | null;
  selectedDonId: string | null;

  lastAction: string;
  lastActionPayloadObj: Record<string, unknown> | null;

  mulliganState: MulliganState;
  opponentMulliganDone: boolean;
  myDeckCards: string[];

  syncFromServer: (msg: MsgGameState) => void;
  setPending: (v: boolean) => void;
  setGameOver: (v: boolean) => void;
  setSelectedHand: (index: number | null) => void;
  setSelectedField: (index: number | null) => void;
  setSelectedDon: (id: string | null) => void;
  setMode: (mode: GameMode) => void;
  setLastAction: (action: string, payload: Record<string, unknown> | null) => void;

  mulliganRedraw: () => void;
  mulliganKeep: () => void;
  setOpponentMulliganDone: () => void;

  executeAction: (msg: string, isFromOpponent: boolean) => void;

  initFromDecks: (myDeckStr: string, enemyDeckStr: string, isFirst: boolean) => void;

  setNames: (myName: string, opponentName: string) => void;
  setIsStart: (v: boolean) => void;
  setPhase: (phase: BattlePhase) => void;
  setCurrentTurn: (v: boolean) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  nextTurn: () => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  addToHand: (side: "my" | "opponent", card: CardData) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  removeFromHand: (side: "my" | "opponent", index: number) => void;
  setHandSelectable: (selectable: boolean) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  playToField: (card: CardData) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  tapCard: (side: "my" | "opponent", index: number) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  applyBuff: (side: "my" | "opponent", cardIndex: number, delta: number) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  removeFromField: (side: "my" | "opponent", index: number) => void;
  /** @deprecated 服务器升级后由 syncFromServer 替代 */
  takeDamage: (side: "my" | "opponent", count: number) => void;
  setCost: (side: "my" | "opponent", active: number, rest: number, max: number) => void;
  setLeader: (side: "my" | "opponent", card: CardData) => void;
  resetGame: () => void;
}

// ── Store 实现 ────────────────────────────────────────────────────────────

export const useGameStore = create<GameStore>()(
  immer((set) => ({
    // 基础状态
    mode: "Player",
    isStart: false,
    currentTurn: false,
    turnCount: 0,
    haveExtraTurn: false,
    phase: "Main" as BattlePhase,
    my: emptyPlayerState(),
    opponent: emptyPlayerState(),
    myName: "",
    opponentName: "",

    // 新架构字段
    isPending: false,
    isGameOver: false,
    selectedHandIndex: null,
    selectedFieldIndex: null,
    selectedDonId: null,
    lastAction: "",
    lastActionPayloadObj: null,

    // 换牌阶段
    mulliganState: "none" as MulliganState,
    opponentMulliganDone: false,
    myDeckCards: [] as string[],

    // ── 新架构方法 ─────────────────────────────────────────────────────

    syncFromServer: (msg: MsgGameState) =>
      set((s) => {
        s.phase = msg.phase as BattlePhase;
        s.currentTurn = msg.currentTurn;
        s.turnCount = msg.turnCount;
        s.lastAction = msg.lastAction;
        s.lastActionPayloadObj = msg.actionPayload
          ? (JSON.parse(msg.actionPayload) as Record<string, unknown>)
          : null;
        s.my = snapshotToPlayerState(msg.my, true);
        s.opponent = snapshotToPlayerState(msg.opponent, false);
        s.selectedHandIndex = null;
        s.selectedFieldIndex = null;
        s.selectedDonId = null;
        s.isPending = false;
      }),

    setPending: (v) => set((s) => { s.isPending = v; }),
    setGameOver: (v) => set((s) => { s.isGameOver = v; }),
    setSelectedHand: (index) => set((s) => { s.selectedHandIndex = index; s.selectedDonId = null; }),
    setSelectedField: (index) =>
      set((s) => {
        // 若已选中咚卡且选中场上角色，则执行附着
        if (index !== null && s.selectedDonId !== null && s.currentTurn && !s.isPending) {
          const donCard = s.my.cost.donCards.find((c) => c.id === s.selectedDonId);
          if (donCard && donCard.state === "active") {
            const chars = s.my.field.characterCards;
            if (index >= 0 && index < chars.length) {
              donCard.state = "attached";
              donCard.attachedTo = index;
              s.my.cost.active -= 1;
              s.selectedDonId = null;
              s.selectedFieldIndex = null;
              return;
            }
          }
        }
        s.selectedFieldIndex = index;
        s.selectedDonId = null;
      }),
    setSelectedDon: (id) =>
      set((s) => {
        s.selectedDonId = s.selectedDonId === id ? null : id;
        s.selectedHandIndex = null;
        s.selectedFieldIndex = null;
      }),
    setMode: (mode) => set((s) => { s.mode = mode; }),
    setLastAction: (action, payload) =>
      set((s) => {
        s.lastAction = action;
        s.lastActionPayloadObj = payload;
      }),

    // ── 换牌方法 ──────────────────────────────────────────────────────────

    mulliganRedraw: () =>
      set((s) => {
        const handNums = s.my.hand.cards.map((c) => c.number);
        s.myDeckCards.push(...handNums);
        shuffle(s.myDeckCards);
        const newHandNums = s.myDeckCards.splice(0, 5);
        s.my.hand.cards = newHandNums.map((n) => getCard(n)).filter(Boolean) as CardData[];
        s.my.deckCount = s.myDeckCards.length;
        s.mulliganState = s.opponentMulliganDone ? "done" : "waiting";
      }),

    mulliganKeep: () =>
      set((s) => {
        s.mulliganState = s.opponentMulliganDone ? "done" : "waiting";
      }),

    setOpponentMulliganDone: () =>
      set((s) => {
        s.opponentMulliganDone = true;
        if (s.mulliganState === "waiting") {
          s.mulliganState = "done";
        }
      }),

    // ── 本地游戏动作执行 ─────────────────────────────────────────────────

    executeAction: (msg, isFromOpponent) =>
      set((s) => {
        const parts = msg.split("^");
        const action = parts[0];
        const side = isFromOpponent ? "opponent" : "my";

        if (action === "AppearFromHand") {
          const cardNumber = parts[1];
          const handIndex = parseInt(parts[2], 10);
          const card = getCard(cardNumber);
          if (!card) return;

          // 从手牌移除
          const handCards = s[side].hand.cards;
          if (handIndex >= 0 && handIndex < handCards.length) {
            handCards.splice(handIndex, 1);
          }

          // 扣除费用：活跃咚 → 休息咚
          const isFree = parts[3] === "True";
          const cost = card.cost;
          if (!isFree && cost > 0) {
            transitionDonCards(s[side].cost.donCards, "active", "rest", cost);
            s[side].cost.active = Math.max(s[side].cost.active - cost, 0);
            s[side].cost.rest += cost;
          }

          // 登场到对应区域
          if (card.type === "Character") {
            s[side].field.characterCards.push({
              card,
              isTapped: false,
              powerBuff: 0,
              isSelected: false,
            });
          } else if (card.type === "Stage") {
            s[side].field.stageCard = card;
          }
        }

        // 抽牌
        if (action === "Draw") {
          const cardNumber = parts[1];
          if (isFromOpponent) {
            s.opponent.hand.cards.push(null as unknown as CardData);
            s.opponent.deckCount = Math.max(s.opponent.deckCount - 1, 0);
          } else {
            const card = getCard(cardNumber);
            if (card) s.my.hand.cards.push(card);
            s.my.deckCount = Math.max(s.my.deckCount - 1, 0);
            const idx = s.myDeckCards.indexOf(cardNumber);
            if (idx >= 0) s.myDeckCards.splice(idx, 1);
          }
        }

        // 附加咚到场上角色（字段: AddDong^fieldIndex^count^isActive）
        if (action === "AddDong") {
          const fieldIndex = parseInt(parts[1], 10);
          const count = parseInt(parts[2], 10);
          const chars = s[side].field.characterCards;
          if (fieldIndex < 0 || fieldIndex >= chars.length) return;
          const fc = chars[fieldIndex];
          if (!fc) return;

          // 活跃咚 → 附着
          transitionDonCards(s[side].cost.donCards, "active", "attached", count, fieldIndex);
          s[side].cost.active = Math.max(s[side].cost.active - count, 0);
        }
      }),

    // ── 对局初始化（对应 C# Deck.LoadMyDeck + SelfInit + Life.InitLife）──

    initFromDecks: (myDeckStr, enemyDeckStr, isFirst) =>
      set((s) => {
        const parseNums = (str: string) => str.split("\n").filter(Boolean);

        // 己方卡组
        const myNums = parseNums(myDeckStr);
        const myLeader = getCard(myNums[0]) ?? null;
        const myDeckCards = myNums.slice(1);
        shuffle(myDeckCards);

        const myLifeCount = myLeader?.cost ?? 5;
        const myLifeNums = myDeckCards.splice(0, myLifeCount);
        const myHandNums = myDeckCards.splice(0, 5);

        s.my.leader = myLeader;
        s.my.life = myLifeNums.map((n) => getCard(n) ?? null).filter(Boolean) as CardData[];
        s.my.hand.cards = myHandNums.map((n) => getCard(n)).filter(Boolean) as CardData[];
        s.my.hand.canChoose = false;
        s.my.field.characterCards = [];
        s.my.field.stageCard = null;
        s.myDeckCards = [...myDeckCards];
        s.my.deckCount = myDeckCards.length;

        // 先手第一回合 +1 DON（deck→active），后手初始 0
        const myDons = createDonCards();
        const myInitDon = isFirst ? 1 : 0;
        transitionDonCards(myDons, "deck", "active", myInitDon);
        s.my.cost = {
          active: myInitDon,
          rest: 0,
          max: myInitDon,
          donCards: myDons,
        };

        // 对手卡组
        const enemyNums = parseNums(enemyDeckStr);
        const enemyLeader = getCard(enemyNums[0]) ?? null;
        const enemyLifeCount = enemyLeader?.cost ?? 5;
        const enemyDeckTotal = enemyNums.length - 1;

        s.opponent.leader = enemyLeader;
        s.opponent.life = Array(enemyLifeCount).fill(null) as CardData[];
        s.opponent.hand.cards = Array(5).fill(null) as CardData[];
        s.opponent.hand.canChoose = false;
        s.opponent.field.characterCards = [];
        s.opponent.field.stageCard = null;
        s.opponent.deckCount = enemyDeckTotal - enemyLifeCount - 5;

        // 对手费用镜像
        const oppDons = createDonCards();
        const oppInitDon = isFirst ? 0 : 1;
        transitionDonCards(oppDons, "deck", "active", oppInitDon);
        s.opponent.cost = {
          active: oppInitDon,
          rest: 0,
          max: oppInitDon,
          donCards: oppDons,
        };

        // 全局状态
        s.isStart = isFirst;
        s.currentTurn = isFirst;
        s.turnCount = 1;
        s.phase = "Main";
        s.isPending = false;
        s.isGameOver = false;
        s.selectedHandIndex = null;
        s.selectedFieldIndex = null;
        s.selectedDonId = null;
        s.mulliganState = "choosing";
        s.opponentMulliganDone = false;
      }),

    // ── 过渡期方法（保持与现有 MsgTransmit 流程兼容）───────────────────

    setNames: (myName, opponentName) => set((s) => { s.myName = myName; s.opponentName = opponentName; }),

    setIsStart: (v) => set((s) => { s.isStart = v; }),

    setPhase: (phase) => set((s) => { s.phase = phase; }),

    setCurrentTurn: (v) => set((s) => { s.currentTurn = v; }),

    nextTurn: () =>
      set((s) => {
        s.currentTurn = !s.currentTurn;
        s.turnCount = s.turnCount + 1;
        s.phase = "Main";

        const addDon = 2;
        if (s.currentTurn) {
          // ── 我方重置阶段 ─────────────────────────────────────────
          // 1. 角色恢复竖置
          s.my.field.characterCards.forEach((c) => { c.isTapped = false; });
          // 2. 附着咚 → 休息咚
          let returnedDon = 0;
          for (const don of s.my.cost.donCards) {
            if (don.state === "attached") {
              don.state = "rest";
              delete don.attachedTo;
              returnedDon++;
            }
          }
          // 3. 休息咚 → 活跃咚
          let restToActive = 0;
          for (const don of s.my.cost.donCards) {
            if (don.state === "rest") {
              don.state = "active";
              restToActive++;
            }
          }
          s.my.cost.rest = 0;
          s.my.cost.active += restToActive;
          // 4. 咚!!阶段：deck → active（+2 咚，上限 10）
          transitionDonCards(s.my.cost.donCards, "deck", "active", addDon);
          s.my.cost.max = Math.min(s.my.cost.max + addDon, 10);
          s.my.cost.active = Math.min(s.my.cost.active + addDon, s.my.cost.max);
        } else {
          // ── 对手重置阶段 ─────────────────────────────────────────
          let returnedDon = 0;
          for (const don of s.opponent.cost.donCards) {
            if (don.state === "attached") {
              don.state = "rest";
              delete don.attachedTo;
              returnedDon++;
            }
          }
          let restToActive = 0;
          for (const don of s.opponent.cost.donCards) {
            if (don.state === "rest") {
              don.state = "active";
              restToActive++;
            }
          }
          s.opponent.cost.rest = 0;
          s.opponent.cost.active += restToActive;
          transitionDonCards(s.opponent.cost.donCards, "deck", "active", addDon);
          s.opponent.cost.max = Math.min(s.opponent.cost.max + addDon, 10);
          s.opponent.cost.active = Math.min(s.opponent.cost.active + addDon, s.opponent.cost.max);
        }

        // 清除选中状态
        s.selectedDonId = null;
        s.selectedFieldIndex = null;
        s.selectedHandIndex = null;
      }),

    addToHand: (side, card) =>
      set((s) => {
        s[side].hand.cards.push(card);
      }),

    removeFromHand: (side, index) =>
      set((s) => {
        s[side].hand.cards.splice(index, 1);
      }),

    setHandSelectable: (selectable) =>
      set((s) => {
        s.my.hand.canChoose = selectable;
      }),

    playToField: (card) =>
      set((s) => {
        const fieldCard: FieldCardState = {
          card,
          isTapped: false,
          powerBuff: 0,
          isSelected: false,
        };
        s.my.field.characterCards.push(fieldCard);
      }),

    tapCard: (side, index) =>
      set((s) => {
        const cards = s[side].field.characterCards;
        if (cards[index]) cards[index].isTapped = true;
      }),

    applyBuff: (side, cardIndex, delta) =>
      set((s) => {
        const cards = s[side].field.characterCards;
        if (cards[cardIndex]) cards[cardIndex].powerBuff += delta;
      }),

    removeFromField: (side, index) =>
      set((s) => {
        // 角色离场时，其附着咚 → 休息
        const chars = s[side].field.characterCards;
        if (index < 0 || index >= chars.length) {
          chars.splice(index, 1);
          return;
        }
        // 先归还附着咚
        let returned = 0;
        for (const don of s[side].cost.donCards) {
          if (don.state === "attached" && don.attachedTo === index) {
            don.state = "rest";
            delete don.attachedTo;
            returned++;
          }
        }
        s[side].cost.rest += returned;
        // 更新大于此 index 的附着咚的 attachedTo（因为 splice 后索引 -1）
        for (const don of s[side].cost.donCards) {
          if (don.state === "attached" && don.attachedTo !== undefined && don.attachedTo > index) {
            don.attachedTo -= 1;
          }
        }
        chars.splice(index, 1);
      }),

    takeDamage: (side, count) =>
      set((s) => {
        s[side].life.splice(0, count);
      }),

    setCost: (side, active, rest, max) =>
      set((s) => {
        s[side].cost.active = active;
        s[side].cost.rest = rest;
        s[side].cost.max = max;
      }),

    setLeader: (side, card) =>
      set((s) => {
        s[side].leader = card;
      }),

    resetGame: () =>
      set((s) => {
        s.mode = "Player";
        s.isStart = false;
        s.currentTurn = false;
        s.turnCount = 0;
        s.haveExtraTurn = false;
        s.phase = "Main";
        s.my = emptyPlayerState();
        s.opponent = emptyPlayerState();
        s.myName = "";
        s.opponentName = "";
        s.isPending = false;
        s.isGameOver = false;
        s.selectedHandIndex = null;
        s.selectedFieldIndex = null;
        s.selectedDonId = null;
        s.mulliganState = "none";
        s.opponentMulliganDone = false;
        s.myDeckCards = [];
      }),
  })),
);
