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

test("桌面和手机均提供一次性回合加时入口", async () => {
  const [board, mobile, page] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/game/MobileTurnExtensionButton.tsx"),
    readSource("../src/app/game/page.tsx"),
  ]);

  assert.match(board, /GameRequest\.requestTurnExtension\(\)/);
  assert.match(board, /min-h-11/);
  assert.match(board, /max-md:hidden/);
  assert.match(mobile, /min-h-12 min-w-32/);
  assert.match(mobile, /--layout-safe-right/);
  assert.match(mobile, /max-md:block/);
  assert.match(page, /<MobileTurnExtensionButton \/>/);
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
  assert.match(page, /!isObserver && !isPlayback && <InactivityWarningOverlay \/>/);
  assert.match(store, /inactivityLossRemainingMs: 240_000/);
  assert.match(types, /inactivityActive\?: "my" \| "opponent" \| null/);
});
