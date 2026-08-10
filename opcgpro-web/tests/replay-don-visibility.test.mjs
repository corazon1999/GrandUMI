import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("replay board keeps both DON zones visible", async () => {
  const source = await readSource("../src/components/game/GameBoard.tsx");

  assert.doesNotMatch(source, /const canShowDon = !isPlayback/);
  assert.match(source, /const donZone = \([\s\S]*?<DonDeckPile side=\{side\} \/>[\s\S]*?<DonArea side=\{side\} \/>[\s\S]*?\);/);
});
