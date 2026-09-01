import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("贴咚直接发送真实动作，玩家活动消息只用于明确在线确认", async () => {
  const request = await readSource("../src/net/GameRequest.ts");

  assert.match(request, /function sendClockControl/);
  assert.match(request, /requestId: createRequestId\(\)/);
  assert.match(request, /function submitAttachDon/);
  assert.match(request, /"AttachDon",\s*\{ targetId, count: safeCount \}/);
  assert.doesNotMatch(request, /kind: "attachDon"|kind: "undoAttachDon"/);
  assert.match(request, /confirmInactivityPresence: \(\) => sendClockControl\("PlayerActivity", \{ kind: "presence" \}\)/);
  const controlBody = request.slice(
    request.indexOf("function sendClockControl"),
    request.indexOf("/** 收到对应权威快照"),
  );
  assert.doesNotMatch(controlBody, /setPending\(true\)/);
});

test("桌面和旋转手机均提供图标化的一次性回合加时入口", async () => {
  const [board, mobile, icon, page, chat] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/game/MobileTurnExtensionButton.tsx"),
    readSource("../src/components/game/TurnExtensionIcon.tsx"),
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/GameChatPanel.tsx"),
  ]);

  assert.match(board, /GameRequest\.requestTurnExtension\(\)/);
  assert.match(board, /min-h-11/);
  assert.match(board, /!rotateQuarterTurn/);
  assert.match(board, /<TurnExtensionIcon \/>/);
  assert.match(board, /title="回合加时 \+2:00"/);
  assert.match(mobile, /useLayoutQuarterTurn/);
  assert.match(mobile, /h-12 w-12 min-h-12 min-w-12/);
  assert.match(mobile, /h-9 w-9/);
  assert.doesNotMatch(mobile, /\bfixed\b|--layout-safe-right|top:|right:/);
  assert.match(mobile, /aria-label="使用本局唯一一次回合加时，增加两分钟"/);
  assert.match(mobile, /title="回合加时 \+2:00"/);
  assert.match(icon, /<circle cx="10\.5" cy="12" r="7"/);
  assert.match(icon, /M18\.5 14\.5v6M15\.5 17\.5h6/);
  assert.doesNotMatch(page, /MobileTurnExtensionButton/);
  assert.match(chat, /!isObserver && <MobileTurnExtensionButton \/>/);
});

test("挂机提醒使用服务端校准倒计时且只挂载给真实玩家", async () => {
  const [overlay, page, store, types] = await Promise.all([
    readSource("../src/components/game/InactivityWarningOverlay.tsx"),
    readSource("../src/app/game/page.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(overlay, /elapsedMillisecondsFromServerSync/);
  assert.match(overlay, /active === "my" && warning && !isGameOver/);
  assert.match(overlay, /我还在，继续对局/);
  assert.match(overlay, /连续 4 分钟没有任何操作将自动判负/);
  assert.match(overlay, /本次无操作计时归零/);
  assert.match(overlay, /min-h-12 min-w-48/);
  assert.match(overlay, /--layout-safe-bottom/);
  assert.match(overlay, /PRESENCE_CONFIRMATION_TIMEOUT_MS = 5_000/);
  assert.match(overlay, /GameRequest\.refreshStateSnapshot\(\)/);
  assert.match(overlay, /setSubmitting\(false\)/);
  assert.match(page, /!isObserver && !isPlayback && <InactivityWarningOverlay \/>/);
  assert.match(store, /inactivityLossRemainingMs: 240_000/);
  assert.match(types, /inactivityActive\?: "my" \| "opponent" \| null/);
});

test("挂机确认超时只重取权威快照，不回滚其他对局动作", async () => {
  const request = await readSource("../src/net/GameRequest.ts");
  const refreshStart = request.indexOf("refreshStateSnapshot:");
  const refreshEnd = request.indexOf("/** 对手断线宽限期内", refreshStart);
  const refreshBody = request.slice(refreshStart, refreshEnd);

  assert.match(refreshBody, /MsgRequestState/);
  assert.doesNotMatch(refreshBody, /rollbackOptimistic|setPending|clearPendingAttachDonState/);
});
