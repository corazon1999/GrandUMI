import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("首回合和后续回合切换都会产生玩家视角的动画事件", async () => {
  const source = await readSource("../src/hooks/useGameAnimation.ts");

  assert.match(source, /case "MulliganComplete":[\s\S]*?case "EndTurn":[\s\S]*?case "TurnStart":/);
  assert.match(source, /side: currentTurn \? "my" : "opponent"/);
  assert.match(source, /turnCount,/);
});

test("回合提示位于中央、足够醒目且不会拦截操作", async () => {
  const source = await readSource("../src/components/game/AnimationLayer.tsx");

  assert.match(source, /pointer-events-none fixed inset-0 z-30 flex items-center justify-center/);
  assert.match(source, /text-5xl font-black/);
  assert.match(source, /bg-black\/25/);
  assert.match(source, /}, 2200\);/);
  assert.match(source, /mode === "Observer"/);
  assert.match(source, /current\?\.id === turnBanner\.id \? null : current/);
});
