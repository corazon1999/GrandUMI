import assert from "node:assert/strict";
import test from "node:test";
import { sortCardDisplayIndices } from "../src/lib/cardDisplaySort.ts";

test("手牌和墓地按卡面费用升序，同费用按卡号归拢且保留重复实例", () => {
  const numbers = ["B", "C", "A", "B", "D", "C"];
  const original = [...numbers];
  const costs = { A: 2, B: 1, C: 0, D: 1 };
  const indices = sortCardDisplayIndices(numbers, (number) => costs[number]);
  assert.deepEqual(indices, [1, 5, 0, 3, 4, 2]);
  assert.deepEqual(indices.map((index) => numbers[index]), ["C", "C", "B", "B", "D", "A"]);
  assert.deepEqual(numbers, original, "不得改变用于出牌与反击的服务端数组");
});

test("相同角色名的不同卡号分别归拢，不按人物名混排", () => {
  const numbers = ["OP02-001", "OP01-001", "OP02-001", "OP01-001"];
  assert.deepEqual(sortCardDisplayIndices(numbers, () => 3), [1, 3, 0, 2]);
});

test("资料缺失或费用无效的卡稳定排在末尾，零费正常排在前面", () => {
  const costs = { A: undefined, B: NaN, C: 0, D: Infinity };
  assert.deepEqual(sortCardDisplayIndices(["B", "A", "C", "A", "D"], (number) => costs[number]),
    [2, 1, 3, 0, 4]);
});

test("空区域、单张卡和重复点击整理均保持稳定", () => {
  assert.deepEqual(sortCardDisplayIndices([], () => 0), []);
  assert.deepEqual(sortCardDisplayIndices(["A"], () => 0), [0]);
  const numbers = ["B", "A", "B"];
  const first = sortCardDisplayIndices(numbers, () => 1).map((index) => numbers[index]);
  assert.deepEqual(sortCardDisplayIndices(first, () => 1), [0, 1, 2]);
});
