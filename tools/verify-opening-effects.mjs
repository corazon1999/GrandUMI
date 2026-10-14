import assert from "node:assert/strict";
import path from "node:path";

function openingSnapshot() {
  const player = name => ({
    name, cardBackId: "classic", spriteMap: {}, leaderId: name, leaderNumber: "OP13-079",
    leaderPower: 5000, leaderAttachedDon: 0, leaderGainedKeywords: [],
    handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCount: 0,
    fieldCards: [], stages: [], trashNumbers: [], lifeNumbers: [], lifeFaceUp: [],
    deckCount: 50, lifeCount: 0, costActive: 0, costRest: 0, costAttached: 0,
    donDeckCount: 10, mulliganDone: false,
  });
  return {
    proto: "MsgGameState", roomId: "opening-effects-verification", tick: 1,
    my: player("开局验证玩家"), opponent: player("开局验证对手"), viewerKind: "player",
    phase: "Main", currentTurn: false, turnCount: 0, firstPlayer: -1, firstPlayerChosen: false,
    openingStage: "ResolvingOpeningEffects", isGameOver: false, lastAction: "GameStart",
    matchKind: "CasualWild", startingDiceRolls: [], pendingPrompt: null,
  };
}

function restorePrompt(state) {
  state.openingStage = "WaitingOpeningPrompt";
  state.pendingPrompt = {
    promptId: "opening-imu", operationId: "opening-imu", kind: "OwnDeck",
    text: "伊姆：从卡组选择最多1张《圣地玛丽乔尔》舞台登场",
    validChoices: ["opening-stage"], minChoose: 0, maxChoose: 1,
    extra: { sourceNumber: "OP13-079", choiceCards: [{ id: "opening-stage", number: "OP13-099" }] },
  };
}

function restoreDice(state) {
  state.openingStage = "WaitingFirstPlayerChoice";
  state.pendingPrompt = null;
  state.startingDiceRolls = [{ my: 6, opponent: 1, tie: false }];
  state.canChooseFirstPlayer = true;
  state.diceWinnerIsMe = true;
  state.startingPlayerChoiceDeadlineUtc = new Date(Date.now() + 60_000).toISOString();
  state.serverNowUtc = new Date().toISOString();
}

