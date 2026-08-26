import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testDir = path.dirname(fileURLToPath(import.meta.url));

function readSet(relativePath) {
  return JSON.parse(fs.readFileSync(path.resolve(testDir, relativePath), "utf8"));
}

test("OP08 反击值与 ST21-015 触发标签在服务端和客户端数据中保持一致", () => {
  const serverOp08 = readSet("../../卡牌数据/OP08.json");
  const clientOp08 = readSet("../public/data/OP08.json");
  const serverSt21 = readSet("../../卡牌数据/ST21.json");
  const clientSt21 = readSet("../public/data/ST21.json");
  const serverSt16 = readSet("../../卡牌数据/ST16.json");
  const clientSt16 = readSet("../public/data/ST16.json");

  for (const cards of [serverOp08, clientOp08]) {
    assert.equal(cards.find((card) => card.number === "OP08-030")?.counter, "1000");
    assert.equal(cards.find((card) => card.number === "OP08-032")?.counter, "2000");
  }
  for (const cards of [serverSt21, clientSt21]) {
    assert.deepEqual(
      cards.find((card) => card.number === "ST21-015")?.effectTags,
      ["OnKO"],
    );
  }
  for (const cards of [serverSt16, clientSt16]) {
    assert.deepEqual(
      cards.find((card) => card.number === "ST16-005")?.effectTags,
      [],
    );
  }
});
