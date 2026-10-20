import assert from "node:assert/strict";
import path from "node:path";

/** 验证真实设置、旋转牌桌、动态开关和持久化，不依赖截图识别或线上账号。 */
export async function verifyVisualEffects(page, baseUrl, viewport) {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.routeWebSocket("**", socket => socket.onMessage(() => {}));
  const url = `${baseUrl}/layout-verification/visual-effects`;
  await page.goto(url, { waitUntil: "domcontentloaded" });
  await page.locator(".blocker-aura").first().waitFor({ state: "visible" });
  const checkStatic = async () => {
    await page.waitForFunction(() => document.documentElement.dataset.visualEffects === "off");
    await page.waitForTimeout(1000);
    assert.equal(await page.locator("animateMotion").count(), 0, "静态攻击仍挂载 SVG 粒子。");
    assert.equal(await page.locator(".blocker-aura").first().evaluate(el => getComputedStyle(el).animationName), "none");
    const loops = await page.evaluate(() => document.getAnimations().filter(a => a.playState === "running" && a.effect?.getTiming().iterations === Infinity).length);
    assert.equal(loops, 0, "静态模式仍运行无限循环动画。");
    assert.ok(await page.locator('[data-attack-vfx="procedural"] path').count() > 0, "静态攻击丢失连线。");
    assert.ok(await page.getByLabel(/卡牌词条：.*阻挡者/).count() > 0, "静态牌桌丢失词条标识。");
    const client = await page.context().newCDPSession(page);
    await client.send("Performance.enable");
    const metrics = async () => Object.fromEntries((await client.send("Performance.getMetrics")).metrics.map(item => [item.name, item.value]));
    const before = await metrics();
    await page.waitForTimeout(600);
    const after = await metrics();
    await client.detach();
    assert.ok(after.RecalcStyleCount - before.RecalcStyleCount <= 3, "静态牌桌仍持续更新样式，疑似残留脚本动画。");
  };
  const openSettings = async (expectedEnabled = false) => {
    await page.getByRole("button", { name: "打开设置", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "设置", exact: true });
    await dialog.waitFor({ state: "visible" });
    const toggle = dialog.getByRole("switch", { name: "动态特效", exact: true });
    await dialog.locator('section[aria-labelledby="animation-settings-title"]').scrollIntoViewIfNeeded();
    const layout = await dialog.evaluate(el => {
      const bounds = node => { const b = node.getBoundingClientRect(); return { x: b.x, y: b.y, width: b.width, height: b.height, right: b.right, bottom: b.bottom }; };
      const toggle = el.querySelector('button[aria-labelledby="animation-settings-title"]');
      const close = el.querySelector('[aria-label="关闭弹窗"]');
      const closeBox = close.getBoundingClientRect();
      const closeAccessible = document.elementFromPoint(closeBox.x + closeBox.width / 2, closeBox.y + closeBox.height / 2)?.closest("button") === close;
      return { dialog: bounds(el), toggle: bounds(toggle), closeAccessible, overflow: el.scrollWidth > el.clientWidth + 1 };
    });
    assert.equal(layout.overflow, false, "设置内容横向溢出。");
    assert.equal(layout.closeAccessible, true, "设置关闭按钮被常驻控件遮挡。");
    for (const trigger of await page.locator("[data-game-fullscreen-trigger]").all()) {
      assert.equal(await trigger.isVisible(), false, "设置打开时全屏按钮仍覆盖弹窗。");
    }
    for (const [name, box] of Object.entries({ dialog: layout.dialog, toggle: layout.toggle })) {
      assert.ok(box.x >= -1 && box.y >= -1 && box.right <= viewport.width + 1 && box.bottom <= viewport.height + 1,
        `${name} 超出可视区：${JSON.stringify(box)}`);
    }
    assert.ok(layout.toggle.width >= 44 && layout.toggle.height >= 44, `特效开关触控不足44px：${JSON.stringify(layout.toggle)}`);
    assert.equal(await toggle.getAttribute("aria-checked"), String(expectedEnabled));
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `effects-settings-${viewport.width}-${page.url().endsWith("/home") ? "lobby" : "game"}.png`) });
    return { dialog, toggle };
  };
  // 新浏览器和旧版保存的关闭状态都应恢复开启；迁移后再次关闭必须持久化。
  await page.waitForFunction(() => document.documentElement.dataset.visualEffects === "on");
  let initialSettings = await openSettings(true);
  await initialSettings.toggle.click();
  await initialSettings.dialog.getByRole("button", { name: "关闭弹窗", exact: true }).click();
  await checkStatic();
  await page.evaluate(() => localStorage.setItem("grandumi_settings", JSON.stringify({
    visualEffectsEnabled: false, animationSpeed: "off", hideOpponentCardBack: true,
  })));
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.locator(".blocker-aura").first().waitFor({ state: "visible" });
  await page.waitForFunction(() => document.documentElement.dataset.visualEffects === "on");
  initialSettings = await openSettings(true);
  assert.equal(await initialSettings.dialog.getByRole("button", { name: "标准", exact: true }).getAttribute("aria-pressed"), "true");
  await initialSettings.toggle.click();
  await initialSettings.dialog.getByRole("button", { name: "关闭弹窗", exact: true }).click();
  await checkStatic();
  let settings = await openSettings();
  await settings.toggle.click();
  await page.waitForFunction(() => document.documentElement.dataset.visualEffects === "on");
  assert.equal(await settings.toggle.getAttribute("aria-checked"), "true");
  await settings.dialog.getByRole("button", { name: "快速", exact: true }).click();
  await settings.dialog.getByRole("button", { name: "关闭弹窗", exact: true }).click();
  await page.waitForFunction(() => document.querySelectorAll("animateMotion").length > 0);
  assert.equal(await page.locator(".blocker-aura").first().evaluate(el => getComputedStyle(el).animationName), "blocker-rotate");
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.waitForFunction(() => document.documentElement.dataset.visualEffects === "on" && document.querySelectorAll("animateMotion").length > 0);
  await page.emulateMedia({ reducedMotion: "reduce" });
  await checkStatic();
  await page.emulateMedia({ reducedMotion: "no-preference" });
  await page.waitForFunction(() => document.documentElement.dataset.visualEffects === "on" && document.querySelectorAll("animateMotion").length > 0);
  await page.getByRole("button", { name: "打开设置", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "设置", exact: true });
  const toggle = dialog.getByRole("switch", { name: "动态特效", exact: true });
  await toggle.scrollIntoViewIfNeeded();
  assert.equal(await dialog.getByRole("button", { name: "快速", exact: true }).getAttribute("aria-pressed"), "true");
  await toggle.click();
  await dialog.getByRole("button", { name: "关闭弹窗", exact: true }).click();
  await checkStatic();
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.locator(".blocker-aura").first().waitFor({ state: "visible" });
  await checkStatic();
  // 同一设置入口在未旋转大厅中也必须可见、可点击和可滚动。
  await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
  settings = await openSettings();
  await settings.dialog.getByRole("button", { name: "关闭弹窗", exact: true }).click();
  // 已经播放的开场演出也必须在关闭开关后立即退出，不能继续遮挡操作。
  await page.goto(`${baseUrl}/layout-verification/player-identities?view=intro`, { waitUntil: "domcontentloaded" });
  const intro = page.locator('[data-leader-clash="playing"]');
  await intro.waitFor({ state: "visible" });
  await page.getByRole("button", { name: "打开设置", exact: true }).click();
  const introSettings = page.getByRole("dialog", { name: "设置", exact: true });
  const introToggle = introSettings.getByRole("switch", { name: "动态特效", exact: true });
  await introToggle.scrollIntoViewIfNeeded();
  await introToggle.click();
  await intro.waitFor({ state: "hidden" });
  await introSettings.getByRole("button", { name: "关闭弹窗", exact: true }).click();
  assert.deepEqual(errors, [], "特效开关出现浏览器运行错误。");
  console.log(`动态特效回归通过：${viewport.width}×${viewport.height}`);
}
