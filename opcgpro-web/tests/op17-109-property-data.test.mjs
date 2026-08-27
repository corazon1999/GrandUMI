import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

for (const relativePath of ["../../卡牌数据/OP17.json", "../public/data/OP17.json"]) {
  test(`OP17-109 在 ${relativePath} 中使用“知”属性`, async () => {
    const cards = JSON.parse(
      await readFile(new URL(relativePath, import.meta.url), "utf8"),
    );
    const card = cards.find((item) => item.number === "OP17-109");

    assert.ok(card, "应能找到 OP17-109");
    assert.equal(card.property, "知");
  });
}
