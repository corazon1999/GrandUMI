// 针对本次61张新卡，检查真实图鉴、详情、卡图及三档视口。
// 用法：GRANDUMI_BROWSER_BASE_URL=http://127.0.0.1:3000 node tools/verify-op18-october29.mjs
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { createRequire } from "node:module";
import { spawn } from "node:child_process";
import net from "node:net";

const root = path.resolve(import.meta.dirname, "..");
const webRoot = path.join(root, "opcgpro-web");
const { chromium } = createRequire(path.join(webRoot, "package.json"))("playwright-core");
const newNumbers = new Set("002 004 005 006 007 008 009 012 014 019 020 023 026 027 030 033 035 037 038 039 040 042 043 045 049 050 053 054 057 058 059 062 067 068 071 072 073 074 080 081 082 083 087 088 090 094 095 096 097 098 099 102 104 105 107 109 110 114 115 116 117"
  .split(" ").map(suffix => `OP18-${suffix}`));
const cards = JSON.parse(await fs.readFile(path.join(root, "卡牌数据/OP18.json"), "utf8")).filter(card => newNumbers.has(card.number));
assert.equal(cards.length, 61);
assert.ok(process.env.GRANDUMI_TEST_TEMP_ROOT, "请通过GrandUmiTemp.ps1设置验证临时目录。");
const candidates = [process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH, chromium.executablePath(),
  "C:/Program Files/Google/Chrome/Application/chrome.exe", "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"];
let executablePath;
for (const candidate of candidates) if (candidate && await fs.stat(candidate).then(() => true, () => false)) { executablePath = candidate; break; }
assert.ok(executablePath, "未找到可用的浏览器。");
let child;
let browser;
let baseUrl = process.env.GRANDUMI_BROWSER_BASE_URL;
try {
  if (!baseUrl) {
    const port = await new Promise((resolve, reject) => {
      const server = net.createServer(); server.once("error", reject);
      server.listen(0, "127.0.0.1", () => { const port = server.address().port; server.close(() => resolve(port)); });
    });
    baseUrl = `http://127.0.0.1:${port}`;
    child = spawn(process.execPath, [path.join(webRoot, "node_modules/next/dist/bin/next"), "start", "-H", "127.0.0.1", "-p", String(port)],
      { cwd: webRoot, env: { ...process.env, NEXT_TELEMETRY_DISABLED: "1" }, stdio: ["ignore", "pipe", "pipe"] });
    let output = ""; child.stdout.on("data", chunk => { output += chunk; }); child.stderr.on("data", chunk => { output += chunk; });
    const deadline = Date.now() + 60000;
    while (true) {
      if (child.exitCode !== null) throw Error(`前端提前退出：${output}`);
      if (await fetch(baseUrl).then(r => r.status < 500, () => false)) break;
      if (Date.now() > deadline) throw Error(`等待前端超时：${output}`);
      await new Promise(resolve => setTimeout(resolve, 250));
    }
  }
  browser = await chromium.launch({ headless: true, executablePath });
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 360, height: 780 }]) {
    const context = await browser.newContext({ viewport, isMobile: viewport.width < 600, hasTouch: viewport.width < 600 });
    const page = await context.newPage(); const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.routeWebSocket("**/ws", socket => socket.onMessage(raw => {
      const message = JSON.parse(String(raw));
      if (message.proto === "MsgSecret") {
        socket.send(JSON.stringify({ proto: "MsgSecret", result: true }));
        socket.send(JSON.stringify({ proto: "MsgLogin", result: true, account: "op18-import-verification", name: "卡牌导入验证", decks: [], authToken: "op18-import-verification" }));
      } else if (message.proto === "MsgPing") socket.send(JSON.stringify(message));
    }));
    await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "开始对战", exact: true }).waitFor();
    const acknowledge = page.getByRole("button", { name: "我知道了", exact: true });
    await acknowledge.waitFor(); await acknowledge.click(); await acknowledge.waitFor({ state: "hidden" });
    const desktop = page.getByRole("button", { name: "卡牌图鉴", exact: true });
    await (await desktop.isVisible() ? desktop : page.getByRole("button", { name: "图鉴", exact: true })).click();
    await page.getByRole("heading", { name: "卡牌图鉴", exact: true }).waitFor();
    for (const card of cards) {
      await page.locator('input[type="search"]:visible').fill(card.number);
      const button = page.getByRole("button", { name: `查看 ${card.name}（${card.number}）详情`, exact: true });
      await button.waitFor();
      await button.locator("img").evaluate(image => new Promise((resolve, reject) => {
        if (image.complete && image.naturalWidth > 0) return resolve();
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", () => reject(Error("新卡缩略图加载失败")), { once: true });
      }));
      assert.doesNotMatch(await button.innerText(), /暂不可对战|图片暂不可用/);
      await button.click(); const dialog = page.getByRole("dialog", { name: card.name, exact: true }); await dialog.waitFor();
      await page.waitForFunction(number => [...document.querySelectorAll("[data-card-info-image] img")].some(image => image.complete && image.naturalWidth > 0 && image.src.includes(number)), card.number);
      assert.doesNotMatch(await dialog.innerText(), /效果开发中|当前暂不可用于对战/);
      const close = dialog.getByRole("button", { name: "关闭弹窗", exact: true });
      await page.waitForFunction(() => { const box = document.querySelector('button[aria-label="关闭弹窗"]')?.getBoundingClientRect(); return box && box.width >= 47.5; });
      await page.waitForFunction(() => {
        const dialog = document.querySelector('[role="dialog"]');
        if (!dialog) return false;
        for (let element = dialog; element; element = element.parentElement)
          if (Number(getComputedStyle(element).opacity) < 0.999) return false;
        return true;
      });
      const bounds = await close.boundingBox();
      assert.ok(bounds && bounds.width >= 44 && bounds.height >= 44 && bounds.x >= -1 && bounds.y >= -1
        && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1, `${card.number}关闭按钮越界`);
      if (card.number === "OP18-117") await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `op18-${viewport.width}.png`) });
      await close.click(); await dialog.waitFor({ state: "hidden" });
    }
    assert.deepEqual(errors, []);
    console.log(`OP18新卡真实浏览器验证通过：${viewport.width}×${viewport.height}，61张卡牌资料、缩略图、详情图、可用状态与关闭按钮。`);
    await context.close();
  }
} finally {
  await browser?.close();
  if (child) { child.kill("SIGTERM"); await Promise.race([new Promise(resolve => child.once("exit", resolve)), new Promise(resolve => setTimeout(resolve, 5000))]); }
}
