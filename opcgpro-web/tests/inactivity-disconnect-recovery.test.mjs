import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";
import { calculateLayoutScale } from "../src/lib/gameLayout.ts";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

async function loadRecoveryPolicy() {
  const source = await readSource("../src/lib/inactivityRecovery.ts");
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const moduleUrl = `data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`;
  return import(moduleUrl);
}

test("断线只推进一次连接世代，恢复阶段不会重复推进", async () => {
  const { nextConnectionEpoch } = await loadRecoveryPolicy();

  let epoch = nextConnectionEpoch(7, "connected", "reconnecting");
  assert.equal(epoch, 8);
  epoch = nextConnectionEpoch(epoch, "reconnecting", "recovering");
  assert.equal(epoch, 8);
  epoch = nextConnectionEpoch(epoch, "recovering", "connected");
  assert.equal(epoch, 8);
  assert.equal(nextConnectionEpoch(epoch, "connected", "disconnected"), 9);
});

test("挂机层在断线、恢复中及新快照到达前始终不可见", async () => {
  const { shouldShowInactivityWarning } = await loadRecoveryPolicy();
  const warning = {
    active: "my",
    warning: true,
    isGameOver: false,
    connectionEpoch: 4,
    snapshotConnectionEpoch: 4,
  };

  assert.equal(shouldShowInactivityWarning({ ...warning, connState: "connected" }), true);
  for (const connState of ["disconnected", "connecting", "handshaking", "reconnecting", "recovering", "failed"]) {
    assert.equal(shouldShowInactivityWarning({ ...warning, connState }), false, connState);
  }
  assert.equal(shouldShowInactivityWarning({
    ...warning,
    connState: "connected",
    connectionEpoch: 5,
  }), false, "登录回包先到但重同步快照尚未到达时不得复活旧弹窗");
  assert.equal(shouldShowInactivityWarning({
    ...warning,
    connState: "connected",
    connectionEpoch: 5,
    snapshotConnectionEpoch: 5,
  }), true, "新连接的权威快照到达后才可恢复显示");
});

test("发送失败强制进入单一恢复流程，且不会重复提交确认", async () => {
  const [overlay, manager, protocol, netStore, gameStore] = await Promise.all([
    readSource("../src/components/game/InactivityWarningOverlay.tsx"),
    readSource("../src/net/NetManager.ts"),
    readSource("../src/net/GameProtocol.ts"),
    readSource("../src/store/netStore.ts"),
    readSource("../src/store/gameStore.ts"),
  ]);

  assert.match(overlay, /if \(submittingRef\.current\) return;/);
  assert.match(overlay, /NetManager\.recoverAfterSendFailure\(getWebSocketEndpoints\(\)\)/);
  assert.match(overlay, /确认未送达，正在恢复连接/);
  assert.match(overlay, /立即换线重连/);
  assert.match(manager, /recoverAfterSendFailure\(url\?: string \| readonly string\[\]\)/);
  assert.match(manager, /this\.socketGeneration\+\+;[\s\S]*this\.stateBaseline = null;[\s\S]*this\.pendingPings\.clear\(\)/);
  assert.match(manager, /!this\.lossNotified[\s\S]*eventBus\.emit\("close"\)/);
  assert.match(protocol, /syncFromServer\([\s\S]*useNetStore\.getState\(\)\.connectionEpoch/);
  assert.match(netStore, /connectionEpoch: nextConnectionEpoch/);
  assert.match(gameStore, /s\.snapshotConnectionEpoch = connectionEpoch/);
});

test("断线恢复层高于全部对局交互层并遵守移动安全区", async () => {
  const [reconnect, inactivity, route, fixture] = await Promise.all([
    readSource("../src/components/game/ReconnectOverlay.tsx"),
    readSource("../src/components/game/InactivityWarningOverlay.tsx"),
    readSource("../src/app/layout-verification/inactivity-recovery/page.tsx"),
    readSource("../src/components/game/InactivityRecoveryLayoutVerification.tsx"),
  ]);

  assert.equal((reconnect.match(/z-\[10100\]/g) ?? []).length, 2);
  assert.doesNotMatch(reconnect, /className="fixed inset-0 z-50/);
  assert.match(reconnect, /--layout-safe-left/);
  assert.match(reconnect, /--layout-safe-right/);
  assert.match(reconnect, /--layout-safe-top/);
  assert.match(reconnect, /--layout-safe-bottom/);
  assert.equal((reconnect.match(/min-h-12/g) ?? []).length, 2);
  assert.equal((inactivity.match(/min-h-12/g) ?? []).length, 2);
  assert.match(route, /GRANDUMI_LAYOUT_VERIFICATION !== "1"/);
  assert.match(fixture, /mode=\{mobile \? "mobile-landscape" : "desktop"\}/);
  assert.match(fixture, /rotateQuarterTurn=\{mobile\}/);

  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const scale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });
    assert.ok(48 * scale >= 44, `${hostWidth}×${hostHeight} 的恢复按钮实际触控高度不足 44px`);
  }
});
