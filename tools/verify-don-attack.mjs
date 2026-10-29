import assert from "node:assert/strict";
import path from "node:path";

/** 通过真实对局页与WebSocket回包检查贴咚后的连续攻击操作。 */
export async function verifyDonAttack(page, baseUrl, viewport) {
  // 专项检查按钮和请求链，关闭演出以免回合横幅遮住截图。
  await page.addInitScript(() => localStorage.setItem("grandumi_settings", JSON.stringify({
    animationSpeed: "off", visualEffectsEnabled: false, visualEffectsDefaultsVersion: 1,
  })));
  const player = (id) => ({ name: "贴咚验证玩家", cardBackId: "classic", spriteMap: {},
    leaderId: id, leaderNumber: "OP01-001", leaderPower: 5000, leaderAttachedDon: 0,
    leaderCanAttack: true, leaderTapped: false, leaderGainedKeywords: [],
    handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCardCanPlay: [], handCount: 0,
    fieldCards: [], stages: [], trashNumbers: [], lifeNumbers: [], lifeFaceUp: [],
    deckCount: 30, lifeCount: 4, costActive: 4, costRest: 0, costAttached: 0, donDeckCount: 6, mulliganDone: true });
  const character = { id: "don-character", number: "OP01-016", isTapped: false,
    powerCurrent: 2000, cost: 1, attachedDon: 0, gainedKeywords: [], effectsNullified: false,
    turnPlayed: 1, canAttack: true, canActivateEffect: false, oncePerTurnEffectAvailable: false };
  const state = { proto: "MsgGameState", roomId: "don-attack-verification", tick: 1,
    my: { ...player("don-leader"), fieldCards: [{ ...character }] }, opponent: player("don-opponent"),
    viewerKind: "player", phase: "Main", currentTurn: true, turnCount: 3,
    firstPlayer: 0, firstPlayerChosen: true, openingStage: "Completed", mulliganBothDone: true,
    isGameOver: false, matchKind: "CasualStandard", pendingPrompt: null, battle: null };
  let socket;
  const actions = [];
  const errors = [];
  const send = (lastAction, requestId) => socket.send(JSON.stringify({ ...state, tick: ++state.tick, lastAction, requestId }));
  page.on("pageerror", (error) => errors.push(error.message));
  await page.routeWebSocket("**/ws", (connection) => {
    socket = connection;
    connection.onMessage((raw) => {
      const message = JSON.parse(String(raw));
      if (message.proto === "MsgSecret") {
        connection.send(JSON.stringify({ proto: "MsgSecret", result: true }));
        connection.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "don-fixture",
          name: "贴咚验证玩家", decks: [], authToken: "don-fixture-token" }));
      } else if (message.proto === "MsgPing") connection.send(JSON.stringify(message));
      else if (message.proto === "MsgRequestState") send("RequestState");
      else if (message.proto === "MsgGameAction" && message.action === "AttachDon") {
        actions.push(message);
        const { count, targetId } = message.data;
        state.my.costActive -= count;
        state.my.costAttached += count;
        if (targetId === "leader" || targetId === state.my.leaderId) {
          state.my.leaderAttachedDon += count;
          state.my.leaderPower += count * 1000;
        } else {
          state.my.fieldCards[0].attachedDon += count;
          state.my.fieldCards[0].powerCurrent += count * 1000;
        }
        Object.assign(state, { canUndoAttachDon: true, undoAttachDonOperationId: "don-undo",
          undoAttachDonCount: count, undoAttachDonDepth: 1 });
        send("AttachDon", message.requestId);
      } else if (message.proto === "MsgGameAction" && ["Attack", "PlayCard", "PlayCounter"].includes(message.action)) {
        actions.push(message);
        state.canUndoAttachDon = false;
        send(message.action, message.requestId);
      }
    });
  });
  const checkButton = async (button) => {
    await button.scrollIntoViewIfNeeded();
    await page.waitForFunction((element) => {
      const rect = element.getBoundingClientRect();
      return !element.disabled && element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
    }, await button.elementHandle(), { timeout: 5000 });
    const box = await button.boundingBox();
    assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1
      && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
    `贴咚后操作按钮越界或触控区不足：${JSON.stringify(box)}`);
    assert.ok(await button.evaluate((element) => {
      const rect = element.getBoundingClientRect();
      return element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
    }), "贴咚后操作按钮被聊天等常驻控件遮挡");
  };
  try {
    await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
    socket.send(JSON.stringify({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "贴咚验证对手" }));
    socket.send(JSON.stringify({ proto: "MsgGameStart", MainDeck: "OP01-001" }));
    send("GameStart");
    await page.waitForURL(`${baseUrl}/game`);
    if (viewport.width < 600) await page.locator('[data-layout-preview][data-layout-rotated="true"]').waitFor();
    for (const target of ["don-leader", "don-character"]) {
      actions.length = 0;
      state.my = { ...player("don-leader"), fieldCards: [{ ...character }] };
      state.canUndoAttachDon = false;
      send("RequestState");
      await page.locator(`[data-battle-card-id="${target}"]`).click();
      const attack = page.getByRole("button", { name: "攻击", exact: true });
      await attack.waitFor();
      const attach = page.locator('[data-game-context-actions] button[title="赋予 1 张咚!!"]');
      await attach.click();
      const undo = page.getByRole("button", { name: "撤回贴咚", exact: true });
      await undo.waitFor();
      await attack.waitFor();
      await checkButton(attack);
      await checkButton(undo);
      await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT,
        `don-attack-${target}-${viewport.width}.png`) });
      await attack.click();
      await page.locator('[data-battle-card-id="don-opponent"]').click();
      await page.waitForFunction(() => document.querySelector('[data-game-context-actions]')
        && ![...document.querySelectorAll('button')].some((button) => button.textContent.trim() === "取消攻击"));
      const declared = actions.find((message) => message.action === "Attack");
      assert.equal(declared?.data.attackerId, target, "贴咚后攻击必须使用原选中的实例");
    }
    // 万雷反馈未包含实际出牌请求；核对零费双用途事件的主要确认链可以提交。
    state.my.handCardIds = ["don-event"];
    state.my.handCardNumbers = ["OP15-078"];
    state.my.handCardCosts = [0]; state.my.handCardCounters = [0];
    state.my.handCardCanPlay = [true]; state.my.handCount = 1;
    send("RequestState");
    await page.locator('[data-hand-order-id="don-event"]').click();
    await page.getByRole("button", { name: "出牌", exact: true }).click();
    const confirm = page.getByRole("button", { name: "确认打出", exact: true });
    await confirm.waitFor();
    await checkButton(page.getByRole("button", { name: "取消", exact: true }));
    await checkButton(confirm);
    await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `event-main-confirm-${viewport.width}.png`) });
    await confirm.click();
    await confirm.waitFor({ state: "hidden" });
    assert.ok(actions.some((message) => message.action === "PlayCard" && message.data.handIndex === 0));
    state.phase = "Counter"; state.currentTurn = false;
    state.battle = { attackerPlayer: 1, attackerCardId: "don-opponent", targetIsLeader: true,
      targetCardId: null, blockerCardId: null, attackerBonus: 0, defenderBonus: 0 };
    send("Attack");
    await page.locator('[data-hand-order-id="don-event"]').click();
    await page.getByText("确认使用反击事件", { exact: true }).waitFor();
    await checkButton(confirm);
    await checkButton(page.getByRole("button", { name: "取消", exact: true }));
    await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `event-counter-confirm-${viewport.width}.png`) });
    await confirm.click();
    await confirm.waitFor({ state: "hidden" });
    assert.ok(actions.some((message) => message.action === "PlayCounter" && message.data.handIndex === 0));
    assert.deepEqual(errors, []);
    console.log(`贴咚后领袖与角色连续攻击、零费主要及反击事件确认通过：${viewport.width}×${viewport.height}`);
  } catch (error) {
    await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `don-attack-failed-${viewport.width}.png`) });
    throw error;
  } finally {
    await page.close();
  }
}
