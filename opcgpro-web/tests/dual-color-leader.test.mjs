import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { parseCardColors, sharesCardColor } from "../src/lib/colorMap.ts";

const read = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("颜色解析兼容历史分隔符且只接受六种规则色", () => {
  assert.deepEqual(parseCardColors("紫/黑"), ["紫", "黑"]);
  assert.deepEqual(parseCardColors("紫／黑"), ["紫", "黑"]);
  assert.deepEqual(parseCardColors("紫色, 黑色"), ["紫", "黑"]);
  assert.deepEqual(parseCardColors("紫/紫/黑/伪造色"), ["紫", "黑"]);
  assert.equal(sharesCardColor("紫／黑", "黑"), true);
  assert.equal(sharesCardColor("紫／黑", "红"), false);
});

test("全部当前双色领航均能使用自己的两个颜色，紫黑领航不再退化为单紫", async () => {
  const bundle = JSON.parse(await read("../public/data/allCards.json"));
  const leaders = bundle.cards.filter((card) => card.type === "领航" || card.type === "Leader");
  const dualLeaders = leaders.filter((card) => parseCardColors(card.color).length === 2);
  const purpleBlackLeaders = dualLeaders.filter((card) => {
    const colors = parseCardColors(card.color);
    return colors.includes("紫") && colors.includes("黑");
  });

  assert.ok(dualLeaders.length > 0, "聚合数据中应存在双色领航");
  assert.ok(purpleBlackLeaders.length >= 3, "应覆盖当前全部紫黑领航");
  for (const leader of dualLeaders) {
    const colors = parseCardColors(leader.color);
    for (const color of colors) {
      assert.equal(sharesCardColor(leader.color, color), true, `${leader.number} 应允许 ${color} 色卡`);
    }
  }
  for (const leader of purpleBlackLeaders) {
    assert.equal(sharesCardColor(leader.color, "紫"), true, `${leader.number} 应允许紫色卡`);
    assert.equal(sharesCardColor(leader.color, "黑"), true, `${leader.number} 应允许黑色卡`);
    assert.equal(sharesCardColor(leader.color, "红"), false, `${leader.number} 不应允许无交集的红色卡`);
  }
});

test("组卡筛选、领航切换和服务端校验共用规范化颜色语义", async () => {
  const [search, store, panel, server] = await Promise.all([
    read("../src/lib/cardSearch.ts"),
    read("../src/store/deckStore.ts"),
    read("../src/components/deck-editor/SearchPanel.tsx"),
    read("../../服务端WebSocket/Cards/CardInfo.cs"),
  ]);

  assert.match(search, /sharesCardColor\(leaderColor, cardColor\)/);
  assert.match(store, /parseCardColors\(card\.color\)/);
  assert.match(panel, /parseCardColors\(leader\.color\)/);
  assert.match(server, /ParseColors\(Color\)/);
  assert.doesNotMatch(`${search}\n${store}\n${panel}`, /color\.split\("\/"\)/);
});
