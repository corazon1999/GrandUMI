import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("放回一张或更多咚时可确认任意正数数量", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /allowVariableReturnCount/);
  assert.match(source, /selected\.length >= 1 && selected\.length <= prompt\.maxChoose/);
  assert.match(source, /确认放回（已选 \$\{selected\.length\} 张）/);
  assert.match(source, /min-h-12 bg-gray-600/);
  assert.match(source, /min-h-12 bg-orange-500/);
});

test("固定咚减费仍要求选满指定数量", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /: selected\.length === prompt\.maxChoose/);
  assert.match(source, /确认放回（\$\{selected\.length\} \/ \$\{prompt\.maxChoose\}）/);
});
