import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("在线玩家信息与图标操作在手机竖屏保持单行且不提供屏蔽入口", async () => {
  const [playerList, safetyActions] = await Promise.all([
    readSource("../src/components/home/PlayerListPanel.tsx"),
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
  ]);

  assert.match(playerList, /flex min-h-16 shrink-0 items-center gap-2/);
  assert.match(playerList, /className="flex shrink-0 items-center gap-1"/);
  assert.match(playerList, /aria-label=\{`\$\{p\.name\} 的玩家操作`\}/);
  assert.match(playerList, /showBlock=\{false\}/);
  assert.match(playerList, /iconOnly/);
  assert.match(playerList, /h-11 w-11 min-h-11 min-w-11/);
  assert.doesNotMatch(playerList, />\s*添加好友\s*</);
  assert.doesNotMatch(playerList, />\s*邀请对战\s*</);
  assert.match(safetyActions, /className\?: string/);
  assert.match(safetyActions, /showBlock\?: boolean/);
  assert.match(safetyActions, /iconOnly\?: boolean/);
  assert.match(safetyActions, /\{showBlock && \(/);
  assert.match(safetyActions, /toolbar \|\| iconOnly \? <ReportPlayerIcon \/>/);
  assert.match(safetyActions, /className \?\? "flex flex-wrap justify-end gap-1"/);
});
