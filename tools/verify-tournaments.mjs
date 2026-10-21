import assert from "node:assert/strict";
import path from "node:path";

export async function verifyTournaments(page, baseUrl, viewport) {
  for (const mode of ["live", "manual"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "GrandUMI 社区周末挑战赛" }).waitFor();
    await page.getByRole("button", { name: "赛事管理", exact: true }).click();
    const next = page.getByRole("button", { name: "开启下一轮并公布对阵" });
    assert.equal(await next.isDisabled(), true, "未结算全部比赛桌时不得推进下一轮。");
    if (mode === "live") {
      const end = page.getByRole("button", { name: "结束当前瑞士轮", exact: true });
      await end.scrollIntoViewIfNeeded();
      const box = await end.boundingBox();
      assert.ok(box && box.width >= 44 && box.height >= 44, "结束本轮按钮触控区域不足。");
      await end.click();
      const dialog = page.getByRole("dialog", { name: "确认结束当前瑞士轮" });
      await dialog.waitFor();
      assert.match(await dialog.innerText(), /每桌都需要手动结算/);
      const confirm = dialog.getByRole("button", { name: "结束并手动结算" });
      assert.equal(await confirm.isDisabled(), true, "二次确认未填写原因时不应执行结束。");
      await dialog.getByRole("textbox", { name: "结束原因" }).fill("管理员决定提前结算本轮");
      assert.equal(await confirm.isEnabled(), true);
      const bounds = await confirm.boundingBox();
      assert.ok(bounds && bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1, "二次确认按钮超出手机可视区。");
      await dialog.getByRole("button", { name: "继续本轮" }).click();
      await dialog.waitFor({ state: "hidden" });
      assert.equal(await page.getByRole("dialog").count(), 0, "取消确认不能结束轮次。");
    } else {
      assert.equal(await page.getByRole("button", { name: "确认本桌结果", exact: true }).count(), 4, "包括已自然结束的桌，都应有独立的手动确认操作。");
      const result = page.getByRole("button", { name: "确认本桌结果", exact: true }).first();
      await result.scrollIntoViewIfNeeded();
      const box = await result.boundingBox();
      assert.ok(box && box.width >= 44 && box.height >= 44, "逐桌确认触控区域不足。");
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
    assert.equal(overflow, false, "赛事页面导致整页横向溢出。");
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-${mode}-${viewport.width}.png`) });
  }
  await page.goto(`${baseUrl}/layout-verification/tournaments?view=game`, { waitUntil: "domcontentloaded" });
  const clock = page.locator("[data-tournament-clock]");
  await clock.waitFor({ state: "visible" });
  // 等回合提示退场后检查常驻赛事时钟与操作区，截图不保留进场瞬态。
  await page.waitForTimeout(2400);
  assert.match(await clock.innerText(), /瑞士轮第1轮 · 第4桌/);
  assert.match(await clock.innerText(), /本桌 \d{2}:\d{2}/);
  assert.equal(await page.locator("[data-layout-rotated]").first().getAttribute("data-layout-rotated"), String(viewport.width < 600), "赛事对局未按手机竖屏自动旋转。");
  const box = await clock.boundingBox();
  assert.ok(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "旋转后赛事时钟超出视口。");
  const actions = await page.locator("[data-game-actions-panel]").boundingBox();
  assert.ok(actions && (box.x + box.width <= actions.x + 1 || actions.x + actions.width <= box.x + 1 || box.y + box.height <= actions.y + 1 || actions.y + actions.height <= box.y + 1), "赛事时钟与主要操作区重叠。");
  if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-game-${viewport.width}.png`) });
  await page.close();
}
