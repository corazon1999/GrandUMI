import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("公开匹配明确保留排位与休闲两个入口", async () => {
  const [lobby, protocol, types] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(lobby, />\s*排位匹配\s*</);
  assert.match(lobby, />\s*休闲匹配\s*</);
  assert.match(lobby, /setMatchQueueKind\("ranked"\)/);
  assert.match(lobby, /setMatchQueueKind\("casual"\)/);
  assert.match(protocol, /enterMatch\(deck: string, deckName\?: string, queueKind: "ranked" \| "casual" = "casual"\)/);
  assert.match(types, /queueKind\?: "ranked" \| "casual"/);
});

test("对局界面展示双方独立的权威操作棋钟", async () => {
  const [board, store, netTypes] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(board, /<OperationClock side="opponent" \/>/);
  assert.match(board, /<OperationClock side="my" \/>/);
  assert.match(store, /s\.myOperationTimeMs = msg\.myOperationTimeMs \?\? 1_200_000/);
  assert.match(store, /s\.opponentOperationTimeMs = msg\.opponentOperationTimeMs \?\? 1_200_000/);
  assert.match(netTypes, /operationClockActive\?: "my" \| "opponent" \| null/);
});

test("断线提示只展示服务端两分钟宽限且不能提前判负", async () => {
  const [banner, manager] = await Promise.all([
    readSource("../src/components/game/OpponentDisconnectBanner.tsx"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
  ]);

  assert.match(banner, /setCountdown\(payload\.gracePeriodSeconds\)/);
  assert.match(banner, /每名玩家每局累计 120 秒宽限/);
  assert.doesNotMatch(banner, /GameRequest/);
  assert.match(manager, /private const int GracePeriodSeconds = 120/);
  assert.match(manager, /DisconnectGraceRemainingMs/);
  assert.match(manager, /对手仍在 2 分钟断线宽限期内/);
});
