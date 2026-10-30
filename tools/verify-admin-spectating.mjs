import assert from "node:assert/strict";

/** 检查管理员可进入全部权限模式，普通玩家受限，双方界面不提供移除管理员操作。 */
export async function verifyAdminSpectating(page, baseUrl, viewport) {
  await page.goto(`${baseUrl}/layout-verification/spectating`, { waitUntil: "domcontentloaded" });
  for (const mode of ["open", "closed", "friends", "password"]) {
    const button = page.locator(`[data-spectate-mode="${mode}"] button`);
    await button.waitFor({ state: "visible" });
    assert.equal(await button.isEnabled(), true, `管理员的${mode}观战入口被禁用`);
    const box = await button.boundingBox();
    assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= 0 && box.y >= 0
      && box.x + box.width <= viewport.width && box.y + box.height <= viewport.height, "观战入口触控区不足或超出视口");
    await button.click();
    await page.waitForFunction(expected => document.querySelector("[data-spectate-entered]")?.textContent === expected, mode);
    assert.equal(await page.getByRole("dialog").count(), 0, "管理员仍被要求输入观战码");
  }
  await page.goto(`${baseUrl}/layout-verification/spectating?admin=false`, { waitUntil: "domcontentloaded" });
  const closed = page.locator('[data-spectate-mode="closed"] button');
  await closed.waitFor();
  assert.equal(await closed.isDisabled(), true);
  assert.equal(await page.locator('[data-spectate-mode="friends"] button').isDisabled(), true);
  await page.locator('[data-spectate-mode="password"] button').click();
  await page.getByRole("dialog", { name: "输入观战码" }).waitFor();

  await page.goto(`${baseUrl}/layout-verification/spectating?view=game`, { waitUntil: "domcontentloaded" });
  if (viewport.width < 500) {
    const canvas = page.locator('[data-layout-preview="mobile-landscape"][data-layout-rotated="true"]');
    await canvas.waitFor({ state: "visible" });
  }
  // 电脑检查观战席，手机检查实际旋转画布上的观战列表。
  if (viewport.width >= 500) {
    const admin = page.getByRole("button", { name: "观战者 管理员观众", exact: true });
    await admin.waitFor({ state: "visible" });
    await admin.click();
    const details = page.locator("[data-spectator-seat-details]");
    await details.waitFor();
    assert.equal(await details.getByRole("button", { name: /移出/ }).count(), 0);
    await page.getByRole("button", { name: "观战者 普通观众", exact: true }).click();
    await details.getByRole("button", { name: "移出观战席", exact: true }).waitFor();
  }
  if (viewport.width < 500) {
    const trigger = page.locator("[data-mobile-spectator-trigger]");
    await trigger.click();
    const row = trigger.locator("..").getByText("管理员观众", { exact: true }).locator("..").locator("..");
    assert.equal(await row.getByRole("button", { name: /踢出/ }).count(), 0);
    const kick = page.getByRole("button", { name: "踢出", exact: true });
    await kick.waitFor({ state: "visible" });
    const box = await kick.boundingBox();
    assert.ok(box && Math.min(box.width, box.height) >= 44 && box.x >= -1 && box.y >= -1
      && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, `旋转观战列表操作越界或触控区不足：${JSON.stringify(box)}`);
  }
  console.log(`管理员观战浏览器验证通过：${viewport.width}×${viewport.height}。`);
  await page.close();
}
