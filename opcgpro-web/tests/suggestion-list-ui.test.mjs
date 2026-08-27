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
  assert.match(zoom, /min-h-12 min-w-12/);
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

test("贴咚确认后立即提交，并由服务端令牌控制撤回、拒绝回滚与重连恢复", async () => {
  const [request, actions, protocol, store, types] = await Promise.all([
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/net/GameProtocol.ts"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);
  assert.doesNotMatch(request, /expiresAt|setTimeout\([^)]*commitPendingAttachDon/);
  assert.match(request, /function submitAttachDon/);
  assert.match(request, /"AttachDon",\s*\{ targetId, count: safeCount \}/);
  assert.match(request, /optimisticAttachDon\(targetId, safeCount\)/);
  assert.doesNotMatch(request, /pendingAttachDonUndoQueue|commitPendingAttachDonUndo|undoLastPendingAttachDon/);
  assert.match(request, /send\("UndoAttachDon", \{ operationId \}\)/);
  assert.match(request, /rollbackOptimistic\(\)/);
  assert.match(request, /requestState:[\s\S]*rollbackOptimistic\(\)[\s\S]*MsgRequestState/);
  assert.doesNotMatch(actions, /getPendingAttachDonUndo/);
  assert.match(actions, /useGameStore\(\(s\) => s\.canUndoAttachDon\)/);
  assert.match(actions, /useGameStore\(\(s\) => s\.undoAttachDonOperationId\)/);
  assert.match(actions, /GameRequest\.undoAttachDon\(undoAttachDonOperationId\)/);
  assert.match(actions, /撤回贴咚/);
  assert.match(actions, /执行其他对局操作后将无法撤回/);
  assert.match(protocol, /case "MsgActionRejected":[\s\S]*rollbackOptimistic\(\)/);
  assert.doesNotMatch(protocol, /reapplyPendingAttachDonOptimistic/);
  assert.match(store, /s\.canUndoAttachDon = msg\.canUndoAttachDon \?\? false/);
  assert.match(store, /s\.undoAttachDonOperationId = msg\.undoAttachDonOperationId \?\? null/);
  assert.match(store, /s\.undoAttachDonCount = msg\.undoAttachDonCount \?\? 0/);
  assert.match(store, /s\.undoAttachDonDepth = msg\.undoAttachDonDepth \?\? 0/);
  assert.match(types, /canUndoAttachDon\?: boolean/);
  assert.match(types, /undoAttachDonOperationId\?: string \| null/);
  assert.match(store, /const powerBonus = s\.currentTurn \? actual \* 1_000 : 0/);
  assert.match(store, /s\.my\.leaderPower \+= powerBonus/);
  assert.match(store, /target\.powerCurrent \+= powerBonus/);
});

test("在线玩家、好友与局内对手均提供屏蔽和举报入口", async () => {
  const [actions, players, friends, gameMenu] = await Promise.all([
    readSource("../src/components/ui/PlayerSafetyActions.tsx"),
    readSource("../src/components/home/PlayerListPanel.tsx"),
    readSource("../src/components/home/FriendsPanel.tsx"),
    readSource("../src/components/game/GameMenu.tsx"),
  ]);
  assert.match(actions, /确认屏蔽/);
  assert.match(actions, /提交举报/);
  assert.match(players, /<PlayerSafetyActions/);
  assert.match(friends, /解除屏蔽/);
  assert.match(gameMenu, /<PlayerSafetyActions[\s\S]*?currentOpponent[\s\S]*?renderActions=/);
  assert.match(gameMenu, /屏蔽对手/);
  assert.match(gameMenu, /举报对手/);
});
