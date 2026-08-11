import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [lobby, protocol, banner, bridge] = await Promise.all([
  readFile(new URL("../src/components/home/LobbyPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/components/ui/GlobalAnnouncementBanner.tsx", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8"),
]);

test("only the specified account sees the announcement composer", () => {
  assert.match(lobby, /account === "释迦"/);
  assert.match(lobby, /aria-label="公告内容"/);
  assert.match(lobby, /maxLength=\{200\}/);
  assert.match(lobby, /min-h-11/);
});

test("announcements use a dedicated protocol and render as a moving banner", () => {
  assert.match(protocol, /sendGlobalAnnouncement\(content: string\)/);
  assert.match(protocol, /proto: "MsgGlobalAnnouncement"/);
  assert.match(banner, /global-announcement-marquee/);
  assert.match(bridge, /case "MsgGlobalAnnouncement": OnGlobalAnnouncement/);
  assert.match(bridge, /GlobalAnnouncementPolicy\.IsAuthorized\(s\.Account\)/);
  assert.match(bridge, /BroadcastAll\(new[\s\S]*proto = "MsgGlobalAnnouncement"/);
});

test("管理员发送公告后保留输入内容", () => {
  const sendHandler = lobby.match(
    /const sendGlobalAnnouncement = \(\) => \{[\s\S]*?\n  \};/,
  )?.[0];

  assert.ok(sendHandler, "应定义全服公告发送处理函数");
  assert.match(sendHandler, /HomeRequest\.sendGlobalAnnouncement\(content\)/);
  assert.doesNotMatch(sendHandler, /setAnnouncementInput\(\s*""\s*\)/);
});
