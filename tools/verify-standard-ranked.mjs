import assert from "node:assert/strict";

/** 验证新旧卡组从同一个入口入队，以及排位操作的触控布局。 */
export async function verifyStandardRanked(page, baseUrl, viewport) {
  const requests = [];
  await page.routeWebSocket("**/ws", socket => socket.onMessage(raw => {
    const message = JSON.parse(String(raw));
    if (message.proto === "MsgSecret") socket.send(JSON.stringify({ proto: "MsgSecret", result: true }));
    else if (message.proto === "MsgPing") socket.send(JSON.stringify(message));
    else if (message.proto === "MsgEnterMatch") {
      requests.push(message);
      socket.send(JSON.stringify({ proto: "MsgEnterMatch", result: true, queueKind: message.queueKind }));
    } else if (message.proto === "MsgCancelMatch") socket.send(JSON.stringify({ proto: "MsgCancelMatch" }));
  }));

  const lobbyUrl = `${baseUrl}/layout-verification/hunters?view=lobby`;
  // 模拟更新前已选实卡环境的玩家。
  await page.evaluate(() => localStorage.setItem("grandumi_standard_ranked_card_pool_v1:account:hunter", "real"));
  const start = page.getByRole("button", { name: "开始标准排位匹配", exact: true });
  const modes = page.locator('[aria-label="排位模式"]');
  const rules = page.locator("[data-matchmaking-rules]");

  async function checkTouchTarget(button) {
    await button.scrollIntoViewIfNeeded();
    const box = await button.boundingBox();
    assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1
      && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
    `排位操作超出视口或触控区不足：${JSON.stringify(box)}`);
    await button.click({ trial: true });
  }

  for (const [url, leader] of [[lobbyUrl, "OP01-001"], [`${lobbyUrl}&deck=preview`, "ST38-001"]]) {
    await page.goto(url, { waitUntil: "domcontentloaded" });
    await start.waitFor();
    assert.match(await rules.innerText(), /标准排位使用统一卡池/);
    assert.equal(await page.locator('[aria-label="标准排位卡池选择"]').count(), 0);
    assert.ok(await start.isEnabled(), "新旧卡组都应可直接排位。");
    await checkTouchTarget(start);
    await start.click();
    await page.getByText("正在寻找标准排位对手…", { exact: true }).waitFor();
    const request = requests.at(-1);
    assert.equal(request?.queueKind, "ranked");
    assert.match(request?.deck ?? "", new RegExp(`^(?:领航: )?${leader}(?:\\r?\\n|$)`));
    assert.equal(Object.hasOwn(request, "cardPool"), false);
    const cancel = page.getByRole("button", { name: "取消匹配", exact: true });
    await checkTouchTarget(cancel);
    await cancel.click();
    await start.waitFor();
  }

  const wild = modes.getByRole("button", { name: "狂野", exact: true });
  const standard = modes.getByRole("button", { name: "标准", exact: true });
  await checkTouchTarget(wild);
  await wild.click();
  await page.getByRole("button", { name: "开始狂野排位匹配", exact: true }).waitFor();
  await checkTouchTarget(standard);
  await standard.click();
  await start.waitFor();
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), "排位界面出现横向溢出。");
  await page.goto(lobbyUrl, { waitUntil: "domcontentloaded" });
  await start.waitFor();
  console.log(`标准排位统一入队与触控布局通过：${viewport.width}×${viewport.height}`);
}
