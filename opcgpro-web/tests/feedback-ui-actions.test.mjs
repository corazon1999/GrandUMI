import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("桌面与手机竖屏旋转布局中的贴咚确认及撤回可由个人设置控制", async () => {
  const [actions, request, dialog, page, settings, store] = await Promise.all([
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/components/game/AttachDonConfirmDialog.tsx"),
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/home/SettingsModal.tsx"),
    readSource("../src/store/settingsStore.ts"),
  ]);

  assert.match(actions, /GameRequest\.attachDon\(attachTargetId, count\)/);
  assert.match(request, /requestAttachDonConfirmation/);
  assert.match(request, /useSettingsStore\.getState\(\)\.confirmAttachDon/);
  assert.match(request, /: queueAttachDonUndo\(targetId, safeCount\)/);
  assert.match(request, /undoLastPendingAttachDon/);
  assert.match(actions, /撤回贴咚/);
  assert.match(actions, /rotateQuarterTurn \? "min-h-\[5\.75rem\]" : "min-h-12"/);
  assert.match(dialog, /确认贴\{pending\.count\}咚？/);
  assert.match(dialog, />\s*取消\s*</);
  assert.match(dialog, />\s*确认\s*</);
  assert.match(dialog, /--layout-safe-bottom/);
  assert.ok((dialog.match(/min-h-12/g)?.length ?? 0) >= 2);
  assert.match(page, /<AttachDonConfirmDialog \/>/);
  assert.match(settings, /role="switch"/);
  assert.match(settings, /aria-checked=\{confirmAttachDon\}/);
  assert.match(settings, /setConfirmAttachDon\(!confirmAttachDon\)/);
  assert.match(store, /confirmAttachDon: true/);
  assert.match(store, /localStorage\.setItem\(KEY/);
});

test("屏蔽和举报与投降、设置在右上角组成紧凑水平工具栏", async () => {
  const [page, board, safety, menu, settingsProvider] = await Promise.all([
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
    readSource("../src/components/game/GameMenu.tsx"),
    readSource("../src/components/home/LayoutSettingsProvider.tsx"),
  ]);

  assert.match(page, /<PlayerSafetyActions[^>]+currentOpponent compact toolbar/);
  assert.doesNotMatch(board, /<PlayerSafetyActions/);
  assert.match(safety, /right: "calc\(7\.625rem \+ var\(--layout-safe-right/);
  assert.match(safety, /pointer-events-auto fixed z-\[70\] flex gap-2/);
  assert.match(safety, /h-12 w-12/);
  assert.match(menu, /right: "calc\(4\.125rem \+ var\(--layout-safe-right/);
  assert.match(settingsProvider, /right: "calc\(0\.625rem \+ var\(--layout-safe-right/);
});

test("全服广播横幅可关闭并为安全区预留空间", async () => {
  const banner = await readSource("../src/components/ui/GlobalAnnouncementBanner.tsx");

  assert.match(banner, /dismissAnnouncements/);
  assert.match(banner, /aria-label="关闭广播横幅"/);
  assert.match(banner, /pointer-events-auto/);
  assert.match(banner, /min-h-12 min-w-12/);
  assert.match(banner, /--layout-safe-right/);
});

test("对战日志中的卡号可单击打开卡牌大图", async () => {
  const gameLog = await readSource("../src/components/game/GameLog.tsx");

  assert.match(gameLog, /CARD_NUMBER_PATTERN/);
  assert.match(gameLog, /getCard\(number\)/);
  assert.match(gameLog, /查看卡牌 \$\{number\} 大图/);
  assert.match(gameLog, /<GameOverlayPortal>/);
  assert.match(gameLog, /<CardZoomOverlay/);
});
