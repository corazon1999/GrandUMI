import assert from "node:assert/strict";
import path from "node:path";
import { verifyTournamentChatChannels } from "./verify-tournament-chat.mjs";

export async function verifyTournaments(page, baseUrl, viewport) {
  await verifyTournamentSchedule(page, baseUrl, viewport);
  for (const mode of ["checkin", "checkedin"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    await page.locator('[data-tournament-layout-ready="true"]').waitFor();
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
      assert.match(await dialog.innerText(), /每次退赛会扣15信誉分/);
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
      await page.getByRole("button", { name: "退出赛事", exact: true }).click();
      const dialog = page.getByRole("dialog", { name: "确认退出赛事" });
      assert.match(await dialog.innerText(), /退赛扣15信誉分：85 → 70分/);
      const confirm = dialog.getByRole("button", { name: "确认退赛", exact: true });
      await confirm.click({ trial: true });
      const box = await confirm.boundingBox();
      assert.ok(box && box.height >= 44 && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "退赛扣分确认按钮应完整可点。");
      await dialog.getByRole("button", { name: "继续参赛" }).click();
      assert.equal(await page.getByRole("status").count(), 0, "取消退赛不得扣分或修改参赛状态。");
    }
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, "签到及QQ显示导致页面横向溢出。");
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-${mode}-${viewport.width}.png`) });
  }
  await page.goto(`${baseUrl}/layout-verification/tournaments?view=host`, { waitUntil: "domcontentloaded" });
  await page.locator('[data-tournament-layout-ready="true"]').waitFor();
  await page.getByRole("button", { name: "赛事管理", exact: true }).click();
  await page.getByRole("button", { name: "立即开赛并公布对阵", exact: true }).click();
  const start = page.getByRole("dialog", { name: "确认立即开赛" });
  assert.match(await start.innerText(), /有效签到7\/8人/);
  assert.match(await start.innerText(), /3轮瑞士轮，前4名晋级/);
  const startButton = start.getByRole("button", { name: "确认开赛并锁定名单" });
  await startButton.click({ trial: true });
  const startBox = await startButton.boundingBox();
  assert.ok(startBox && startBox.height >= 44 && startBox.x >= 0 && startBox.y >= 0 && startBox.x + startBox.width <= viewport.width + 1 && startBox.y + startBox.height <= viewport.height + 1, "未满员开赛确认按钮应完整可点。");
  if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-start-${viewport.width}.png`) });
  await start.getByRole("button", { name: "暂不开赛" }).click();
  const schedule = page.getByRole("button", { name: "保存并通知参赛者" });
  await schedule.scrollIntoViewIfNeeded();
  assert.equal(await page.getByLabel("新的开赛时间").count(), 1, "仅举办者身份也应有调期开赛入口。");
  await page.getByRole("button", { name: "移出或代办退赛", exact: true }).first().click();
  const removal = page.getByRole("dialog", { name: "确认调整参赛资格" });
  assert.match(await removal.innerText(), /QQ 1000000000/);
  const removeButton = removal.getByRole("button", { name: "确认处理参赛资格" });
  assert.equal(await removeButton.isDisabled(), true, "缺少移出原因必须阻止提交。");
  await removal.getByLabel("移出或退赛原因").fill("违反赛事约定，经核实后移出");
  await removeButton.click({ trial: true });
  const removeBox = await removeButton.boundingBox();
  assert.ok(removeBox && removeBox.height >= 44 && removeBox.x >= 0 && removeBox.y >= 0 && removeBox.x + removeBox.width <= viewport.width + 1 && removeBox.y + removeBox.height <= viewport.height + 1, "移出确认按钮应完整可点。");
  await removal.getByLabel("处理方式").selectOption("withdraw");
  assert.match(await removal.innerText(), /已签到者扣15信誉分/);
  if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-remove-${viewport.width}.png`) });
  await removal.getByRole("button", { name: "保留参赛资格" }).click();
  assert.equal(await page.getByRole("status").count(), 0, "取消移出不得执行写操作。");
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, "名单及调期管理导致横向溢出。");
  await page.goto(`${baseUrl}/layout-verification/tournaments?view=bye`, { waitUntil: "domcontentloaded" });
  await page.locator('[data-tournament-layout-ready="true"]').waitFor();
  await page.getByRole("button", { name: "赛程对阵", exact: true }).click();
  assert.equal(await page.getByText("轮空胜", { exact: true }).count(), 1, "奇数人数应清楚显示唯一轮空结果。");
  if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-bye-${viewport.width}.png`) });
  for (const mode of ["live", "manual"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    await page.locator('[data-tournament-layout-ready="true"]').waitFor();
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
  await verifyTournamentTimeControls(page, baseUrl, viewport);
  await verifyTournamentChatChannels(page, baseUrl, viewport);
  await page.close();
}

export async function verifyTournamentSchedule(page, baseUrl, viewport) {
  await page.goto(`${baseUrl}/layout-verification/tournaments?view=custom`, { waitUntil: "domcontentloaded" });
  await page.locator('[data-tournament-layout-ready="true"]').waitFor();
  assert.match(await page.getByRole("region", { name: "赛事系统" }).innerText(), /9轮瑞士轮 · 瑞士轮第1名直接夺冠/);
  await page.getByRole("button", { name: "赛事管理", exact: true }).click();
  await page.getByRole("button", { name: "立即开赛并公布对阵", exact: true }).click();
  const start = page.getByRole("dialog", { name: "确认立即开赛" });
  assert.match(await start.innerText(), /9轮瑞士轮，瑞士轮第1名直接夺冠/);
  await accessibleControl(page, start.getByRole("button", { name: "确认开赛并锁定名单" }));
  await start.getByRole("button", { name: "暂不开赛" }).click();
  await page.getByRole("button", { name: "修改申请并重新审核" }).click();
  const edit = page.getByRole("dialog", { name: "修改赛事并重新提交审核" });
  assert.equal(await edit.getByLabel("人数规格").inputValue(), "64");
  assert.equal(await edit.getByLabel("瑞士轮轮次").inputValue(), "9");
  assert.equal(await edit.getByLabel("出轮人数").inputValue(), "1");
  await edit.getByRole("button", { name: "关闭弹窗" }).click();
  await edit.waitFor({ state: "hidden" });
  await page.getByRole("button", { name: "返回赛事大厅" }).click();
  await page.getByRole("button", { name: "申请举办赛事", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "申请举办赛事" });
  const capacity = dialog.getByLabel("人数规格");
  const rounds = dialog.getByLabel("瑞士轮轮次");
  const cut = dialog.getByLabel("出轮人数");
  assert.deepEqual(await capacity.locator("option").evaluateAll(options => options.map(o => o.value)), ["8", "16", "32", "64", "128", "256"]);
  assert.deepEqual(await rounds.locator("option").evaluateAll(options => options.map(o => o.value)), ["3", "4", "5", "6", "7", "8", "9"]);
  assert.deepEqual(await cut.locator("option").evaluateAll(options => options.map(o => o.value)), ["1", "2", "4", "8", "16", "32"]);
  assert.equal(await rounds.inputValue(), "3"); assert.equal(await cut.inputValue(), "4");
  for (const [count, recommendedRounds, recommendedCut] of [[16, 4, 4], [32, 5, 8], [64, 6, 8], [128, 7, 8], [256, 8, 16], [8, 3, 4]]) {
    await rounds.selectOption("9"); await cut.selectOption("1");
    await capacity.selectOption(String(count));
    assert.equal(await rounds.inputValue(), String(recommendedRounds));
    assert.equal(await cut.inputValue(), String(recommendedCut));
    assert.match(await capacity.locator(`option[value="${count}"]`).innerText(), new RegExp(`推荐${recommendedRounds}轮 / ${recommendedCut}人出轮`));
  }
  assert.equal(await cut.locator('option[value="16"]').evaluate(option => option.disabled), true);
  await capacity.selectOption("32");
  await rounds.selectOption("9"); await cut.selectOption("32");
  for (const control of [capacity, rounds, cut]) await accessibleControl(page, control);
  assert.equal(await dialog.evaluate(el => el.scrollWidth > el.clientWidth + 1), false, "赛事自定义表单不得横向溢出。");
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, "赛事自定义表单不得撑宽手机页面。");
  if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournaments-schedule-${viewport.width}.png`) });
  await accessibleControl(page, dialog.getByRole("button", { name: "提交管理员审核" }));
  await dialog.getByRole("button", { name: "关闭弹窗" }).click();
  await dialog.waitFor({ state: "hidden" });
}

async function accessibleControl(page, control) {
  await control.scrollIntoViewIfNeeded();
  const box = await control.boundingBox(); const viewport = page.viewportSize();
  assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1
    && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "赛事管理按钮必须完整可见且触控区域至少44×44px。");
  assert.ok(await control.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
  }), "赛事管理按钮不得被遮挡。");
}

export async function verifyTournamentTimeControls(page, baseUrl, viewport) {
  for (const mode of ["overdue", "final"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    await page.locator('[data-tournament-layout-ready="true"]').waitFor();
    if (mode === "overdue") {
      await page.locator("[data-tournament-overdue-alert]").waitFor();
      await page.getByRole("button", { name: "处理超时对局", exact: true }).click();
    } else {
      assert.equal(await page.locator("[data-tournament-overdue-alert]").count(), 0, "不限时决赛不应产生超时提醒。");
      await page.getByRole("button", { name: "赛事管理", exact: true }).click();
      await page.getByText("决赛不限时", { exact: true }).waitFor();
    }
    const controls = page.locator('[data-tournament-table-controls="table-0"]');
    const modeSelect = controls.getByLabel("第1桌计时方式");
    assert.equal(await modeSelect.inputValue(), mode === "final" ? "unlimited" : "timed");
    const save = controls.getByRole("button", { name: "保存本桌计时", exact: true });
    assert.equal(await save.isDisabled(), true, "未填写调整时间原因不得提交。");
    await modeSelect.selectOption("timed");
    await controls.getByLabel("第1桌剩余分钟").fill("10");
    await controls.getByLabel("第1桌调整计时原因").fill("管理员允许本桌继续十分钟");
    await accessibleControl(page, save);
    await save.click();
    assert.match(await page.getByRole("status").innerText(), /布局预览不执行赛事操作/);
    await modeSelect.selectOption("unlimited");
    assert.equal(await controls.getByLabel("第1桌剩余分钟").count(), 0, "不限时不应保留剩余分钟输入框。");
    await save.click();
    await controls.getByText("人工判定第1桌结果", { exact: true }).click();
    const decision = controls.getByRole("button", { name: "确认本桌判定", exact: true });
    assert.equal(await decision.isDisabled(), true, "缺少明确结果或原因不得人工判定。");
    await controls.getByLabel("第1桌最终结果").selectOption("doubleLoss");
    await controls.getByLabel("第1桌结算原因").fill("现场核实后由管理员人工判定双败");
    await accessibleControl(page, decision);
    assert.equal(await decision.isEnabled(), true);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, "单桌时间调整及判定导致页面横向溢出。");
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournament-${mode}-management-${viewport.width}.png`) });
  }
  for (const mode of ["game-overdue", "game-final"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=${mode}`, { waitUntil: "domcontentloaded" });
    const clock = page.locator("[data-tournament-clock]");
    await clock.waitFor();
    await page.locator("[data-layout-preview]").evaluate(frame => {
      for (const side of ["top", "right", "bottom", "left"]) frame.style.setProperty(`--layout-safe-${side}`, "12px");
    });
    await page.waitForTimeout(2400);
    assert.match(await clock.innerText(), mode === "game-final" ? /决赛[\s\S]*不限时/ : /已超时，等待管理员裁定/);
    const box = await clock.boundingBox(); const actions = await page.locator("[data-game-actions-panel]").boundingBox();
    assert.ok(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "超时或不限时赛事时钟超出视口。");
    assert.ok(actions && (box.x + box.width <= actions.x + 1 || actions.x + actions.width <= box.x + 1 || box.y + box.height <= actions.y + 1 || actions.y + actions.height <= box.y + 1), "赛事时钟与主要操作区重叠。");
    await accessibleControl(page, page.getByRole("button", { name: "结束回合", exact: true }));
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournament-${mode}-${viewport.width}.png`) });
  }
  console.log(`赛事单桌计时、人工双败、超时提醒与不限时决赛通过：${viewport.width}×${viewport.height}`);
}
