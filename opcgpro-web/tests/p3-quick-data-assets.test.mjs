import assert from "node:assert/strict";
import { existsSync, readFileSync, statSync } from "node:fs";
import test from "node:test";

const loadJson = (path) => JSON.parse(readFileSync(new URL(path, import.meta.url), "utf8"));

test("G629 与 G824 的卡图已存在并纳入 manifest", () => {
  const manifest = loadJson("../public/data/imageManifest.json");
  for (const [number, relativePath] of [
    ["P-121", "/cards/p/P-121.png"],
    ["ST33-003", "/cards/st33/ST33-003.png"],
  ]) {
    assert.ok(manifest[number]?.includes(relativePath), `${number} 未纳入图片 manifest`);
    const file = new URL(`../public${relativePath}`, import.meta.url);
    assert.equal(existsSync(file), true, `${number} 图片文件不存在`);
    assert.ok(statSync(file).size > 0, `${number} 图片文件为空`);
  }
});

test("G916 ST07-010 在源数据、公开数据与聚合数据中均为 7 费", () => {
  const source = loadJson("../../卡牌数据/ST07.json");
  const publicSet = loadJson("../public/data/ST07.json");
  const allCards = loadJson("../public/data/allCards.json").cards;
  for (const cards of [source, publicSet, allCards]) {
    const card = cards.find(({ number }) => number === "ST07-010");
    assert.ok(card, "缺少 ST07-010");
    assert.equal(card.cost, "7");
  }
});
