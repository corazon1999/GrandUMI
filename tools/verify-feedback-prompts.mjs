import assert from "node:assert/strict";
import path from "node:path";

/** 使用真实游戏页面验证私密看牌、顶底选择和终局关闭成本框。 */
export async function verifyFeedbackPrompts(page, baseUrl, viewport) {
  const current = await page.context().newPage();
  const errors = [];
  current.on("pageerror", error => errors.push(error.message));
  const player = name => ({ name, cardBackId: "classic", spriteMap: {}, leaderId: name,
    leaderNumber: "OP15-001", leaderPower: 5000, leaderAttachedDon: 0, leaderGainedKeywords: [],
    handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCount: 0,
    fieldCards: [], stages: [], trashNumbers: [], lifeNumbers: [], lifeFaceUp: [],
    deckCount: 40, lifeCount: 4, costActive: 2, costRest: 0, costAttached: 0,
    donDeckCount: 8, mulliganDone: true });
  const state = { proto: "MsgGameState", roomId: "feedback-prompts-verification", tick: 1,
    my: player("反馈验证玩家"), opponent: player("反馈验证对手"), viewerKind: "player",
    phase: "Main", currentTurn: true, turnCount: 3, firstPlayer: 0, firstPlayerChosen: true,
    openingStage: "Completed", mulliganBothDone: true, isGameOver: false, matchKind: "CasualStandard",
    pendingPrompt: { promptId: "look-life", operationId: "look-life", kind: "LookLife",
      text: "确认所选生命区最上方的卡牌", validChoices: [], minChoose: 0, maxChoose: 0,
      extra: { sourceNumber: "ST20-003", choiceCards: [{ id: "private-life", number: "OP15-004" }] } } };
  const actions = [];
  let socket;
  // 只替换游戏连接，开发服务器的热更新连接继续正常工作。
  await current.routeWebSocket("**/ws", connection => {
    socket = connection;
    connection.onMessage(raw => {
      const message = JSON.parse(String(raw));
      if (message.proto === "MsgSecret") {
        connection.send(JSON.stringify({ proto: "MsgSecret", result: true }));
        connection.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "feedback-fixture",
          name: "反馈验证玩家", decks: [], authToken: "feedback-fixture-token" }));
      } else if (message.proto === "MsgPing") connection.send(JSON.stringify(message));
      else if (message.proto === "MsgRequestState") connection.send(JSON.stringify(state));
      else if (message.proto === "MsgPromptResponse") {
        actions.push(message);
        if (message.promptId === "look-life") {
          state.pendingPrompt = { promptId: "position-life", kind: "Option", text: "将该卡放置到生命区最上方还是最下方？",
            validChoices: ["0", "1"], minChoose: 1, maxChoose: 1, extra: { options: ["最上方", "最下方"] } };
        } else {
          state.pendingPrompt = { promptId: "cost-selection", kind: "ReturnOwnDon", text: "选择效果成本",
            validChoices: ["don-1"], minChoose: 1, maxChoose: 1,
            extra: { donChoices: [{ id: "don-1", state: "Active" }] } };
        }
        connection.send(JSON.stringify({ ...state, tick: ++state.tick }));
      }
    });
  });
  try {
    await current.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
    await current.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
    socket.send(JSON.stringify({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "反馈验证对手" }));
    socket.send(JSON.stringify({ proto: "MsgGameStart", MainDeck: "OP15-001" }));
    socket.send(JSON.stringify(state));
    await current.waitForURL(`${baseUrl}/game`);
    if (viewport.width < 600)
      await current.locator('[data-layout-preview][data-layout-rotated="true"]').waitFor();
    const lookText = current.getByText("确认所选生命区最上方的卡牌", { exact: true });
    await lookText.waitFor();
    const confirm = current.getByRole("button", { name: "确认（已选 0 / 0）", exact: true });
    const bounds = await confirm.boundingBox();
    assert.ok(bounds && bounds.width >= 44 && bounds.height >= 44 && bounds.x >= -1 && bounds.y >= -1
      && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1,
    `看牌确认按钮超出视口或不足44像素：${JSON.stringify(bounds)}`);
    const image = current.locator('img[src*="OP15-004"]').last();
    await image.waitFor({ state: "visible" });
    await current.waitForFunction(() => [...document.querySelectorAll('img[src*="OP15-004"]')]
      .some(element => element.complete && element.naturalWidth > 0));
    // 截图等开局与回合提示的进场动画结束，避免瞬态画面遮住牌面。
    await current.waitForTimeout(2400);
    if (process.env.GRANDUMI_TEST_TEMP_ROOT)
      await current.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `feedback-look-${viewport.width}.png`) });
    assert.equal(actions.length, 0, "看牌确认前不得提前替玩家选择顶底");
    await confirm.click();
    const bottom = current.getByRole("button", { name: "最下方", exact: true });
    await bottom.waitFor();
    await bottom.click();
    await current.getByText("选择效果成本", { exact: true }).waitFor();
    assert.equal(actions.length, 2);
    assert.deepEqual(actions[0].chosen ?? actions[0].choices, []);
    state.isGameOver = true;
    state.winnerIsMe = true;
    state.gameOverReason = "反馈验证对手 投降";
    // 故意保留旧成本提示，验证客户端以终局为准清理。
    socket.send(JSON.stringify({ ...state, tick: ++state.tick }));
    await current.getByText("选择效果成本", { exact: true }).waitFor({ state: "hidden" });
    const board = current.getByRole("button", { name: "查看牌桌", exact: true });
    await board.waitFor();
    await current.waitForTimeout(1200);
    const exitBounds = await board.boundingBox();
    assert.ok(exitBounds && exitBounds.width >= 44 && exitBounds.height >= 44
      && exitBounds.x >= -1 && exitBounds.y >= -1
      && exitBounds.x + exitBounds.width <= viewport.width + 1 && exitBounds.y + exitBounds.height <= viewport.height + 1,
    `终局操作按钮超出视口或不足44像素：${JSON.stringify(exitBounds)}`);
    if (process.env.GRANDUMI_TEST_TEMP_ROOT)
      await current.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `feedback-game-over-${viewport.width}.png`) });
    await board.click();
    assert.equal(actions.length, 2, "投降关闭提示不能发送伪造成本响应");
    console.log(`反馈看牌与终局交互通过：${viewport.width}×${viewport.height}`);
  } catch (error) {
    if (process.env.GRANDUMI_TEST_TEMP_ROOT)
      await current.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `feedback-failure-${viewport.width}.png`) });
    throw new Error(`${error.message}\n页面错误：${errors.join("；")}\n页面状态：${(await current.locator("body").innerText()).slice(0, 800)}`);
  } finally {
    await current.close();
  }
}
