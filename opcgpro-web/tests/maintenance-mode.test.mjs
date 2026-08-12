import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [bridge, roomManager, protocol, panel, lobby, main] = await Promise.all([
  readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/Game/GameRoomManager.cs", import.meta.url), "utf8"),
  readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/MaintenanceControlPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/LobbyPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/MainPanel.tsx", import.meta.url), "utf8"),
]);

test("维护开关由服务端验证管理员权限并广播权威状态", () => {
  assert.match(bridge, /case "MsgSetMaintenance": OnSetMaintenance/);
  assert.match(bridge, /GlobalAnnouncementPolicy\.IsAuthorized\(s\.Account\)/);
  assert.match(bridge, /GameRoomManager\.SetMaintenanceMode\(enabled\)/);
  assert.match(bridge, /BroadcastMaintenanceState\(\)/);
  assert.match(protocol, /case "MsgMaintenanceState"/);
  assert.match(protocol, /setMaintenance\(enabled: boolean\)/);
});

test("所有新对局入口都受维护门禁保护", () => {
  for (const protocolName of ["MsgEnterMatch", "MsgEnterBotMatch", "MsgCreateRoom", "MsgJoinRoom", "MsgInvitePlayer"]) {
    assert.match(bridge, new RegExp(`RejectForMaintenance\\(s, "${protocolName}"\\)`));
  }
  assert.match(bridge, /OnFriendlyReady[\s\S]*GameRoomManager\.GetMaintenanceSnapshot\(\)\.Enabled/);
  assert.match(roomManager, /Maintenance\.TryReserveRoomCreation/);
  assert.match(roomManager, /throw new GameMaintenanceException/);
});

test("管理面板展示活跃房间并保留移动端触控尺寸", () => {
  assert.match(main, /maintenance\.canManage \|\| maintenance\.enabled/);
  assert.match(panel, /aria-label="维护控制面板"/);
  assert.match(panel, /maintenance\.activeRoomCount/);
  assert.match(panel, /全部对局已结束，可以开始正式服更新发布/);
  assert.match(panel, /min-h-11/);
});

test("维护时玩家看到提示且不能发起对局", () => {
  assert.match(main, /maintenance\.canManage \|\| maintenance\.enabled/);
  assert.match(panel, /aria-label="维护更新中"/);
  assert.match(panel, /排位、休闲匹配、好友房和单人对局已暂停/);
  assert.match(lobby, /!maintenance\.enabled/);
  assert.match(bridge, /GameMaintenanceState\.PlayerMessage/);
});
