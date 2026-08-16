import assert from "node:assert/strict";
import test from "node:test";
import { canPayActivatedMainCost } from "../src/lib/activatedMainCost.ts";

test("OP17-044 无法转为休息状态时不能发动主要效果", () => {
  assert.equal(canPayActivatedMainCost("OP17-044", false, true), false);
  assert.equal(canPayActivatedMainCost("OP17-044", false, false), true);
});

test("OP17-044 已经休息时不能再支付休息成本", () => {
  assert.equal(canPayActivatedMainCost("OP17-044", true, false), false);
});

test("不以自身休息为成本的卡牌不受该判定影响", () => {
  assert.equal(canPayActivatedMainCost("OP17-040", true, true), true);
  assert.equal(canPayActivatedMainCost(null, true, true), true);
});
