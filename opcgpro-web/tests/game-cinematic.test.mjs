import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  DEFEAT_IMPACT_MS,
  VICTORY_ENTRANCE_MS,
  VICTORY_HOLD_MS,
  advanceGameCinematic,
  createInitialGameCinematicState,
  finishGameCinematicFallback,
  ingestGameCinematic,
  needsGameCinematicFallback,
  shouldIgnoreLiveGameStateSnapshot,
  shouldPreserveTerminalGameState,
} from "../src/lib/gameCinematic.mjs";

function phrase(eventId, displaySide, text = eventId) {
  return {
    eventId,
    sourceSeat: displaySide === "self" ? 0 : 1,
    displaySide,
    displayName: displaySide,
    id: eventId,
    name: eventId,
    text,
    rarity: "legendary",
    styleToken: "emperor",
  };
}

function snapshot({ openingEvents = [], terminal = null } = {}) {
  return { matchId: "match-a", openingEvents, terminal };
}

test("开场事件按稳定 ID 去重，重复快照不会续期或重复展示", () => {
  const first = ingestGameCinematic(createInitialGameCinematicState(), snapshot({
    openingEvents: [phrase("match-a:opening:0", "self"), phrase("match-a:opening:1", "opponent")],
  }), 1_000);
  assert.equal(first.openingBubbles.length, 2);
  assert.deepEqual(first.seenOpeningEventIds, ["match-a:opening:0", "match-a:opening:1"]);

  const repeated = ingestGameCinematic(first, snapshot({
    openingEvents: [phrase("match-a:opening:0", "self"), phrase("match-a:opening:1", "opponent")],
  }), 3_000);
  assert.equal(repeated.openingBubbles.length, 2);
  assert.equal(repeated.openingBubbles[0].expiresAt, first.openingBubbles[0].expiresAt);
});

test("终局严格按战败冲击、胜利气泡进入、完整可见两秒、结算顺序推进", () => {
  const terminal = {
    eventId: "match-a:terminal",
    winnerSeat: 0,
    loserSeat: 1,
    winnerSide: "self",
    loserSide: "opponent",
    reason: "测试胜利",
    victory: phrase("match-a:victory:0", "self", "唯有胜者才是正义！"),
  };
  const started = ingestGameCinematic(createInitialGameCinematicState(), snapshot({ terminal }), 1_000);
  assert.equal(started.phase, "impact");
  assert.equal(started.settlementReady, false);

  const beforeImpactEnds = advanceGameCinematic(started, 1_000 + DEFEAT_IMPACT_MS - 1, false);
  assert.equal(beforeImpactEnds.phase, "impact");
  const victory = advanceGameCinematic(started, 1_000 + DEFEAT_IMPACT_MS, false);
  assert.equal(victory.phase, "victory");
  assert.equal(victory.settlementReady, false);

  const repeated = ingestGameCinematic(victory, snapshot({ terminal }), 9_000);
  assert.equal(repeated.phaseStartedAt, victory.phaseStartedAt);
  const finalDeadline = 1_000 + DEFEAT_IMPACT_MS + VICTORY_ENTRANCE_MS + VICTORY_HOLD_MS;
  assert.equal(advanceGameCinematic(repeated, finalDeadline - 1, false).settlementReady, false);
  const completed = advanceGameCinematic(repeated, finalDeadline, false);
  assert.equal(completed.phase, "complete");
  assert.equal(completed.settlementReady, true);
});

test("迟到定时器和重挂载按绝对时间追赶，不会重新开始终局动画", () => {
  const started = ingestGameCinematic(createInitialGameCinematicState(), snapshot({
    terminal: {
      eventId: "match-a:terminal",
      winnerSeat: 0,
      loserSeat: 1,
      winnerSide: "self",
      loserSide: "opponent",
      reason: "测试",
      victory: phrase("match-a:victory:0", "self"),
    },
  }), 500);
  const remountedLater = advanceGameCinematic(started, 20_000, false);
  assert.equal(remountedLater.phase, "complete");
  assert.equal(remountedLater.settlementReady, true);
});

test("终局后的同 Tick 缺字段快照不会触发旧协议兜底并截断演出", () => {
  const started = ingestGameCinematic(createInitialGameCinematicState(), snapshot({
    terminal: {
      eventId: "match-a:terminal",
      winnerSeat: 0,
      loserSeat: 1,
      winnerSide: "self",
      loserSide: "opponent",
      reason: "测试",
      victory: phrase("match-a:victory:0", "self"),
    },
  }), 1_000);
  const lateWithoutTerminal = ingestGameCinematic(started, snapshot(), 1_001);

  assert.equal(lateWithoutTerminal.phase, "impact");
  assert.equal(lateWithoutTerminal.settlementReady, false);
  assert.equal(needsGameCinematicFallback(lateWithoutTerminal, true), false);
  assert.equal(needsGameCinematicFallback(createInitialGameCinematicState(), true), true);
});

