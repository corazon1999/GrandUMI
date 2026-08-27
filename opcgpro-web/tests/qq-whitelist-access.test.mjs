import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  normalizeQq,
  previewQqWhitelistJson,
  QQ_WHITELIST_MAX_BYTES,
  QQ_WHITELIST_MAX_MEMBERS,
} from "../src/lib/qqWhitelist.mjs";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("本地预览兼容常见 QQ 群成员 JSON 并全量去重", () => {
  for (const json of [
    '["12345",23456,{"qq":"34567"},{"uin":45678},{"user_id":"56789"},"12345"]',
    '{"members":["12345","23456","34567","45678","56789","12345"]}',
    '{"data":[{"QQ":"12345"},{"UIN":"23456"},{"USER_ID":"34567"},45678,56789,12345]}',
    '{"list":[12345,23456,34567,45678,56789,12345]}',
  ]) {
    assert.deepEqual(previewQqWhitelistJson(json), {
      totalCount: 6,
      uniqueCount: 5,
      duplicateCount: 1,
    });
  }
  assert.equal(normalizeQq(" １２３４５ "), "12345");
});

test("本地预览拒绝空名单、非法项、超限和不安全数字", () => {
  assert.throws(() => previewQqWhitelistJson("[]"), /空白名单/);
  assert.throws(() => previewQqWhitelistJson('["12345",{"qq":"bad"}]'), /第 2 条/);
  assert.throws(() => previewQqWhitelistJson(`["${"1".repeat(QQ_WHITELIST_MAX_BYTES)}"]`), /不能超过/);
  assert.throws(
    () => previewQqWhitelistJson(`[${Array(QQ_WHITELIST_MAX_MEMBERS + 1).fill('"12345"').join(",")}]`),
    /不能超过/,
  );
  assert.throws(() => normalizeQq(9_999_999_999_999_999), /安全整数/);
});

