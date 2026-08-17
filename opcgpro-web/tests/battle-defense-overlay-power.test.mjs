import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("阻挡候选卡显示服务端下发的场上实时力量", async () => {
  const source = await readSource("../src/components/game/BattleDefenseOverlay.tsx");

  assert.match(source, /const card = getGameCard\(b\.number, my\.spriteMap\) \?\? null/);
  assert.match(source, /const basePower = card\?\.power \?\? 0/);
  assert.match(
    source,
    /const powerBuff = b\.powerCurrent - basePower - b\.attachedDon \* 1000/,
  );
  assert.match(source, /attachedDonCount=\{b\.attachedDon\}/);
  assert.match(source, /powerBuff=\{powerBuff\}/);
  assert.match(source, /hideCounter/);
});
