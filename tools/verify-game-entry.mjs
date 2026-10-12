import assert from "node:assert/strict";
import path from "node:path";

function snapshot() {
  const player = name => ({
    name, cardBackId: "classic", spriteMap: {}, leaderId: name, leaderNumber: "OP15-001",
    leaderPower: 5000, leaderAttachedDon: 0, leaderGainedKeywords: [],
    handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCount: 0,
    fieldCards: [], stages: [], trashNumbers: [], lifeNumbers: [], lifeFaceUp: [],
    deckCount: 50, lifeCount: 0, costActive: 0, costRest: 0, costAttached: 0,
    donDeckCount: 10, mulliganDone: false,
  });
  return { proto: "MsgGameState", roomId: "entry-verification", tick: 1,
    my: player("入场验证玩家"), opponent: player("入场验证对手"), viewerKind: "player",
    phase: "Main", currentTurn: false, turnCount: 0, firstPlayer: -1, firstPlayerChosen: false,
    openingStage: "NotStarted", isGameOver: false, lastAction: "Resync", matchKind: "CasualWild",
    openingPreparationDeadlineUtc: new Date(Date.now() + 30_000).toISOString(),
  };
}

/** 使用真实大厅、WebSocket 消息处理和路由，模拟入场失败并确认回到原牌桌。 */
export async function verifyGameEntry(page, baseUrl, viewport) {
  for (const mode of ["storage-failure", "missing-start", "route-stall"]) {
    const current = await page.context().newPage();
    const errors = [];
    const sockets = [];
    let matched = false;
    let requests = 0;
    let blockedRoute = false;
    let reloaded = false;
    let releaseRoute;
    const routeWait = new Promise(resolve => { releaseRoute = resolve; });
    current.on("pageerror", error => errors.push(error.message));
    if (mode === "storage-failure") await current.addInitScript(() => {
      const original = Storage.prototype.setItem;
      Storage.prototype.setItem = function (key, value) {
        if (key === "myDeck") throw new DOMException("模拟会话存储已满", "QuotaExceededError");
        return original.call(this, key, value);
      };
    });
    if (mode === "route-stall") await current.route("**/game*", async route => {
      if (route.request().headers().rsc === "1" && !reloaded) {
        blockedRoute = true;
        await routeWait;
        await route.abort().catch(() => {});
      } else {
        if (route.request().isNavigationRequest()) {
          reloaded = true;
          releaseRoute();
        }
        await route.continue();
      }
    });
    await current.routeWebSocket("**", socket => {
      sockets.push(socket);
      socket.onMessage(data => {
        const message = JSON.parse(String(data));
        if (message.proto === "MsgSecret") {
          socket.send(JSON.stringify({ proto: "MsgSecret", result: true }));
          socket.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "entry-fixture",
            name: "入场验证玩家", decks: [], authToken: "entry-fixture-token" }));
          if (matched) setTimeout(() => socket.send(JSON.stringify(snapshot())), 200);
        } else if (message.proto === "MsgPing") socket.send(JSON.stringify(message));
        else if (message.proto === "MsgRequestState") {
          requests++;
          socket.send(JSON.stringify(snapshot()));
        }
      });
    });
    try {
      await current.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
      await current.getByRole("heading", { name: "开始对战", exact: true }).waitFor({ state: "visible" });
      matched = true;
      const send = value => sockets.at(-1).send(JSON.stringify(value));
      send({ proto: "MsgEnterMatch", result: true, queueKind: "casual" });
      send({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "入场验证对手" });
      await current.getByText("正在进入牌桌…", { exact: true }).waitFor({ state: "visible" });
      if (mode !== "missing-start") {
        send({ proto: "MsgGameStart", MainDeck: "OP15-001" });
        send(snapshot());
      }
      await current.waitForURL(`${baseUrl}/game`, { timeout: 30_000 });
      await current.locator(".layout-preview-route").waitFor({ state: "visible", timeout: 15_000 });
      await current.locator('[data-player-name-text]:visible').filter({ hasText: "入场验证对手" })
        .first().waitFor({ state: "visible", timeout: 15_000 });
      if (viewport.width < 600) await current.locator('[data-layout-preview][data-layout-rotated="true"]').waitFor();
      const layout = await current.locator(".layout-preview-route").evaluate(element => {
        const box = element.getBoundingClientRect();
        return { x: box.x, y: box.y, right: box.right, bottom: box.bottom };
      });
      assert.ok(layout.x >= -1 && layout.y >= -1 && layout.right <= viewport.width + 1
        && layout.bottom <= viewport.height + 1, `${mode} 的牌桌超出视口：${JSON.stringify(layout)}`);
      if (mode === "missing-start") assert.ok(requests >= 1, "开局消息缺失后未补取房间状态。");
      if (mode === "route-stall") {
        assert.ok(blockedRoute, "未实际阻断客户端路由请求。");
        assert.ok(sockets.length >= 2, "路由停滞后没有通过整页加载恢复对局。");
      }
      assert.deepEqual(errors, [], `${mode} 入场出现浏览器异常。`);
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) {
        await current.waitForTimeout(800);
        await current.screenshot({
          path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `game-entry-${mode}-${viewport.width}.png`),
        });
      }
    } catch (error) {
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) await current.screenshot({
        path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `game-entry-failed-${mode}-${viewport.width}.png`),
      }).catch(() => {});
      throw new Error(`${mode} 入场验证失败，连接数 ${sockets.length}、补取状态 ${requests} 次、路径 ${current.url()}，浏览器异常：${errors.join("；")}；${error.message}`, { cause: error });
    } finally {
      releaseRoute();
      await current.close();
    }
  }
  await page.close();
}