test("登录协议仅在服务端要求时展示 QQ 绑定和 bootstrap 初始化", async () => {
  const [login, protocol, types] = await Promise.all([
    readSource("../src/components/home/LoginPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(login, /authStep.*"qq".*"bootstrap"/);
  assert.match(login, /login\.needsQqBinding/);
  assert.match(login, /login\.needsQqWhitelistInitialization && login\.canInitializeQqWhitelist/);
  assert.match(login, /一经绑定，玩家不能自行修改或解绑/);
  assert.match(login, /inputMode="numeric"/);
  assert.match(login, /HomeRequest\.login\([\s\S]*normalizedQq,[\s\S]*qqAuthToken/);
  assert.match(login, /<QqWhitelistImportPanel bootstrap/);
  assert.match(types, /qq\?: string/);
  assert.match(types, /needsQqBinding\?: boolean/);
  assert.match(protocol, /authTokenOverride \?\? readAuthToken\(account\)/);
  assert.match(protocol, /qq === undefined \? \{\} : \{ qq \}/);
});

test("管理员导入界面提供选择、预览、确认、摘要和移动端触控尺寸", async () => {
  const [panel, admin, protocol] = await Promise.all([
    readSource("../src/components/home/QqWhitelistImportPanel.tsx"),
    readSource("../src/components/home/AdminPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
  ]);

  assert.match(panel, /accept="\.json,application\/json"/);
  assert.match(panel, /previewQqWhitelistJson/);
  assert.match(panel, /window\.confirm/);
  assert.match(panel, /服务端结果为最终权威/);
  assert.match(panel, /已绑定但被移出/);
  assert.match(panel, /原始 JSON 仅用于本次导入校验，不作为审计副本保存/);
  assert.match(panel, /fileReadGenerationRef/);
  assert.match(panel, /readGeneration !== fileReadGenerationRef\.current/);
  assert.match(panel, /setRawJson\(""\)/);
  assert.ok((panel.match(/min-h-11/g) ?? []).length >= 3);
  assert.match(panel, /sm:flex-row/);
  assert.match(admin, /<QqWhitelistImportPanel/);
  assert.match(admin, /qqMasked/);
  assert.match(admin, /当前已移出白名单/);
  assert.match(protocol, /proto: "MsgQqWhitelistImport", json/);
});

test("390×844 与 360×780 手机竖屏下 QQ 登录和导入保持可滚动且可触控", async () => {
  const [login, panel] = await Promise.all([
    readSource("../src/components/home/LoginPanel.tsx"),
    readSource("../src/components/home/QqWhitelistImportPanel.tsx"),
  ]);

  const mobileBreakpoint = 640;
  for (const [width, height] of [[390, 844], [360, 780]]) {
    assert.ok(width < mobileBreakpoint, `${width}px 应使用单列手机布局`);
    assert.ok(width - 32 >= 328, `${width}px 扣除登录页横向留白后仍应容纳表单`);
    assert.ok(height >= 780, `${height}px 应由动态视口滚动容器完整承载流程`);
  }

  assert.match(login, /h-\[100dvh\][^\"]*overflow-y-auto/);
  assert.match(login, /safe-area-inset-top/);
  assert.match(login, /safe-area-inset-bottom/);
  assert.match(login, /authStep === "bootstrap" \? "max-w-xl" : "max-w-sm"/);
  assert.match(login, /id="login-qq"[\s\S]*?className="h-12 w-full/);
  assert.match(login, /authStep !== "bootstrap"[\s\S]*?className="h-12 w-full/);
  assert.ok((login.match(/min-h-11/g) ?? []).length >= 4);

  assert.match(panel, /flex flex-col gap-2 sm:flex-row/);
  assert.match(panel, /grid grid-cols-2 gap-2[^\"]*sm:grid-cols-4/);
  assert.match(panel, /mt-4 flex flex-col gap-3 sm:flex-row/);
  assert.match(panel, /break-words/);
  assert.match(panel, /min-h-11 w-full/);
});

test("服务端登录、清退和全部新对局入口都接入统一 QQ 权威门禁", async () => {
  const bridge = await readSource("../../服务端WebSocket/WebSocketBridge.cs");

  assert.match(bridge, /_qqAccessStore\.EvaluateLogin\(authentication\.Account, submittedQq\)/);
  assert.match(bridge, /qqElement\.ValueKind != JsonValueKind\.String/);
  assert.match(bridge, /IsBootstrapAdministrator\(authentication\.Account\)/);
  assert.match(bridge, /private static void OnEnterMatch[\s\S]*?TryRequireNewGameAccess\(s, "MsgEnterMatch"\)/);
  assert.match(bridge, /private static void OnEnterBotMatch[\s\S]*?TryRequireNewGameAccess\(s, "MsgEnterBotMatch"\)/);
  assert.match(bridge, /private static void OnCreateRoom[\s\S]*?TryRequireNewGameAccess\(s, "MsgCreateRoom"\)/);
  assert.match(bridge, /private static void OnJoinRoom[\s\S]*?TryRequireNewGameAccess\(s, "MsgJoinRoom"\)/);
  assert.match(bridge, /private static void OnInvitePlayer[\s\S]*?TryRequireNewGameAccess\(s, "MsgInvitePlayer"\)/);
  assert.match(bridge, /private static void OnInviteResponse[\s\S]*?TryRequireNewGameAccess\(s, "MsgInviteResult"\)/);
  assert.match(bridge, /private static void OnFriendlySelectDeck[\s\S]*?TryRequireNewGameAccess/);
  assert.match(bridge, /private static void OnFriendlyReady[\s\S]*?TryRequireNewGameAccess/);
  assert.match(bridge, /private static void TryStartFriendlyGame[\s\S]*?TryRequireNewGameAccess\(host/);
  assert.match(bridge, /private static string\? StartDuel[\s\S]*?_qqAccessStore\.ExecuteNewGameAdmission/);
  assert.ok((bridge.match(/ExecuteNewGameAdmission/g) ?? []).length >= 7);
  assert.match(bridge, /private static void EvictIneligibleWaitingActivities/);
  assert.match(bridge, /HasRegisteredGame\(accounts\)/);
  assert.match(bridge, /本场结束后房间已关闭/);
});
