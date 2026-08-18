import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("移动端页头使用项目研究标题", async () => {
  const source = await readFile(
    new URL("../src/components/home/MainPanel.tsx", import.meta.url),
    "utf8",
  );

  assert.match(source, /TCG博弈智能AI研究项目/);
  assert.doesNotMatch(source, /<p className="text-xs text-gray-500">海贼王卡牌对战<\/p>/);
});
