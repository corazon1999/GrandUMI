import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import {
  DEFAULT_LEADER_FILTER_TIER,
  LEADER_FILTER_TIER_STORAGE_KEY,
  getLeaderFilterTierStorage,
  normalizeLeaderFilterTier,
  readLeaderFilterTier,
  writeLeaderFilterTier,
} from "../src/lib/leaderFilterTier.ts";

const readSource = (relativePath) => readFile(new URL(relativePath, import.meta.url), "utf8");

test("场次筛选接受六个固定档位并迁移旧版存储值", () => {
  assert.equal(DEFAULT_LEADER_FILTER_TIER, "500");
  for (const tier of ["100", "300", "500", "1000", "3000", "all"]) {
    assert.equal(normalizeLeaderFilterTier(tier), tier);
  }
  assert.equal(normalizeLeaderFilterTier(" relaxed "), "100");
  assert.equal(normalizeLeaderFilterTier("STANDARD"), "500");
  assert.equal(normalizeLeaderFilterTier("all"), "all");
  assert.equal(normalizeLeaderFilterTier("50"), "500");
  assert.equal(normalizeLeaderFilterTier(null), "500");

  assert.equal(readLeaderFilterTier({ getItem: () => "relaxed" }), "100");
  assert.equal(readLeaderFilterTier({ getItem: () => "invalid" }), "500");
  assert.equal(readLeaderFilterTier({ getItem: () => { throw new Error("blocked"); } }), "500");
  assert.equal(getLeaderFilterTierStorage(), null);

  const writes = [];
  writeLeaderFilterTier({ setItem: (key, value) => writes.push([key, value]) }, "3000");
  assert.deepEqual(writes, [[LEADER_FILTER_TIER_STORAGE_KEY, "3000"]]);
  assert.doesNotThrow(() => writeLeaderFilterTier({ setItem: () => { throw new Error("full"); } }, "100"));

  const memory = new Map();
  const storage = {
    getItem: (key) => memory.get(key) ?? null,
    setItem: (key, value) => memory.set(key, value),
  };
  writeLeaderFilterTier(storage, "1000");
  assert.equal(readLeaderFilterTier(storage), "1000");
});

