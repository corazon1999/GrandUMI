import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("构筑检索支持仅显示拥有触发效果的卡牌", async () => {
  const [panel, search, store] = await Promise.all([
    readSource("../src/components/deck-editor/SearchPanel.tsx"),
    readSource("../src/lib/cardSearch.ts"),
    readSource("../src/store/deckStore.ts"),
  ]);
  assert.match(panel, /仅显示拥有【触发】的卡/);
  assert.match(search, /filterHasTrigger && !card\.trigger\.trim\(\)/);
  assert.match(store, /setFilterHasTrigger/);
});

test("移动端反馈入口与聊天入口分置于安全区两侧", async () => {
  const feedback = await readSource("../src/components/game/FeedbackOverlay.tsx");
  assert.match(feedback, /--layout-safe-right/);
  assert.match(feedback, /min-h-11 min-w-11/);
});

test("卡图失败时显示效果文字且卡牌详情有明确关闭按钮", async () => {
  const [item, zoom] = await Promise.all([
    readSource("../src/components/ui/CardItem.tsx"),
    readSource("../src/components/ui/CardZoomOverlay.tsx"),
  ]);
  assert.match(item, /card!\.effectEvent \|\| card!\.trigger/);
  assert.match(zoom, /卡图暂不可用/);
  assert.match(zoom, /aria-label="关闭卡牌详情"/);
  assert.match(zoom, /min-h-11 min-w-11/);
});

test("对局同时显示六分钟回合时钟、一次加时与总操作时钟", async () => {
  const [board, store, types] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);
  assert.match(board, /回合 \{formatOperationTime\(turnRemaining\)\}/);
  assert.match(board, /总计 \{formatOperationTime\(totalRemaining\)\}/);
  assert.match(store, /myTurnOperationTimeMs: 360_000/);
  assert.match(store, /myTurnExtensionUsed: false/);
  assert.match(board, /加时 \+2:00/);
  assert.match(types, /opponentTurnOperationTimeMs\?: number/);
});

test("贴咚不再计时提交，并在下一项操作前提供撤回按钮", async () => {
  const [request, actions, protocol, store] = await Promise.all([
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/net/GameProtocol.ts"),
    readSource("../src/store/gameStore.ts"),
  ]);
  assert.doesNotMatch(request, /expiresAt|setTimeout\([^)]*commitPendingAttachDon/);
  assert.match(request, /pendingAttachDonUndoQueue\.push/);
  assert.match(request, /commitPendingAttachDonUndo\(\)/);
  assert.match(request, /if \(action !== "AttachDon" && !commitPendingAttachDonUndo\(\)\) return false/);
  assert.match(request, /rollbackOptimistic\(\)/);
  assert.match(actions, /getPendingAttachDonUndo/);
  assert.match(actions, />\s*撤回贴咚\s*</);
  assert.match(actions, /执行下一项操作后将无法撤回/);
  assert.match(actions, /rotateQuarterTurn \? "min-h-\[5\.75rem\]" : "min-h-12"/);
  assert.match(protocol, /reapplyPendingAttachDonOptimistic\(\)/);
  assert.match(store, /const powerBonus = s\.currentTurn \? actual \* 1_000 : 0/);
  assert.match(store, /s\.my\.leaderPower \+= powerBonus/);
  assert.match(store, /target\.powerCurrent \+= powerBonus/);
});

test("在线玩家、好友与局内对手均提供屏蔽和举报入口", async () => {
  const [actions, players, friends, gamePage] = await Promise.all([
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
    readSource("../src/components/home/PlayerListPanel.tsx"),
    readSource("../src/components/home/FriendsPanel.tsx"),
    readSource("../src/app/game/page.tsx"),
  ]);
  assert.match(actions, /确认屏蔽/);
  assert.match(actions, /提交举报/);
  assert.match(players, /<PlayerSafetyActions/);
  assert.match(friends, /解除屏蔽/);
  assert.match(gamePage, /currentOpponent compact toolbar/);
});
