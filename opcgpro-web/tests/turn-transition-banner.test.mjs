import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("回合提示直接监听服务端权威回合状态", async () => {
  const [layer, animationHook] = await Promise.all([
    readSource("../src/components/game/AnimationLayer.tsx"),
    readSource("../src/hooks/useGameAnimation.ts"),
  ]);

  assert.match(layer, /const currentTurn = useGameStore/);
  assert.match(layer, /const turnCount = useGameStore/);
  assert.match(layer, /lastShownTurnRef/);
  assert.match(layer, /lastShownTurnRef\.current === turnCount/);
  assert.match(layer, /side: currentTurn \? "my" : "opponent"/);
  assert.doesNotMatch(animationHook, /case "MulliganComplete"/);
  assert.doesNotMatch(animationHook, /case "EndTurn"/);
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
