import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const dataSources = [
  ["服务端权威卡牌数据", "../../卡牌数据/P.json"],
  ["前端公开卡牌数据", "../public/data/P.json"],
];

for (const [source, relativePath] of dataSources) {
  test(`P-099 在${source}中为“紫”`, async () => {
    const cards = JSON.parse(await readFile(new URL(relativePath, import.meta.url), "utf8"));
    const card = cards.find((item) => item.number === "P-099");

    assert.ok(card, `${source}应包含 P-099`);
    assert.equal(card.color, "紫");
  });
}

test("P-099 在前端聚合卡牌数据中为“紫”", async () => {
  const bundle = JSON.parse(
    await readFile(new URL("../public/data/allCards.json", import.meta.url), "utf8"),
  );
  const card = bundle.cards.find((item) => item.number === "P-099");

  assert.ok(card, "前端聚合卡牌数据应包含 P-099");
  assert.equal(card.color, "紫");
});
