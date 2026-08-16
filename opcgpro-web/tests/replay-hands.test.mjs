import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { revealReplayHands } from "../src/lib/replayHands.ts";

function player(handCardNumbers, handCount = handCardNumbers.length, lifeCount = 2) {
  return {
    handCardNumbers,
    handCount,
    lifeCount,
    lifeNumbers: [],
    lifeFaceUp: Array.from({ length: lifeCount }, () => ({ faceUp: false, number: null })),
  };
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
  assert.deepEqual(revealed[2].my.lifeNumbers, []);
  assert.deepEqual(revealed[2].my.lifeFaceUp, [
    { faceUp: false, number: null },
    { faceUp: false, number: null },
  ]);
});

test("按最近一次变化帧为回放明示双方生命区", () => {
  const timeline = [
    {
      tick: 0,
      myCardNumbers: ["A"],
      opponentCardNumbers: ["X"],
      myLifeCardNumbers: ["L1", "L2"],
      opponentLifeCardNumbers: ["R1", "R2"],
    },
    {
      tick: 3,
      myCardNumbers: ["A", "L1"],
      opponentCardNumbers: ["X"],
      myLifeCardNumbers: ["L2"],
      opponentLifeCardNumbers: ["R1", "R2"],
    },
  ];
  const source = [
    snapshot(1, ["A"], 1),
    snapshot(3, ["A", "L1"], 1, timeline),
  ];

  const revealed = revealReplayHands(source);

  assert.deepEqual(revealed[0].my.lifeNumbers, ["L1", "L2"]);
  assert.deepEqual(revealed[0].opponent.lifeNumbers, ["R1", "R2"]);
  assert.deepEqual(revealed[1].my.lifeNumbers, ["L2"]);
  assert.equal(revealed[1].my.lifeCount, 1);
  assert.deepEqual(revealed[1].my.lifeFaceUp, [{ faceUp: true, number: "L2" }]);
  assert.deepEqual(revealed[1].opponent.lifeFaceUp, [
    { faceUp: true, number: "R1" },
    { faceUp: true, number: "R2" },
  ]);
});

test("旧回放没有时间线时保留脱敏对手手牌", () => {
  const source = [snapshot(1, ["A"], 2)];

  const revealed = revealReplayHands(source);

  assert.deepEqual(revealed[0].opponent.handCardNumbers, []);
  assert.equal(revealed[0].opponent.handCount, 2);
  assert.deepEqual(revealed[0].my.lifeNumbers, []);
  assert.deepEqual(revealed[0].my.lifeFaceUp, [
    { faceUp: false, number: null },
    { faceUp: false, number: null },
  ]);
});

test("回放控件适配手机竖屏安全区和 44px 触控目标", async () => {
  const source = await readFile(
    new URL("../src/components/game/PlaybackControls.tsx", import.meta.url),
    "utf8",
  );

  assert.match(source, /var\(--layout-safe-bottom, 0px\)/);
  assert.match(source, /aria-label=\{collapsed \? "展开回放控件" : "收起回放控件"\}/);
  assert.equal(source.match(/h-12 w-12/g)?.length, 6);
  assert.match(source, /h-12 min-w-12/);
});
