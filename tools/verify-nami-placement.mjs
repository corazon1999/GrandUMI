import assert from "node:assert/strict";
import path from "node:path";

/** 验证真实对局页面中的奈美同端放置、选牌顺序及旋转画布触控区。 */
export async function verifyNamiPlacement(page, baseUrl, viewport) {
  const current = page;
  const player = name => ({ name, cardBackId: "classic", spriteMap: {}, leaderId: name,
    leaderNumber: "OP06-001", leaderPower: 5000, leaderAttachedDon: 0, leaderGainedKeywords: [],
    handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCount: 0,
    fieldCards: [], stages: [], trashNumbers: [], lifeNumbers: [], lifeFaceUp: [],
    deckCount: 30, lifeCount: 4, costActive: 0, costRest: 5, costAttached: 0,
    donDeckCount: 5, mulliganDone: true });
  const choices = [{ id: "nami-first", number: "OP15-006" }, { id: "nami-second", number: "OP15-007" }];
  const state = { proto: "MsgGameState", roomId: "nami-placement-verification", tick: 1,
    my: { ...player("奈美验证玩家"), handCardIds: choices.map(c => c.id),
      handCardNumbers: choices.map(c => c.number), handCount: 2, handCardCosts: [1, 1], handCardCounters: [1000, 1000] },
    opponent: player("奈美验证对手"), viewerKind: "player", phase: "Main", currentTurn: true,
    turnCount: 3, firstPlayer: 0, firstPlayerChosen: true, openingStage: "Completed",
    mulliganBothDone: true, isGameOver: false, matchKind: "CasualStandard" };
  const handPrompt = (id, text, cards) => ({ promptId: id, kind: "OwnHand", text,
    validChoices: cards.map(c => c.id), minChoose: 1, maxChoose: 1,
    extra: { sourceNumber: "OP11-054", choiceCards: cards } });
  const actions = [];
  const errors = [];
  let socket;
  let end = "0";
  current.on("pageerror", error => errors.push(error.message));
  await current.routeWebSocket("**/ws", connection => {
    socket = connection;
    connection.onMessage(raw => {
      const message = JSON.parse(String(raw));
      if (message.proto === "MsgSecret") {
        connection.send(JSON.stringify({ proto: "MsgSecret", result: true }));
        connection.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "nami-fixture",
          name: "奈美验证玩家", decks: [], authToken: "nami-fixture-token" }));
      } else if (message.proto === "MsgPing") connection.send(JSON.stringify(message));
      else if (message.proto === "MsgRequestState") connection.send(JSON.stringify(state));
      else if (message.proto === "MsgPromptResponse") {
        actions.push(message);
        if (message.promptId === "nami-first-pick") {
          state.pendingPrompt = { promptId: "nami-direction", kind: "Option",
            text: "第 1/2 张：选择两张手牌放回卡组的方向。", validChoices: ["0", "1"], minChoose: 1, maxChoose: 1,
            extra: { sourceNumber: "OP11-054", options: [
              "两张一起放到牌顶：第 1 张最上，第 2 张在其下方",
              "两张一起放到牌底：第 1 张在第 2 张上方，第 2 张最下" ] } };
        } else if (message.promptId === "nami-direction") {
          end = (message.chosen ?? message.choices)[0];
          state.my.handCardIds = [choices[1].id];
          state.my.handCardNumbers = [choices[1].number];
          state.my.handCount = 1;
          state.pendingPrompt = handPrompt("nami-second-pick",
            `第 2/2 张：选择要放回卡组的手牌。两张一起放牌${end === "0" ? "顶" : "底"}，本张位于第 1 张下方。`, [choices[1]]);
        } else {
          state.pendingPrompt = null;
          state.my.handCardIds = [];
          state.my.handCardNumbers = [];
          state.my.handCount = 0;
        }
        connection.send(JSON.stringify({ ...state, tick: ++state.tick }));
      }
    });
  });
  async function checkButton(button) {
    const bounds = await button.boundingBox();
    assert.ok(bounds && bounds.width >= 44 && bounds.height >= 44 && bounds.x >= -1 && bounds.y >= -1
      && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1,
    `奈美按钮不可见或触控区不足：${JSON.stringify(bounds)}`);
  }
  try {
    await current.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
    await current.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
    socket.send(JSON.stringify({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "奈美验证对手" }));
    socket.send(JSON.stringify({ proto: "MsgGameStart", MainDeck: "OP06-001" }));
    socket.send(JSON.stringify(state));
    await current.waitForURL(`${baseUrl}/game`);
    if (viewport.width < 600)
      await current.locator('[data-layout-preview][data-layout-rotated="true"]').waitFor();
    for (const direction of ["0", "1"]) {
      actions.length = 0;
      state.my.handCardIds = choices.map(c => c.id);
      state.my.handCardNumbers = choices.map(c => c.number);
      state.my.handCount = 2;
      state.pendingPrompt = handPrompt("nami-first-pick",
        "第 1/2 张：选择要放回卡组的手牌；两张必须一起放牌顶或一起放牌底，先选的牌在较上方。", choices);
      socket.send(JSON.stringify({ ...state, tick: ++state.tick }));
      await current.locator('[data-prompt-choice-id="nami-first"]').click();
      const firstConfirm = current.getByRole("button", { name: "确认（已选 1 / 1）", exact: true });
      await checkButton(firstConfirm);
      await firstConfirm.click();
      const top = current.getByRole("button", { name: "两张一起放到牌顶：第 1 张最上，第 2 张在其下方", exact: true });
      const bottom = current.getByRole("button", { name: "两张一起放到牌底：第 1 张在第 2 张上方，第 2 张最下", exact: true });
      await top.waitFor();
      await checkButton(top);
      await checkButton(bottom);
      if (process.env.GRANDUMI_TEST_TEMP_ROOT)
        await current.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `nami-direction-${direction}-${viewport.width}.png`) });
      await (direction === "0" ? top : bottom).click();
      await current.getByText(`第 2/2 张：选择要放回卡组的手牌。两张一起放牌${direction === "0" ? "顶" : "底"}，本张位于第 1 张下方。`, { exact: true }).waitFor();
      await current.locator('[data-prompt-choice-id="nami-second"]').click();
      const secondConfirm = current.getByRole("button", { name: "确认（已选 1 / 1）", exact: true });
      await checkButton(secondConfirm);
      await secondConfirm.click();
      await secondConfirm.waitFor({ state: "hidden" });
      assert.deepEqual(actions.map(a => a.chosen ?? a.choices), [[choices[0].id], [direction], [choices[1].id]]);
      assert.equal(await top.count(), 0, "第二张不应重复出现顶底选择");
    }
    assert.deepEqual(errors, []);
    console.log(`奈美同端放置交互通过：${viewport.width}×${viewport.height}`);
  } finally {
    await current.close();
  }
}
