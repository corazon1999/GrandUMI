import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("检索排序可以按默认顺序直接放回", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /prompt\.kind === "ReorderToDeckBottom"/);
  assert.match(source, /prompt\.extra\?\.allowDefaultOrder === true/);
  assert.match(source, /确定（按默认顺序放回）/);
  assert.match(source, /onClick=\{handleSkip\}/);
  assert.match(source, /min-h-12/);
});

test("对手调度起始手牌时显示重要对战信息", async () => {
  const source = await readSource("../src/components/game/MulliganOverlay.tsx");

  assert.match(source, /opp\?\.mulliganDone === true && opp\.hasReDraw === false/);
  assert.match(source, /showMessage\("重要对战信息：对手已调度起始手牌", "warn"\)/);
  assert.match(source, /role="status"/);
  assert.match(source, /重要对战信息：对手已调度起始手牌/);
});

test("调度提示与按钮适配手机安全区和旋转缩放后的44像素触控区", async () => {
  const source = await readSource("../src/components/game/MulliganOverlay.tsx");

  assert.match(source, /var\(--layout-safe-left,0px\)/);
  assert.match(source, /var\(--layout-safe-right,0px\)/);
  assert.match(source, /var\(--layout-safe-top,0px\)/);
  assert.match(source, /var\(--layout-safe-bottom,0px\)/);
  assert.equal(source.match(/min-h-12 min-w-28/g)?.length, 2);
  assert.match(source, /overflow-x-auto/);
});

test("个人设置可调整卡牌大小与动画速度并持久化", async () => {
  const [store, settings, responsive] = await Promise.all([
    readSource("../src/store/settingsStore.ts"),
    readSource("../src/components/home/SettingsModal.tsx"),
    readSource("../src/hooks/useResponsive.ts"),
  ]);

  assert.match(store, /CardSizePreference = "auto" \| "sm" \| "md" \| "lg"/);
  assert.match(store, /AnimationSpeed = "off" \| "fast" \| "standard"/);
  assert.match(store, /localStorage\.setItem\(KEY/);
  assert.match(settings, /卡牌显示/);
  assert.match(settings, /对局动画/);
  assert.match(responsive, /preferredSize === "auto"/);
});

test("手牌拖动只改变本地显示顺序并保留服务端下标", async () => {
  const [hand, snapshot] = await Promise.all([
    readSource("../src/components/game/HandArea.tsx"),
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
  ]);

  assert.match(snapshot, /handCardIds = asSelf \|\| revealHand/);
  assert.match(hand, /const serverIndex = displayIndices\[i\]/);
  assert.match(hand, /draggable=\{side === "my"/);
  assert.match(hand, /onPointerMove=/);
  assert.match(hand, /GameRequest\.playCounterFromHand\(i\)/);
  assert.match(hand, /onClick=\{\(\) => handleClick\(serverIndex\)\}/);
});

test("电脑与手机都显示全屏按钮且触控区不少于44像素", async () => {
  const [route, fullscreen] = await Promise.all([
    readSource("../src/components/home/LayoutPreviewRoute.tsx"),
    readSource("../src/components/game/MobileFullscreenButton.tsx"),
  ]);

  assert.match(route, /<MobileFullscreenButton \/>/);
  assert.doesNotMatch(route, /isPhonePortrait && <MobileFullscreenButton/);
  assert.match(fullscreen, /var\(--layout-safe-right/);
  assert.match(fullscreen, /h-12 w-12/);
});
