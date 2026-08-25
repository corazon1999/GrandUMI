import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [bridge, players, auth, metrics] = await Promise.all([
  readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/Persistence/PlayerDataStore.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/Persistence/AccountAuthenticationStore.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/Diagnostics/AdminOperationsMetricsCache.cs", import.meta.url), "utf8"),
]);

test("玩家管理协议由服务端管理员权限和独立限流保护", () => {
  assert.match(bridge, /case "MsgAdminPlayerSearch": OnAdminPlayerSearch/);
  assert.match(bridge, /case "MsgAdminPlayerUpdate": OnAdminPlayerUpdate/);
  assert.match(bridge, /AdministratorPolicy\.IsAuthorized\(session\.Account\)/);
  assert.match(bridge, /admin-player-update/);
  assert.match(bridge, /TryGetActiveSession\(reset\.Account/);
});

test("改名与密码重置写入审计且临时密码不会进入日志", () => {
  assert.match(players, /INSERT INTO admin_player_audit/);
  assert.match(players, /"rename"/);
  assert.match(auth, /"reset_password"/);
  assert.match(auth, /CreateTemporaryPassword/);
  assert.doesNotMatch(bridge, /Log(?:Err)?\([^;\n]*temporaryPassword/);
  assert.match(bridge, /管理员玩家操作 reset_password/);
});

test("场次和磁盘指标使用不同低频缓存周期", () => {
  assert.match(metrics, /TimeSpan\.FromMinutes\(10\)/);
  assert.match(metrics, /TimeSpan\.FromHours\(3\)/);
});