/** 在真实对局页面模拟遗失提示、遗失完成快照和对手长时间选择，不连接外部服务器。 */
export async function verifyOpeningEffects(page, baseUrl, viewport) {
  try {
    for (const scenario of ["opponent-wait", "missing-prompt", "missing-completion"]) {
      const current = await page.context().newPage();
      const state = openingSnapshot();
      if (scenario === "opponent-wait") state.openingStage = "WaitingOpeningPrompt";
      const errors = [];
      const actions = [];
      let socket;
      let requests = 0;
      let recovering = false;
      current.on("pageerror", error => errors.push(error.message));
      await current.routeWebSocket("**", connection => {
        socket = connection;
        connection.onMessage(raw => {
          const message = JSON.parse(String(raw));
          if (message.proto === "MsgSecret") {
            connection.send(JSON.stringify({ proto: "MsgSecret", result: true }));
            connection.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "opening-fixture",
              name: "开局验证玩家", decks: [], authToken: "opening-fixture-token" }));
          } else if (message.proto === "MsgPing") connection.send(JSON.stringify(message));
          else if (message.proto === "MsgRequestState") {
            if (recovering) {
              requests++;
              if (scenario === "missing-prompt" || scenario === "opponent-wait" && requests === 4)
                restorePrompt(state);
              if (scenario === "missing-completion") restoreDice(state);
            }
            connection.send(JSON.stringify({ ...state, tick: ++state.tick }));
          } else if (message.proto === "MsgGameAction" || message.proto === "MsgPromptResponse") {
            actions.push(message);
            if (message.proto === "MsgPromptResponse") {
              restoreDice(state);
              connection.send(JSON.stringify({ ...state, tick: ++state.tick }));
            }
          }
        });
      });
      try {
        // 页面启动前安装时钟，保证开局组件首次注册的计时器也能被快进。
        await current.clock.install();
        await current.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
        await current.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
        socket.send(JSON.stringify({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "开局验证对手" }));
        socket.send(JSON.stringify({ proto: "MsgGameStart", MainDeck: "OP13-079" }));
        socket.send(JSON.stringify(state));
        await current.waitForURL(`${baseUrl}/game`);
        const overlay = current.locator("[data-first-player-overlay]");
        await overlay.waitFor({ state: "visible" });
        if (viewport.width < 600)
          await current.locator('[data-layout-preview][data-layout-rotated="true"]').waitFor();
        // 每步给真实 WebSocket 回包留出事件循环，避免快进时间误触发网络心跳超时。
        const advance = async duration => {
          for (let elapsed = 0; elapsed < duration; elapsed += 1000) {
            await current.clock.runFor(Math.min(1000, duration - elapsed));
            await new Promise(resolve => setTimeout(resolve, 30));
          }
        };
        recovering = true;
        requests = 0;
        if (scenario === "opponent-wait") {
          assert.match(await overlay.innerText(), /等待对手处理开局效果/);
          await advance(15_000);
          await current.waitForFunction(() => document.body.textContent.includes("重新同步对局"));
          assert.equal(requests, 3, "重复状态快照不应重置自动恢复次数。");
          await advance(10_000);
          assert.equal(requests, 3, "开局自动恢复超过上限。");
          const button = current.getByRole("button", { name: "重新同步对局", exact: true });
          const bounds = await button.boundingBox();
          assert.ok(bounds && bounds.width >= 44 && bounds.height >= 44 && bounds.x >= -1 && bounds.y >= -1
            && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1,
          `旋转后的恢复按钮不可见或触控区不足：${JSON.stringify(bounds)}`);
          if (process.env.GRANDUMI_TEST_TEMP_ROOT) await current.screenshot({
            path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `opening-recovery-${viewport.width}.png`),
          });
          assert.equal(actions.length, 0, "等待期间不应替玩家回答或跳过效果。");
          await button.click();
        } else {
          await advance(6000);
        }
        if (scenario !== "missing-completion") {
          await current.getByText("伊姆：从卡组选择最多1张《圣地玛丽乔尔》舞台登场", { exact: true }).waitFor();
          assert.equal(await overlay.count(), 0, "己方选择恢复后开局遮罩仍挡住操作。");
          const requestsAfterPrompt = requests;
          await advance(10_000);
          assert.equal(requests, requestsAfterPrompt, "选择恢复后自动补取未停止。");
          await current.locator('[data-prompt-choice-id="opening-stage"]').click();
          await current.getByRole("button", { name: "确认（已选 1 / 1）", exact: true }).click();
          await current.getByText("投骰决定选择权", { exact: true }).waitFor();
          assert.equal(actions.at(-1)?.proto, "MsgPromptResponse");
          assert.deepEqual(actions.at(-1)?.chosen, ["opening-stage"]);
        }
        await advance(1500);
        await current.getByRole("button", { name: "选择先手", exact: true }).waitFor();
        assert.match(await overlay.innerText(), /投骰决定选择权/);
        assert.equal(await current.getByRole("button", { name: "重新同步对局", exact: true }).count(), 0);
        assert.deepEqual(errors, [], `${scenario} 开局恢复发生浏览器错误。`);
      } catch (error) {
        if (process.env.GRANDUMI_TEST_TEMP_ROOT) await current.screenshot({
          path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `opening-failed-${scenario}-${viewport.width}.png`),
        }).catch(() => {});
        throw new Error(`${scenario} 开局验证失败，补取 ${requests} 次，浏览器异常：${errors.join("；")}；${error.message}`, { cause: error });
      } finally {
        await current.close();
      }
    }
    console.log(`开局效果恢复回归通过：${viewport.width}×${viewport.height}。`);
  } finally {
    await page.close();
  }
}
