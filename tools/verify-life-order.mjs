import assert from "node:assert/strict";
import path from "node:path";

/** 在真实对局页验证混合正反面生命的叠放顺序与手机详情操作。 */
export async function verifyLifeOrder(page, baseUrl, viewport) {
  await page.addInitScript(() => localStorage.setItem("grandumi_settings", JSON.stringify({
    animationSpeed: "off", visualEffectsEnabled: false, visualEffectsDefaultsVersion: 1,
  })));
  const player = (id) => ({ name: "生命顺序验证", cardBackId: "classic", spriteMap: {},
    leaderId: id, leaderNumber: "OP18-079", leaderPower: 5000, leaderAttachedDon: 0,
    leaderCanAttack: true, leaderTapped: false, leaderGainedKeywords: [],
    handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCardCanPlay: [], handCount: 0,
    fieldCards: [], stages: [], trashNumbers: [], lifeNumbers: [],
    lifeFaceUp: [{ faceUp: false, number: null }, { faceUp: true, number: "OP18-115" },
      { faceUp: false, number: null }, { faceUp: false, number: null }],
    deckCount: 30, lifeCount: 4, costActive: 4, costRest: 0, costAttached: 0, donDeckCount: 6, mulliganDone: true });
  const state = { proto: "MsgGameState", roomId: "life-order-verification", tick: 1,
    my: player("life-leader"), opponent: player("life-opponent"), viewerKind: "player",
    phase: "Main", currentTurn: true, turnCount: 11, firstPlayer: 0, firstPlayerChosen: true,
    openingStage: "Completed", mulliganBothDone: true, isGameOver: false,
    matchKind: "CasualStandard", pendingPrompt: null, battle: null };
  let socket;
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  const send = () => socket.send(JSON.stringify({ ...state, tick: ++state.tick, lastAction: "RequestState" }));
  await page.routeWebSocket("**/ws", connection => {
    socket = connection;
    connection.onMessage(raw => {
      const message = JSON.parse(String(raw));
      if (message.proto === "MsgSecret") {
        connection.send(JSON.stringify({ proto: "MsgSecret", result: true }));
        connection.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "life-fixture",
          name: "生命顺序验证", decks: [], authToken: "life-fixture-token" }));
      } else if (message.proto === "MsgPing") connection.send(JSON.stringify(message));
      else if (message.proto === "MsgRequestState") send();
    });
  });
  try {
    await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
    socket.send(JSON.stringify({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "生命顺序验证对手" }));
    socket.send(JSON.stringify({ proto: "MsgGameStart", MainDeck: "OP18-079" }));
    send();
    await page.waitForURL(`${baseUrl}/game`);
    if (viewport.width < 600) await page.locator('[data-layout-preview][data-layout-rotated="true"]').waitFor();
    for (const side of ["my", "opponent"]) {
      const zone = page.locator(`[data-zone="life"][data-zone-side="${side}"]`);
      await zone.locator('[data-life-index="0"]').waitFor();
      const layers = await zone.locator("[data-life-index]").evaluateAll(elements => elements.map(element => ({
        index: Number(element.dataset.lifeIndex), z: Number(getComputedStyle(element).zIndex),
      })));
      assert.deepEqual(layers.map(layer => layer.index), [0, 1, 2, 3]);
      assert.ok(layers.every((layer, index) => index === 0 || layers[index - 1].z > layer.z),
        `正面生命覆盖了真实顶牌：${JSON.stringify(layers)}`);
      assert.ok(await zone.locator('[data-life-index="0"]').evaluate(element => {
        const rect = element.getBoundingClientRect();
        return element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
      }), "生命顶牌中心被下层正面牌或常驻控件覆盖");
      await zone.click();
      await page.getByText(`${side === "my" ? "我方" : "对手"}生命区（4 张，1 张正面）`, { exact: true }).waitFor();
      await page.getByText("最上方", { exact: true }).waitFor();
      await page.getByText("第 2 张", { exact: true }).waitFor();
      const close = page.getByRole("button", { name: "关闭", exact: true });
      const box = await close.boundingBox();
      assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1
        && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
        `生命详情关闭按钮越界或触控区不足：${JSON.stringify(box)}`);
      if (side === "my") await page.screenshot({
        path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `life-inspector-${viewport.width}.png`),
      });
      await close.click();
      await close.waitFor({ state: "hidden" });
    }
    state.my.lifeCount = 7;
    state.my.lifeFaceUp = Array.from({ length: 7 }, (_, index) => ({
      faceUp: index === 5, number: index === 5 ? "OP18-115" : null,
    }));
    send();
    const longPile = page.locator('[data-zone="life"][data-zone-side="my"]');
    await page.getByRole("button", { name: "查看我方生命区，1 张正面牌", exact: true }).waitFor();
    assert.equal(await longPile.locator("[data-life-index]").count(), 5);
    await longPile.click();
    await page.getByText("我方生命区（7 张，1 张正面）", { exact: true }).waitFor();
    await page.getByText("第 6 张", { exact: true }).waitFor();
    await page.getByRole("button", { name: "关闭", exact: true }).click();
    await page.getByRole("button", { name: "关闭", exact: true }).waitFor({ state: "hidden" });
    await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `life-order-${viewport.width}.png`) });
    assert.deepEqual(errors, []);
    console.log(`生命顶牌层级、双方公开生命详情及深层公开牌检查通过：${viewport.width}×${viewport.height}`);
  } catch (error) {
    await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `life-order-failed-${viewport.width}.png`) });
    throw error;
  } finally {
    await page.close();
  }
}
