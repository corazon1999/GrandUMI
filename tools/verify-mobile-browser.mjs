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

async function verifyChatDecorationExchange(page, baseUrl, viewport, view, expected) {
  await page.goto(`${baseUrl}/layout-verification/chat-decoration?view=${view}`, { waitUntil: "domcontentloaded" });
  const panel = page.locator("[data-chat-decoration-exchange]");
  await panel.waitFor({ state: "visible" });
  await page.locator(`[data-chat-decoration-wallet-balance="${expected.balance}"]`).waitFor({ state: "visible" });
  assert.doesNotMatch(await panel.innerText(), /\u4eba\u5934/, "交易所仍显示旧后端下发的计分称谓。");
  assert.match(await panel.innerText(), /S2 击败数量不计入交易所额度/, "交易所余额说明未完成前端兼容转换。");

  const itemIds = await page.locator("[data-chat-decoration-item]").evaluateAll((items) =>
    items.map((item) => item.getAttribute("data-chat-decoration-item")));
  assert.deepEqual(itemIds, expected.itemIds, `${view} 的拥有分组或目录顺序错误。`);

  const layout = await page.evaluate(() => {
    const root = document.querySelector("[data-chat-decoration-layout-verification]");
    const exchange = document.querySelector("[data-chat-decoration-exchange]");
    if (!(root instanceof HTMLElement) || !(exchange instanceof HTMLElement)) {
      throw new Error("聊天装饰交易所布局验证节点缺失。");
    }
    const rootBox = root.getBoundingClientRect();
    const interactive = Array.from(exchange.querySelectorAll("button"))
      .filter((element) => {
        const box = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return box.width > 0 && box.height > 0 && style.display !== "none" && style.visibility !== "hidden";
      })
      .map((element) => {
        const box = element.getBoundingClientRect();
        return {
          label: element.getAttribute("data-chat-decoration-item") || element.textContent?.trim().slice(0, 40),
          width: box.width,
          height: box.height,
        };
      });
    const ownedFlags = Array.from(exchange.querySelectorAll("[data-chat-decoration-item]"))
      .map((element) => element.getAttribute("data-chat-decoration-owned"));
    return {
      documentWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      documentHeight: document.documentElement.scrollHeight,
      clientHeight: document.documentElement.clientHeight,
      root: { x: rootBox.x, y: rootBox.y, width: rootBox.width, height: rootBox.height },
      panelScrollWidth: exchange.scrollWidth,
      panelClientWidth: exchange.clientWidth,
      panelScrollHeight: exchange.scrollHeight,
      panelClientHeight: exchange.clientHeight,
      interactive,
      ownedFlags,
      visibleText: exchange.innerText,
    };
  });
  assert.ok(layout.documentWidth <= layout.clientWidth, `${view} 页面发生横向溢出：${JSON.stringify(layout)}`);
  assert.ok(layout.documentHeight <= layout.clientHeight, `${view} 页面发生纵向溢出：${JSON.stringify(layout)}`);
  assert.ok(layout.panelScrollWidth <= layout.panelClientWidth, `${view} 交易所发生横向溢出：${JSON.stringify(layout)}`);
  assert.ok(layout.panelScrollHeight > layout.panelClientHeight, `${view} 交易所没有提供内部纵向滚动：${JSON.stringify(layout)}`);
  assert.ok(layout.root.x >= -1 && layout.root.y >= -1
    && layout.root.width <= viewport.width + 1 && layout.root.height <= viewport.height + 1,
  `${view} 交易所根节点超出手机视口：${JSON.stringify(layout.root)}`);
  assert.deepEqual(
    layout.interactive.filter((entry) => entry.width < 43.5 || entry.height < 43.5),
    [],
    `${view} 交易所存在不足 44×44px 的主要触控区。`,
  );
  const firstUnowned = layout.ownedFlags.indexOf("false");
  assert.ok(firstUnowned >= 0, `${view} 样本缺少未拥有条目。`);
  assert.ok(layout.ownedFlags.slice(0, firstUnowned).every((flag) => flag === "true")
    && layout.ownedFlags.slice(firstUnowned).every((flag) => flag === "false"),
  `${view} 未将全部拥有条目稳定放在未拥有条目前。`);
  assert.doesNotMatch(layout.visibleText, /(?:^|\s)RP(?:\s|$)|可用标准排位悬赏金\s*0\s*RP/,
    `${view} 仍显示 RP 钱包或价格。`);
  assert.match(layout.visibleText, /50,000,000\s*贝里/, `${view} 未显示固定 50,000,000 贝里价格。`);

  const firstPurchasable = page.locator('[data-chat-decoration-item="quote-pirate-king-man"]');
  await firstPurchasable.click();
  const purchaseButton = page.locator("[data-chat-decoration-purchase]");
  const purchaseBox = await purchaseButton.boundingBox();
  assert.ok(purchaseBox && purchaseBox.width >= 44 && purchaseBox.height >= 44,
    `${view} 购买按钮触控区域不足：${JSON.stringify(purchaseBox)}`);
}

