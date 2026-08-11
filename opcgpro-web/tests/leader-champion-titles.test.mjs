import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("最强称号在动画、操作计时、在线名单和好友名单中统一展示", async () => {
  const [badge, clash, board, players, friends, netTypes] = await Promise.all([
    readSource("../src/components/ui/LeaderChampionBadge.tsx"),
    readSource("../src/components/game/LeaderClashOverlay.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/home/PlayerListPanel.tsx"),
    readSource("../src/components/home/FriendsPanel.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(badge, /split\(\/\[·・\.\]\//);
  assert.match(badge, /最强\$\{leaderChampionName/);
  assert.match(clash, /championLeaderNumber/);
  assert.match(board, /myChampionLeaderNumber/);
  assert.match(board, /opponentChampionLeaderNumber/);
  assert.match(players, /LeaderChampionBadgeList/);
  assert.match(players, /flex min-w-0 items-center gap-1/);
  assert.match(players, /flex min-w-0 items-center gap-1[\s\S]*\{p\.name\}[\s\S]*LeaderChampionBadgeList/);
  assert.match(players, /leaderNumbers=\{p\.championLeaderNumbers\}\s+maxVisible=\{1\}/);
  assert.doesNotMatch(players, /leaderNumbers=\{p\.championLeaderNumbers\} className="mt-1"/);
  assert.match(friends, /LeaderChampionBadgeList/);
  assert.match(await readSource("../src/components/home/LeaderLeaderboardPanel.tsx"), /ChampionOwner/);
  assert.match(await readSource("../src/components/home/LeaderLeaderboardPanel.tsx"), /最强使用者/);
  assert.match(netTypes, /championLeaderNumbers\?: string\[\]/);
  assert.match(netTypes, /championLeaderNumber\?: string \| null/);
  assert.match(netTypes, /champion\?: LeaderChampionInfo \| null/);
});
