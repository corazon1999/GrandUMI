import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("局内投降菜单入口使用白旗图标", async () => {
  const source = await readSource("../src/components/game/GameMenu.tsx");

  assert.match(source, /function SurrenderFlagIcon/);
  assert.match(source, /fill="currentColor"/);
  assert.match(source, /text-white/);
  assert.match(source, /aria-label="打开投降菜单"/);
  assert.match(source, /title="投降"/);
  assert.doesNotMatch(source, />\s*≡\s*</);
});
