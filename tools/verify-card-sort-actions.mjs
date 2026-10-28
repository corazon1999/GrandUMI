import assert from "node:assert/strict";
import path from "node:path";

/** 复现时钟、双行昵称、撤回贴咚同时出现时，整理手牌挤走回合结束的反馈。 */
export async function verifyCardSortActions(page, baseUrl, viewport) {
  const mobile = viewport.width < 500;
  const address = `${baseUrl}/layout-verification/card-sort?crowded=1${mobile ? "&device=mobile" : ""}`;
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(address, { waitUntil: "domcontentloaded" });
  const sort = page.getByRole("button", { name: "整理手牌", exact: true });
  const end = page.getByRole("button", { name: "结束回合", exact: true });
  await sort.waitFor({ state: "visible" });
  await end.waitFor({ state: "visible" });
  async function clickable(button) {
    const bounds = await button.boundingBox();
    assert.ok(bounds && bounds.width >= 43.5 && bounds.height >= 43.5
      && bounds.x >= -1 && bounds.y >= -1
      && bounds.x + bounds.width <= viewport.width + 1
      && bounds.y + bounds.height <= viewport.height + 1,
    `操作超出视口或触控区不足：${await button.getAttribute("aria-label") || await button.innerText()} ${JSON.stringify(bounds)}`);
    const clear = await button.evaluate(element => {
      const r = element.getBoundingClientRect();
      return [0.2, 0.5, 0.8].every(x => [0.2, 0.5, 0.8].every(y => {
        const hit = document.elementFromPoint(r.x + r.width * x, r.y + r.height * y);
        return hit === element || element.contains(hit);
      }));
    });
    assert.equal(clear, true, `操作被遮挡：${await button.getAttribute("aria-label") || await button.innerText()}`);
  }
  await clickable(end);
  assert.equal(await sort.evaluate(element => !!element.closest("[data-game-control-dock]")), true,
    "整理入口应位于聊天工具栏，避免占用回合操作空间");
  await clickable(sort);
  assert.equal(await page.locator("[data-game-chat-root]").getAttribute("data-layout-rotated"), String(mobile));
  const before = await page.locator("[data-hand-order-id]").evaluateAll(cards => cards.map(card => card.dataset.handOrderId));
  assert.equal(before.length, 5);
  await sort.click();
  await page.waitForFunction(() => document.querySelector("[data-hand-order-id]")?.getAttribute("data-hand-order-id") === "sort-hand-1");
  const after = await page.locator("[data-hand-order-id]").evaluateAll(cards => cards.map(card => card.dataset.handOrderId));
  assert.deepEqual(after, ["sort-hand-1", "sort-hand-3", "sort-hand-0", "sort-hand-4", "sort-hand-2"]);
  const dockButtons = page.locator("[data-game-control-dock] button:visible");
  for (const button of await dockButtons.all()) await clickable(button);
  const dockBounds = await page.locator("[data-game-control-dock]").boundingBox();
  const endBounds = await end.boundingBox();
  assert.ok(dockBounds.x + dockBounds.width <= endBounds.x
    || endBounds.x + endBounds.width <= dockBounds.x
    || dockBounds.y + dockBounds.height <= endBounds.y
    || endBounds.y + endBounds.height <= dockBounds.y, "侧边工具栏与回合结束重叠");
  if (process.env.GRANDUMI_SCREENSHOT_DIR) {
    await page.screenshot({ path: path.join(process.env.GRANDUMI_SCREENSHOT_DIR, `hand-sort-${viewport.width}.png`) });
  }
  await end.click();
  const confirm = page.getByRole("button", { name: "确认结束", exact: true });
  const cancel = page.getByRole("button", { name: "取消", exact: true });
  await clickable(confirm);
  await clickable(cancel);
  await confirm.click();
  await page.waitForFunction(() => document.querySelector("[data-card-sort-verification]")?.getAttribute("data-card-sort-request") === "结束回合");
  await page.goto(`${address}&observer=1`, { waitUntil: "domcontentloaded" });
  await page.locator("[data-game-right-rail]").waitFor({ state: "visible" });
  assert.equal(await page.locator("[data-sort-hand]").count(), 0, "观战不得显示整理他人手牌的入口");
  assert.deepEqual(errors, [], "整理与回合控制出现浏览器异常");
  console.log(`整理手牌与回合结束回归通过：${viewport.width}×${viewport.height}，旋转=${mobile}。`);
}
