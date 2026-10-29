import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import test from "node:test";

const execFileAsync = promisify(execFile);
const projectRoot = fileURLToPath(new URL("..", import.meta.url));

test("贴咚和撤回回包保留合法攻击目标，跨回合或离场等边界清空选择", async () => {
  const script = String.raw`
    import assert from "node:assert/strict";
    import { useGameStore } from "./src/store/gameStore.ts";
    const snapshot = () => ({ proto: "MsgGameState", roomId: "don-selection", tick: 1,
      phase: "Main", currentTurn: true, turnCount: 3, firstPlayer: 0,
      my: { leaderId: "leader-id", leaderNumber: "OP01-001", handCardNumbers: [],
        fieldCards: [{ id: "character-id", number: "OP01-016" }], stages: [] },
      opponent: null, pendingPrompt: null, battle: null });
    const check = (target, change, expected) => {
      useGameStore.getState().resetGame();
      useGameStore.getState().setMode("Player");
      useGameStore.getState().syncFromServer(snapshot());
      useGameStore.getState().setSelectedField(target);
      useGameStore.getState().syncFromServer({ ...snapshot(), tick: 2, lastAction: "AttachDon", ...change });
      assert.equal(useGameStore.getState().selectedFieldId, expected);
    };
    check("leader-id", {}, "leader-id");
    check("character-id", {}, "character-id");
    check("leader-id", { lastAction: "UndoAttachDon" }, "leader-id");
    check("leader-id", { currentTurn: false }, null);
    check("leader-id", { turnCount: 4 }, null);
    check("leader-id", { roomId: "other-room" }, null);
    check("leader-id", { phase: "Counter" }, null);
    check("leader-id", { isGameOver: true }, null);
    check("leader-id", { pendingPrompt: { promptId: "cost", validChoices: [] } }, null);
    check("leader-id", { battle: { attackerCardId: "leader-id" } }, null);
    check("character-id", { my: { ...snapshot().my, fieldCards: [] } }, null);
    check("leader-id", { lastAction: "PlayCard" }, null);
  `;
  const { stdout } = await execFileAsync(process.execPath,
    ["--disable-warning=MODULE_TYPELESS_PACKAGE_JSON", "--experimental-strip-types", "--input-type=module", "-e", script],
    { cwd: projectRoot });
  assert.equal(stdout, "");
});
