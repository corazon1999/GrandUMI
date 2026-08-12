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

test("连接层在五秒握手超时后关闭旧连接并轮换端点", async () => {
  const source = await readSource("../src/net/NetManager.ts");

  assert.match(source, /connect\(url: string \| readonly string\[\]/);
  assert.match(source, /CONNECTION_TIMEOUT_MS = 5_000/);
  assert.match(source, /this\.ws = null;\s*this\.socketGeneration\+\+;/);
  assert.match(source, /socket\.close\(4001, "连接握手超时"\)/);
  assert.match(source, /this\.endpointIndex = \(this\.endpointIndex \+ 1\) % this\.endpoints\.length/);
});

test("首次连接与登录重试共用端点选择逻辑", async () => {
  const [hook, login] = await Promise.all([
    readSource("../src/hooks/useNet.ts"),
    readSource("../src/components/home/LoginPanel.tsx"),
  ]);

  assert.match(hook, /NetManager\.connect\(getWebSocketEndpoints\(\)\)/);
  assert.match(login, /NetManager\.connect\(getWebSocketEndpoints\(\)\)/);
});
