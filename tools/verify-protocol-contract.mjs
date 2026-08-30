import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const root = path.resolve(import.meta.dirname, "..");
const contractPath = path.join(root, "protocol", "contracts", "websocket.v1.json");
const bridgePath = path.join(root, "服务端WebSocket", "WebSocketBridge.cs");
const frontendRoot = path.join(root, "opcgpro-web", "src");
const backendRoot = path.join(root, "服务端WebSocket");

async function sourceFiles(directory, extensions) {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) result.push(...await sourceFiles(fullPath, extensions));
    else if (extensions.has(path.extname(entry.name))) result.push(fullPath);
  }
  return result;
}

function values(source, regex) {
  return [...source.matchAll(regex)].map((match) => match[1]);
}

function uniqueSorted(items) {
  return [...new Set(items)].sort((left, right) => left.localeCompare(right, "en"));
}

function assertExact(label, actual, expected, errors) {
  const actualSet = new Set(actual);
  const expectedSet = new Set(expected);
  const missing = expected.filter((item) => !actualSet.has(item));
  const unexpected = actual.filter((item) => !expectedSet.has(item));
  if (missing.length) errors.push(`${label} 缺少：${missing.join("、")}`);
  if (unexpected.length) errors.push(`${label} 未登记：${unexpected.join("、")}`);
}

function interfaceBody(source, protocol) {
  for (const match of source.matchAll(/interface\s+\w+\s+extends\s+MsgBase\s*\{([\s\S]*?)\n\}/gm)) {
    if (new RegExp(`proto\\s*:\\s*"${protocol}"`).test(match[1])) return match[1];
  }
  return null;
}

const contract = JSON.parse(await readFile(contractPath, "utf8"));
const bridge = await readFile(bridgePath, "utf8");
const switchBody = bridge.match(/switch\s*\(proto\)([\s\S]*?)default:/)?.[1];
if (!switchBody) throw new Error("无法定位 WebSocketBridge 的协议分发表。");

const backendFiles = await sourceFiles(backendRoot, new Set([".cs"]));
const backendSource = (await Promise.all(backendFiles.map((file) => readFile(file, "utf8")))).join("\n");
const frontendFiles = await sourceFiles(frontendRoot, new Set([".ts", ".tsx"]));
const frontendSource = (await Promise.all(frontendFiles.map((file) => readFile(file, "utf8")))).join("\n");

const inbound = uniqueSorted(values(switchBody, /case\s+"(Msg[A-Za-z0-9]+)"/g));
const outbound = uniqueSorted([
  ...values(backendSource, /proto\s*=\s*"(Msg[A-Za-z0-9]+)"/g),
  ...values(backendSource, /WriteString\(\s*"proto"\s*,\s*"(Msg[A-Za-z0-9]+)"/g),
]);
const errors = [];

if (contract.schemaVersion !== "grandumi.websocket-contract.v1") {
  errors.push(`未知契约版本：${contract.schemaVersion ?? "<空>"}`);
}
assertExact("服务端入站协议", inbound, uniqueSorted(contract.clientToServer), errors);
assertExact("服务端出站协议", outbound, uniqueSorted(contract.serverToClient), errors);

const legacyClient = new Set(contract.compatibility?.legacyClientMessages ?? []);
for (const protocol of contract.clientToServer) {
  if (legacyClient.has(protocol)) continue;
  const sent = new RegExp(`proto\\s*:\\s*"${protocol}"`).test(frontendSource);
  if (!sent) errors.push(`前端没有构造入站协议：${protocol}`);
}

const legacyServer = new Set(contract.compatibility?.legacyServerMessages ?? []);
const structural = new Set(contract.compatibility?.structuralClientHandlers ?? []);
const frontendHandlers = new Set([
  ...values(frontendSource, /case\s+"(Msg[A-Za-z0-9]+)"/g),
  ...values(frontendSource, /msg\.proto\s*(?:===|!==)\s*"(Msg[A-Za-z0-9]+)"/g),
]);
for (const protocol of contract.serverToClient) {
  if (legacyServer.has(protocol)) continue;
  if (frontendHandlers.has(protocol)) continue;
  if (structural.has(protocol) && new RegExp(`msg\\.proto\\s*===\\s*"${protocol}"`).test(frontendSource)) continue;
  errors.push(`前端没有接收处理出站协议：${protocol}`);
}

for (const [direction, messages] of Object.entries(contract.criticalMessages ?? {})) {
  for (const [protocol, fields] of Object.entries(messages)) {
    const body = interfaceBody(frontendSource, protocol);
    if (!body) {
      errors.push(`关键协议 ${direction}.${protocol} 缺少前端 MsgBase 接口。`);
      continue;
    }
    for (const field of fields) {
      if (!new RegExp(`\\b${field}\\??\\s*:`).test(body)) {
        errors.push(`关键协议 ${direction}.${protocol} 缺少字段：${field}`);
      }
    }
  }
}

if (errors.length) {
  console.error("WebSocket 协议契约校验失败：");
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`WebSocket 协议契约通过：入站 ${inbound.length}，出站 ${outbound.length}，关键消息字段已同步。`);
