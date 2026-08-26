import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("桌面与手机竖屏旋转布局中的贴咚确认及立即提交可由个人设置控制", async () => {
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
  assert.match(request, /: submitAttachDon\(targetId, safeCount\)/);
  assert.match(request, /"AttachDon",\s*\{ targetId, count: safeCount \}/);
  assert.match(request, /optimisticAttachDon\(targetId, safeCount\)/);
  assert.doesNotMatch(request, /pendingAttachDonUndo|undoLastPendingAttachDon|queueAttachDonUndo/);
  assert.doesNotMatch(actions, /撤回贴咚|执行下一项操作后将无法撤回/);
  assert.match(dialog, /确认贴\{pending\.count\}咚？/);
  assert.match(dialog, /确认后会立即提交并生效，无法撤回/);
  assert.match(dialog, />\s*取消\s*</);
  assert.match(dialog, />\s*确认\s*</);
  assert.match(dialog, /--layout-safe-bottom/);
  assert.ok((dialog.match(/min-h-12/g)?.length ?? 0) >= 2);
  assert.match(page, /<AttachDonConfirmDialog \/>/);
  assert.match(settings, /role="switch"/);
  assert.match(settings, /aria-checked=\{confirmAttachDon\}/);
  assert.match(settings, /setConfirmAttachDon\(!confirmAttachDon\)/);
  assert.match(settings, /关闭时点选数量即提交/);
  assert.match(settings, /提交后会立即生效，无法撤回/);
  assert.match(store, /confirmAttachDon: false/);
  assert.match(store, /localStorage\.setItem\(KEY/);
});

test("低频对局工具统一收进左下更多菜单并移除右侧独立入口", async () => {
  const [page, board, chat, safety, menu, settingsProvider, feedback] = await Promise.all([
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/game/GameChatPanel.tsx"),
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
    readSource("../src/components/game/GameMenu.tsx"),
    readSource("../src/components/home/LayoutSettingsProvider.tsx"),
    readSource("../src/components/game/FeedbackOverlay.tsx"),
  ]);

  assert.doesNotMatch(page, /<PlayerSafetyActions/);
  assert.doesNotMatch(page, /<GameMenu/);
  assert.match(page, /showTrigger=\{false\}/);
  assert.doesNotMatch(board, /<PlayerSafetyActions/);
  assert.doesNotMatch(board, /F · 反馈 Bug 和建议/);
  assert.match(chat, /data-game-control-dock/);
  assert.match(chat, /type ActiveControl = "chat" \| "friends" \| "spectators" \| "more" \| null/);
  assert.match(chat, /<GameMenu/);
  assert.match(menu, /data-game-more-trigger/);
  assert.match(menu, /title="更多"/);
  assert.match(menu, /设置/);
  assert.match(menu, /反馈 Bug \/ 建议/);
  assert.match(menu, /请求平局/);
  assert.match(menu, />\s*投降\s*</);
  assert.match(menu, /屏蔽对手/);
  assert.match(menu, /举报对手/);
  assert.match(menu, /<PlayerSafetyActions[\s\S]*?currentOpponent[\s\S]*?renderActions=/);
  assert.match(safety, /renderActions\?: \(controller: PlayerSafetyActionController\)/);
  assert.match(settingsProvider, /pathname !== "\/game" && settingsTriggerSuppressionCount === 0/);
  assert.match(settingsProvider, /setSettingsTriggerSuppressionCount\(\(count\) => count \+ 1\)/);
  assert.match(settingsProvider, /setSettingsTriggerSuppressionCount\(\(count\) => Math\.max\(0, count - 1\)\)/);
  assert.match(menu, /useEffect\(\(\) => suppressSettingsTrigger\(\), \[suppressSettingsTrigger\]\)/);
  assert.match(feedback, /<GameOverlayPortal>/);
  assert.doesNotMatch(feedback, /document\.body/);
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
