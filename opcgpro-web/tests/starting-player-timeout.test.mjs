import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("服务端下发先后手权威截止时间并在超时后默认骰点胜者先手", async () => {
  const [engine, state, manager, snapshot] = await Promise.all([
    readSource("../../服务端WebSocket/Game/GameEngine.cs"),
    readSource("../../服务端WebSocket/Game/GameState.cs"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
  ]);

  assert.match(engine, /StartingPlayerChoiceTimeoutSeconds = 60/);
  assert.match(engine, /State\.StartingPlayerChoiceDeadlineUtc = DateTime\.UtcNow\.AddSeconds/);
  assert.match(engine, /State\.StartingPlayerChoiceDeadlineUtc = null/);
  assert.match(state, /StartingPlayerChoiceDeadlineUtc/);
  assert.match(manager, /ResolveExpiredStartingPlayerChoiceAsync/);
  assert.match(manager, /new \{ goFirst = true \}/);
  assert.match(manager, /EnqueueCriticalWorkAsync\(active, new RoomWork\("StartingPlayerChoiceTimeout"/);
  assert.match(snapshot, /startingPlayerChoiceDeadlineUtc = state\.StartingPlayerChoiceDeadlineUtc/);
});

test("投骰界面显示权威倒计时并在超时后请求恢复", async () => {
  const [overlay, store, netTypes] = await Promise.all([
    readSource("../src/components/game/FirstPlayerOverlay.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(netTypes, /startingPlayerChoiceDeadlineUtc\?: string \| null/);
  assert.match(store, /s\.startingPlayerChoiceDeadlineUtc = msg\.startingPlayerChoiceDeadlineUtc \?\? null/);
  assert.match(overlay, /选择剩余 \{remainingSeconds\} 秒/);
  assert.match(overlay, /超时后将默认由骰点胜者先手/);
  assert.match(overlay, /GameRequest\.requestState\(\)/);
  assert.match(overlay, /min-h-11/);
  assert.match(overlay, /var\(--layout-safe-top, env\(safe-area-inset-top\)\)/);
  assert.match(overlay, /var\(--layout-safe-bottom, env\(safe-area-inset-bottom\)\)/);
});

test("断线提示位于投骰遮罩上方并使用对局安全区", async () => {
  const [overlay, banner] = await Promise.all([
    readSource("../src/components/game/FirstPlayerOverlay.tsx"),
    readSource("../src/components/game/OpponentDisconnectBanner.tsx"),
  ]);

  assert.match(overlay, /z-\[55\]/);
  assert.match(banner, /z-\[70\]/);
  assert.match(banner, /var\(--layout-safe-top, env\(safe-area-inset-top\)\)/);
});
