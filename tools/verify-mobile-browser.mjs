import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import { createRequire } from "node:module";
import path from "node:path";
import process from "node:process";

const root = path.resolve(import.meta.dirname, "..");
const frontend = path.join(root, "opcgpro-web");
const requireFromFrontend = createRequire(path.join(frontend, "package.json"));
const { chromium } = requireFromFrontend("playwright-core");
const nextBin = path.join(frontend, "node_modules", "next", "dist", "bin", "next");

function resolveBrowserExecutable() {
  const candidates = [
    process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH,
    chromium.executablePath(),
  ];
  if (process.platform === "win32") {
    candidates.push(
      "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
      "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
    );
  }
  return candidates.find((candidate) => candidate && fs.existsSync(candidate));
}

async function freePort() {
  return await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = typeof address === "object" && address ? address.port : 0;
      server.close((error) => error ? reject(error) : resolve(port));
    });
  });
}

async function waitUntilReady(url, child, output) {
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`Next.js 提前退出（${child.exitCode}）：\n${output.value}`);
    try {
      const response = await fetch(url, { redirect: "manual" });
      if (response.status >= 200 && response.status < 500) return;
    } catch {
      // 构建后的服务器尚未监听。
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`等待 Next.js 启动超时：\n${output.value}`);
}

const port = await freePort();
const baseUrl = `http://127.0.0.1:${port}`;
const output = { value: "" };
const child = spawn(process.execPath, [nextBin, "start", "-H", "127.0.0.1", "-p", String(port)], {
  cwd: frontend,
  env: {
    ...process.env,
    NEXT_TELEMETRY_DISABLED: "1",
    GRANDUMI_LAYOUT_VERIFICATION: "1",
  },
  stdio: ["ignore", "pipe", "pipe"],
});
for (const stream of [child.stdout, child.stderr]) {
  stream.setEncoding("utf8");
  stream.on("data", (chunk) => { output.value = `${output.value}${chunk}`.slice(-12_000); });
}

