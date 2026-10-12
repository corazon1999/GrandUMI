import assert from "node:assert/strict";
import path from "node:path";

export async function verifyGamePreparation(page, baseUrl, viewport) {
  const mobile = viewport.width < 600;
  for (const view of ["waiting", "failed"]) {
    await page.goto(`${baseUrl}/layout-verification/game-preparation?device=${mobile ? "mobile" : "desktop"}&view=${view}`, { waitUntil: "domcontentloaded" });
    const overlay = page.locator(`[data-game-preparation="${view}"]`);
    await overlay.waitFor({ state: "visible" });
    if (mobile) assert.equal(await page.locator("[data-layout-preview]").getAttribute("data-layout-rotated"), "true");
    const panel = overlay.locator("div").first();
    const box = await panel.boundingBox();
    assert.ok(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1
      && box.y + box.height <= viewport.height + 1, `开局提示超出视口：${JSON.stringify(box)}`);
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({
      path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `game-preparation-${view}-${viewport.width}.png`),
    });
    if (view === "failed") {
      const button = page.getByRole("button", { name: "重新加载", exact: true });
      const bounds = await button.boundingBox();
      assert.ok(bounds && bounds.width >= 44 && bounds.height >= 44, `旋转后重试按钮触控区不足：${JSON.stringify(bounds)}`);
      await button.click();
      await page.locator('[data-game-preparation="waiting"]').waitFor({ state: "visible" });
      assert.equal(await page.locator("[data-game-preparation]").getByRole("alert").count(), 0);
    }
  }
  await page.close();
}
