import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("在线玩家操作在手机竖屏使用两列网格且不挤占玩家信息", async () => {
  const [playerList, safetyActions] = await Promise.all([
    readSource("../src/components/home/PlayerListPanel.tsx"),
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
  ]);

  assert.match(playerList, /grid min-h-14 shrink-0 grid-cols-1 gap-2/);
  assert.match(
    playerList,
    /grid auto-rows-\[3rem\] grid-cols-2 gap-1 @\[640px\]:grid-cols-4/,
  );
  assert.match(playerList, /aria-label=\{`\$\{p\.name\} 的玩家操作`\}/);
  assert.match(playerList, /className="col-span-2 grid grid-cols-2 gap-1"/);
  assert.doesNotMatch(playerList, /max-w-48 flex-wrap/);
  assert.match(safetyActions, /className\?: string/);
  assert.match(safetyActions, /className \?\? "flex flex-wrap justify-end gap-1"/);
});
