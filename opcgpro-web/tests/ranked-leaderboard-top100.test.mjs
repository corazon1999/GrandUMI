import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("排位榜返回前100并在底部固定显示当前玩家", async () => {
  const [panel, types, rankedStore] = await Promise.all([
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);

  assert.match(rankedStore, /entry\.Item\.Rank <= 100[\s\S]*entry\.Item\.FactionRank <= 100[\s\S]*entry\.AccountKey, player\.Profile\.AccountKey/);
  assert.match(rankedStore, /ReadRankedLeaderboardEntries/);
  assert.match(rankedStore, /isCurrentPlayer = value\.IsCurrentPlayer/);
  assert.match(types, /isCurrentPlayer\?: boolean/);
  assert.match(panel, /item\.rank <= 100/);
  assert.match(panel, /items\.find\(\(item\) => item\.isCurrentPlayer\)/);
  assert.match(panel, /我的排名/);
  assert.match(panel, /border-t-2 border-violet-400\/50/);
  assert.match(panel, /<RankedTable items=\{\[currentPlayer\]\} pinned/);
  assert.match(panel, /<RankedMobileRow item=\{currentPlayer\} pinned/);
});
