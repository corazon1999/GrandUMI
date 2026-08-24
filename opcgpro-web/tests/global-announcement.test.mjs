import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [admin, protocol, banner, bridge, gamePage] = await Promise.all([
  readFile(new URL("../src/components/home/AdminPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/components/ui/GlobalAnnouncementBanner.tsx", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8"),
  readFile(new URL("../src/app/game/page.tsx", import.meta.url), "utf8"),
]);

test("只有服务端授权管理员可以看到公告编辑器", () => {
  assert.match(admin, /if \(!maintenance\.canManage\)/);
  assert.match(admin, /aria-label="公告内容"/);
  assert.match(admin, /maxLength=\{200\}/);
  assert.match(admin, /min-h-11/);
});

test("announcements use a dedicated protocol and render as a moving banner", () => {
  assert.match(protocol, /sendGlobalAnnouncement\(content: string\)/);
  assert.match(protocol, /proto: "MsgGlobalAnnouncement"/);
  assert.match(banner, /global-announcement-marquee/);
  assert.match(banner, /z-\[80\]/);
  assert.match(banner, /data-global-announcement-banner/);
  assert.match(banner, /--global-announcement-height/);
  assert.match(banner, /new ResizeObserver\(updateOffset\)/);
  assert.match(gamePage, /isObserver[\s\S]*z-\[90\][\s\S]*退出观战/, "观战退出控件应显示在公告之上");
  assert.match(bridge, /case "MsgGlobalAnnouncement": OnGlobalAnnouncement/);
  assert.match(bridge, /GlobalAnnouncementPolicy\.IsAuthorized\(s\.Account\)/);
  assert.match(bridge, /BroadcastAll\(new[\s\S]*proto = "MsgGlobalAnnouncement"/);
});

test("排位连胜播报不显示全服公告前缀且连续消息排队展示", () => {
  assert.match(banner, /announcement\.kind === "rankedStreak"/);
  assert.match(banner, /`全服公告：\$\{announcement\.content\}`/);
  assert.match(banner, /setAnnouncements\(\(current\) => \[\.\.\.current,/);
  assert.match(banner, /current\.slice\(1\)/);
  assert.match(bridge, /kind = "rankedStreak"/);
  assert.match(bridge, /BroadcastRankedWinStreakEnded/);
});

test("管理员发送公告后保留输入内容", () => {
  const sendHandler = admin.match(
    /const sendAnnouncement = \(\) => \{[\s\S]*?\n  \};/,
  )?.[0];

  assert.ok(sendHandler, "应定义全服公告发送处理函数");
  assert.match(sendHandler, /HomeRequest\.sendGlobalAnnouncement\(content\)/);
  assert.doesNotMatch(sendHandler, /setAnnouncement\(\s*""\s*\)/);
});
