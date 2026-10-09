import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { normalizeHunterCopy } from "../src/lib/rankAffiliation.ts";

test("排位页面、规则说明、结算和更新公告均不再使用旧计分称谓", async () => {
  const paths = [
    "components/home/HunterSeasonPanel.tsx", "components/home/LobbyPanel.tsx",
    "components/home/ProfilePanel.tsx", "components/home/LeaderLeaderboardPanel.tsx", "components/home/MainPanel.tsx",
    "components/game/RankResultPanel.tsx", "components/ui/HunterDefeatCount.tsx",
    "lib/rankAffiliation.ts", "net/HomeProtocol.ts", "data/changelog.ts",
  ];
  for (const path of paths) {
    const source = await readFile(new URL(`../src/${path}`, import.meta.url), "utf8");
    assert.doesNotMatch(source, /\u4eba\u5934/, `${path} 仍包含旧计分称谓。`);
  }
});

test("旧后端的排位与交易所说明在前端转换为击败数量", () => {
  assert.equal(normalizeHunterCopy("S2 猎人\u4eba\u5934不计入交易所额度。"), "S2 击败数量不计入交易所额度。");
  assert.equal(normalizeHunterCopy("\u4eba\u5934结算失败"), "击败数量结算失败");
  assert.equal(normalizeHunterCopy("十人斩 · S1 背包余额原样保留"), "十人斩 · S1 背包余额原样保留");
});
