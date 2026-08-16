import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  isDisconnectFinishReason,
  shouldHideDisconnectLoss,
} from "../src/data/matchHistoryPolicy.ts";

test("只隐藏因断线输掉的个人战绩", () => {
  assert.equal(shouldHideDisconnectLoss({
    winnerIsMe: false,
    gameOverReason: "玩家甲 断线超时",
  }), true);
  assert.equal(shouldHideDisconnectLoss({
    winnerIsMe: false,
    gameOverReason: "DisconnectTimeout",
  }), true);

  assert.equal(shouldHideDisconnectLoss({
    winnerIsMe: true,
    gameOverReason: "对手断线，对手确认结束对局",
  }), false);
  assert.equal(shouldHideDisconnectLoss({
    winnerIsMe: false,
    isDraw: true,
    gameOverReason: "双方断线后同意平局",
  }), false);
  assert.equal(shouldHideDisconnectLoss({
    winnerIsMe: false,
    gameOverReason: "生命耗尽",
  }), false);
});

test("中英文断线终局原因均可识别", () => {
  assert.equal(isDisconnectFinishReason("玩家乙断线超时"), true);
  assert.equal(isDisconnectFinishReason("DISCONNECT_TIMEOUT"), true);
  assert.equal(isDisconnectFinishReason("操作时间耗尽"), false);
  assert.equal(isDisconnectFinishReason(""), false);
});

test("新旧断线败局共用同一隐藏规则", async () => {
  const recorder = await readFile(new URL("../src/data/matchRecorder.ts", import.meta.url), "utf8");
  const database = await readFile(new URL("../src/data/matchHistoryDB.ts", import.meta.url), "utf8");

  assert.match(recorder, /if \(shouldHideDisconnectLoss\(meta\)\)/);
  assert.match(recorder, /enqueueWrite\(s, \(\) => deleteMatch\(s\.id\)\)/);
  assert.match(database, /const hidden = all\.filter\(shouldHideDisconnectLoss\)/);
  assert.match(database, /hidden\.map\(\(match\) => deleteMatch\(match\.id\)\)/);
});
