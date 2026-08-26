import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("检索排序可以按默认顺序直接放回", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /prompt\.kind === "ReorderToDeckBottom"/);
  assert.match(source, /prompt\.extra\?\.allowDefaultOrder === true/);
  assert.match(source, /确定（按默认顺序放回）/);
  assert.match(source, /onClick=\{handleSkip\}/);
  assert.match(source, /min-h-12/);
});

test("对手调度起始手牌时显示重要对战信息", async () => {
  const source = await readSource("../src/components/game/MulliganOverlay.tsx");

  assert.match(source, /opp\?\.mulliganDone === true && opp\.hasReDraw === false/);
  assert.match(source, /showMessage\("重要对战信息：对手已调度起始手牌", "warn"\)/);
  assert.match(source, /role="status"/);
  assert.match(source, /重要对战信息：对手已调度起始手牌/);
});

test("调度提示与按钮适配手机安全区和旋转缩放后的44像素触控区", async () => {
  const source = await readSource("../src/components/game/MulliganOverlay.tsx");

  assert.match(source, /var\(--layout-safe-left,0px\)/);
  assert.match(source, /var\(--layout-safe-right,0px\)/);
  assert.match(source, /var\(--layout-safe-top,0px\)/);
  assert.match(source, /var\(--layout-safe-bottom,0px\)/);
  assert.equal(source.match(/min-h-12 min-w-28/g)?.length, 2);
  assert.match(source, /overflow-x-auto/);
});

