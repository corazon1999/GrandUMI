import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  extractMatchOpeningMeta,
  getMatchOpeningLabels,
} from "../src/data/matchHistoryOpening.ts";

test("终局快照会映射我方骰子胜负与先后手", () => {
  assert.deepEqual(extractMatchOpeningMeta({
    firstPlayerChosen: true,
    diceWinnerIsMe: true,
    isFirstPlayer: false,
  }), {
    diceWinnerIsMe: true,
    isFirstPlayer: false,
  });

  assert.deepEqual(extractMatchOpeningMeta({
    firstPlayerChosen: true,
    diceWinnerIsMe: false,
    isFirstPlayer: true,
  }), {
    diceWinnerIsMe: false,
    isFirstPlayer: true,
  });
});

test("历史概要生成清晰的开局结果文案", () => {
  assert.deepEqual(getMatchOpeningLabels({ diceWinnerIsMe: true, isFirstPlayer: true }), [
    "骰子：胜",
    "先手",
  ]);
  assert.deepEqual(getMatchOpeningLabels({ diceWinnerIsMe: false, isFirstPlayer: false }), [
    "骰子：负",
    "后手",
  ]);
});

test("旧数据和未完成开局流程不会被误报为骰子负或后手", () => {
  assert.deepEqual(extractMatchOpeningMeta({
    firstPlayerChosen: false,
    diceWinnerIsMe: false,
    isFirstPlayer: false,
  }), {});
  assert.deepEqual(extractMatchOpeningMeta({}), {});
  assert.deepEqual(getMatchOpeningLabels({}), []);
});

test("历史卡片以可换行徽标展示概要并由录制器写入元信息", async () => {
  const history = await readFile(new URL("../src/components/home/HistoryPanel.tsx", import.meta.url), "utf8");
  const recorder = await readFile(new URL("../src/data/matchRecorder.ts", import.meta.url), "utf8");

  assert.match(recorder, /\.\.\.extractMatchOpeningMeta\(last\)/);
  assert.match(history, /getMatchOpeningLabels\(m\)/);
  assert.match(history, /aria-label="开局结果"/);
  assert.match(history, /flex-wrap/);
  assert.match(history, /max-w-full/);
});
