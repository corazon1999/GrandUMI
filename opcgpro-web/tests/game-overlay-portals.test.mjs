import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("对局全局浮层统一挂到旋转宿主", async () => {
  const [portal, cardItem, lifeArea, trashPile, messageBox] = await Promise.all([
    readSource("../src/components/ui/GameOverlayPortal.tsx"),
    readSource("../src/components/ui/CardItem.tsx"),
    readSource("../src/components/game/LifeArea.tsx"),
    readSource("../src/components/game/TrashPile.tsx"),
    readSource("../src/components/ui/MessageBox.tsx"),
  ]);

  assert.match(portal, /if \(!mounted\) return null/);
  assert.match(portal, /gameOverlayHost \?\? document\.body/);
  assert.match(cardItem, /<GameOverlayPortal>[\s\S]*?<CardZoomOverlay/);
  assert.match(lifeArea, /<GameOverlayPortal>/);
  assert.doesNotMatch(lifeArea, /document\.body/);
  assert.match(trashPile, /<GameOverlayPortal>/);
  assert.doesNotMatch(trashPile, /document\.body/);
  assert.match(messageBox, /<GameOverlayPortal>/);
});

test("旋转宿主中的交互浮层显式接收指针事件", async () => {
  const [zoom, lifeArea, trashPile] = await Promise.all([
    readSource("../src/components/ui/CardZoomOverlay.tsx"),
    readSource("../src/components/game/LifeArea.tsx"),
    readSource("../src/components/game/TrashPile.tsx"),
  ]);

  for (const source of [zoom, lifeArea, trashPile]) {
    assert.match(source, /pointer-events-auto fixed/);
  }
});

test("共享弹窗在旋转对局画布中使用容器尺寸并保留可滚动内容区", async () => {
  const modal = await readSource("../src/components/ui/Modal.tsx");

  assert.match(modal, /const useMobileSheet = mobileSheet && !rotateQuarterTurn/);
  assert.match(modal, /w-\[calc\(100cqw-2rem\)\]/);
  assert.match(modal, /calc\(100cqh - 2rem - var\(--layout-safe-top/);
  assert.match(modal, /style=\{\{ maxHeight: dialogMaxHeight \}\}/);
  assert.match(modal, /data-modal-scroll-region/);
  assert.match(modal, /touch-pan-y overflow-y-auto overscroll-contain/);
  assert.match(modal, /tabIndex=\{0\}/);
  assert.match(modal, /h-12 w-12/);
  assert.match(modal, /var\(--layout-safe-left/);
  assert.match(modal, /var\(--layout-safe-right/);
});

test("对局设置的同类浮层不再使用未旋转的视口宽高", async () => {
  const [zoom, cardInfo, life, trash, menu] = await Promise.all([
    readSource("../src/components/ui/CardZoomOverlay.tsx"),
    readSource("../src/components/game/CardInfoPanel.tsx"),
    readSource("../src/components/game/LifeArea.tsx"),
    readSource("../src/components/game/TrashPile.tsx"),
    readSource("../src/components/game/GameMenu.tsx"),
  ]);

  assert.match(zoom, /100cqh/);
  assert.match(zoom, /100cqw/);
  assert.match(zoom, /@\[640px\]:flex-row/);
  assert.match(cardInfo, /78cqh/);
  assert.match(cardInfo, /62cqw/);
  assert.match(life, /75cqh/);
  assert.match(trash, /75cqh/);
  assert.match(menu, /maxWidthClass="max-w-sm"/);
  assert.match(menu, /var\(--layout-safe-right/);
  assert.equal(menu.match(/min-h-12/g)?.length, 2);
  assert.match(menu, /h-12 w-12/);
});
