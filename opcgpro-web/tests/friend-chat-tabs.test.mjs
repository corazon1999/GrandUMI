import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("在线玩家列表可直接申请或接受好友", async () => {
  const playerList = await readSource("../src/components/home/PlayerListPanel.tsx");

  assert.match(playerList, /HomeRequest\.sendFriendRequest\(p\.account\)/);
  assert.match(playerList, /HomeRequest\.respondFriendRequest\(relationship\.requestId!, true\)/);
  assert.match(playerList, /添加好友/);
  assert.match(playerList, /已是好友/);
});

test("局内聊天面板提供局内与好友分页并保留各自消息流", async () => {
  const [panel, request, protocol, store] = await Promise.all([
    readSource("../src/components/game/GameChatPanel.tsx"),
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/net/GameProtocol.ts"),
    readSource("../src/store/netStore.ts"),
  ]);

  assert.match(panel, /type ChatTab = "game" \| "friends"/);
  assert.match(panel, /局内/);
  assert.match(panel, /好友/);
  assert.match(panel, /GameRequest\.sendFriendChat\(selectedFriend\.account, text\)/);
  assert.match(request, /proto: "MsgFriendChat", toAccount, text/);
  assert.match(protocol, /case "MsgFriendChat"/);
  assert.match(store, /friendChatMessages/);
});

test("好友私聊由服务端验证好友关系且只回显给双方", async () => {
  const bridge = await readSource("../../服务端WebSocket/WebSocketBridge.cs");

  assert.match(bridge, /_playerDataStore\.AreFriends\(s\.Account!, toAccount\)/);
  assert.match(bridge, /只能给好友发送消息/);
  assert.match(bridge, /好友当前不在线/);
  assert.match(bridge, /Send\(s\.SessionId, packet\);\s*Send\(target\.SessionId, packet\);/);
});
