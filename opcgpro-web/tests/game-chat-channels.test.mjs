import assert from "node:assert/strict";
import test from "node:test";
import { gameChatChannels, appendGameChatMessage } from "../src/lib/gameChatChannels.ts";

test("赛事双方只显示各自频道，管理视图按服务端授权同时显示两边", () => {
  assert.deepEqual(gameChatChannels(true, false), ["players"]);
  assert.deepEqual(gameChatChannels(true, true), ["spectators"]);
  assert.deepEqual(gameChatChannels(true, true, ["players", "spectators"]), ["players", "spectators"]);
  assert.deepEqual(gameChatChannels(true, true, ["spectators"]), ["spectators"]);
  assert.deepEqual(gameChatChannels(true, false, ["room", "伪造频道"]), ["players"]);
  assert.deepEqual(gameChatChannels(false, true, ["players", "spectators"]), ["room"]);
});

test("两边各保留50条记录，观战刷屏不挤掉选手频道消息", () => {
  let messages = [{ id: "选手消息", channel: "players" }];
  for (let i = 0; i < 100; i++) messages = appendGameChatMessage(messages, { id: i, channel: "spectators" });
  assert.equal(messages.length, 51);
  assert.equal(messages[0].id, "选手消息");
  assert.deepEqual(messages.slice(1).map(m => m.id), Array.from({ length: 50 }, (_, i) => i + 50));
  for (let i = 0; i < 75; i++) messages = appendGameChatMessage(messages, { id: i, channel: "players" });
  assert.equal(messages.length, 100);
  assert.deepEqual(messages.filter(m => m.channel === "players").map(m => m.id), Array.from({ length: 50 }, (_, i) => i + 25));
});
