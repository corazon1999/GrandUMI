import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

async function readJson(relativePath) {
  return JSON.parse(
    await readFile(new URL(relativePath, import.meta.url), "utf8"),
  );
}

function findCard(cards, number) {
  const card = cards.find((item) => item.number === number);
  assert.ok(card, `未找到卡牌 ${number}`);
  return card;
}

test("EB01-020 在规则源和前端数据中均归类为事件卡", async () => {
  const [ruleCards, publicCards] = await Promise.all([
    readJson("../../卡牌数据/EB01.json"),
    readJson("../public/data/EB01.json"),
  ]);

  assert.equal(findCard(ruleCards, "EB01-020").type, "事件");
  assert.equal(findCard(publicCards, "EB01-020").type, "事件");
});