async function verifyKnowledgePropertySearch(page, baseUrl, viewport) {
  await page.goto(`${baseUrl}/layout-verification/card-playability`, { waitUntil: "domcontentloaded" });
  const search = page.getByRole("searchbox", { name: "搜索卡名、卡号或关键词" });
  await search.fill("OP14-103");
  const summaries = page.locator("summary").filter({ hasText: "筛选条件" });
  if (await summaries.isVisible()) await summaries.click();
  const property = page.locator("select:visible").filter({ has: page.locator('option[value="知"]') });
  await property.waitFor({ state: "visible" });
  assert.equal(await property.locator('option[value="智"]').count(), 0, "属性列表仍显示智。");
  await property.selectOption("知");
  await page.locator('button[aria-label*="OP14-103"]').first().waitFor({ state: "visible" });
  const box = await property.boundingBox();
  assert.ok(box && box.x >= -1 && box.x + box.width <= viewport.width + 1,
    `属性筛选器超出视口：${JSON.stringify(box)}`);
  if (viewport.width < 1024) assert.ok(box.width >= 44 && box.height >= 44, "属性筛选触控区不足44×44。");
  await property.selectOption("");
}

async function verifyHunters(page, baseUrl, viewport) {
  // 排位布局验证使用浏览器内的连接替身，不访问外部游戏服务器。
  await page.routeWebSocket("**", socket => { socket.onMessage(() => {}); });
  const mobile = viewport.width < 600;
  for (const view of ["choice", "lobby", "lobby-zero", "rank", "profile", "effects", ...(mobile ? ["game", "win"] : [])]) {
    await page.goto(`${baseUrl}/layout-verification/hunters?view=${view}`, { waitUntil: "domcontentloaded" });
    await page.locator(view === "game" || view === "win" ? "[data-hex-actions-layout-verification]" : "[data-hunters-layout-verification]").waitFor({ state: "visible" });
    const overflow = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, width: innerWidth }));
    assert.ok(overflow.scroll <= overflow.width, `猎人 ${view} 页面横向溢出：${JSON.stringify(overflow)}`);
    assert.doesNotMatch(await page.locator("body").innerText(), /\u4eba\u5934/, `猎人 ${view} 页面仍显示旧计分称谓。`);
    const counters = page.locator("[data-hunter-defeats]:visible");
    for (const counter of await counters.all()) {
      const icon = counter.locator("[data-hunter-skull]");
      const box = await icon.boundingBox();
      assert.equal(await icon.count(), 1, "击败数量缺少骷髅头图标。");
      assert.ok(box && box.width > 0 && box.height > 0 && box.x >= -1 && box.x + box.width <= viewport.width + 1,
        `计分图标不可见或超出视口：${view}`);
      assert.equal(await counter.getAttribute("title"), "击败数量", "计分图标缺少可理解的提示。");
    }
    if (process.env.GRANDUMI_TEST_TEMP_ROOT && (view === "choice" || view === "game" || view === "rank"))
      await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `hunters-${view}-${viewport.width}.png`), fullPage: true });
    if (view === "choice") {
      assert.equal(await page.locator("[data-sea-choice]").count(), 4, "四海选择不完整。");
      for (const sea of ["east", "west", "south", "north"]) {
        const button = page.locator(`[data-sea-choice="${sea}"]`);
        await button.scrollIntoViewIfNeeded();
        const box = await button.boundingBox();
        assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= 0 && box.x + box.width <= viewport.width + 1, `海域按钮越界或触控区不足：${sea}`);
      }
      const start = page.getByRole("button", { name: "开始标准排位匹配", exact: true });
      assert.ok(await start.isDisabled(), "未选择海域应阻止排位。");
      await start.scrollIntoViewIfNeeded();
    }
    if (view === "lobby") {
      assert.equal(await page.locator("[data-sea-choice]").count(), 0, "已锁定海域仍允许重新选择。");
      const start = page.getByRole("button", { name: "开始标准排位匹配", exact: true });
      await page.locator("button:enabled").filter({ hasText: "开始标准排位匹配" }).waitFor({ state: "visible" });
      assert.ok(await start.isEnabled(), "已有海域和卡组应允许出海。");
      await start.scrollIntoViewIfNeeded();
      const box = await start.boundingBox();
      assert.ok(box && box.height >= 44 && box.width >= 44, "出海按钮触控区不足。");
      const rules = page.locator("summary").filter({ hasText: "猎人规则与段位" });
      await rules.click();
      assert.match(await page.locator("[data-hunter-season-panel]").innerText(), /终结对手至少 3 连胜/);
    }
    if (view === "lobby-zero") {
      const panel = page.locator("[data-hunter-season-panel]");
      assert.equal(await panel.locator('[data-hunter-defeats="0"]').count(), 1, "零分未保留图标计数展示。");
      assert.equal(await panel.locator('[data-hunter-defeats="10"]').count(), 1, "首个段位的剩余击败数量错误。");
      await panel.locator("summary").click();
      assert.match(await panel.innerText(), /获胜后击败数量 \+1/);
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) await panel.screenshot({
        path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `hunter-defeats-${viewport.width}.png`), animations: "disabled" });
    }
    if (view === "rank") {
      assert.match(await page.locator("[data-hunters-layout-verification]").innerText(), /累计击败数量/);
      assert.ok(await page.locator("[data-season-honor]:visible").count() >= 6, "排行榜荣誉未完整显示。");
      await page.getByRole("button").filter({ has: page.locator('[data-sea-effect="north"]') }).click();
      assert.equal(await page.locator('[data-testid="ranked-leaderboard-scroll"] [data-season-honor]:visible').count(), 1, "北海筛选未生效。");
    }
    if (view === "effects") {
      assert.equal(await page.locator("[data-season-honor]").count(), 7, "六类特效和个人荣誉缺失。");
      const colors = await page.locator("[data-season-honor]").evaluateAll(elements => elements.slice(1).map(el => getComputedStyle(el).getPropertyValue("--accent")));
      assert.equal(new Set(colors).size, 4, "荣誉阵营与王冠配色未区分。");
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `hunters-${viewport.width}.png`), fullPage: true });
      await page.emulateMedia({ reducedMotion: "reduce" });
      const animated = await page.locator("[data-season-honor] *").evaluateAll(elements => elements.filter(el => getComputedStyle(el).animationName !== "none").length);
      assert.equal(animated, 0, "减少动态效果设置未生效。");
      await page.emulateMedia({ reducedMotion: "no-preference" });
    }
    if (view === "profile") {
      const center = page.locator("[data-season-title-center]");
      assert.equal(await center.locator("[data-title-choice]").count(), 6, "称号中心未收录全部六种称号。");
      const ornaments = center.locator("[data-title-choice] [data-title-ornament]");
      assert.equal(await ornaments.count(), 6, "称号中心存在缺失的雕刻纹饰。");
      assert.deepEqual(await ornaments.evaluateAll(elements => elements.map(element => element.getAttribute("data-title-ornament"))),
        ["solar", "ember", "fleet", "ice", "eclipse", "constellation"], "称号的主题纹饰错配。");
      const ornamentLayout = await ornaments.evaluateAll(elements => elements.map(element => {
        const box = element.getBoundingClientRect();
        const geometry = element.getBBox();
        const gradientIds = Array.from(element.querySelectorAll("defs [id]")).map(gradient => gradient.id);
        return { width: box.width, height: box.height, geometryWidth: geometry.width,
          geometryHeight: geometry.height, pointerEvents: getComputedStyle(element).pointerEvents, gradientIds };
      }));
      assert.ok(ornamentLayout.every(item => item.width > 0 && item.height > 0 && item.geometryWidth > 0
        && item.geometryHeight > 0 && item.pointerEvents === "none"), "纹饰未正常绘制或会拦截佩戴操作。");
      const gradientIds = ornamentLayout.flatMap(item => item.gradientIds);
      assert.equal(new Set(gradientIds).size, gradientIds.length, "多枚称号的纹饰渐变发生引用冲突。");
      assert.doesNotMatch(await center.innerText(), /荣誉/, "称号中心仍有荣誉字样。");
      assert.equal(await center.locator("[data-title-current] [data-season-honor]").count(), 0, "未佩戴时应保持空展示。");
      for (const option of await center.locator("[data-title-choice]").all()) {
        await option.scrollIntoViewIfNeeded();
        const box = await option.boundingBox();
        assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.x + box.width <= viewport.width + 1, "称号选择超出视口或触控区不足。");
      }
      const emperor = center.locator('[data-title-choice="S1 四皇"]');
      await emperor.click();
      await center.locator('[data-title-current] [data-season-honor="四皇"]').waitFor({ state: "visible" });
      assert.equal(await emperor.getAttribute("aria-pressed"), "true");
      assert.equal(await center.locator('[data-title-choice][aria-pressed="true"]').count(), 1, "一次只能佩戴一枚称号。");
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `title-center-${viewport.width}.png`), fullPage: true });
      const admiral = center.locator('[data-title-choice="S1 海军大将"]');
      await admiral.focus();
      await page.keyboard.press("Enter");
      await center.locator('[data-title-current] [data-season-honor="海军大将"]').waitFor({ state: "visible" });
      assert.equal(await center.locator('[data-title-current] [data-season-honor]').count(), 1, "切换后仍显示多枚称号。");
      const cancel = center.locator("[data-title-unequip]");
      await cancel.scrollIntoViewIfNeeded();
      const box = await cancel.boundingBox();
      assert.ok(box && box.width >= 44 && box.height >= 44, "取消佩戴触控区不足。");
      await cancel.click();
      await page.waitForFunction(() => document.querySelector('[data-title-current]')?.textContent?.includes('未佩戴称号'));
      assert.equal(await center.locator('[data-title-current] [data-season-honor]').count(), 0, "取消后仍展示称号。");
    }
    if (view === "game") {
      const canvas = page.locator('[data-layout-preview="mobile-landscape"]');
      assert.equal(await canvas.getAttribute("data-layout-rotated"), "true");
      const honors = page.locator("[data-season-honor]:visible");
      assert.equal(await honors.count(), 2, "双方开局身份缺少 S1 荣誉。");
      for (const honor of await honors.all()) {
        const box = await honor.boundingBox();
        assert.ok(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "旋转画布中的荣誉超出视口。");
      }
    }
    if (view === "win") {
      const result = page.locator("[data-hunter-result]");
      await result.waitFor({ state: "visible" });
      assert.match(await result.innerText(), /\+7/);
      assert.match(await result.innerText(), /终结对手 5 连胜/);
      assert.match(await result.innerText(), /击败领先海域猎人/);
      const back = page.getByRole("button", { name: "返回大厅", exact: true });
      await back.scrollIntoViewIfNeeded();
      const box = await back.boundingBox();
      assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, "旋转结算的返回按钮不可见或触控区不足。");
    }
  }
}

