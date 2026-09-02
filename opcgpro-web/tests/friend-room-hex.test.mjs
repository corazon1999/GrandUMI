import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { translateText } from "../src/i18n/core.mjs";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("好友房创建可选择海克斯并把房主选项写入建房协议", async () => {
  const [lobby, protocol, types] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(lobby, /const \[friendHexMode, setFriendHexMode\] = useState\(false\)/);
  assert.match(lobby, /aria-label="好友房玩法"/);
  assert.match(lobby, /setFriendHexMode\(false\)/);
  assert.match(lobby, /setFriendHexMode\(true\)/);
  assert.match(lobby, /普通模式/);
  assert.match(lobby, /海克斯模式/);
  assert.ok((lobby.match(/min-h-11 rounded-lg/g)?.length ?? 0) >= 2);
  assert.match(lobby, /HomeRequest\.createRoom\(selectedDeck\.cards, selectedDeck\.name, friendHexMode\)/);
  assert.match(lobby, /加入房间时沿用房主锁定的玩法/);

  assert.match(protocol, /createRoom\(deck: string, deckName: string, hexMode = false\)/);
  assert.match(protocol, /proto: "MsgCreateRoom",[\s\S]*deckName,[\s\S]*hexMode,/);
  assert.match(types, /interface MsgCreateRoom[\s\S]*hexMode\?: boolean/);
});

test("好友房状态以服务端玩法为准且旧服务端安全回退普通模式", async () => {
  const [protocol, store, panel, types] = await Promise.all([
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/store/netStore.ts"),
    readSource("../src/components/home/FriendlyRoomPanel.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(protocol, /hexMode: msg\.hexMode === true/);
  assert.match(store, /type FriendlyRoomState = \{[\s\S]*hexMode: boolean/);
  assert.match(types, /interface MsgFriendlyRoom[\s\S]*hexMode\?: boolean/);
  assert.match(types, /interface MsgJoinRoom[\s\S]*hexMode\?: boolean/);
  assert.match(panel, /data-friendly-room-gameplay=\{room\.hexMode \? "hex" : "normal"\}/);
  assert.match(panel, /room\.hexMode \? "海克斯模式" : "普通模式"/);
  assert.match(panel, /房主已锁定/);
  assert.match(panel, /min-h-11/);
});

test("好友房海克斯使用独立权威开关并可通过日志重放恢复", async () => {
  const [lobby, bridge, engine, manager, replay, rules] = await Promise.all([
    readSource("../../服务端WebSocket/DuelLobby.cs"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../../服务端WebSocket/Game/GameEngine.cs"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
    readSource("../../服务端WebSocket/Game/MatchReplay.cs"),
    readSource("../../服务端WebSocket/Game/Hex/HexRules.cs"),
  ]);

  assert.match(lobby, /public bool HexMode \{ get; init; \}/);
  assert.match(lobby, /DuelLobbyStartData\([\s\S]*bool HexMode\)/);
  assert.match(bridge, /HexMode = Bool\(msg, "hexMode"\)/);
  assert.match(bridge, /hexMode = existingRoom\.HexMode/);
  assert.match(bridge, /hexMode = room\.HexMode/);
  assert.match(bridge, /room\.RoomId, room\.MatchKind, start\.HexMode/);
  assert.match(engine, /MatchKind matchKind = MatchKind\.UnknownHuman,[\s\S]*bool hexMode = false/);
  assert.match(rules, /state\.HexState\.Enabled = hexMode \|\| state\.MatchKind == MatchKind\.Hex/);
  assert.match(manager, /hexMode = engine\.State\.HexState\.Enabled/);
  assert.match(manager, /var hexMode = matchKind == MatchKind\.Hex/);
  assert.match(manager, /hexMode = storedHexMode\.GetBoolean\(\) \|\| matchKind == MatchKind\.Hex/);
  assert.match(manager, /matchKind: matchKind,[\s\S]*hexMode: hexMode,[\s\S]*hexRulesRevision/);
  assert.match(replay, /MatchKind matchKind = MatchKind\.UnknownHuman,[\s\S]*bool hexMode = false/);
  assert.match(replay, /matchKind: matchKind,[\s\S]*hexMode: hexMode/);
});

test("好友房海克斯布局夹具默认隔离连接且对局界面按权威海克斯状态展示", async () => {
  const [fixture, route, board] = await Promise.all([
    readSource("../src/components/home/FriendlyHexLayoutVerification.tsx"),
    readSource("../src/app/layout-verification/friendly-hex/page.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
  ]);

  assert.match(fixture, /connState: "disconnected"/);
  assert.match(fixture, /hexMode: true/);
  assert.match(route, /process\.env\.GRANDUMI_LAYOUT_VERIFICATION !== "1"/);
  assert.match(board, /const showHexSlots = matchKind === "Hex" \|\| hexState\?\.enabled === true/);
});

test("好友房海克斯新增文案保持中英日三语可用", () => {
  assert.equal(translateText("海克斯模式", "en"), "Hex mode");
  assert.equal(translateText("海克斯模式", "ja"), "ヘックスモード");
  assert.equal(translateText("房主已锁定", "en"), "Locked by host");
  assert.equal(translateText("普通模式不启用海克斯强化。加入房间时沿用房主锁定的玩法。", "ja"),
    "通常モードではヘックス強化を使用しません。ルーム参加時はホストが固定したルールが適用されます。");
});
