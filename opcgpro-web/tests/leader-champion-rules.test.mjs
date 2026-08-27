import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("最强称号使用动态门槛和稳定的贝叶斯参数", async () => {
  const server = await readSource("../../服务端WebSocket/Game/Stats/LeaderChampionStore.cs");

  assert.match(server, /DefaultMinimumChampionGames = 50;/);
  assert.match(server, /LowVolumeMinimumChampionGames = 30;/);
  assert.match(server, /LowVolumeLeaderMatchThreshold = 1_000;/);
  assert.match(server, /MinimumActiveDays = 5;/);
  assert.match(server, /MinimumDistinctOpponents = 15;/);
  assert.match(server, /BayesianPriorEquivalentGames = 20;/);
  assert.match(server, /LeaderPriorBaselineEquivalentGames = 50;/);
  assert.match(server, /ChampionWindowDays = 30/);
  assert.match(server, /COUNT\(DISTINCT date\(ended_at_utc, \$businessDayOffset\)\)/);
  assert.match(server, /COUNT\(DISTINCT opponent_key\)/);
  assert.match(server, /BayesianAdjustedWinRate/);
  assert.doesNotMatch(server, /WilsonLowerBound|WilsonZ/);
});

test("Leader 榜提供可触控的最强称号规则说明", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");
  const modal = await readSource("../src/components/ui/Modal.tsx");

  assert.match(panel, /aria-label="查看最强称号规则"/);
  assert.match(panel, /h-11 w-11/);
  assert.match(panel, /<ChampionRulesModal open=\{championRulesOpen\}/);
  assert.match(panel, /至少 <strong[^>]*>50 场<\/strong>/);
  assert.match(panel, /少于\s*<strong[^>]*> 1000 局<\/strong>/);
  assert.match(panel, /门槛降为 <strong[^>]*>30 场<\/strong>/);
  assert.match(panel, /同 Leader 镜像局只算一局；个人场次按玩家实际出场计/);
  assert.match(panel, /5 个 UTC\+8 自然日/);
  assert.match(panel, /15 名不同对手/);
  assert.match(panel, /服务器匿名标识去重/);
  assert.match(panel, /贝叶斯修正胜率/);
  assert.match(panel, /50 场、50% 胜率/);
  assert.match(panel, /20 场等效样本/);
  assert.match(panel, /排位、休闲匹配和普通公开匹配/);
  assert.match(panel, /好友房、房间码及机器人对局不计入/);
  assert.match(panel, /少于 8 回合、掉线结束、没有明确胜负或同账号之间的对局不计入/);
  assert.match(modal, /data-modal-scroll-region/);
  assert.match(modal, /var\(--layout-safe-bottom,env\(safe-area-inset-bottom\)\)/);
  assert.match(modal, /calc\(100dvh - 2rem - env\(safe-area-inset-top\) - env\(safe-area-inset-bottom\)\)/);
});
