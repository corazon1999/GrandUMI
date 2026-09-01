import assert from "node:assert/strict";
import test from "node:test";
import { getLeaderBreathingEffect } from "../src/lib/leaderBreathingEffects.ts";

test("OP17-039 洛克斯的普通画面不再启用动态呼吸效果", () => {
  assert.equal(
    getLeaderBreathingEffect("OP17-039", "/cards/op17/OP17-039.png"),
    null,
  );
  assert.equal(
    getLeaderBreathingEffect(
      "OP17-039",
      "/cards/op17/OP17-039.png?v=01dd10803ffa",
    ),
    null,
  );
});

test("OP17-039 洛克斯的异画仍保持常规静态渲染", () => {
  assert.equal(
    getLeaderBreathingEffect("OP17-039", "/cards/op17/OP17-039_01.png"),
    null,
  );
});
