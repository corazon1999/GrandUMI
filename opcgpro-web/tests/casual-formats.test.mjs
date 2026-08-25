import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("休闲匹配可选择标准与狂野且新客户端默认标准", async () => {
  const [lobby, protocol, store, types] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/store/netStore.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(types, /MatchQueueKind = "ranked" \| "rankedWild" \| "casualStandard" \| "casual"/);
  assert.match(store, /matchQueueKind: "casualStandard" as const/);
  assert.match(protocol, /queueKind: MatchQueueKind = "casualStandard"/);
  assert.match(lobby, /aria-label=\{isRanked \? "排位模式" : "休闲模式"\}/);
  assert.match(lobby, /setMatchQueueKind\(isRanked \? "ranked" : "casualStandard"\)/);
  assert.match(lobby, /setMatchQueueKind\(isRanked \? "rankedWild" : "casual"\)/);
  assert.match(lobby, /标准休闲.*遵循当前环境禁限卡表/s);
  assert.match(lobby, /狂野休闲可使用角标 1 等标准禁限卡/);
});

test("标准与狂野休闲贯穿牌组校验、独立队列和房间快照", async () => {
  const [bridge, matchKind, roomManager, gameBoard, types] = await Promise.all([
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../../服务端WebSocket/Game/MatchKind.cs"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(bridge, /StandardCasualMatchQueue/);
  assert.match(bridge, /"casualStandard" => StandardCasualMatchQueue/);
  assert.match(bridge, /queueKind is "ranked" or "casualStandard"/);
  assert.match(bridge, /"casualStandard" => MatchKind\.CasualStandard/);
  assert.match(bridge, /_ => MatchKind\.CasualWild/);
  assert.match(matchKind, /CasualStandard/);
  assert.match(matchKind, /CasualWild/);
  assert.match(roomManager, /or MatchKind\.CasualStandard/);
  assert.match(roomManager, /or MatchKind\.CasualWild/);
  assert.match(types, /"CasualStandard" \| "CasualWild"/);
  assert.match(gameBoard, /matchKind === "CasualWild" \? "狂野休闲"/);
  assert.match(gameBoard, /matchKind === "CasualStandard" \? "标准休闲"/);
});

test("旧 casual 协议继续保持无限制休闲语义", async () => {
  const [types, bridge] = await Promise.all([
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
  ]);

  assert.match(types, /casual 保留为旧客户端兼容值，语义等同狂野休闲/);
  assert.match(bridge, /历史 casual 协议值继续表示无限制（狂野）休闲匹配/);
  assert.match(bridge, /_ => "casual"/);
  assert.match(bridge, /_ => MatchQueue/);
  assert.match(bridge, /DeckValidator\.FormatUnrestricted/);
});

test("休闲格式控件在手机竖屏保持四十四像素触控区", async () => {
  const lobby = await readSource("../src/components/home/LobbyPanel.tsx");

  assert.match(lobby, /overflow-y-auto px-4 py-3/);
  assert.match(lobby, /aria-label=\{isRanked \? "排位模式" : "休闲模式"\}/);
  assert.match(lobby, /min-h-11 rounded-lg px-3 text-sm font-black/g);
});
