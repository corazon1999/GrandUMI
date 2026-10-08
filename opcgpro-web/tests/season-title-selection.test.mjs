import assert from "node:assert/strict";
import test from "node:test";
import { displayedSeasonTitle } from "../src/lib/seasonTitles.ts";

const titles = ["S1 海贼王", "S1 四皇", "S1 海军元帅", "S1 海军大将", "S1 世界之王", "S1 五老星"];

test("拥有六称号时只展示用户佩戴的一枚", () => {
  for (const title of titles) assert.equal(displayedSeasonTitle({ seasonTitles: titles, equippedSeasonTitle: title }), title);
});

test("取消佩戴不会回退为展示全部或自动佩戴第一个称号", () => {
  assert.equal(displayedSeasonTitle({ seasonTitles: titles, equippedSeasonTitle: null }), null);
  assert.equal(displayedSeasonTitle(null), null);
});

test("旧回放缺少佩戴字段时保持单枚历史称号兼容", () => {
  assert.equal(displayedSeasonTitle({ seasonTitles: titles }), titles[0]);
  assert.equal(displayedSeasonTitle({ seasonTitles: [] }), null);
});
