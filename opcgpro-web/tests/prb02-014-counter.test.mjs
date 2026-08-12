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

test("PRB02-014 萨波的规则源数据与前端数据均为反击 +2000", async () => {
  const [ruleCards, publicCards] = await Promise.all([
    readJson("../../卡牌数据/PRB02.json"),
    readJson("../public/data/PRB02.json"),
  ]);

  assert.equal(findCard(ruleCards, "PRB02-014").counter, "2000");
  assert.equal(findCard(publicCards, "PRB02-014").counter, "2000");
});
