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
  assert.match(actions, /rotateQuarterTurn \? "min-h-\[5\.75rem\]" : "min-h-12"/);

  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const outerScale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });
    // 对局牌桌还会把 1280×720 固定舞台缩放到 844×390 旋转画布内。
    const stageScale = Math.min(844 / 1280, 390 / 720);
    assert.ok(92 * outerScale * stageScale >= 44, `${hostWidth}×${hostHeight} 的按钮触控短边不足44px`);
  }
});
