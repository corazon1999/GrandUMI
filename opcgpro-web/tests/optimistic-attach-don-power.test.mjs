import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import test from "node:test";

const execFileAsync = promisify(execFile);
const projectRoot = fileURLToPath(new URL("..", import.meta.url));

test("暂存贴咚按实际数量立即同步领袖与角色力量", async () => {
  const script = String.raw`
    import assert from "node:assert/strict";
    import { useGameStore } from "./src/store/gameStore.ts";

    const player = {
      costActive: 3,
      costAttached: 0,
      leaderId: "leader-id",
      leaderAttachedDon: 0,
      leaderPower: 5_000,
      fieldCards: [{ id: "character-id", attachedDon: 0, powerCurrent: 4_000 }],
    };

    useGameStore.setState({ currentTurn: true, my: player });
    useGameStore.getState().optimisticAttachDon("leader", 2);
    useGameStore.getState().optimisticAttachDon("character-id", 1);

    const current = useGameStore.getState().my;
    assert.equal(current.costActive, 0);
    assert.equal(current.costAttached, 3);
    assert.equal(current.leaderAttachedDon, 2);
    assert.equal(current.leaderPower, 7_000);
    assert.equal(current.fieldCards[0].attachedDon, 1);
    assert.equal(current.fieldCards[0].powerCurrent, 5_000);
  `;

  const { stdout } = await execFileAsync(
    process.execPath,
    ["--disable-warning=MODULE_TYPELESS_PACKAGE_JSON", "--experimental-strip-types", "--input-type=module", "-e", script],
    { cwd: projectRoot },
  );

  assert.equal(stdout, "");
});
