import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { calculateLayoutScale } from "../src/lib/gameLayout.ts";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("启动效果按钮只采用服务端权威可发动字段", async () => {
  const actions = await read("src/components/game/GameActions.tsx");
  const store = await read("src/store/gameStore.ts");

  assert.match(actions, /selectedCanActivateEffect/);
  assert.match(actions, /my\.leaderCanActivateEffect/);
  assert.match(actions, /selectedStage\.canActivateEffect/);
  assert.match(actions, /selectedFieldCard\?\.canActivateEffect/);
  assert.doesNotMatch(actions, /canPayActivatedMainCost/);
  assert.match(store, /canActivateEffect: card\.canActivateEffect \?\? false/);
});

test("两档手机竖屏旋转后启动按钮保持至少44像素触控高度", async () => {
  const actions = await read("src/components/game/GameActions.tsx");
  assert.match(actions, /const btn =\s*\n?\s*"min-h-12/);

  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const scale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });
    assert.ok(48 * scale >= 44, `${hostWidth}×${hostHeight} 的按钮触控高度不足44px`);
  }
});