test("个人设置可调整卡牌大小与动画速度并持久化", async () => {
  const [store, settings, responsive] = await Promise.all([
    readSource("../src/store/settingsStore.ts"),
    readSource("../src/components/home/SettingsModal.tsx"),
    readSource("../src/hooks/useResponsive.ts"),
  ]);

  assert.match(store, /CardSizePreference = "auto" \| "sm" \| "md" \| "lg"/);
  assert.match(store, /AnimationSpeed = "off" \| "fast" \| "standard"/);
  assert.match(store, /localStorage\.setItem\(KEY/);
  assert.match(settings, /卡牌显示/);
  assert.match(settings, /对局动画/);
  assert.ok((settings.match(/min-h-12/g) ?? []).length >= 5);
  assert.match(responsive, /preferredSize === "auto"/);
});

test("手牌拖动只改变本地显示顺序并保留服务端下标", async () => {
  const [hand, snapshot] = await Promise.all([
    readSource("../src/components/game/HandArea.tsx"),
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
  ]);

  assert.match(snapshot, /handCardIds = asSelf \|\| revealHand/);
  assert.match(hand, /const serverIndex = displayIndices\[i\]/);
  assert.match(hand, /draggable=\{side === "my"/);
  assert.match(hand, /onPointerMove=/);
  assert.match(hand, /GameRequest\.playCounterFromHand\(i\)/);
  assert.match(hand, /onClick=\{\(\) => handleClick\(serverIndex\)\}/);
});

test("反击阶段存在服务器选牌提示时不会误把查看手牌当成反击", async () => {
  const hand = await readSource("../src/components/game/HandArea.tsx");

  assert.match(hand, /const pendingPrompt = useGameStore\(\(s\) => s\.pendingPrompt\)/);
  assert.match(
    hand,
    /phase === "Counter" && isDefender && pendingPrompt === null/,
  );
});

test("P-117 奈美组卡器只允许加入东海特征卡牌", async () => {
  const store = await readSource("../src/store/deckStore.ts");

  assert.match(store, /leader\.number === "P-117"/);
  assert.match(store, /card\.keyWords\.includes\("东海"\)/);
  assert.match(store, /const leaderRule = isCardAllowedByLeaderRule\(s\.leader, card\)/);
});

test("电脑与手机都显示全屏按钮且触控区不少于44像素", async () => {
  const [route, fullscreen] = await Promise.all([
    readSource("../src/components/home/LayoutPreviewRoute.tsx"),
    readSource("../src/components/game/MobileFullscreenButton.tsx"),
  ]);

  assert.match(route, /<MobileFullscreenButton \/>/);
  assert.doesNotMatch(route, /isPhonePortrait && <MobileFullscreenButton/);
  assert.match(fullscreen, /var\(--layout-safe-right/);
  assert.match(fullscreen, /h-12 w-12/);
});

test("主页我的页面提供手机可点击的反馈入口并复用大厅反馈窗口", async () => {
  const [home, main, profile, feedback] = await Promise.all([
    readSource("../src/app/home/HomeClient.tsx"),
    readSource("../src/components/home/MainPanel.tsx"),
    readSource("../src/components/home/ProfilePanel.tsx"),
    readSource("../src/components/game/FeedbackOverlay.tsx"),
  ]);

  assert.match(home, /feedbackOpenRequest/);
  assert.match(home, /<MainPanel onOpenFeedback=/);
  assert.match(home, /<FeedbackOverlay context="lobby" openRequest=\{feedbackOpenRequest\}/);
  assert.match(main, /onOpenFeedback=\{onOpenFeedback\}/);
  assert.match(profile, /data-testid="profile-feedback-button"/);
  assert.match(profile, /onClick=\{onOpenFeedback\}/);
  assert.match(profile, /反馈 Bug 和建议/);
  assert.match(profile, /min-h-20/);
  assert.match(feedback, /context === "lobby" \? "问题反馈" : "游戏反馈（F）"/);
});

test("反馈窗口适配手机安全区且主要操作触控区不少于44像素", async () => {
  const feedback = await readSource("../src/components/game/FeedbackOverlay.tsx");

  for (const side of ["top", "right", "bottom", "left"]) {
    assert.match(feedback, new RegExp(`var\\(--layout-safe-${side}`));
  }
  assert.match(feedback, /max-h-full/);
  assert.match(feedback, /aria-modal="true"/);
  assert.ok((feedback.match(/min-h-11/g) ?? []).length >= 3);
  assert.match(feedback, /min-w-11/);
});

test("问题反馈提示要求使用卡牌编号并提供三语翻译", async () => {
  const [feedback, i18n] = await Promise.all([
    readSource("../src/components/game/FeedbackOverlay.tsx"),
    readSource("../src/i18n/core.mjs"),
  ]);
  const gamePrompt =
    "描述触发 Bug 的操作、实际现象和期望结果；提及卡牌时请勿使用俗称，必须使用卡牌编号（如 OP01-006）……提交时会自动附带当前对局信息。";
  const lobbyPrompt =
    "描述大厅中触发 Bug 的操作、实际现象和期望结果；提及卡牌时请勿使用俗称，必须使用卡牌编号（如 OP01-006）……提交时会自动附带当前页面信息。";

  assert.ok(feedback.includes(gamePrompt));
  assert.ok(feedback.includes(lobbyPrompt));
  assert.ok(i18n.includes(`"${gamePrompt}": "Describe`));
  assert.ok(i18n.includes(`"${lobbyPrompt}": "Describe`));
  assert.ok(i18n.includes(`"${gamePrompt}": "不具合`));
  assert.ok(i18n.includes(`"${lobbyPrompt}": "ロビー`));
});

test("对局反馈统一从更多菜单进入且弹窗挂到旋转宿主并高于公告", async () => {
  const [page, board, chat, menu, feedback] = await Promise.all([
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/game/GameChatPanel.tsx"),
    readSource("../src/components/game/GameMenu.tsx"),
    readSource("../src/components/game/FeedbackOverlay.tsx"),
  ]);

  assert.match(page, /feedbackOpenRequest/);
  assert.match(page, /<FeedbackOverlay[\s\S]*?context="game"[\s\S]*?openRequest=\{feedbackOpenRequest\}[\s\S]*?showTrigger=\{false\}/);
  assert.match(page, /onOpenFeedback=/);
  assert.doesNotMatch(board, /F · 反馈 Bug 和建议/);
  assert.match(chat, /<GameMenu/);
  assert.match(menu, /反馈 Bug \/ 建议/);
  assert.match(menu, /onOpenFeedback\(\)/);
  assert.match(feedback, /<GameOverlayPortal>/);
  assert.doesNotMatch(feedback, /createPortal|document\.body/);
  assert.match(feedback, /pointer-events-auto fixed inset-0/);
  assert.match(feedback, /z-\[100\]/);
});

test("公开牌浮层不会因普通快照或卡图映射变化反复重置计时器", async () => {
  const reveal = await readSource("../src/components/game/RevealOverlay.tsx");

  assert.match(reveal, /useGameStore\.getState\(\)/);
  assert.match(reveal, /\[animationSpeed, reveal\?\.nonce, clearReveal\]/);
  assert.doesNotMatch(reveal, /\breveal, clearReveal/);
  assert.doesNotMatch(reveal, /mySpriteMap, opponentSpriteMap/);
});

test("同赛季更旧的排位快照不会覆盖刚完成的结算", async () => {
  const store = await readSource("../src/store/netStore.ts");
  const snapshotState = await readSource("../src/lib/rankSnapshotState.ts");

  assert.match(store, /const current = state\.rankProfiles\[mode\]/);
  assert.match(store, /shouldReplaceRankProfile\(current, rankProfile\)/);
  assert.match(store, /return \{\};/);
  assert.match(snapshotState, /current\.seasonId === incoming\.seasonId/);
  assert.match(snapshotState, /incoming\.games >= current\.games/);
});