test("在线乱序快照不能回滚同局 Tick 或终局吸收态，回放与新局仍可倒退", () => {
  const base = {
    mode: "Player",
    previousTick: 12,
    previousMatchId: "match-a",
    incomingMatchId: "match-a",
  };
  assert.equal(shouldIgnoreLiveGameStateSnapshot({ ...base, incomingTick: 11 }), true);
  assert.equal(shouldIgnoreLiveGameStateSnapshot({ ...base, mode: "Playback", incomingTick: 11 }), false);
  assert.equal(shouldIgnoreLiveGameStateSnapshot({ ...base, incomingMatchId: "match-b", incomingTick: 1 }), false);

  assert.equal(shouldPreserveTerminalGameState({
    ...base,
    previousIsGameOver: true,
    incomingIsGameOver: false,
    incomingTick: 12,
  }), true);
  assert.equal(shouldPreserveTerminalGameState({
    ...base,
    previousIsGameOver: true,
    incomingIsGameOver: false,
    incomingMatchId: "match-b",
    incomingTick: 1,
  }), false);
  assert.equal(shouldPreserveTerminalGameState({
    ...base,
    mode: "Playback",
    previousIsGameOver: true,
    incomingIsGameOver: false,
    incomingTick: 11,
  }), false);
});

test("无胜利语录、平局与旧 MsgDuelOver 均有确定性非阻塞兜底", () => {
  const noVictory = ingestGameCinematic(createInitialGameCinematicState(), snapshot({
    terminal: {
      eventId: "match-a:terminal",
      winnerSeat: 0,
      loserSeat: 1,
      winnerSide: "self",
      loserSide: "opponent",
      reason: "无语录",
      victory: null,
    },
  }), 1_000);
  assert.equal(advanceGameCinematic(noVictory, 1_000 + DEFEAT_IMPACT_MS, false).settlementReady, true);

  const draw = ingestGameCinematic(createInitialGameCinematicState(), snapshot({
    terminal: {
      eventId: "match-a:terminal",
      winnerSeat: null,
      loserSeat: null,
      winnerSide: null,
      loserSide: null,
      reason: "平局",
      victory: null,
    },
  }), 2_000);
  assert.equal(draw.phase, "complete");
  assert.equal(draw.settlementReady, true);

  const fallback = finishGameCinematicFallback(createInitialGameCinematicState(), 3_000);
  assert.equal(fallback.phase, "complete");
  assert.equal(fallback.settlementReady, true);
});

test("减少动态效果仍保留胜利气泡完整可见后的两秒", () => {
  const started = ingestGameCinematic(createInitialGameCinematicState(), snapshot({
    terminal: {
      eventId: "match-a:terminal",
      winnerSeat: 0,
      loserSeat: 1,
      winnerSide: "self",
      loserSide: "opponent",
      reason: "测试",
      victory: phrase("match-a:victory:0", "self"),
    },
  }), 1_000);
  assert.equal(advanceGameCinematic(started, 2_999, true).settlementReady, false);
  assert.equal(advanceGameCinematic(started, 3_000, true).settlementReady, true);
});

test("页面门控和动画层保留清理定时器及 reduced-motion 契约", () => {
  const controller = readFileSync(new URL("../src/components/game/GameCinematicLayer.tsx", import.meta.url), "utf8");
  const overlay = readFileSync(new URL("../src/components/game/GameOverOverlay.tsx", import.meta.url), "utf8");
  const store = readFileSync(new URL("../src/store/gameStore.ts", import.meta.url), "utf8");
  const replay = readFileSync(new URL("../src/app/replay/[id]/page.tsx", import.meta.url), "utf8");
  const fixturePage = readFileSync(new URL("../src/app/layout-verification/chat-decoration/page.tsx", import.meta.url), "utf8");
  const fixture = readFileSync(new URL("../src/components/game/ChatDecorationLayoutVerification.tsx", import.meta.url), "utf8");
  const css = readFileSync(new URL("../src/app/globals.css", import.meta.url), "utf8");
  assert.match(controller, /window\.clearTimeout\(timer\)/);
  assert.match(controller, /prefers-reduced-motion: reduce/);
  assert.match(overlay, /!isGameOver \|\| !settlementReady/);
  assert.match(css, /leader-defeat-fracture/);
  assert.match(css, /leader-defeat-shockwave/);
  assert.match(css, /game-cinematic-board-shake/);
  assert.match(store, /s\.mode === "Playback" && incomingTick < previousTick/);
  assert.match(replay, /<GameCinematicController \/>/);
  assert.match(fixturePage, /process\.env\.GRANDUMI_LAYOUT_VERIFICATION !== "1"/);
  assert.match(fixturePage, /notFound\(\)/);
  assert.match(fixture, /connState: "disconnected"/);
  assert.doesNotMatch(fixture, /NetManager/);
});
