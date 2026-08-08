import assert from "node:assert/strict";
import test from "node:test";
import { revealReplayHands } from "../src/lib/replayHands.ts";

function player(handCardNumbers, handCount = handCardNumbers.length) {
  return { handCardNumbers, handCount };
}

function snapshot(tick, myCards, opponentCount, replayHands) {
  return {
    proto: "MsgGameState",
    tick,
    my: player(myCards),
    opponent: player([], opponentCount),
    replayHands,
  };
}

test("按最近一次变化帧为回放补齐双方手牌", () => {
  const timeline = [
    { tick: 0, myCardNumbers: ["A", "B"], opponentCardNumbers: ["X", "Y"] },
    { tick: 3, myCardNumbers: ["B"], opponentCardNumbers: ["X", "Y", "Z"] },
  ];
  const source = [
    snapshot(1, ["A", "B"], 2),
    snapshot(2, ["A", "B"], 2),
    snapshot(3, ["B"], 3, timeline),
  ];

  const revealed = revealReplayHands(source);

  assert.deepEqual(revealed[0].opponent.handCardNumbers, ["X", "Y"]);
  assert.deepEqual(revealed[1].opponent.handCardNumbers, ["X", "Y"]);
  assert.deepEqual(revealed[2].my.handCardNumbers, ["B"]);
  assert.deepEqual(revealed[2].opponent.handCardNumbers, ["X", "Y", "Z"]);
  assert.equal(revealed[2].opponent.handCount, 3);
});

test("旧回放没有时间线时保留脱敏对手手牌", () => {
  const source = [snapshot(1, ["A"], 2)];

  const revealed = revealReplayHands(source);

  assert.deepEqual(revealed[0].opponent.handCardNumbers, []);
  assert.equal(revealed[0].opponent.handCount, 2);
});
