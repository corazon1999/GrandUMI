import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("Leader 榜最强使用者不公开近 30 日个人战绩", async () => {
  const panel = await readFile(
    new URL("../src/components/home/LeaderLeaderboardPanel.tsx", import.meta.url),
    "utf8",
  );
  const types = await readFile(new URL("../src/types/net.ts", import.meta.url), "utf8");
  const server = await readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8");
  const championOwner = panel.match(/function ChampionOwner\([\s\S]*?\n}\n\nfunction formatGeneratedAt/)?.[0];
  const championType = types.match(/export interface LeaderChampionInfo \{[\s\S]*?\n}/)?.[0];
  const championResponse = server.match(/champion = champion is null \? null : new[\s\S]*?\n\s*},/)?.[0];

  assert.ok(championOwner, "应能找到最强使用者展示组件");
  assert.ok(championType, "应能找到最强使用者响应类型");
  assert.ok(championResponse, "应能找到最强使用者服务端响应");
  assert.match(championOwner, /item\.champion\.displayName/);
  assert.doesNotMatch(championOwner, /近\s*30\s*日/);
  assert.doesNotMatch(championOwner, /item\.champion\.(wins|games|winRate)/);
  assert.doesNotMatch(championType, /\b(games|wins|winRate)\b/);
  assert.doesNotMatch(championResponse, /\b(games|wins|winRate)\s*=/);
});
