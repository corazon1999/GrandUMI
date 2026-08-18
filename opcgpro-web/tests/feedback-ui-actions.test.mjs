import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("桌面与手机竖屏旋转布局中的贴咚操作均需要二次确认", async () => {
  const [actions, request, dialog, page] = await Promise.all([
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/components/game/AttachDonConfirmDialog.tsx"),
    readSource("../src/app/game/page.tsx"),
  ]);

  assert.match(actions, /GameRequest\.attachDon\(attachTargetId, count\)/);
  assert.match(request, /requestAttachDonConfirmation/);
  assert.match(dialog, /确认贴\{pending\.count\}咚？/);
  assert.match(dialog, />\s*取消\s*</);
  assert.match(dialog, />\s*确认\s*</);
  assert.match(dialog, /--layout-safe-bottom/);
  assert.ok((dialog.match(/min-h-12/g)?.length ?? 0) >= 2);
  assert.match(page, /<AttachDonConfirmDialog \/>/);
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
