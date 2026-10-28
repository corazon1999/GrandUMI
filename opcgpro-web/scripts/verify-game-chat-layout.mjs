// 先以 GRANDUMI_LAYOUT_VERIFICATION=1 启动前端；临时目录由 GrandUmiTemp.ps1 提供。
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright-core";

const output = process.env.GRANDUMI_TEST_TEMP_ROOT;
assert.ok(output && (process.platform !== "win32" || /^E:\\GrandUMI-Temp\\/i.test(output)), "请设置项目专用的验证临时目录");
await mkdir(output, { recursive: true });
const origin = process.env.GRANDUMI_VERIFY_ORIGIN ?? "http://127.0.0.1:3010";
const browser = await chromium.launch({ headless: true, ...(process.platform === "win32" ? { channel: "msedge" } : {}) });
const reports = [];

async function checkAccessible(page, locator) {
  const box = await locator.boundingBox();
  const viewport = page.viewportSize();
  assert.ok(box && box.width >= 44 && box.height >= 44, "按钮触控区域必须至少为44×44px");
  assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "按钮不得超出可视区域");
  const hit = await locator.evaluate((button) => {
    const rect = button.getBoundingClientRect();
    const target = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
    return { accessible: button.contains(target), text: button.textContent, target: target?.outerHTML.slice(0, 240) };
  });
  assert.ok(hit.accessible, `按钮不得被其他控件遮挡：${JSON.stringify(hit)}`);
}

try {
  for (const scenario of [
    { width: 1440, height: 900, mobile: false },
    { width: 390, height: 844, mobile: false },
    { width: 360, height: 780, mobile: false },
    { width: 390, height: 844, mobile: true },
    { width: 360, height: 780, mobile: true },
  ]) {
    const { width, height, mobile } = scenario;
    const context = await browser.newContext({ viewport: { width, height }, hasTouch: width < 600, isMobile: width < 600 });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    // 复用真实牌桌样本，同时显示观战与菜单入口，检查常驻控件的遮挡。
    await page.goto(`${origin}/layout-verification/card-sort?device=${mobile ? "mobile" : "desktop"}&crowded=1`);
    const trigger = page.getByRole("button", { name: "打开局内聊天", exact: true });
    await trigger.waitFor();
    await page.locator("[data-layout-preview]").evaluate((frame) => {
      for (const side of ["top", "right", "bottom", "left"]) frame.style.setProperty(`--layout-safe-${side}`, "12px");
    });
    if (mobile) assert.equal(await page.locator('[data-layout-preview][data-layout-rotated="true"]').count(), 1, "手机必须验证实际旋转画布");
    await checkAccessible(page, trigger);
    await trigger.click();
    const presets = page.getByRole("group", { name: "快捷聊天", exact: true });
    await presets.waitFor();
    const geometry = await presets.evaluate((rail) => {
      const buttons = [...rail.querySelectorAll("button")];
      return {
        rows: [...new Set(buttons.map((button) => button.offsetTop))].length,
        overflowing: rail.scrollWidth > rail.clientWidth,
        height: rail.offsetHeight,
        buttons: buttons.map((button) => ({ text: button.textContent, width: button.offsetWidth, height: button.offsetHeight, singleLine: button.scrollHeight <= button.clientHeight })),
      };
    });
    assert.equal(geometry.rows, 1, "快捷语必须始终只有一排");
    assert.equal(geometry.buttons.length, 9, "必须保留全部九条快捷语");
    assert.ok(geometry.overflowing, "超出面板宽度的快捷语必须允许横向滚动");
    assert.ok(geometry.height < 72, "快捷语栏不得占用多排高度");
    for (const button of geometry.buttons) {
      assert.ok(button.width >= 48 && button.height >= 48 && button.singleLine, `${button.text} 必须保持完整单行和足够触控尺寸`);
    }
    const first = presets.getByRole("button", { name: "你好", exact: true });
    await checkAccessible(page, first);
    await checkAccessible(page, page.getByRole("button", { name: "收起局内聊天", exact: true }));
    await page.locator('[placeholder="输入局内消息…"]').fill("布局验证");
    await checkAccessible(page, page.getByRole("button", { name: "发送", exact: true }));
    await page.screenshot({ path: path.join(output, `quick-chat-${width}x${height}-${mobile ? "rotated" : "normal"}.png`) });

    // 用键盘移动到最后一条，浏览器须自动滚动，使长句完整可达。
    await first.focus();
    for (let index = 1; index < geometry.buttons.length; index++) await page.keyboard.press("Tab");
    const last = presets.getByRole("button", { name: "老板来了，等我一会", exact: true });
    assert.equal(await last.evaluate((button) => button === document.activeElement), true, "键盘必须能访问最后一条快捷语");
    // 浏览器的焦点滚动可能跨越多个动画帧，等实际可见后再检查命中位置。
    await page.waitForFunction(() => {
      const button = document.activeElement;
      const rail = button?.closest("[data-game-chat-presets]");
      if (!rail) return false;
      const inner = button.getBoundingClientRect();
      const outer = rail.getBoundingClientRect();
      return inner.left >= outer.left - 1 && inner.right <= outer.right + 1 && inner.top >= outer.top - 1 && inner.bottom <= outer.bottom + 1;
    });
    assert.ok(await presets.evaluate((rail) => rail.scrollLeft > 0), "后排快捷语应滚动到视区内");
    await page.screenshot({ path: path.join(output, `quick-chat-${width}x${height}-${mobile ? "rotated" : "normal"}-last.png`) });
    await checkAccessible(page, last);
    await last.click();
    assert.equal(await presets.locator("button:disabled").count(), 9, "快捷语发送后必须保留冷却");
    await page.waitForFunction(() => !document.querySelector("[data-game-chat-presets] button").disabled);
    assert.deepEqual(errors, [], "聊天操作不得产生浏览器运行时错误");
    reports.push({ ...scenario, rows: geometry.rows, presetHeight: geometry.height, result: "通过" });
    await context.close();
  }
  console.log(JSON.stringify({ message: "单排快捷聊天浏览器回归通过", reports }, null, 2));
} finally {
  await browser.close();
}
