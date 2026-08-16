import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/lib/cardSearch.ts", import.meta.url),
  "utf8",
);

const EXPECTED_STANDARD_LEGAL_CARDS = [
  "OP01-016",
  "OP01-039",
  "OP01-055",
  "OP01-120",
  "OP02-005",
  "OP02-013",
  "OP02-068",
  "OP03-008",
  "OP03-025",
  "OP03-044",
  "OP03-048",
  "OP03-072",
  "OP03-097",
  "OP04-016",
  "OP04-077",
  "OP04-083",
  "OP04-096",
  "ST01-011",
  "ST02-007",
  "ST06-008",
];

function extractWhitelist() {
  const block = source.match(
    /STANDARD_LEGAL_SUBSCRIPT_ONE_CARDS = new Set<string>\(\[([\s\S]*?)\]\);/,
  )?.[1];
  assert.ok(block, "应定义已过标角标 1 卡白名单");
  return [...block.matchAll(/"([A-Z0-9-]+)"/g)].map((match) => match[1]);
}

async function loadCard(cardNumber, root) {
  const setCode = cardNumber.split("-")[0];
  const cards = JSON.parse(
    await readFile(new URL(`../../${root}/${setCode}.json`, import.meta.url), "utf8"),
  );
  return cards.find((card) => card.number === cardNumber);
}

test("已确认的20张角标1卡使用精确白名单", () => {
  const actual = extractWhitelist();
  assert.deepEqual(actual, EXPECTED_STANDARD_LEGAL_CARDS);
  assert.equal(new Set(actual).size, 20);
});

test("过标白名单只绕过默认隐藏，不修改真实角标数据", async () => {
  for (const cardNumber of EXPECTED_STANDARD_LEGAL_CARDS) {
    const [sourceCard, webCard] = await Promise.all([
      loadCard(cardNumber, "卡牌数据"),
      loadCard(cardNumber, "opcgpro-web/public/data"),
    ]);
    assert.ok(sourceCard, `卡牌源数据应包含 ${cardNumber}`);
    assert.ok(webCard, `前端数据应包含 ${cardNumber}`);
    assert.equal(sourceCard.subscript, 1, `${cardNumber} 源数据应保留角标 1`);
    assert.equal(webCard.subscript, 1, `${cardNumber} 前端数据应保留角标 1`);
  }

  assert.match(
    source,
    /card\.subscript === 1 && !STANDARD_LEGAL_SUBSCRIPT_ONE_CARDS\.has\(card\.number\)/,
  );
  assert.match(
    source,
    /if \(!filterShowSub1 && isSubscriptOneHiddenByDefault\(card\)\) return false;/,
  );
});
