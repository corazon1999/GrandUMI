import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";

test("OP08-067 的费用数据使用可直接解析的半角数字", () => {
  for (const file of ["../卡牌数据/OP08.json", "public/data/OP08.json"]) {
    const cards = JSON.parse(fs.readFileSync(file, "utf8"));
    const card = cards.find((item) => item.number === "OP08-067");

    assert.ok(card, `${file} 应包含 OP08-067`);
    assert.equal(card.cost, "3");
    assert.equal(Number(card.cost), 3);
  }
});

test("卡牌数值解析会先归一化全角数字", () => {
  const source = fs.readFileSync("src/data/CardLoader.ts", "utf8");

  assert.match(source, /function parseNumericField[\s\S]*?normalize\("NFKC"\)/);
  assert.match(source, /cost: parseNumericField\(raw\.cost\)/);
  assert.equal(Number("３".normalize("NFKC")), 3);
});
