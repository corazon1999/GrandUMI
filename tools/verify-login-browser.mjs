import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";

const requireFromFrontend = createRequire(new URL("../opcgpro-web/package.json", import.meta.url));
const ts = requireFromFrontend("typescript");

/** 检查真实构建产物，防止依赖中的类静态初始化块再次使 Safari 16.0 整包解析失败。 */
export function assertSafari16Syntax(source, filename = "前端脚本") {
  const tree = ts.createSourceFile(filename, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS);
  function visit(node) {
    assert.ok(!ts.isClassStaticBlockDeclaration(node), `${filename} 含 Safari 16.0 不支持的类静态初始化块。`);
    ts.forEachChild(node, visit);
  }
  visit(tree);
}

/** 在隔离连接中验证首屏、密码步骤和进入大厅，不使用真实账号或外部服务器。 */
export async function verifyLoginBrowser(browser, baseUrl, viewport) {
  for (const javaScriptEnabled of [false, true]) {
    const context = await browser.newContext({
      viewport, javaScriptEnabled, isMobile: viewport.width < 1024, hasTouch: viewport.width < 1024,
    });
    try {
      const page = await context.newPage();
      const errors = [];
      page.on("pageerror", error => errors.push(error.message));
      await context.addInitScript(() => {
        // 使用各代 WebKit 都支持的接口注入连接替身，杜绝向外部环境发送测试登录。
        class LoginSocket {
          constructor() {
            this.readyState = 0;
            setTimeout(() => {
              this.readyState = 1;
              if (this.onopen) this.onopen({});
            }, 0);
          }
          send(data) {
            const message = JSON.parse(data);
            let reply;
            if (message.proto === "MsgSecret") reply = { proto: "MsgSecret", result: true };
            if (message.proto === "MsgPing") reply = message;
            if (message.proto === "MsgLogin") {
              reply = message.password
                ? { proto: "MsgLogin", result: true, account: message.account, name: "兼容性验证玩家", decks: [] }
                : { proto: "MsgLogin", result: false, account: message.account, needsPassword: true };
            }
            if (reply) setTimeout(() => {
              if (this.onmessage) this.onmessage({ data: JSON.stringify(reply) });
            }, 0);
          }
          close() { this.readyState = 3; }
        }
        LoginSocket.OPEN = 1;
        LoginSocket.CLOSED = 3;
        window.WebSocket = LoginSocket;
      });
      await page.goto(`${baseUrl}/home`, { waitUntil: "load" });
      const panel = page.locator("[data-login-panel]");
      await panel.waitFor({ state: "visible" });
      assert.equal(await panel.evaluate(element => getComputedStyle(element).opacity), "1",
        "登录面板不应依赖 JavaScript 或动画解除透明状态。");
      const account = page.getByLabel("玩家账号", { exact: true });
      const button = panel.locator("button").first();
      for (const control of [account, button]) {
        const box = await control.boundingBox();
        assert.ok(box && box.width >= 44 && box.height >= 44 && box.x >= -1 && box.y >= -1
          && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
        `登录控件超出视口或不足 44×44：${JSON.stringify(box)}`);
      }
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), "登录页横向溢出。");
      if (javaScriptEnabled) {
        // 读取页面实际引用的构建脚本，而不是只检查源码或配置声明。
        for (const src of await page.locator("script[src]").evaluateAll(nodes => nodes.map(node => node.src))) {
          const response = await page.request.get(src);
          assert.ok(response.ok(), `登录脚本下载失败：${src}`);
          assertSafari16Syntax(await response.text(), src);
        }
        await account.fill("safari16-fixture");
        await page.getByRole("button", { name: "继续", exact: true }).click();
        await page.getByLabel("密码", { exact: true }).fill("safari16-fixture-password");
        await page.getByRole("button", { name: "验证密码", exact: true }).click();
        await page.getByRole("heading", { name: "开始对战", exact: true }).waitFor({ state: "visible" });
        assert.equal(await page.evaluate(() => localStorage.getItem("grandumi_account")), "safari16-fixture");
        assert.deepEqual(errors, [], "登录和进入大厅出现浏览器异常。");
      }
      if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({
        path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT,
          `login-${browser.browserType().name()}-${viewport.width}-${javaScriptEnabled ? "lobby" : "ssr"}.png`),
      });
    } finally {
      await context.close();
    }
  }
}
