import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("提示操作身份由服务端下发并兼容旧快照回退到提示ID", async () => {
  const [overlay, store, net, snapshot] = await Promise.all([
    readSource("../src/components/game/PromptOverlay.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
  ]);

  assert.match(snapshot, /operationId = p\.PromptId/);
  assert.match(store, /export interface PromptView[\s\S]*?operationId\?: string/);
  assert.match(net, /export interface PromptSnapshot[\s\S]*?operationId\?: string/);
  assert.match(overlay, /const operationId = prompt\?\.operationId \?\? prompt\?\.promptId \?\? null/);
});

test("同一操作的普通快照和重连重发不会清空选择或隐藏面板", async () => {
  const overlay = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(overlay, /useEffect\(\(\) => \{[\s\S]*?setSelected\(\[\]\)[\s\S]*?\}, \[operationId\]\)/);
  assert.match(overlay, /if \(!prompt\) return null/);
  assert.doesNotMatch(overlay, /if \(!prompt \|\| submitting[^)]*\) return null/);
  assert.match(overlay, /const isSubmitting = operationId !== null && submittingOperationId === operationId/);
  assert.match(overlay, /aria-busy=\{isSubmitting\}/);
  assert.match(overlay, /已提交，正在等待服务器确认/);
});

test("提交只在发送成功后加锁，超时仅开放幂等重试且权威推进才关闭", async () => {
  const overlay = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(
    overlay,
    /const sent = GameRequest\.respondPrompt\(prompt\.promptId, chosen\);[\s\S]*?if \(!sent\) return false;[\s\S]*?setSubmittingOperationId\(operationId\)/,
  );
  assert.match(
    overlay,
    /window\.setTimeout\(\(\) => setSubmittingOperationId\(null\), 3000\)/,
  );
  assert.doesNotMatch(overlay, /setTimeout\([^)]*clearLocalOverflow/);
  assert.match(overlay, /disabled=\{!canConfirm \|\| isSubmitting\}/);
  assert.match(overlay, /if \(isSubmitting\) return/);
  assert.equal(
    [...overlay.matchAll(/onClick=\{\(\) => setIsMinimized\(true\)\}[\s\S]{0,120}?disabled=\{isSubmitting\}/g)].length,
    2,
    "提交等待期间，效果确认框和通用选择面板都不得被收起",
  );
});

test("旋转手机牌桌的关键操作按64设计像素保留至少44实际像素", async () => {
  const overlay = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(overlay, /const promptActionHeightClass = rotateQuarterTurn \? "min-h-16" : "min-h-12"/);
  assert.match(overlay, /const promptDecisionHeightClass = rotateQuarterTurn \? "h-16" : "h-12"/);
  assert.match(overlay, /const promptToggleSizeClass = rotateQuarterTurn \? "h-16 w-16" : "h-12 w-12"/);
  assert.match(overlay, /\$\{promptActionHeightClass\} bg-orange-500/);
  assert.match(overlay, /\$\{promptActionHeightClass\} bg-gray-600/);
  assert.match(overlay, /\$\{promptActionHeightClass\} bg-blue-600/);

  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780], [344, 582]]) {
    const rotatedCanvasScale = Math.min(hostHeight / 844, hostWidth / 390);
    assert.ok(
      64 * rotatedCanvasScale >= 44,
      `${hostWidth}×${hostHeight} 的64设计像素操作区缩放后不足44像素`,
    );
  }
});
