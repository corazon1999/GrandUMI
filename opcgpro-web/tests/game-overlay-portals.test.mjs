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
