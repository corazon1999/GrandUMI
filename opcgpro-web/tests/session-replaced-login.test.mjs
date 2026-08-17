import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("同账号新登录会向旧连接发送终止通知和专用关闭码", async () => {
  const server = await readSource("../../服务端WebSocket/WebSocketBridge.cs");
  const session = await readSource("../../服务端WebSocket/WsSession.cs");

  assert.match(server, /SessionReplacedCloseCode = 4009/);
  assert.match(server, /proto = "MsgSessionReplaced"/);
  assert.match(server, /SupersedeSession\(superseded\)/);
  assert.match(server, /IsSupersededClientInstance\(clientInstanceId\)/);
  assert.match(server, /!string\.Equals\(superseded\.ClientInstanceId, clientInstanceId, StringComparison\.Ordinal\)/);
  assert.match(server, /MarkClientInstanceSuperseded\(superseded\.ClientInstanceId\)/);
  assert.doesNotMatch(server, /superseded\.Socket\.Abort\(\)/);
  assert.match(session, /EnqueueTerminalAsync/);
});

test("旧客户端收到异地登录后停止重连并清空登录态", async () => {
  const manager = await readSource("../src/net/NetManager.ts");
  const protocol = await readSource("../src/net/HomeProtocol.ts");
  const provider = await readSource("../src/components/NetProvider.tsx");

  assert.match(manager, /msg\.proto === "MsgSessionReplaced"/);
  assert.match(manager, /event\.code === SESSION_REPLACED_CLOSE_CODE/);
  assert.match(manager, /this\.wasConnectedBefore = false/);
  assert.match(manager, /eventBus\.emit\("sessionReplaced"/);
  assert.match(protocol, /eventBus\.on\("sessionReplaced"/);
  assert.match(protocol, /HomeRequest\.login\(account, undefined, true\)/);
  assert.match(protocol, /clientInstanceId: getClientInstanceId\(\)/);
  assert.match(protocol, /resume,/);
  assert.match(protocol, /store\.reset\(\)/);
  assert.match(protocol, /store\.setNavigateTo\("\/home"\)/);
  assert.match(provider, /savedAccount && !getSessionReplacedNotice\(\)/);
});

test("登录面板会持续显示异地登录原因，直到玩家主动重新登录", async () => {
  const loginPanel = await readSource("../src/components/home/LoginPanel.tsx");
  const replacement = await readSource("../src/net/sessionReplacement.ts");

  assert.match(replacement, /账号已在其他地方登录，请重新登录。/);
  assert.match(loginPanel, /getSessionReplacedNotice\(\)/);
  assert.match(loginPanel, /clearSessionReplacedNotice\(\)/);
  assert.match(loginPanel, /role="alert"/);
});
