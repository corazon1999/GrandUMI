import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("最强称号最低候选场次为 20 场", async () => {
  const server = await readSource("../../服务端WebSocket/Game/Stats/LeaderChampionStore.cs");

  assert.match(server, /public const int MinimumChampionGames = 20;/);
  assert.match(server, /ChampionWindowDays = 30/);
  assert.match(server, /WilsonZ = 1\.645/);
});

test("Leader 榜提供可触控的最强称号规则说明", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");

  assert.match(panel, /aria-label="查看最强称号规则"/);
  assert.match(panel, /h-11 w-11/);
  assert.match(panel, /<ChampionRulesModal open=\{championRulesOpen\}/);
  assert.match(panel, /至少 <strong[^>]*>20 场<\/strong>/);
  assert.match(panel, /90% Wilson 胜率下限/);
  assert.match(panel, /排位、休闲匹配和普通公开匹配/);
  assert.match(panel, /好友房、房间码及机器人对局不计入/);
  assert.match(panel, /少于 8 回合、掉线结束、没有明确胜负或同账号之间的对局不计入/);
});
