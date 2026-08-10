import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("好友列表协议附带可观战房间与好友座位", async () => {
  const [bridge, types] = await Promise.all([
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(bridge, /presence\.Status == "playing"/);
  assert.match(bridge, /GameRoomManager\.GetRoomBySession\(friendSession\.SessionId\)/);
  assert.match(bridge, /roomId = gameRoom\.RoomId/);
  assert.match(bridge, /seatIndex = resolvedSeatIndex/);
  assert.match(types, /interface FriendInfo[\s\S]*roomId\?: string \| null;[\s\S]*seatIndex\?: 0 \| 1 \| null;/);
});

test("好友面板可从正在进行的好友对局直接进入观战", async () => {
  const panel = await readSource("../src/components/home/FriendsPanel.tsx");

  assert.match(panel, /friend\.status === "playing" && friend\.roomId/);
  assert.match(panel, /HomeRequest\.spectateRoom\(friend\.roomId, friend\.seatIndex \?\? 0\)/);
  assert.match(panel, /spectateState === "joining"/);
  assert.match(panel, /spectateRoomId === friend\.roomId \? "进入中…" : "观战"/);
});

test("移动端好友列表在受限高度内支持纵向触摸滚动", async () => {
  const panel = await readSource("../src/components/home/FriendsPanel.tsx");

  assert.match(panel, /h-\[min\(70cqh,36rem\)\]/);
  assert.match(panel, /max-h-\[calc\(100cqh-7rem\)\]/);
  assert.match(panel, /min-h-0 flex-1 touch-pan-y overflow-y-auto overscroll-contain/);
  assert.match(panel, /\[-webkit-overflow-scrolling:touch\]/);
  assert.doesNotMatch(panel, /min-h-\[28rem\]/);
});
