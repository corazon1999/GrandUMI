import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("个人详情可管理持有称号并匿名查询称号战绩", async () => {
  const [profile, protocol, types, store] = await Promise.all([
    readSource("../src/components/home/ProfilePanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
    readSource("../src/store/netStore.ts"),
  ]);

  assert.match(profile, /data-testid="champion-title-management"/);
  assert.match(profile, /championLeaderNumbers\.map/);
  assert.match(profile, /equippedChampionLeaderNumber/);
  assert.match(profile, /HomeRequest\.updateChampionTitle/);
  assert.match(profile, /HomeRequest\.requestLeaderChampion/);
  assert.match(profile, /message\.proto === "MsgPlayerData" && message\.result === false/);
  assert.match(profile, /近 30 日总场次/);
  assert.match(profile, /原始胜率/);
  assert.match(profile, /min-h-14/);
  assert.match(profile, /min-h-11/);
  assert.match(profile, /@\[760px\]:grid-cols-2/);

  assert.match(protocol, /case "MsgLeaderChampionQuery"/);
  assert.match(protocol, /proto: "MsgUpdateChampionTitle"/);
  assert.match(protocol, /proto: "MsgLeaderChampionQuery"/);
  assert.match(protocol, /if \(!sent\) store\.setLeaderChampionQuery/);
  assert.match(types, /equippedChampionLeaderNumber\?: string \| null/);
  assert.match(types, /interface MsgLeaderChampionQuery[\s\S]*games: number;[\s\S]*winRate: number;/);
  assert.match(store, /championLeaderNumbers: string\[\]/);
  assert.match(store, /leaderChampionQuery: MsgLeaderChampionQuery \| null/);
});

test("服务端装备资格与对局展示均实时复核，旧玩家自动回退", async () => {
  const [bridge, championStore, snapshot, playerStore] = await Promise.all([
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../../服务端WebSocket/Game/Stats/LeaderChampionStore.cs"),
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
    readSource("../../服务端WebSocket/Persistence/PlayerDataStore.cs"),
  ]);

  assert.match(bridge, /IsChampion\(s\.Account, leaderNumber\)/);
  assert.match(bridge, /champion = champion is null \? null : new[\s\S]*games = champion\.Games[\s\S]*winRate =/);
  assert.match(championStore, /ResolveEquippedChampionLeaderNumber/);
  assert.match(championStore, /owned\.Contains\(preferred/);
  assert.match(championStore, /: owned\[0\]/);
  assert.match(snapshot, /ResolveEquippedChampionLeaderNumber\(p\.AccountName\)/);
  assert.doesNotMatch(snapshot, /IsChampion\(p\.AccountName, board\.LeaderNumber\)/);
  assert.match(playerStore, /EnsureColumn\(connection, "players", "equipped_champion_leader_number", "TEXT NULL"\)/);
});
