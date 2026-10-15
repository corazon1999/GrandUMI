import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

/** 派生图及异画缺失时，真实图鉴和详情应继续尝试仍可用的本地原图。 */
export async function verifyCardImageDetails(page, baseUrl, viewport) {
  await page.bringToFront();
  const image = fs.readFileSync(path.resolve(import.meta.dirname,
    "../opcgpro-web/public/sprites-thumb/OP12/OP12-102.webp"));
  const errors = [];
  const requests = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.route("**/cards*/op12/OP12-102*", async route => {
    const pathname = new URL(route.request().url()).pathname;
    requests.push(pathname);
    // 只提供默认原图，真实走完缺失派生图、缺失异画到默认原图的回退链。
    if (pathname === "/cards/op12/OP12-102.png")
      await route.fulfill({ contentType: "image/webp", body: image });
    else await route.fulfill({ status: 404, body: "验证场景：图片不存在" });
  });
  await page.routeWebSocket("**/ws", socket => socket.onMessage(raw => {
    const message = JSON.parse(String(raw));
    if (message.proto === "MsgSecret") {
      socket.send(JSON.stringify({ proto: "MsgSecret", result: true }));
      socket.send(JSON.stringify({ proto: "MsgLogin", result: true,
        account: "card-image-fixture", name: "卡图验证", decks: [], authToken: "card-image-fixture" }));
    } else if (message.proto === "MsgPing") socket.send(JSON.stringify(message));
  }));
  const acknowledge = page.getByRole("button", { name: "我知道了", exact: true });
  // 同一浏览器此前可能登录过其它验证账号；等待动作时也正常确认异步出现的更新日志。
  await page.addLocatorHandler(acknowledge, async () => { await acknowledge.click(); });
  await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
  await page.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
  const desktopCatalog = page.getByRole("button", { name: "卡牌图鉴", exact: true });
  await (await desktopCatalog.isVisible()
    ? desktopCatalog : page.getByRole("button", { name: "图鉴", exact: true })).click();
  await page.getByRole("heading", { name: "卡牌图鉴", exact: true }).waitFor();
  await page.locator('input[type="search"]:visible').fill("OP12-102");
  const card = page.getByRole("button", { name: "查看 白星（OP12-102）详情", exact: true });
  await card.waitFor();
  await page.waitForFunction(() => [...document.querySelectorAll('button[aria-label="查看 白星（OP12-102）详情"] img')]
    .some(element => element.complete && element.naturalWidth > 0 && element.src.includes("/cards/op12/OP12-102.png")),
  null, { timeout: 15_000 });
  assert.ok(requests.includes("/cards-thumb/op12/OP12-102_01.webp"));
  assert.ok(requests.includes("/cards/op12/OP12-102.png"));
  assert.doesNotMatch(await card.innerText(), /图片暂不可用/);
  await card.click();
  const dialog = page.getByRole("dialog", { name: "白星", exact: true });
  await dialog.waitFor();
  await page.waitForFunction(() => [...document.querySelectorAll("[data-card-info-image] img")]
    .some(element => element.complete && element.naturalWidth > 0 && element.src.includes("/cards/op12/OP12-102.png")));
  const close = dialog.getByRole("button", { name: "关闭弹窗", exact: true });
  // 等弹窗缩放动画回到实际48像素，避免把进场中的0.9倍缩放当成触控区不足。
  await page.waitForFunction(() => {
    const button = document.querySelector('button[aria-label="关闭弹窗"]');
    const box = button?.getBoundingClientRect();
    return box && box.width >= 47.5 && box.height >= 47.5;
  });
  const bounds = await close.boundingBox();
  assert.ok(bounds && bounds.width >= 44 && bounds.height >= 44 && bounds.x >= -1 && bounds.y >= -1
    && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1,
  `卡牌详情关闭按钮越界或触控范围不足：${JSON.stringify(bounds)}`);
  if (process.env.GRANDUMI_TEST_TEMP_ROOT)
    await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `feedback-card-image-${viewport.width}.png`) });
  await close.click();
  await dialog.waitFor({ state: "hidden" });
  assert.deepEqual(errors, []);
  console.log(`卡图回退及详情验证通过：${viewport.width}×${viewport.height}`);
}
