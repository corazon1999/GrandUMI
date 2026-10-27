// 先以 GRANDUMI_LAYOUT_VERIFICATION=1 启动本地前端，再运行本脚本。
// 截图与浏览器临时文件由调用方通过 GrandUmiTemp.ps1 提供 E 盘目录。
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright-core";

const output = process.env.GRANDUMI_TEST_TEMP_ROOT;
assert.ok(output && (process.platform !== "win32" || /^E:\\GrandUMI-Temp\\/i.test(output)), "请设置项目专用的验证临时目录");
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.platform === "win32" ? { channel: "msedge" } : {}) });
const origin = process.env.GRANDUMI_VERIFY_ORIGIN ?? "http://127.0.0.1:3010";
const initial = ["sort-hand-0", "sort-hand-1", "sort-hand-2", "sort-hand-3", "sort-hand-4"];
const sorted = ["sort-hand-1", "sort-hand-3", "sort-hand-0", "sort-hand-4", "sort-hand-2"];
const reports = [];

async function expectValue(read, expected, message) {
  const deadline = Date.now() + 5000;
  let actual;
  do {
    actual = await read();
    if (JSON.stringify(actual) === JSON.stringify(expected)) return;
    await new Promise((resolve) => setTimeout(resolve, 50));
  } while (Date.now() < deadline);
  assert.deepEqual(actual, expected, message);
}

async function checkButton(page, locator) {
  const box = await locator.boundingBox();
  const size = page.viewportSize();
  assert.ok(box && box.width >= 44 && box.height >= 44, `触控区域不足：${JSON.stringify(box)}`);
  assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= size.width + 1 && box.y + box.height <= size.height + 1, "按钮超出可视区域");
  assert.ok(await locator.evaluate((button) => {
    const rect = button.getBoundingClientRect();
    return button.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
  }), "按钮被其他控件遮挡");
  return box;
}

