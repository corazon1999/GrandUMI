import assert from "node:assert/strict";

/** 检查密集手牌及中大牌尺寸不会遮住咚卡组数量，使用真实牌桌和竖屏旋转。 */
export async function verifyDonDeckVisibility(page, baseUrl, viewport) {
  await page.route("**/*", route => route.request().resourceType() === "image"
    ? route.fulfill({ status: 200, contentType: "image/svg+xml", body: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="140"><rect width="100" height="140" fill="#172b45"/></svg>' })
    : route.continue());
  await page.goto(`${baseUrl}/layout-verification/visual-effects?hand=12`, { waitUntil: "domcontentloaded" });
  const saved = await page.evaluate(() => localStorage.getItem("grandumi_settings"));
  try {
    for (const cardSize of ["auto", "md", "lg"]) {
      await page.evaluate(({ saved, cardSize }) => {
        localStorage.setItem("grandumi_settings", JSON.stringify({ ...JSON.parse(saved || "{}"), cardSize }));
      }, { saved, cardSize });
      await page.reload({ waitUntil: "domcontentloaded" });
      const hand = page.locator('[data-hand-order-id="layout-hand-3"]');
      await hand.waitFor({ state: "visible" });
      await hand.hover();
      await page.waitForTimeout(400);
      const result = await page.locator('[data-don-deck="my"] [data-don-deck-count]').evaluate(badge => {
        const bounds = badge.getBoundingClientRect();
        const visible = [0.2, 0.5, 0.8].every(x => [0.2, 0.5, 0.8].every(y => {
          const hit = document.elementFromPoint(bounds.x + bounds.width * x, bounds.y + bounds.height * y);
          return hit === badge || badge.contains(hit);
        }));
        return {
          text: badge.textContent.trim(),
          visible,
          inViewport: bounds.x >= 0 && bounds.y >= 0 && bounds.right <= innerWidth + 1 && bounds.bottom <= innerHeight + 1,
          handCount: document.querySelectorAll("[data-hand-order-id]").length,
          rotated: document.querySelector("[data-layout-rotated]")?.getAttribute("data-layout-rotated"),
        };
      });
      assert.equal(result.text, "5", "咚卡组数量未按快照显示。");
      assert.equal(result.handCount, 12, "密集手牌夹具没有正确加载。");
      assert.equal(result.visible && result.inViewport, true,
        `${viewport.width}×${viewport.height}/${cardSize}的咚数量被遮挡或越界：${JSON.stringify(result)}`);
      if (viewport.width < 500) assert.equal(result.rotated, "true", "手机竖屏未验证实际旋转画布。");
    }
    console.log(`密集手牌咚数量通过：${viewport.width}×${viewport.height}，自动/中/大三档。`);
  } finally {
    await page.evaluate(saved => saved === null
      ? localStorage.removeItem("grandumi_settings")
      : localStorage.setItem("grandumi_settings", saved), saved);
    await page.close();
  }
}
