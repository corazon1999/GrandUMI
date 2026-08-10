import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("主页可选四种观战权限和默认手牌公开策略", async () => {
  const panel = await read("src/components/home/SpectateSettingsPanel.tsx");
  for (const text of ["自由观战", "不可观战", "仅好友观战", "密码观战", "默认不公开", "默认公开"]) {
    assert.match(panel, new RegExp(text));
  }
  assert.match(panel, /grandumi_spectate_settings/);
  assert.match(panel, /min-h-11/);
});

test("密码观战入口收集六位码并交由服务端验证", async () => {
  const button = await read("src/components/home/SpectateJoinButton.tsx");
  const protocol = await read("src/net/HomeProtocol.ts");
  assert.match(protocol, /spectateCode: spectateCode\?\.trim\(\)/);
  assert.match(button, /code\.length !== 6/);
  assert.match(button, /normalizedMode === "closed"/);
  assert.match(button, /normalizedMode === "friends" && !isFriend/);
  assert.match(button, /env\(safe-area-inset-bottom\)/);
});

test("手牌申请、审批、冷却与踢出都有局内交互", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");
  const protocol = await read("src/net/GameProtocol.ts");
  for (const text of ["申请查看主视角手牌", "等待玩家同意", "同意公开", "确认踢出"]) {
    assert.match(panel, new RegExp(text));
  }
  assert.match(panel, /observerHandRequestStatus !== "idle"/);
  assert.match(protocol, /case "MsgSpectatorKicked"/);
  assert.match(protocol, /setNavigateTo\("\/home"\)/);
});

test("局内新增固定控件使用安全区变量", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");
  assert.match(panel, /var\(--layout-safe-left, env\(safe-area-inset-left\)\)/);
  assert.match(panel, /var\(--layout-safe-bottom, env\(safe-area-inset-bottom\)\)/);
});
