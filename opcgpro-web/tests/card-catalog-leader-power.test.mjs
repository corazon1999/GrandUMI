import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/CardCatalogPanel.tsx", import.meta.url),
  "utf8",
);

test("card catalog shows a Leader's life value in the upper-left card badge", () => {
  assert.match(source, /function leaderLife\(card: CardData\) \{[\s\S]*?card\.cost > 0 \? card\.cost : 5/);
  assert.match(source, /card\.type === "Leader" && \(/);
  assert.match(
    source,
    /absolute left-1 top-1 rounded bg-black\/75 px-1\.5 py-0\.5 text-\[9px\] font-bold text-white/,
  );
  assert.match(source, /\{leaderLife\(card\)\}/);
});