let browser;
try {
  await waitUntilReady(`${baseUrl}/home`, child, output);
  const executablePath = resolveBrowserExecutable();
  browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
  for (const viewport of [{ width: 390, height: 844 }, { width: 360, height: 780 }]) {
    const context = await browser.newContext({ viewport, isMobile: true, hasTouch: true });
    const page = await context.newPage();
    await page.goto(`${baseUrl}/replay/layout-verification`, { waitUntil: "networkidle" });
    const canvas = page.locator('[data-layout-preview="mobile-landscape"]');
    await canvas.waitFor({ state: "visible" });
    assert.equal(await canvas.getAttribute("data-layout-rotated"), "true");

    const box = await canvas.boundingBox();
    assert.ok(box, "移动端旋转画布没有可见包围盒。");
    assert.ok(Math.abs(box.x) <= 1.5 && Math.abs(box.y) <= 1.5, `画布左上角越界：${JSON.stringify(box)}`);
    assert.ok(Math.abs(box.width - viewport.width) <= 2, `画布宽度未贴合视口：${JSON.stringify(box)}`);
    assert.ok(Math.abs(box.height - viewport.height) <= 2, `画布高度未贴合视口：${JSON.stringify(box)}`);

    const overflow = await page.evaluate(() => ({
      width: document.documentElement.scrollWidth,
      height: document.documentElement.scrollHeight,
      clientWidth: document.documentElement.clientWidth,
      clientHeight: document.documentElement.clientHeight,
    }));
    assert.ok(overflow.width <= overflow.clientWidth, `页面发生横向溢出：${JSON.stringify(overflow)}`);
    assert.ok(overflow.height <= overflow.clientHeight, `页面发生纵向溢出：${JSON.stringify(overflow)}`);

    const fullscreen = page.locator('button[aria-label="进入全屏"], button[aria-label="退出全屏"]').first();
    await fullscreen.waitFor({ state: "visible" });
    const buttonBox = await fullscreen.boundingBox();
    assert.ok(buttonBox && buttonBox.width >= 44 && buttonBox.height >= 44, `全屏按钮触控区域不足：${JSON.stringify(buttonBox)}`);
    assert.ok(
      buttonBox.x >= -1 && buttonBox.y >= -1
        && buttonBox.x + buttonBox.width <= viewport.width + 1
        && buttonBox.y + buttonBox.height <= viewport.height + 1,
      `全屏按钮超出安全可视区：${JSON.stringify(buttonBox)}`,
    );

    await page.goto(`${baseUrl}/layout-verification/hex-actions`, { waitUntil: "networkidle" });
    const hexCanvas = page.locator('[data-layout-preview="mobile-landscape"]');
    await hexCanvas.waitFor({ state: "visible" });
    assert.equal(await hexCanvas.getAttribute("data-layout-rotated"), "true");
    const lockedDonBadge = page.locator('[data-next-reset-inactive-count="2"]');
    await lockedDonBadge.waitFor({ state: "visible" });
    const lockedDonLayout = await page.evaluate(() => {
      const badge = document.querySelector('[data-next-reset-inactive-count="2"]');
      const slot = badge?.closest("button");
      const chatDock = document.querySelector("[data-game-control-dock]");
      if (!(badge instanceof HTMLElement) || !(slot instanceof HTMLButtonElement)
          || !(chatDock instanceof HTMLElement)) {
        throw new Error("咚!!锁定提示布局验证节点缺失。");
      }
      const badgeBox = badge.getBoundingClientRect();
      const slotBox = slot.getBoundingClientRect();
      const chatBox = chatDock.getBoundingClientRect();
      const overlapsChat = badgeBox.left < chatBox.right && badgeBox.right > chatBox.left
        && badgeBox.top < chatBox.bottom && badgeBox.bottom > chatBox.top;
      return {
        documentWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        documentHeight: document.documentElement.scrollHeight,
        clientHeight: document.documentElement.clientHeight,
        badge: { x: badgeBox.x, y: badgeBox.y, width: badgeBox.width, height: badgeBox.height },
        slot: { x: slotBox.x, y: slotBox.y, width: slotBox.width, height: slotBox.height },
        slotLayout: { width: slot.offsetWidth, height: slot.offsetHeight },
        overlapsChat,
      };
    });
    assert.ok(lockedDonLayout.documentWidth <= lockedDonLayout.clientWidth, `咚!!锁定提示页面横向溢出：${JSON.stringify(lockedDonLayout)}`);
    assert.ok(lockedDonLayout.documentHeight <= lockedDonLayout.clientHeight, `咚!!锁定提示页面纵向溢出：${JSON.stringify(lockedDonLayout)}`);
    assert.ok(lockedDonLayout.badge.x >= -1 && lockedDonLayout.badge.y >= -1
      && lockedDonLayout.badge.x + lockedDonLayout.badge.width <= viewport.width + 1
      && lockedDonLayout.badge.y + lockedDonLayout.badge.height <= viewport.height + 1,
    `咚!!锁定提示超出安全可视区：${JSON.stringify(lockedDonLayout)}`);
    assert.ok(lockedDonLayout.slotLayout.width >= 44 && lockedDonLayout.slotLayout.height >= 44,
      `咚!!休息区布局尺寸不足 44px：${JSON.stringify(lockedDonLayout)}`);
    assert.equal(lockedDonLayout.overlapsChat, false, `咚!!锁定提示与聊天控制坞重叠：${JSON.stringify(lockedDonLayout)}`);

    await page.goto(`${baseUrl}/layout-verification/cloud-replay`, { waitUntil: "networkidle" });
    const cloudPanel = page.locator("[data-cloud-replay-panel]");
    await cloudPanel.waitFor({ state: "visible" });
    assert.equal(await page.locator("[data-cloud-replay-item]").count(), 2, "云回放布局样本没有完整渲染。");

    const cloudLayout = await page.evaluate(() => {
      const panel = document.querySelector("[data-cloud-replay-panel]");
      const filters = document.querySelector("[data-cloud-replay-filters]");
      const shared = document.querySelector("[data-cloud-replay-shared-access]");
      if (!(panel instanceof HTMLElement)
          || !(filters instanceof HTMLElement)
          || !(shared instanceof HTMLElement)) {
        throw new Error("云回放布局验证节点缺失。");
      }
      const interactive = panel.querySelectorAll(
        "button, select, input:not([type='checkbox']), label:has(input[type='checkbox'])",
      );
      const undersized = Array.from(interactive)
        .map((element) => {
          const box = element.getBoundingClientRect();
          return { tag: element.tagName, label: element.getAttribute("aria-label") || element.textContent?.trim(), height: box.height };
        })
        .filter((entry) => entry.height < 43.5);
      const panelBox = panel.getBoundingClientRect();
      const filterColumns = getComputedStyle(filters).gridTemplateColumns.split(" ").filter(Boolean).length;
      const sharedColumns = getComputedStyle(shared).gridTemplateColumns.split(" ").filter(Boolean).length;
      return {
        documentWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        documentHeight: document.documentElement.scrollHeight,
        clientHeight: document.documentElement.clientHeight,
        panel: { x: panelBox.x, y: panelBox.y, width: panelBox.width, height: panelBox.height },
        panelScrollWidth: panel.scrollWidth,
        panelClientWidth: panel.clientWidth,
        filterColumns,
        sharedColumns,
        undersized,
      };
    });
    assert.ok(cloudLayout.documentWidth <= cloudLayout.clientWidth, `云回放页面横向溢出：${JSON.stringify(cloudLayout)}`);
    assert.ok(cloudLayout.documentHeight <= cloudLayout.clientHeight, `云回放页面纵向溢出：${JSON.stringify(cloudLayout)}`);
    assert.ok(cloudLayout.panelScrollWidth <= cloudLayout.panelClientWidth, `云回放面板横向溢出：${JSON.stringify(cloudLayout)}`);
    assert.ok(cloudLayout.panel.x >= -1 && cloudLayout.panel.y >= -1, `云回放面板左上角越界：${JSON.stringify(cloudLayout)}`);
    assert.ok(cloudLayout.panel.width <= viewport.width + 1 && cloudLayout.panel.height <= viewport.height + 1, `云回放面板超出视口：${JSON.stringify(cloudLayout)}`);
    assert.equal(cloudLayout.filterColumns, 1, `云回放筛选器在手机竖屏未切为单列：${JSON.stringify(cloudLayout)}`);
    assert.equal(cloudLayout.sharedColumns, 1, `分享凭证区在手机竖屏未切为单列：${JSON.stringify(cloudLayout)}`);
    assert.deepEqual(cloudLayout.undersized, [], `云回放存在不足 44px 的主要触控区：${JSON.stringify(cloudLayout.undersized)}`);

    await page.goto(`${baseUrl}/layout-verification/operations-workbench`, { waitUntil: "networkidle" });
    const operationsPanel = page.locator("[data-operations-workbench]");
    await operationsPanel.waitFor({ state: "visible" });
    assert.equal(await page.locator("[data-operations-case-list] button").count(), 2, "运营工作台 Case 样本没有完整渲染。");
    const operationsLayout = await page.evaluate(() => {
      const panel = document.querySelector("[data-operations-workbench]");
      const main = document.querySelector("[data-operations-workbench-layout-verification]");
      const filters = document.querySelector("[data-operations-workbench-filters]");
      const detail = document.querySelector("[data-operations-case-detail]");
      if (!(panel instanceof HTMLElement) || !(main instanceof HTMLElement)
          || !(filters instanceof HTMLElement) || !(detail instanceof HTMLElement)) {
        throw new Error("运营工作台布局验证节点缺失。");
      }
      const interactive = panel.querySelectorAll("button, select, input, summary");
      const undersized = Array.from(interactive)
        .filter((element) => {
          const style = getComputedStyle(element);
          return style.display !== "none" && style.visibility !== "hidden";
        })
        .map((element) => {
          const box = element.getBoundingClientRect();
          return { tag: element.tagName, label: element.getAttribute("aria-label") || element.textContent?.trim(), width: box.width, height: box.height };
        })
        .filter((entry) => entry.width > 0 && entry.height > 0 && entry.height < 43.5);
      const panelBox = panel.getBoundingClientRect();
      const detailBox = detail.getBoundingClientRect();
      return {
        documentWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        documentHeight: document.documentElement.scrollHeight,
        clientHeight: document.documentElement.clientHeight,
        mainScrollHeight: main.scrollHeight,
        mainClientHeight: main.clientHeight,
        panelScrollWidth: panel.scrollWidth,
        panelClientWidth: panel.clientWidth,
        panelX: panelBox.x,
        panelWidth: panelBox.width,
        detailWidth: detailBox.width,
        filterColumns: getComputedStyle(filters).gridTemplateColumns.split(" ").filter(Boolean).length,
        undersized,
      };
    });
    assert.ok(operationsLayout.documentWidth <= operationsLayout.clientWidth, `运营工作台页面横向溢出：${JSON.stringify(operationsLayout)}`);
    assert.ok(operationsLayout.documentHeight <= operationsLayout.clientHeight, `运营工作台页面纵向溢出：${JSON.stringify(operationsLayout)}`);
    assert.ok(operationsLayout.mainScrollHeight > operationsLayout.mainClientHeight, `运营工作台未提供内部纵向滚动：${JSON.stringify(operationsLayout)}`);
    assert.ok(operationsLayout.panelScrollWidth <= operationsLayout.panelClientWidth, `运营工作台横向溢出：${JSON.stringify(operationsLayout)}`);
    assert.ok(operationsLayout.panelX >= -1 && operationsLayout.panelWidth <= viewport.width + 1, `运营工作台超出手机视口：${JSON.stringify(operationsLayout)}`);
    assert.ok(operationsLayout.detailWidth <= viewport.width - 24 + 1, `Case 详情没有切为手机单列：${JSON.stringify(operationsLayout)}`);
    assert.equal(operationsLayout.filterColumns, 1, `运营工作台筛选器在手机竖屏未切为单列：${JSON.stringify(operationsLayout)}`);
    assert.deepEqual(operationsLayout.undersized, [], `运营工作台存在不足 44px 的触控区：${JSON.stringify(operationsLayout.undersized)}`);

    await page.getByRole("button", { name: "审计", exact: true }).click();
    await page.locator("[data-operations-audit]").waitFor({ state: "visible" });
    await page.getByRole("button", { name: "Doctor", exact: true }).click();
    await page.locator("[data-operations-doctor]").waitFor({ state: "visible" });
    const doctorButton = page.getByRole("button", { name: "申请修复凭证", exact: true });
    await doctorButton.scrollIntoViewIfNeeded();
    const doctorButtonBox = await doctorButton.boundingBox();
    assert.ok(doctorButtonBox && doctorButtonBox.height >= 44 && doctorButtonBox.width >= 44, `一致性修复按钮触控区域不足：${JSON.stringify(doctorButtonBox)}`);
    assert.ok(doctorButtonBox.x >= -1 && doctorButtonBox.x + doctorButtonBox.width <= viewport.width + 1, `一致性修复按钮横向越界：${JSON.stringify(doctorButtonBox)}`);
    await context.close();
  }
  const narrowViewport = { width: 344, height: 582 };
  const narrowContext = await browser.newContext({ viewport: narrowViewport, isMobile: true, hasTouch: true });
  const narrowPage = await narrowContext.newPage();
  await narrowPage.goto(`${baseUrl}/layout-verification/hex-actions`, { waitUntil: "networkidle" });
  const narrowCanvas = narrowPage.locator('[data-layout-preview="mobile-landscape"]');
  await narrowCanvas.waitFor({ state: "visible" });
  assert.equal(await narrowCanvas.getAttribute("data-layout-rotated"), "true");
  await narrowPage.locator('[data-next-reset-inactive-count="2"]').waitFor({ state: "visible" });
  const narrowLayout = await narrowPage.evaluate(() => {
    const badge = document.querySelector('[data-next-reset-inactive-count="2"]');
    const chatDock = document.querySelector("[data-game-control-dock]");
    if (!(badge instanceof HTMLElement) || !(chatDock instanceof HTMLElement)) {
      throw new Error("344×582 咚!!锁定提示布局验证节点缺失。");
    }
    const badgeBox = badge.getBoundingClientRect();
    const chatBox = chatDock.getBoundingClientRect();
    return {
      documentWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      documentHeight: document.documentElement.scrollHeight,
      clientHeight: document.documentElement.clientHeight,
      badge: { x: badgeBox.x, y: badgeBox.y, width: badgeBox.width, height: badgeBox.height },
      overlapsChat: badgeBox.left < chatBox.right && badgeBox.right > chatBox.left
        && badgeBox.top < chatBox.bottom && badgeBox.bottom > chatBox.top,
    };
  });
  assert.ok(narrowLayout.documentWidth <= narrowLayout.clientWidth && narrowLayout.documentHeight <= narrowLayout.clientHeight,
    `344×582 咚!!锁定提示页面溢出：${JSON.stringify(narrowLayout)}`);
  assert.ok(narrowLayout.badge.x >= -1 && narrowLayout.badge.y >= -1
    && narrowLayout.badge.x + narrowLayout.badge.width <= narrowViewport.width + 1
    && narrowLayout.badge.y + narrowLayout.badge.height <= narrowViewport.height + 1,
  `344×582 咚!!锁定提示超出安全可视区：${JSON.stringify(narrowLayout)}`);
  assert.equal(narrowLayout.overlapsChat, false, `344×582 咚!!锁定提示与聊天控制坞重叠：${JSON.stringify(narrowLayout)}`);
  await narrowContext.close();
  console.log("真实浏览器移动端回归通过：390×844、360×780 的既有页面门禁通过；344×582 的咚!!锁定提示可见、无溢出且未与聊天控制坞重叠。");
} finally {
  await browser?.close();
  child.kill("SIGTERM");
  await Promise.race([
    new Promise((resolve) => child.once("exit", resolve)),
    new Promise((resolve) => setTimeout(resolve, 5_000)),
  ]);
}
