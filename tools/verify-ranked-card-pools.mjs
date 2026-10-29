import assert from "node:assert/strict";
import path from "node:path";

/** 使用真实排位面板验证偏好恢复、匹配协议、实卡限制与三档触控布局。 */
export async function verifyRankedCardPools(page, baseUrl, viewport) {
  const requests = [];
  await page.routeWebSocket("**/ws", socket => socket.onMessage(raw => {
    const message = JSON.parse(String(raw));
    if (message.proto === "MsgSecret") socket.send(JSON.stringify({ proto: "MsgSecret", result: true }));
    else if (message.proto === "MsgPing") socket.send(JSON.stringify(message));
    else if (message.proto === "MsgEnterMatch") {
      requests.push(message);
      socket.send(JSON.stringify({ proto: "MsgEnterMatch", result: true, queueKind: message.queueKind, cardPool: message.cardPool }));
    } else if (message.proto === "MsgCancelMatch") socket.send(JSON.stringify({ proto: "MsgCancelMatch" }));
  }));
  const storageKey = "grandumi_standard_ranked_card_pool_v1:account:hunter";
  await page.evaluate(key => localStorage.removeItem(key), storageKey);
  await page.goto(`${baseUrl}/layout-verification/hunters?view=lobby`, { waitUntil: "domcontentloaded" });
  const panel = page.locator('[aria-label="标准排位卡池选择"][data-pool-ready="true"]');
  await panel.waitFor();
  assert.deepEqual(await panel.locator("[data-card-pool-choice]").evaluateAll(elements =>
    elements.map(element => element.getAttribute("data-card-pool-choice"))), ["real", "preview", "all"]);
  assert.equal(await panel.getAttribute("data-ranked-card-pool"), "real", "首次使用应默认实卡环境。");
  const start = page.getByRole("button", { name: "开始标准排位匹配", exact: true });
  for (const pool of ["real", "preview", "all"]) {
    const choice = panel.locator(`[data-card-pool-choice="${pool}"]`);
    await choice.scrollIntoViewIfNeeded();
    const box = await choice.boundingBox();
    assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1
      && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
    `卡池按钮超出视口或不足44像素：${pool} ${JSON.stringify(box)}`);
    await choice.click();
    assert.equal(await choice.getAttribute("aria-pressed"), "true");
    await page.reload({ waitUntil: "domcontentloaded" });
    await panel.waitFor();
    assert.equal(await panel.getAttribute("data-ranked-card-pool"), pool, "刷新后没有恢复上次环境。");
    await start.scrollIntoViewIfNeeded();
    assert.ok(await start.isEnabled());
    await start.click();
    await page.getByText("正在寻找标准排位对手…", { exact: true }).waitFor();
    assert.equal(requests.at(-1)?.cardPool, pool, "匹配请求没有携带当前选择。");
    assert.equal(requests.at(-1)?.queueKind, "ranked");
    assert.equal(await page.locator('[aria-label="标准排位卡池选择"]').count(), 0, "匹配期间不应允许改变本次环境。");
    await page.getByRole("button", { name: "取消匹配", exact: true }).click();
    await panel.waitFor();
  }
  const modes = page.locator('[aria-label="排位模式"]');
  await modes.getByRole("button", { name: "狂野", exact: true }).click();
  await page.waitForFunction(() => document.querySelector("[data-matchmaking-rules]")?.textContent?.includes("狂野排位"));
  assert.equal(await page.locator('[aria-label="标准排位卡池选择"]').count(), 0, "狂野排位不应显示标准环境选择。");
  await modes.getByRole("button", { name: "标准", exact: true }).click();
  await panel.waitFor();
  assert.equal(await panel.getAttribute("data-ranked-card-pool"), "all", "切换模式不应清空环境偏好。");
  await panel.locator('[data-card-pool-choice="real"]').click();
  await page.goto(`${baseUrl}/layout-verification/hunters?view=lobby&deck=preview`, { waitUntil: "domcontentloaded" });
  await panel.waitFor();
  await page.locator("[data-ranked-card-pool-error]").waitFor();
  assert.ok(await start.isDisabled(), "含新卡的卡组不能进入实卡环境。");
  for (const pool of ["preview", "all"]) {
    await panel.locator(`[data-card-pool-choice="${pool}"]`).click();
    assert.ok(await start.isEnabled(), "新卡卡组应可选择新卡或全环境。");
  }
  // 恢复实卡默认状态，供同页后续排位与布局回归继续使用。
  await panel.locator('[data-card-pool-choice="real"]').click();
  await page.goto(`${baseUrl}/layout-verification/hunters?view=lobby`, { waitUntil: "domcontentloaded" });
  await panel.waitFor();
  await panel.scrollIntoViewIfNeeded();
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), "卡池面板出现横向溢出。");
  if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({
    path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `ranked-card-pools-${viewport.width}.png`),
  });
  await start.scrollIntoViewIfNeeded();
  const startBox = await start.boundingBox();
  assert.ok(startBox && startBox.width >= 44 && startBox.height >= 44 && startBox.x >= -1 && startBox.y >= -1
    && startBox.x + startBox.width <= viewport.width + 1 && startBox.y + startBox.height <= viewport.height + 1,
  "新增卡池面板后匹配按钮不可见或触控区不足。");
  console.log(`排位环境选择、协议、记忆恢复和实卡限制通过：${viewport.width}×${viewport.height}`);
}
