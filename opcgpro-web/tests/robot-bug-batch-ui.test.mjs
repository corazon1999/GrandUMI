import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");

test("攻击按钮使用服务端权威攻击机会字段", () => {
  const source = fs.readFileSync(path.join(root, "src/components/game/GameActions.tsx"), "utf8");
  assert.match(source, /my\.leaderCanAttack/);
  assert.match(source, /fieldCards\.find\(\(c\) => c\.id === selectedFieldId\)\?\.canAttack/);
  assert.doesNotMatch(source, /attackerTapped === false/);
});

test("OP07-097 领袖生命值数据为 2", () => {
  const cards = JSON.parse(fs.readFileSync(path.join(root, "public/data/OP07.json"), "utf8"));
  const vegapunk = cards.find((card) => card.number === "OP07-097");
  assert.equal(vegapunk?.cost, "2");
});
