import assert from "node:assert/strict";
import path from "node:path";

export async function verifyTournaments(page, baseUrl, viewport) {
  for (const mode of ["checkin", "checkedin"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    await page.getByRole("button", { name: "报名签到", exact: true }).click();
    await page.locator('[data-tournament-player-name="赛事选手1"]').waitFor();
    assert.match(await page.locator('[data-tournament-player-name="赛事选手1"]').innerText(), /QQ 1000000000/);
    assert.match(await page.locator('[data-tournament-player-name="赛事选手8"]').innerText(), /未绑定QQ/);
    if (mode === "checkin") {
      const sign = page.getByRole("button", { name: "签到", exact: true });
      assert.equal(await sign.isEnabled(), true, "开赛两小时前已报名的玩家也应可以签到。");
      await sign.click();
      const dialog = page.getByRole("dialog", { name: "确认赛事签到" });
      await dialog.waitFor();
      assert.match(await dialog.innerText(), /签到后将不能退出本场赛事/);
      const confirm = dialog.getByRole("button", { name: "我已了解，确认签到" });
      await confirm.click({ trial: true });
      const box = await confirm.boundingBox();
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-checkin-dialog-${viewport.width}.png`) });
      assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, `签到确认按钮触控区不足或超出手机可视区：${JSON.stringify(box)}`);
      await dialog.getByRole("button", { name: "暂不签到" }).click();
      assert.equal(await page.getByRole("status").count(), 0, "取消二次说明不应提交签到。");
      await sign.click();
      await page.getByRole("dialog", { name: "确认赛事签到" }).getByRole("button", { name: "我已了解，确认签到" }).click();
      assert.match(await page.getByRole("status").innerText(), /布局预览不执行赛事操作/);
      await page.getByRole("dialog", { name: "确认赛事签到" }).getByRole("button", { name: "暂不签到" }).click();
    } else {
      assert.equal(await page.getByRole("button", { name: "已签到", exact: true }).isDisabled(), true);
      assert.equal(await page.getByRole("button", { name: "退出赛事", exact: true }).count(), 0, "已签到选手不得再显示退赛入口。");
      assert.match(await page.locator('[data-tournament-layout-verification]').innerText(), /已签到，不能退出本场赛事/);
    }
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, "签到及QQ显示导致页面横向溢出。");
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-${mode}-${viewport.width}.png`) });
  }
  for (const mode of ["live", "manual"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "GrandUMI 社区周末挑战赛" }).waitFor();
    assert.match(await page.locator('[data-tournament-player-name="社区举办者"]').first().innerText(), /QQ 2000000000/);
    assert.match(await page.locator('[data-tournament-player-name="赛事裁判"]').innerText(), /QQ 3000000000/);
    for (const section of ["赛程对阵", "积分排名"]) {
      await page.getByRole("button", { name: section, exact: true }).click();
      assert.match(await page.locator('[data-tournament-player-name="赛事选手1"]').first().innerText(), /QQ 1000000000/);
    }
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
  assert.match(await page.locator('[data-player-nameplate="赛事选手甲"]').first().innerText(), /QQ 1000000000/);
  assert.match(await page.locator('[data-player-nameplate="赛事选手乙"]').first().innerText(), /QQ 1000000001/);
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
