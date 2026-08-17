import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("手机竖屏旋转布局中的贴咚操作需要二次确认", async () => {
  const actions = await readSource("../src/components/game/GameActions.tsx");

  assert.match(actions, /useLayoutQuarterTurn/);
  assert.match(actions, /pendingAttachDonCount/);
  assert.match(actions, /if \(rotateQuarterTurn\)/);
  assert.match(actions, /确认赋予 \{pendingAttachDonCount\} 张咚/);
  assert.match(actions, /确认贴咚/);
  assert.ok((actions.match(/min-h-12/g)?.length ?? 0) >= 6);
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