test("榜单、单 Leader 详情和一图流请求都携带档位并拒收过期请求", async () => {
  const [protocol, types, store, exporter] = await Promise.all([
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
    readSource("../src/store/netStore.ts"),
    readSource("../src/lib/leaderMatchupMatrixExport.ts"),
  ]);

  assert.match(types, /type LeaderFilterTier = "100" \| "300" \| "500" \| "1000" \| "3000" \| "all"/);
  assert.match(types, /interface MsgLeaderLeaderboard[\s\S]*?filterTier\?: LeaderFilterTier;[\s\S]*?requestId\?: string;/);
  assert.match(types, /interface MsgLeaderMatchups[\s\S]*?filterTier\?: LeaderFilterTier;[\s\S]*?requestId\?: string;/);
  assert.match(types, /interface MsgLeaderMatchupMatrix[\s\S]*?filterTier\?: LeaderFilterTier;[\s\S]*?requestId\?: string;/);

  assert.match(protocol, /requestLeaderLeaderboard\(period: LeaderboardPeriod, filterTier: LeaderFilterTier = "500"\)/);
  assert.match(protocol, /requestLeaderMatchups\([\s\S]*?filterTier: LeaderFilterTier = "500"[\s\S]*?filterTier: normalizedFilterTier,[\s\S]*?requestId/);
  assert.match(protocol, /requestLeaderMatchupMatrix\(period: LeaderboardPeriod, filterTier: LeaderFilterTier = "500"\)/);
  assert.match(protocol, /msg\.requestId !== pendingLeaderLeaderboardRequestId/);
  assert.match(protocol, /pendingLeaderMatchupRequestIds\.get\(key\) !== msg\.requestId/);
  assert.match(protocol, /msg\.requestId !== pendingLeaderMatchupMatrixRequestId/);
  assert.match(store, /return `\$\{period\}:\$\{filterTier\}:\$\{leaderNumber\}`/);
  for (const tier of ["100", "300", "500", "1000", "3000"]) {
    assert.match(exporter, new RegExp(`"${tier}": "${tier} 场"`));
  }
  assert.doesNotMatch(exporter, /100 \/ 300 场|500 \/ 3000 场/);
});

test("筛选控件位于周期左侧并为手机竖屏保留 44px 触控区", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");
  const leaderControls = panel.slice(
    panel.indexOf('{rankingTab === "leader" && ('),
    panel.indexOf("</header>"),
  );

  assert.ok(leaderControls.indexOf("FILTER_TIERS.map") < leaderControls.indexOf("PERIODS.map"));
  assert.match(leaderControls, /aria-label="Leader 榜场次筛选"/);
  for (const label of ["100 场", "300 场", "500 场", "1000 场", "3000 场"]) {
    assert.match(panel, new RegExp(`label: "${label}"`));
  }
  assert.doesNotMatch(panel, /100 \/ 300 场|500 \/ 3000 场/);
  assert.match(leaderControls, /flex-col gap-2 @\[900px\]:w-auto @\[900px\]:flex-row/);
  assert.match(leaderControls, /grid-cols-3[\s\S]*?@\[900px\]:grid-cols-6/);
  assert.match(leaderControls, /FILTER_TIERS\.map[\s\S]*?className={`min-h-11/);
  assert.match(leaderControls, /PERIODS\.map[\s\S]*?className={`min-h-11/);
  assert.match(leaderControls, /刷新[\s\S]*?<\/button>/);
  assert.match(panel, /readLeaderFilterTier\(getLeaderFilterTierStorage\(\)\)/);
  assert.match(panel, /writeLeaderFilterTier\(getLeaderFilterTierStorage\(\), filterTier\)/);
  assert.match(panel, /leaderboard\.filterTier === filterTier/);
  assert.match(panel, /leaderMatchupMatrix\.filterTier !== filterTier/);
});

test("服务端固定阈值、旧协议兼容、缓存隔离和三类回包口径一致", async () => {
  const [store, bridge] = await Promise.all([
    readSource("../../服务端WebSocket/Game/Stats/LeaderStatsStore.cs"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
  ]);

  assert.match(store, /var minimumLeaderboardGames = filterTier switch/);
  assert.match(store, /HundredGameFilterTier => 100/);
  assert.match(store, /ThreeHundredGameFilterTier => 300/);
  assert.match(store, /FiveHundredGameFilterTier => 500/);
  assert.match(store, /ThousandGameFilterTier => 1000/);
  assert.match(store, /ThreeThousandGameFilterTier => 3000/);
  assert.match(store, /LegacyRelaxedFilterTier => period switch/);
  assert.match(store, /NormalizeFilterTierForResponse/);
  assert.match(store, /var cacheKey = \$"\{period\}:\{filterTier\}"/);
  assert.match(store, /\.Where\(x => x\.Games >= minimumLeaderboardGames\);[\s\S]*?\.OrderBy/);

  assert.equal((bridge.match(/NormalizeFilterTier\(requestedFilterTier, requestedPeriod\)/g) ?? []).length, 3);
  assert.equal((bridge.match(/NormalizeFilterTierForResponse\(requestedFilterTier, requestedPeriod\)/g) ?? []).length, 3);
  assert.match(bridge, /GetLeaderboard\([\s\S]*?requestedFilterTier: filterTier\)/);
  assert.match(bridge, /GetMatchups\([\s\S]*?requestedFilterTier: filterTier\)/);
  assert.match(bridge, /GetMatchupMatrix\([\s\S]*?requestedFilterTier: filterTier\)/);
  assert.equal((bridge.match(/^\s+filterTier = responseFilterTier,/gm) ?? []).length, 10);
});
