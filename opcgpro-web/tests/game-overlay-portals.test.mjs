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

test("旋转对局中的设置弹窗使用横屏紧凑双栏布局", async () => {
  const [settings, provider, previewRoute] = await Promise.all([
    readSource("../src/components/home/SettingsModal.tsx"),
    readSource("../src/components/home/LayoutSettingsProvider.tsx"),
    readSource("../src/components/home/LayoutPreviewRoute.tsx"),
  ]);

  assert.match(settings, /useLayoutQuarterTurn/);
  assert.match(settings, /max-w-\[52rem\]/);
  assert.match(settings, /data-settings-layout=/);
  assert.match(settings, /landscape-compact/);
  assert.match(settings, /grid grid-cols-2 items-start gap-3/);
  assert.match(settings, /col-span-2/);
  assert.match(settings, /order-1/);
  assert.match(settings, /order-2/);
  assert.match(settings, /order-3/);
  assert.match(settings, /@\[640px\]:grid-cols-4/);
  assert.doesNotMatch(settings, /sm:grid-cols-4/);
  assert.match(provider, /LayoutQuarterTurnProvider rotateQuarterTurn=\{gameOverlayQuarterTurn\}/);
  assert.match(provider, /<ContainerResponsiveProvider>\{settingsUi\}<\/ContainerResponsiveProvider>/);
  assert.match(previewRoute, /setGameOverlayHost\(host, layout\.rotateQuarterTurn\)/);
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
  assert.ok((menu.match(/min-h-12/g)?.length ?? 0) >= 2);
  assert.match(menu, /h-12 w-12/);
});

test("开局卡效提示优先于骰子遮罩且保留安全区", async () => {
  const [overlay, feedback] = await Promise.all([
    readSource("../src/components/game/FirstPlayerOverlay.tsx"),
    readSource("../src/components/game/FeedbackOverlay.tsx"),
  ]);

  assert.match(overlay, /const pendingPrompt = useGameStore/);
  assert.match(overlay, /if \(!my \|\| firstPlayerChosen \|\| pendingPrompt\) return null/);
  assert.match(overlay, /openingStage === "ResolvingOpeningEffects"[\s\S]*?openingStage === "WaitingOpeningPrompt"/);
  assert.match(overlay, /var\(--layout-safe-top/);
  assert.match(overlay, /var\(--layout-safe-right/);
  assert.match(overlay, /var\(--layout-safe-bottom/);
  assert.match(overlay, /var\(--layout-safe-left/);
  assert.ok((overlay.match(/min-h-11/g)?.length ?? 0) >= 2);
  assert.match(feedback, /right: "calc\(6\.75rem \+ var\(--layout-safe-right/);
});
