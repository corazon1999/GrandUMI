import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("局内更多菜单提供可触控的举报入口，观战和回放不显示", async () => {
  const [page, chat, menu, actions] = await Promise.all([
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/GameChatPanel.tsx"),
    readSource("../src/components/game/GameMenu.tsx"),
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
  ]);

  assert.match(page, /!isObserver && !isPlayback/);
  assert.match(chat, /playerToolsEnabled=\{!isObserver\}/);
  assert.match(chat, /<GameMenu/);
  assert.match(menu, /<PlayerSafetyActions[\s\S]*?currentOpponent/);
  assert.match(menu, /举报对手/);
  assert.match(actions, /aria-label=\{`举报玩家 \$\{targetName\}`\}/);
  assert.ok((actions.match(/min-h-12 min-w-12/g) ?? []).length >= 2);
});

test("举报分类覆盖对局反馈中的交流、公平性、拖延和刷屏问题", async () => {
  const [actions, types] = await Promise.all([
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  for (const category of ["harassment", "stalling", "cheating", "spam", "other"]) {
    assert.match(actions, new RegExp(`value: "${category}"`));
    assert.match(types, new RegExp(`"${category}"`));
  }
  assert.match(actions, /恶意拖延或挂机/);
  assert.match(actions, /疑似作弊或利用漏洞/);
  assert.match(actions, /minLength=\{2\}/);
  assert.match(actions, /useLayoutQuarterTurn/);
  assert.match(actions, /grid grid-cols-2 items-start gap-3/);
  assert.match(actions, /系统会自动附带本局编号、回合、阶段、计时与最近局内聊天/);
});

test("举报弹窗适配安全区，服务端校验固定分类并记录权威对局上下文", async () => {
  const [modal, bridge, store] = await Promise.all([
    readSource("../src/components/ui/Modal.tsx"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../../服务端WebSocket/Persistence/PlayerDataStore.cs"),
  ]);

  assert.match(modal, /--layout-safe-top/);
  assert.match(modal, /--layout-safe-bottom/);
  assert.match(modal, /--layout-safe-left/);
  assert.match(modal, /--layout-safe-right/);
  assert.match(bridge, /source = "active_match"/);
  assert.match(bridge, /operationTurnClockRemainingMs/);
  assert.match(bridge, /recentGameChat = SnapshotGameChatEvidence/);
  assert.match(bridge, /reporterSeat is 0 or 1/);
  assert.match(bridge, /source = "recent_match"/);
  assert.match(store, /ValidPlayerReportCategories/);
  assert.match(store, /DuplicatePlayerReportWindow/);
});
