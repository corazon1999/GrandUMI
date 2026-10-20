import assert from "node:assert/strict";

export async function verifyLobbyEffects(page, baseUrl, viewport) {
  await page.routeWebSocket("**", socket => socket.onMessage(() => {}));
  const scenarios = [
    ["排行榜", "/layout-verification/hunters?view=rank"],
    ["个人中心", "/layout-verification/hunters?view=profile"],
    ["玩家列表", "/layout-verification/player-identities?view=players"],
    ["聊天", "/layout-verification/player-identities?view=chat"],
    ["称号典藏", "/layout-verification/hunters?view=titles"],
  ];
  // 全部通过真实设置切换；跨页面保持同一选择，关闭后再检查每个大厅功能。
  for (const enabled of [false, true, false]) {
    await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
    await page.getByRole("button", { name: "打开设置", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "设置", exact: true });
    const toggle = dialog.getByRole("switch", { name: "动态特效", exact: true });
    await toggle.scrollIntoViewIfNeeded();
    if ((await toggle.getAttribute("aria-checked") === "true") !== enabled) await toggle.click();
    await dialog.getByRole("button", { name: "关闭弹窗", exact: true }).click();
    for (const [label, route] of scenarios) {
      await page.goto(`${baseUrl}${route}`, { waitUntil: "domcontentloaded" });
      await page.locator("[data-equipped-season-title]:visible, [data-season-honor]:visible").first().waitFor({ state: "visible" });
      await page.waitForFunction(value => document.documentElement.dataset.visualEffects === value, enabled ? "on" : "off");
      const loops = () => page.evaluate(() => document.getAnimations().filter(a => a.playState === "running" && a.effect?.getTiming().iterations === Infinity).length);
      if (enabled) await page.waitForFunction(() => document.getAnimations().some(a => a.playState === "running" && a.effect?.getTiming().iterations === Infinity));
      else assert.equal(await loops(), 0, `${label} 关闭特效后仍有循环动画。`);
    }
  }
  await page.close();
  console.log(`大厅特效开关通过：${viewport.width}×${viewport.height}`);
}
