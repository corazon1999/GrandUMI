/**
 * mockBoard.ts — 仅开发用（DEV）。
 *
 * 把一份「满桌」权威快照灌进 gameStore，让
 * `/game?__test_bypass=1&__mock_board=1` 在没有 C# 后端对局时也能渲染
 * 卡牌 / 手牌 / DON / 生命 / 墓地，便于按 redesign/battle.jsx 做牌桌样式 1:1 比对。
 *
 * 生产构建不会引入：main.ts 仅在 import.meta.env.DEV 且带 ?__mock_board=1 时动态加载。
 * 用到的卡号都真实存在（ST01 炎/红、ST04 暗/紫、ST06 地/黑），useGameInit 会按需懒加载卡集。
 */
import type { MsgGameState, PlayerSnapshot, FieldCardSnapshot } from "@/types/net";
import { useGameStore } from "@/store/gameStore";
import { loadCardSet } from "./CardLoader";

function fc(
  id: string,
  number: string,
  power: number,
  cost: number,
  over: Partial<FieldCardSnapshot> = {},
): FieldCardSnapshot {
  return {
    id,
    number,
    isTapped: false,
    powerCurrent: power,
    cost,
    attachedDon: 0,
    gainedKeywords: [],
    cannotActivateNextReset: false,
    cannotBeRested: false,
    activatedUsedThisTurn: false,
    turnPlayed: 1,
    canAttack: false,
    ...over,
  };
}

function player(over: Partial<PlayerSnapshot>): PlayerSnapshot {
  return {
    name: "玩家",
    handCardNumbers: [],
    handCardCosts: [],
    handCount: 0,
    fieldCards: [],
    stageNumber: null,
    stageId: null,
    stageTapped: false,
    trashNumbers: [],
    deckCount: 40,
    lifeCount: 5,
    lifeNumbers: [],
    lifeFaceUp: [],
    leaderId: "leader",
    leaderNumber: "ST01-001",
    leaderTapped: false,
    leaderPower: 5000,
    leaderAttachedDon: 0,
    leaderCanAttack: false,
    leaderActivatedUsedThisTurn: false,
    stageActivatedUsedThisTurn: false,
    costActive: 0,
    costRest: 0,
    costAttached: 0,
    donDeckCount: 10,
    hasReDraw: false,
    mulliganDone: true,
    ...over,
  };
}

export function buildMockGameState(): MsgGameState {
  return {
    proto: "MsgGameState",
    tick: 1,
    phase: "Main",
    currentTurn: true,
    turnCount: 1,
    firstPlayer: 0,
    mulliganBothDone: true,
    isGameOver: false,
    winnerIsMe: false,
    gameOverReason: "",
    viewerKind: "player",
    lastAction: "",
    actionPayload: "",
    logLine: "进入主要阶段",
    pendingPrompt: null,
    battle: null,
    my: player({
      name: "测试玩家",
      leaderId: "my-leader",
      leaderNumber: "ST01-001",
      leaderPower: 5000,
      fieldCards: [
        fc("my-f1", "ST01-002", 2000, 2, { canAttack: true }),
        fc("my-f2", "ST01-003", 3000, 1),
        fc("my-f3", "ST04-002", 5000, 4, { isTapped: true, attachedDon: 1 }),
      ],
      handCardNumbers: ["ST01-002", "ST04-002", "ST06-002", "ST06-001", "ST01-001"],
      handCardCosts: [2, 4, 1, 5, 5],
      handCount: 5,
      lifeCount: 4,
      costActive: 5,
      costRest: 1,
      donDeckCount: 4,
      deckCount: 40,
      trashNumbers: ["ST01-003"],
    }),
    opponent: player({
      name: "电脑对手",
      leaderId: "opp-leader",
      leaderNumber: "ST06-001",
      leaderPower: 5000,
      fieldCards: [
        fc("opp-f1", "ST06-002", 2000, 1),
        fc("opp-f2", "ST04-002", 5000, 4),
      ],
      handCardNumbers: [],
      handCount: 5,
      lifeCount: 5,
      costActive: 0,
      costRest: 0,
      donDeckCount: 8,
      deckCount: 40,
      trashNumbers: ["ST06-002", "ST04-002"],
    }),
  };
}

export async function loadMockBoard(): Promise<void> {
  // 必须先把卡集灌进 cardCache，再注入快照——getCard 读的是非响应式 Map，
  // 若注入时缓存为空，组件首渲染会拿到 null（卡背），且之后缓存填充不会触发重渲染。
  await Promise.all(["ST01", "ST04", "ST06"].map((name) => loadCardSet(name).catch(() => {})));
  const s = useGameStore.getState();
  s.setMode("Player");
  s.setIsStart(true);
  s.syncFromServer(buildMockGameState());
}