try {
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 360, height: 780 }]) {
    const mobile = viewport.width < 600;
    const context = await browser.newContext({ viewport, hasTouch: mobile, isMobile: mobile });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await page.goto(`${origin}/layout-verification/card-sort?device=${mobile ? "mobile" : "desktop"}`);
    const sortHand = page.locator("[data-sort-hand]");
    await sortHand.waitFor();
    await expectValue(() => sortHand.isEnabled(), true, "手牌整理入口未就绪");
    await page.locator("[data-layout-preview]").evaluate((frame) => {
      for (const side of ["top", "right", "bottom", "left"]) frame.style.setProperty(`--layout-safe-${side}`, "12px");
    });
    const hand = () => page.locator('[data-hand-order-id][data-zone-side="my"]');
    const order = () => hand().evaluateAll((elements) => elements.map((element) => element.dataset.handOrderId));
    assert.deepEqual(await order(), initial);
    if (mobile) assert.equal(await page.locator('[data-layout-preview][data-layout-rotated="true"]').count(), 1, "手机必须使用实际旋转画布");
    const handBox = await checkButton(page, sortHand);
    // 先选中再整理，出牌仍指向同一实例的原始下标。
    await page.locator('[data-hand-order-id="sort-hand-0"]').click();
    await sortHand.click();
    await expectValue(order, sorted, "整理后的手牌顺序不正确");
    assert.equal(await page.locator("[data-card-sort-verification]").getAttribute("data-card-sort-request"), "", "整理不得发送对局操作请求");
    assert.equal(await page.locator("[data-card-sort-verification]").getAttribute("data-card-sort-selected"), "0");
    await page.screenshot({ path: path.join(output, `hand-sort-${viewport.width}x${viewport.height}.png`) });
    await page.getByRole("button", { name: "出牌", exact: true }).click();
    await expectValue(() => page.locator("[data-card-sort-verification]").getAttribute("data-card-sort-request"), "出牌:0");
    if (!mobile) {
      await page.locator('[data-hand-order-id="sort-hand-1"]').dragTo(page.locator('[data-hand-order-id="sort-hand-4"]'));
      assert.notDeepEqual(await order(), sorted, "整理后仍须支持手动拖动");
      await sortHand.click();
      await expectValue(order, sorted);
    }
    await page.locator("[data-verification-draw]").evaluate((button) => button.click());
    await expectValue(order, [...sorted, "sort-hand-new"], "抽牌不得自动覆盖玩家的本地顺序");
    await sortHand.click();
    await expectValue(order, ["sort-hand-1", "sort-hand-3", "sort-hand-new", "sort-hand-0", "sort-hand-4", "sort-hand-2"]);
    await page.locator("[data-verification-remove]").evaluate((button) => button.click());
    await expectValue(order, ["sort-hand-3", "sort-hand-new", "sort-hand-0", "sort-hand-4", "sort-hand-2"], "移除一张同号牌不得影响其他实例");
    await page.locator("[data-verification-counter]").evaluate((button) => button.click());
    await page.locator('[data-hand-order-id="sort-hand-3"]').click();
    await expectValue(() => page.locator("[data-card-sort-verification]").getAttribute("data-card-sort-request"), "反击:2");
    await page.locator('[data-zone="trash"][data-zone-side="my"]').click();
    const sortTrash = page.locator('[data-sort-trash="my"]');
    await sortTrash.waitFor();
    const trashBox = await checkButton(page, sortTrash);
    const trashOrder = () => page.locator('[data-trash-cards="my"] [data-trash-index]').evaluateAll((cards) => cards.map((card) => Number(card.dataset.trashIndex)));
    const recent = Array.from({ length: 50 }, (_, index) => 49 - index);
    assert.deepEqual(await trashOrder(), recent);
    await sortTrash.click();
    await expectValue(() => sortTrash.getAttribute("aria-pressed"), "true");
    const indices = await trashOrder();
    assert.deepEqual(indices.slice(0, 10), [1, 3, 6, 8, 11, 13, 16, 18, 21, 23]);
    assert.equal(new Set(indices).size, 50, "墓地整理不得丢牌或重复牌");
    const scroll = await page.locator('[data-trash-cards="my"]').evaluate((element) => ({ height: element.clientHeight, total: element.scrollHeight }));
    if (mobile) assert.ok(scroll.total > scroll.height, "手机上的大量墓地卡应能滚动查看");
    await page.screenshot({ path: path.join(output, `card-sort-${viewport.width}x${viewport.height}.png`) });
    await sortTrash.click();
    await expectValue(trashOrder, recent, "墓地应能恢复时间顺序");
    await page.getByRole("button", { name: "关闭", exact: true }).click();
    await page.locator('[data-zone="trash"][data-zone-side="opponent"]').click();
    await page.locator('[data-sort-trash="opponent"]').click();
    await expectValue(() => page.locator('[data-trash-cards="opponent"] [data-trash-index]').evaluateAll((cards) => cards.map((card) => Number(card.dataset.trashIndex))), [1, 3, 0, 4, 2]);
    await page.getByRole("button", { name: "关闭", exact: true }).click();
    assert.equal(await page.locator('[data-hand-order-id][data-zone-side="opponent"]').count(), 0, "隐藏手牌不得获得身份或排序入口");
    await page.locator("[data-verification-empty]").evaluate((button) => button.click());
    await expectValue(() => sortHand.isDisabled(), true);
    await page.locator('[data-zone="trash"][data-zone-side="my"]').click();
    assert.equal(await sortTrash.isDisabled(), true);
    assert.equal(await page.getByText("墓地为空", { exact: true }).count(), 1);
    assert.deepEqual(errors, [], "浏览器不得产生运行时错误");
    reports.push({ viewport, handBox, trashBox, result: "通过" });
    await context.close();
  }
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.goto(`${origin}/layout-verification/card-sort?device=mobile&observer=1`);
  await page.locator("[data-game-right-rail]").waitFor();
  assert.equal(await page.locator("[data-sort-hand]").count(), 0, "观战不得显示整理隐藏手牌的按钮");
  await page.close();
  console.log(JSON.stringify({ message: "手牌与墓地整理浏览器回归通过", reports }, null, 2));
} finally {
  await browser.close();
}