async function verifyEventCost(page, baseUrl, viewport) {
  const mobile = viewport.width < 600;
  const url = `${baseUrl}/layout-verification/event-cost${mobile ? "?device=mobile" : ""}`;
  await page.goto(url, { waitUntil: "domcontentloaded" });
  const tokens = page.getByText("活跃咚", { exact: true });
  await tokens.first().waitFor({ state: "visible" });
  assert.equal(await tokens.count(), 3, "费用选择没有显示三张活跃咚。");
  const confirm = page.getByRole("button", { name: /^确认（已选/ });
  await tokens.nth(0).click();
  // 跨过玩家报告的一秒消失窗口，同时经历多次同操作快照重发。
  await page.waitForTimeout(1200);
  assert.equal(await tokens.count(), 3, "玩家未响应时费用面板被关闭。");
  assert.equal(await confirm.isDisabled(), true, "只选一张时不应允许支付两张咚的成本。");
  assert.match(await confirm.innerText(), /已选 1 \/ 2/, "重复快照清空了已选费用。");
  await tokens.nth(2).click();
  await confirm.scrollIntoViewIfNeeded();
  const box = await confirm.boundingBox();
  assert.ok(box && box.width >= 44 && box.height >= 44, `费用确认触控区不足：${JSON.stringify(box)}`);
  assert.ok(box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1
    && box.y + box.height <= viewport.height + 1, `费用确认超出实际视口：${JSON.stringify(box)}`);
  await confirm.click();
  await page.getByRole("status").waitFor({ state: "visible" });
  assert.deepEqual(JSON.parse(await page.locator("[data-event-cost-verification]").getAttribute("data-event-cost-answer")), ["don-a", "don-c"]);
  await confirm.waitFor({ state: "hidden" });

  await page.goto(url, { waitUntil: "domcontentloaded" });
  const cancel = page.getByRole("button", { name: "取消支付并返回是否发动" });
  await cancel.waitFor({ state: "visible" });
  await cancel.scrollIntoViewIfNeeded();
  await cancel.click();
  await page.getByRole("status").waitFor({ state: "visible" });
  assert.deepEqual(JSON.parse(await page.locator("[data-event-cost-verification]").getAttribute("data-event-cost-answer")),
    ["__return_to_effect_confirm__:0", "__return_to_effect_confirm__:1"]);
  await cancel.waitFor({ state: "hidden" });
}

