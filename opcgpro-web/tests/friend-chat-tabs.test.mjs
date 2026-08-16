import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("在线玩家列表可直接申请或接受好友", async () => {
  const playerList = await readSource(
    "../src/components/home/PlayerListPanel.tsx",
  );

  assert.match(playerList, /HomeRequest\.sendFriendRequest\(p\.account\)/);
  assert.match(
    playerList,
    /HomeRequest\.respondFriendRequest\(relationship\.requestId!, true\)/,
  );
  assert.match(playerList, /添加好友/);
  assert.match(playerList, /aria-label="已是好友"/);
  assert.match(playerList, /<svg[\s\S]*?m14\.5 17 2 2 4-5/);
});

test("局内聊天气泡只保留局内消息并通过独立按钮打开完整好友中心", async () => {
  const [panel, friendsPanel, request, protocol, store] = await Promise.all([
    readSource("../src/components/game/GameChatPanel.tsx"),
    readSource("../src/components/home/FriendsPanel.tsx"),
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/net/GameProtocol.ts"),
    readSource("../src/store/netStore.ts"),
  ]);

  assert.match(panel, /局内聊天/);
  assert.match(panel, /aria-label="打开局内聊天"/);
  assert.match(panel, /<FriendsPanel open={friendsOpen}/);
  assert.match(panel, /aria-label={`打开好友中心/);
  assert.match(panel, /friendChatUnreadByAccount/);
  assert.match(panel, /incomingFriendRequests\.length/);
  assert.match(panel, /GameRequest\.sendGameChat\(text\)/);
  assert.doesNotMatch(panel, /type ChatTab/);
  assert.doesNotMatch(panel, /FriendConversationPicker/);
  assert.doesNotMatch(panel, /GameRequest\.sendFriendChat/);
  assert.match(friendsPanel, /type Tab = "chat" \| "requests" \| "search"/);
  assert.match(
    friendsPanel,
    /GameRequest\.sendFriendChat\(selectedFriend\.account, text\)/,
  );
  assert.match(request, /proto: "MsgFriendChat", toAccount, text/);
  assert.match(protocol, /case "MsgFriendChat"/);
  assert.match(store, /friendChatMessages/);
});

test("大厅聊天面板提供大厅与好友分页并支持实时私聊", async () => {
  const [panel, mainPanel, friendChatView, layoutSettings, modal, globals] =
    await Promise.all([
      readSource("../src/components/home/ChatPanel.tsx"),
      readSource("../src/components/home/MainPanel.tsx"),
      readSource("../src/components/chat/FriendChatView.tsx"),
      readSource("../src/components/home/LayoutSettingsProvider.tsx"),
      readSource("../src/components/ui/Modal.tsx"),
      readSource("../src/app/globals.css"),
    ]);

  assert.match(panel, /type ChatTab = "lobby" \| "friends"/);
  assert.match(panel, /大厅/);
  assert.match(panel, /好友/);
  assert.match(panel, /HomeRequest\.requestFriendList\(\)/);
  assert.match(
    panel,
    /GameRequest\.sendFriendChat\(selectedFriend\.account, text\)/,
  );
  assert.match(panel, /messages={friendChatMessages}/);
  assert.doesNotMatch(panel, /!selectedFriend\?\.online/);
  assert.match(panel, /friendChatUnreadByAccount/);
  assert.match(panel, /markFriendChatRead\(selectedFriendAccount\)/);
  assert.match(panel, /friendConversationOpen/);
  assert.match(panel, /<FriendChatView/);
  assert.match(mainPanel, /title="聊天"/);
  assert.match(mainPanel, /aria-label="打开聊天"/);
  assert.match(mainPanel, /max-w-3xl/);
  assert.match(friendChatView, /aria-label="好友会话列表"/);
  assert.match(friendChatView, /placeholder="搜索好友"/);
  assert.match(friendChatView, /role="listbox"/);
  assert.match(friendChatView, /lastMessageByAccount/);
  assert.match(
    friendChatView,
    /Number\(b\.online\) - Number\(a\.online\) \|\| bLast - aLast/,
  );
  assert.doesNotMatch(friendChatView, /仅聊天/);
  assert.match(friendChatView, /离线 · 可留言/);
  assert.match(friendChatView, /rounded-br-sm bg-\[#005c4b\]/);
  assert.match(friendChatView, /返回好友会话列表/);
  assert.match(friendChatView, /@\[560px\]:flex/);
  assert.match(friendChatView, /min-h-11/);
  assert.match(friendChatView, /Shift \+ Enter 换行/);
  assert.match(layoutSettings, /data-layout-settings-trigger/);
  assert.match(modal, /document\.body\.dataset\.modalOpenCount/);
  assert.match(
    globals,
    /body\[data-modal-open-count\] \[data-layout-settings-trigger\]/,
  );
});

test("好友聊天未读数由全局状态维护并可按好友清除", async () => {
  const store = await readSource("../src/store/netStore.ts");

  assert.match(store, /friendChatUnreadByAccount: Record<string, number>/);
  assert.match(
    store,
    /\[senderKey\]: \(state\.friendChatUnreadByAccount\[senderKey\] \?\? 0\) \+ 1/,
  );
  assert.match(store, /markFriendChatRead: \(account\) => set/);
  assert.match(store, /delete friendChatUnreadByAccount\[key\]/);
});

test("好友中心默认就是微信式会话列表并可直接发送好友消息", async () => {
  const [panel, friendChatView] = await Promise.all([
    readSource("../src/components/home/FriendsPanel.tsx"),
    readSource("../src/components/chat/FriendChatView.tsx"),
  ]);

  assert.match(panel, /type Tab = "chat" \| "requests" \| "search"/);
  assert.match(panel, /useState<Tab>\("chat"\)/);
  assert.match(panel, /<FriendChatView/);
  assert.match(
    panel,
    /GameRequest\.sendFriendChat\(selectedFriend\.account, text\)/,
  );
  assert.match(panel, /markFriendChatRead\(selectedFriendAccount\)/);
  assert.match(panel, /headerActions=\{chatHeaderActions\}/);
  assert.match(panel, /邀请 \$\{selectedFriend\.name\} 对战/);
  assert.match(panel, /删除好友 \$\{selectedFriend\.name\}/);
  assert.match(friendChatView, /headerActions &&/);
});

test("好友私聊先持久化，在线时立即投递，离线时登录补发", async () => {
  const [bridge, dataStore] = await Promise.all([
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../../服务端WebSocket/Persistence/PlayerDataStore.cs"),
  ]);

  assert.match(bridge, /_playerDataStore\.QueueFriendMessage\(/);
  assert.match(bridge, /PushQueuedFriendMessages\(s\)/);
  assert.match(
    bridge,
    /TryGetActiveSession\(queued\.ToAccount, out var target\)/,
  );
  assert.doesNotMatch(bridge, /好友当前不在线/);
  assert.match(dataStore, /CREATE TABLE IF NOT EXISTS friend_message_queue/);
  assert.match(dataStore, /MaxQueuedFriendMessagesPerPlayer = 500/);
  assert.match(dataStore, /只能给好友发送消息/);
  assert.match(dataStore, /TakeQueuedFriendMessages/);
});

test("大厅和好友中心均允许给离线好友留言", async () => {
  const [homeChat, friendsPanel] = await Promise.all([
    readSource("../src/components/home/ChatPanel.tsx"),
    readSource("../src/components/home/FriendsPanel.tsx"),
  ]);

  for (const panel of [homeChat, friendsPanel]) {
    assert.doesNotMatch(panel, /!selectedFriend\?\.online/);
    assert.doesNotMatch(panel, /好友当前离线/);
    assert.match(panel, /留言/);
  }
});
