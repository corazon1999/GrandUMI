import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("正式服主域名和备用域名均优先香港直连，Cloudflare 作为兜底", async () => {
  const source = await readSource("../src/net/wsEndpoint.ts");
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const moduleUrl = `data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`;
  const { buildWebSocketEndpoints } = await import(moduleUrl);

  assert.deepEqual(
    buildWebSocketEndpoints("wss://grand-umi.com/ws", "grand-umi.com", "https:"),
    ["wss://direct.grand-umi.com/ws", "wss://grand-umi.com/ws"],
  );
  assert.deepEqual(
    buildWebSocketEndpoints("wss://grand-umi.com/ws", "direct.grand-umi.com", "https:"),
    ["wss://direct.grand-umi.com/ws", "wss://grand-umi.com/ws"],
  );
  assert.deepEqual(
    buildWebSocketEndpoints("wss://test.grand-umi.com/ws", "test.grand-umi.com", "https:"),
    ["wss://test.grand-umi.com/ws"],
  );
});

test("连接层快速放弃异常首选线路，并为最后一条线路保留完整握手时间", async () => {
  const source = await readSource("../src/net/NetManager.ts");

  assert.match(source, /connect\(url: string \| readonly string\[\]/);
  assert.match(source, /CONNECTION_TIMEOUT_MS = 5_000/);
  assert.match(source, /FALLBACK_SWITCH_TIMEOUT_MS = 1_500/);
  assert.match(source, /hasUnusedFallback[\s\S]*FALLBACK_SWITCH_TIMEOUT_MS[\s\S]*CONNECTION_TIMEOUT_MS/);
  assert.match(source, /this\.ws = null;\s*this\.socketGeneration\+\+;/);
  assert.match(source, /socket\.close\(4001, "连接握手超时"\)/);
  assert.match(source, /this\.endpointIndex = \(this\.endpointIndex \+ 1\) % this\.endpoints\.length/);
});

test("重连等待可以由玩家或浏览器恢复联网事件立即打断", async () => {
  const [manager, hook, login, overlay] = await Promise.all([
    readSource("../src/net/NetManager.ts"),
    readSource("../src/hooks/useNet.ts"),
    readSource("../src/components/home/LoginPanel.tsx"),
    readSource("../src/components/game/ReconnectOverlay.tsx"),
  ]);

  assert.match(manager, /retryNow\(url\?: string \| readonly string\[\]\)/);
  assert.match(manager, /socket\?\.close\(4002, "立即切换线路重连"\)/);
  assert.match(hook, /window\.addEventListener\("online", onBrowserOnline\)/);
  assert.match(hook, /window\.removeEventListener\("online", onBrowserOnline\)/);
  assert.match(login, /立即换线重试/);
  assert.match(overlay, /立即换线重试/);
});

test("首次连接与登录重试共用端点选择逻辑", async () => {
  const [hook, login] = await Promise.all([
    readSource("../src/hooks/useNet.ts"),
    readSource("../src/components/home/LoginPanel.tsx"),
  ]);

  assert.match(hook, /NetManager\.connect\(getWebSocketEndpoints\(\)\)/);
  assert.match(login, /NetManager\.connect\(getWebSocketEndpoints\(\)\)/);
});

test("线路清单支持运行时刷新且失败时回退缓存", async () => {
  const source = await readSource("../src/net/wsEndpoint.ts");

  assert.match(source, /fetch\(`\$\{RUNTIME_CONFIG_PATH\}\?t=\$\{Date\.now\(\)\}`/);
  assert.match(source, /cache: "no-store"/);
  assert.match(source, /RUNTIME_CACHE_TTL_MS = 10 \* 60 \* 1000/);
  assert.match(source, /window\.location\.protocol !== "https:" \|\| url\.protocol === "wss:"/);
});

test("连接恢复包含抖动、线路熔断、成功线路记忆和前台唤醒探测", async () => {
  const [manager, hook] = await Promise.all([
    readSource("../src/net/NetManager.ts"),
    readSource("../src/hooks/useNet.ts"),
  ]);

  assert.match(manager, /CIRCUIT_FAILURE_THRESHOLD = 2/);
  assert.match(manager, /CIRCUIT_OPEN_MS = 45_000/);
  assert.match(manager, /0\.75 \+ Math\.random\(\) \* 0\.5/);
  assert.match(manager, /grandumi_last_good_ws/);
  assert.match(manager, /handleForegroundResume/);
  assert.match(hook, /visibilitychange/);
  assert.match(hook, /pageshow/);
  assert.match(hook, /refreshWebSocketEndpoints/);
});
