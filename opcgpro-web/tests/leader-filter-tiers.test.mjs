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

test("场次筛选档位只接受三种值且异常存储回退标准档", () => {
  assert.equal(DEFAULT_LEADER_FILTER_TIER, "standard");
  assert.equal(normalizeLeaderFilterTier("relaxed"), "relaxed");
  assert.equal(normalizeLeaderFilterTier("standard"), "standard");
  assert.equal(normalizeLeaderFilterTier("all"), "all");
  assert.equal(normalizeLeaderFilterTier("500"), "standard");
  assert.equal(normalizeLeaderFilterTier(null), "standard");

  assert.equal(readLeaderFilterTier({ getItem: () => "relaxed" }), "relaxed");
  assert.equal(readLeaderFilterTier({ getItem: () => "invalid" }), "standard");
  assert.equal(readLeaderFilterTier({ getItem: () => { throw new Error("blocked"); } }), "standard");
  assert.equal(getLeaderFilterTierStorage(), null);

  const writes = [];
  writeLeaderFilterTier({ setItem: (key, value) => writes.push([key, value]) }, "all");
  assert.deepEqual(writes, [[LEADER_FILTER_TIER_STORAGE_KEY, "all"]]);
  assert.doesNotThrow(() => writeLeaderFilterTier({ setItem: () => { throw new Error("full"); } }, "relaxed"));

  const memory = new Map();
  const storage = {
    getItem: (key) => memory.get(key) ?? null,
    setItem: (key, value) => memory.set(key, value),
  };
  writeLeaderFilterTier(storage, "relaxed");
  assert.equal(readLeaderFilterTier(storage), "relaxed");
});

test("榜单、单 Leader 详情和一图流请求都携带档位并拒收过期请求", async () => {
  const [protocol, types, store] = await Promise.all([
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
    readSource("../src/store/netStore.ts"),
  ]);

  assert.match(types, /type LeaderFilterTier = "relaxed" \| "standard" \| "all"/);
  assert.match(types, /interface MsgLeaderLeaderboard[\s\S]*?filterTier\?: LeaderFilterTier;[\s\S]*?requestId\?: string;/);
  assert.match(types, /interface MsgLeaderMatchups[\s\S]*?filterTier\?: LeaderFilterTier;[\s\S]*?requestId\?: string;/);
  assert.match(types, /interface MsgLeaderMatchupMatrix[\s\S]*?filterTier\?: LeaderFilterTier;[\s\S]*?requestId\?: string;/);

  assert.match(protocol, /requestLeaderLeaderboard\(period: LeaderboardPeriod, filterTier: LeaderFilterTier = "standard"\)/);
  assert.match(protocol, /requestLeaderMatchups\([\s\S]*?filterTier: LeaderFilterTier = "standard"[\s\S]*?filterTier: normalizedFilterTier,[\s\S]*?requestId/);
  assert.match(protocol, /requestLeaderMatchupMatrix\(period: LeaderboardPeriod, filterTier: LeaderFilterTier = "standard"\)/);
  assert.match(protocol, /msg\.requestId !== pendingLeaderLeaderboardRequestId/);
  assert.match(protocol, /pendingLeaderMatchupRequestIds\.get\(key\) !== msg\.requestId/);
  assert.match(protocol, /msg\.requestId !== pendingLeaderMatchupMatrixRequestId/);
  assert.match(store, /return `\$\{period\}:\$\{filterTier\}:\$\{leaderNumber\}`/);
});

test("筛选控件位于周期左侧并为手机竖屏保留 44px 触控区", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");
  const leaderControls = panel.slice(
    panel.indexOf('{rankingTab === "leader" && ('),
    panel.indexOf("</header>"),
  );

  assert.ok(leaderControls.indexOf("FILTER_TIERS.map") < leaderControls.indexOf("PERIODS.map"));
  assert.match(leaderControls, /aria-label="Leader 榜场次筛选"/);
  assert.match(panel, /100 \/ 300 场/);
  assert.match(panel, /500 \/ 3000 场/);
  assert.match(leaderControls, /flex-col gap-2 @\[900px\]:w-auto @\[900px\]:flex-row/);
  assert.match(leaderControls, /FILTER_TIERS\.map[\s\S]*?className={`min-h-11/);
  assert.match(leaderControls, /PERIODS\.map[\s\S]*?className={`min-h-11/);
  assert.match(leaderControls, /刷新[\s\S]*?<\/button>/);
  assert.match(panel, /readLeaderFilterTier\(getLeaderFilterTierStorage\(\)\)/);
  assert.match(panel, /writeLeaderFilterTier\(getLeaderFilterTierStorage\(\), filterTier\)/);
  assert.match(panel, /leaderboard\.filterTier === filterTier/);
  assert.match(panel, /leaderMatchupMatrix\.filterTier !== filterTier/);
});

test("服务端默认兼容、阈值映射、缓存隔离和三类回包共享规范化档位", async () => {
  const [store, bridge] = await Promise.all([
    readSource("../../服务端WebSocket/Game/Stats/LeaderStatsStore.cs"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
  ]);

  assert.match(store, /_ => StandardFilterTier/);
  assert.match(store, /\("7d", RelaxedFilterTier\) => RelaxedSevenDayLeaderboardGames/);
  assert.match(store, /\("30d", RelaxedFilterTier\) => RelaxedThirtyDayLeaderboardGames/);
  assert.match(store, /\("7d", StandardFilterTier\) => MinimumSevenDayLeaderboardGames/);
  assert.match(store, /\("30d", StandardFilterTier\) => MinimumThirtyDayLeaderboardGames/);
  assert.match(store, /var cacheKey = \$"\{period\}:\{filterTier\}"/);
  assert.match(store, /\.Where\(x => x\.Games >= minimumLeaderboardGames\);[\s\S]*?\.OrderBy/);

  assert.equal((bridge.match(/NormalizeFilterTier\(Str\(msg, "filterTier"\)\)/g) ?? []).length, 3);
  assert.match(bridge, /GetLeaderboard\([\s\S]*?requestedFilterTier: filterTier\)/);
  assert.match(bridge, /GetMatchups\([\s\S]*?requestedFilterTier: filterTier\)/);
  assert.match(bridge, /GetMatchupMatrix\([\s\S]*?requestedFilterTier: filterTier\)/);
});
