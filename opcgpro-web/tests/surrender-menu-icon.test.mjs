import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("局内低频工具使用更多图标且投降保留在面板内", async () => {
  const source = await readSource("../src/components/game/GameMenu.tsx");

  assert.match(source, /function MoreIcon/);
  assert.match(source, /fill="currentColor"/);
  assert.match(source, /data-game-more-trigger/);
  assert.match(source, /aria-label="打开更多对局工具"/);
  assert.match(source, /title="更多"/);
  assert.match(source, /GameRequest\.surrender\(\)/);
  assert.match(source, />\s*投降\s*</);
  assert.doesNotMatch(source, />\s*≡\s*</);
});