async function verifyChangelog(page, baseUrl, viewport) {
  await page.goto(`${baseUrl}/layout-verification/changelog`, { waitUntil: "domcontentloaded" });
  const dialog = page.getByRole("dialog", { name: "更新日志", exact: true });
  await dialog.waitFor({ state: "visible" });
  const confirm = dialog.getByRole("button", { name: "我知道了", exact: true });
  // 等待弹窗入场动画结束，避免在缩放期间测量触控区域。
  await page.waitForFunction(() => {
    const dialog = document.querySelector("[data-modal-dialog]");
    return dialog instanceof HTMLElement && getComputedStyle(dialog).transform === "none";
  });
  const layout = await dialog.evaluate((element) => {
    const latest = element.querySelector("section");
    const contents = latest?.parentElement;
    const box = element.getBoundingClientRect();
    const buttons = Array.from(element.querySelectorAll("button")).map((button) => {
      const rect = button.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    return {
      box: { x: box.x, y: box.y, width: box.width, height: box.height },
      width: element.scrollWidth,
      clientWidth: element.clientWidth,
      documentWidth: document.documentElement.scrollWidth,
      contentHeight: contents?.scrollHeight,
      contentClientHeight: contents?.clientHeight,
      latestItems: latest?.querySelectorAll("li").length,
      buttons,
    };
  });
  assert.ok(layout.box.x >= -1 && layout.box.y >= -1
    && layout.box.x + layout.box.width <= viewport.width + 1
    && layout.box.y + layout.box.height <= viewport.height + 1,
  `更新日志超出视口：${JSON.stringify(layout)}`);
  assert.ok(layout.width <= layout.clientWidth && layout.documentWidth <= viewport.width,
    `更新日志发生横向溢出：${JSON.stringify(layout)}`);
  assert.ok(layout.latestItems > 0 && layout.contentHeight > layout.contentClientHeight,
    `长更新日志缺少内容或内部滚动：${JSON.stringify(layout)}`);
  assert.ok(layout.buttons.every((button) => button.width >= 43.5 && button.height >= 43.5
    && button.x >= -1 && button.y >= -1
    && button.x + button.width <= viewport.width + 1
    && button.y + button.height <= viewport.height + 1),
  `更新日志主要操作不可见或不足44px：${JSON.stringify(layout.buttons)}`);
  await dialog.evaluate((element) => {
    const contents = element.querySelector("section")?.parentElement;
    if (contents) contents.scrollTop = contents.scrollHeight;
  });
  await confirm.click();
  await dialog.waitFor({ state: "hidden" });
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
  const desktop = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  await verifyKnowledgePropertySearch(await desktop.newPage(), baseUrl, { width: 1440, height: 900 });
  await verifyHunters(await desktop.newPage(), baseUrl, { width: 1440, height: 900 });
  await verifyChangelog(await desktop.newPage(), baseUrl, { width: 1440, height: 900 });
  await verifyEventCost(await desktop.newPage(), baseUrl, { width: 1440, height: 900 });
  await desktop.close();
  for (const viewport of [{ width: 390, height: 844 }, { width: 360, height: 780 }]) {
    const actualContext = await browser.newContext({ viewport, isMobile: true, hasTouch: true });
    const actualPage = await actualContext.newPage();
    await verifyKnowledgePropertySearch(actualPage, baseUrl, viewport);
    await verifyHunters(actualPage, baseUrl, viewport);
    await verifyChangelog(actualPage, baseUrl, viewport);
    await verifyEventCost(actualPage, baseUrl, viewport);
    await actualPage.goto(`${baseUrl}/layout-verification/card-playability`, { waitUntil: "domcontentloaded" });
    await actualPage.getByRole("heading", { name: "卡牌图鉴", exact: true }).waitFor({ state: "visible" });
    const actualSearch = actualPage.getByRole("searchbox", { name: "搜索卡名、卡号或关键词" });
    await actualSearch.fill("EB05-014");
    const actualCard = actualPage.locator('button[aria-label*="EB05-014"]').first();
    await actualCard.waitFor({ state: "visible" });
    assert.equal(await actualCard.locator('[data-card-playability="pending"]').count(), 0,
      "已完成效果的 EB05-014 不应继续显示暂不可对战标记。");
    await actualCard.click();
    const actualDialog = actualPage.getByRole("dialog");
    await actualDialog.waitFor({ state: "visible" });
    assert.equal(await actualDialog.locator('[data-card-playability="pending"]').count(), 0,
      "已完成效果的 EB05-014 详情不应继续显示暂不可对战说明。");
    await actualContext.close();

    const context = await browser.newContext({ viewport, isMobile: true, hasTouch: true });
    const page = await context.newPage();
    await page.route("**/data/allCards.json*", async (route) => {
      const response = await route.fetch();
      const bundle = await response.json();
      bundle.playability = {
        ...(bundle.playability ?? {}),
        cards: [{ number: "EB05-014", state: "pending" }],
      };
      await route.fulfill({ response, contentType: "application/json", body: JSON.stringify(bundle) });
    });

    await page.goto(`${baseUrl}/layout-verification/card-playability`, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "卡牌图鉴", exact: true }).waitFor({ state: "visible" });
    const search = page.getByRole("searchbox", { name: "搜索卡名、卡号或关键词" });
    const searchBox = await search.boundingBox();
    assert.ok(searchBox && searchBox.height >= 44 && searchBox.width >= 44,
      `卡牌图鉴搜索框触控区域不足：${JSON.stringify(searchBox)}`);
    await search.fill("EB05-014");
    const pendingCard = page.locator('button[aria-label*="EB05-014"]').first();
    await pendingCard.waitFor({ state: "visible" });
    const pendingBadge = pendingCard.locator('[data-card-playability="pending"]');
    await pendingBadge.waitFor({ state: "visible" });
    assert.match(await pendingBadge.innerText(), /效果开发中 · 暂不可对战/);
    const catalogLayout = await page.evaluate(() => {
      const root = document.querySelector("[data-card-playability-layout-verification]");
      const card = document.querySelector('button[aria-label*="EB05-014"]');
      const badge = card?.querySelector('[data-card-playability="pending"]');
      if (!(root instanceof HTMLElement) || !(card instanceof HTMLButtonElement)
          || !(badge instanceof HTMLElement)) {
        throw new Error("卡牌图鉴待实现状态验证节点缺失。");
      }
      const rootBox = root.getBoundingClientRect();
      const cardBox = card.getBoundingClientRect();
      const badgeBox = badge.getBoundingClientRect();
      const artworkCount = Array.from(card.querySelectorAll("span"))
        .find((element) => element.children.length === 0
          && /^2\s*画$/.test((element.textContent ?? "").trim()));
      const artworkBox = artworkCount?.getBoundingClientRect();
      const overlapsArtwork = artworkBox
        ? badgeBox.left < artworkBox.right && badgeBox.right > artworkBox.left
          && badgeBox.top < artworkBox.bottom && badgeBox.bottom > artworkBox.top
        : true;
      return {
        documentWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        documentHeight: document.documentElement.scrollHeight,
        clientHeight: document.documentElement.clientHeight,
        root: { x: rootBox.x, y: rootBox.y, width: rootBox.width, height: rootBox.height },
        card: { x: cardBox.x, y: cardBox.y, width: cardBox.width, height: cardBox.height },
        badge: { x: badgeBox.x, y: badgeBox.y, width: badgeBox.width, height: badgeBox.height },
        artwork: artworkBox
          ? { x: artworkBox.x, y: artworkBox.y, width: artworkBox.width, height: artworkBox.height }
          : null,
        overlapsArtwork,
      };
    });
    assert.ok(catalogLayout.documentWidth <= catalogLayout.clientWidth,
      `卡牌图鉴页面横向溢出：${JSON.stringify(catalogLayout)}`);
    assert.ok(catalogLayout.documentHeight <= catalogLayout.clientHeight,
      `卡牌图鉴页面纵向溢出：${JSON.stringify(catalogLayout)}`);
    assert.ok(catalogLayout.root.x >= -1 && catalogLayout.root.y >= -1
      && catalogLayout.root.width <= viewport.width + 1 && catalogLayout.root.height <= viewport.height + 1,
    `卡牌图鉴根节点超出手机视口：${JSON.stringify(catalogLayout.root)}`);
    assert.ok(catalogLayout.card.width >= 44 && catalogLayout.card.height >= 44,
      `待实现卡牌触控区域不足：${JSON.stringify(catalogLayout.card)}`);
    assert.equal(catalogLayout.overlapsArtwork, false,
      `待实现标记与异画数量重叠：${JSON.stringify(catalogLayout)}`);

    await pendingCard.click();
    const dialog = page.getByRole("dialog");
    await dialog.waitFor({ state: "visible" });
    await page.waitForTimeout(250);
    const detailBadge = dialog.locator('[data-card-playability="pending"]');
    await detailBadge.waitFor({ state: "visible" });
    assert.match(await detailBadge.innerText(), /资料与卡图可查阅，当前暂不可用于对战/);
    const dialogBox = await dialog.boundingBox();
    assert.ok(dialogBox && dialogBox.x >= -1 && dialogBox.y >= -1
      && dialogBox.x + dialogBox.width <= viewport.width + 1
      && dialogBox.y + dialogBox.height <= viewport.height + 1,
    `待实现卡牌详情超出手机视口：${JSON.stringify(dialogBox)}`);
    const closeButton = dialog.getByRole("button", { name: "关闭弹窗" });
    const closeBox = await closeButton.boundingBox();
    assert.ok(closeBox && closeBox.width >= 44 && closeBox.height >= 44,
      `卡牌详情关闭按钮触控区域不足：${JSON.stringify(closeBox)}`);
    await closeButton.click();

    await page.goto(`${baseUrl}/replay/layout-verification`, { waitUntil: "domcontentloaded" });
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

    await page.goto(`${baseUrl}/layout-verification/hex-actions`, { waitUntil: "domcontentloaded" });
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

    await page.goto(`${baseUrl}/layout-verification/cloud-replay`, { waitUntil: "domcontentloaded" });
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

    await page.goto(`${baseUrl}/layout-verification/operations-workbench`, { waitUntil: "domcontentloaded" });
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

    await verifyChatDecorationExchange(page, baseUrl, viewport, "exchange-before", {
      balance: 100_000_000,
      itemIds: ["greeting-straw-hat", "quote-pirate-king-man", "quote-binks-laugh"],
    });
    await verifyChatDecorationExchange(page, baseUrl, viewport, "exchange-after", {
      balance: 50_000_000,
      itemIds: ["greeting-straw-hat", "quote-binks-laugh", "quote-pirate-king-man"],
    });
    await context.close();
  }
  const narrowViewport = { width: 344, height: 582 };
  const narrowContext = await browser.newContext({ viewport: narrowViewport, isMobile: true, hasTouch: true });
  const narrowPage = await narrowContext.newPage();
  await narrowPage.goto(`${baseUrl}/layout-verification/hex-actions`, { waitUntil: "domcontentloaded" });
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
  console.log("真实浏览器回归通过：1440×900、390×844、360×780 的事件额外选咚等待、重复同步保留选择、确认与取消、S2 四海、S1 六类徽章、称号选择与取消、排行榜与旋转对局结算通过；390×844、360×780 的已实现卡牌可用状态、独立 pending 夹具标记、详情、异画角标、触控区，以及交易所和既有页面门禁通过；344×582 的咚!!锁定提示可见、无溢出且未与聊天控制坞重叠。");
} finally {
  await browser?.close();
  child.kill("SIGTERM");
  await Promise.race([
    new Promise((resolve) => child.once("exit", resolve)),
    new Promise((resolve) => setTimeout(resolve, 5_000)),
  ]);
}
