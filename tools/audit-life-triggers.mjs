import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const definitionsDir = path.join(root, "服务端WebSocket", "Effects", "Definitions");
const scriptedDir = path.join(root, "服务端WebSocket", "Effects", "Scripted");

// 2026-08-08 卡图复核确认：这些卡明确印有【触发】，运行时数据不得再次丢失。
const confirmedPrintedTriggers = new Set(`
EB01-039 EB02-030 OP05-038 OP05-039 OP08-037 OP08-038 OP08-053 OP08-056
OP08-068 OP08-075 OP08-091 OP08-094 OP08-095 OP08-096 OP08-097 OP08-104
OP08-105 OP08-111 OP08-112 OP08-113 OP08-114 OP10-100 OP12-075 OP14-089
ST10-016 ST10-017 ST12-002 ST12-016 ST14-016 ST22-016
`.trim().split(/\s+/));

// 这两张卡只有【反击】，历史数据曾把反击文本误写进 trigger 字段。
const confirmedNoPrintedTrigger = new Set("EB04-029 ST10-015".split(/\s+/));

// 上述卡图复核中需要新增 DSL/脚本结算的卡号；用于防止只补数据、未补效果。
const repairedEffectTargets = new Set(`
EB02-030 OP08-037 OP08-038 OP08-053 OP08-056 OP08-068 OP08-075 OP08-091
OP08-095 OP08-096 OP08-097 OP08-104 OP08-105 OP08-111 OP08-113 OP08-114
OP10-100 ST10-016 ST10-017
`.trim().split(/\s+/));

// 2026-08-06 待办的实际卡号清单（文档标题误写66张，逐项统计为64张）。
// 审计对本批缺口采用非零退出码；后续卡池新出现的候选另行报告，避免把范围扩展伪装成本批失败。
const batchTargets = new Set(`
EB01-026 EB01-028 EB01-029 EB01-035 EB01-038 EB01-053 EB01-059 EB01-060
EB02-018 EB02-056 EB03-059 EB04-027 EB04-028 EB04-041 EB04-059 EB04-060
OP02-069 OP02-089 OP02-090 OP02-091 OP03-119 OP04-037 OP05-094 OP05-115
OP06-023 OP06-038 OP06-057 OP06-059 OP06-101 OP06-116 OP07-036 OP07-078
OP07-116 OP08-115 OP09-059 OP09-104 OP09-106 OP09-107 OP10-080 OP10-109
OP10-116 OP11-019 OP11-079 OP11-081 OP12-113 OP13-117 OP14-018 OP14-019
OP14-057 OP14-082 OP14-116 OP14-117 OP14-118 OP16-101 OP16-115 OP16-117
P-106 PRB02-017 ST01-016 ST13-017 ST13-018 ST29-017 ST36-002 ST36-003
`.trim().split(/\s+/));

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

function loadCardDirectory(dir) {
  const cards = new Map();
  if (!fs.existsSync(dir)) return cards;
  for (const name of fs.readdirSync(dir).filter((x) => x.endsWith(".json"))) {
    let raw;
    try {
      raw = readJson(path.join(dir, name));
    } catch {
      continue;
    }
    const list = Array.isArray(raw) ? raw : Object.values(raw);
    for (const card of list) {
      if (card?.number) cards.set(card.number, card);
    }
  }
  return cards;
}

function loadCards() {
  const cards = loadCardDirectory(path.join(root, "卡牌数据_含原文"));
  // 运行时数据必须最后覆盖，否则已修正的 trigger 会被旧原文缓存重新清空。
  for (const [number, card] of loadCardDirectory(path.join(root, "卡牌数据")))
    cards.set(number, card);
  return cards;
}

function hasRealTriggerDefinition(value) {
  if (!value || typeof value !== "object" || !("trigger" in value)) return false;
  const trigger = value.trigger;
  if (Array.isArray(trigger)) return trigger.length > 0;
  return trigger !== null && typeof trigger === "object" && Object.keys(trigger).length > 0;
}

function loadDslTriggers() {
  const implemented = new Set();
  for (const name of fs.readdirSync(definitionsDir).filter((x) => x.endsWith(".json"))) {
    const file = path.join(definitionsDir, name);
    let definitions;
    try {
      definitions = readJson(file);
    } catch (error) {
      throw new Error(`DSL JSON 解析失败：${name}：${error.message}`);
    }
    for (const [number, value] of Object.entries(definitions)) {
      if (hasRealTriggerDefinition(value)) implemented.add(number);
    }
  }
  return implemented;
}

function loadScriptedLifeTriggers() {
  const implemented = new Set();
  for (const name of fs.readdirSync(scriptedDir).filter((x) => x.endsWith(".cs"))) {
    const text = fs.readFileSync(path.join(scriptedDir, name), "utf8");
    if (!text.includes("OnLifeRevealTrigger")) continue;
    // 兼容普通脚本的 CardNumber，以及 OP17 聚合脚本的 protected override string Number。
    for (const match of text.matchAll(/(?:CardNumber|Number)\s*=>\s*"([A-Z0-9-]+)"/g)) {
      implemented.add(match[1]);
    }
  }
  return implemented;
}

function isEngineGeneric(trigger) {
  if (!trigger) return false;
  if (/发动此卡牌的【(?:主要|反击|登场时|KO时|K\.O\.时)】效果/.test(trigger)) return true;
  if (!trigger.includes("此卡牌登场")) return false;
  return !trigger.includes("：")
    && !trigger.includes(":")
    && !trigger.includes("场合")
    && !trigger.includes("之后");
}

const cards = loadCards();
const runtimeCards = loadCardDirectory(path.join(root, "卡牌数据"));
const clientCards = loadCardDirectory(path.join(root, "opcgpro-web", "public", "data"));
const dsl = loadDslTriggers();
const scripted = loadScriptedLifeTriggers();
const triggerCards = [...cards.values()]
  .filter((card) => typeof card.trigger === "string" && card.trigger.trim() && card.trigger.trim() !== "-")
  .sort((a, b) => a.number.localeCompare(b.number));

const generic = triggerCards.filter((card) => isEngineGeneric(card.trigger));
const missing = triggerCards.filter((card) =>
  !dsl.has(card.number) && !scripted.has(card.number) && !isEngineGeneric(card.trigger));
const batchMissing = [...batchTargets].filter((number) => !dsl.has(number) && !scripted.has(number));
const laterCandidates = missing.filter((card) => !batchTargets.has(card.number));
const repairedEffectMissing = [...repairedEffectTargets].filter((number) => {
  const card = runtimeCards.get(number);
  return !dsl.has(number) && !scripted.has(number) && !isEngineGeneric(card?.trigger);
});
const printedTriggerMissing = [...confirmedPrintedTriggers].filter((number) =>
  !runtimeCards.get(number)?.trigger?.trim());
const falseTriggerPresent = [...confirmedNoPrintedTrigger].filter((number) =>
  runtimeCards.get(number)?.trigger?.trim());
const invalidTimingData = [...runtimeCards.values()].filter((card) =>
  typeof card.trigger === "string"
  && /^(?:【反击】|【主要】)/.test(card.trigger.trim()));
const clientParityMismatches = [...runtimeCards.entries()].filter(([number, card]) =>
  (card.trigger ?? "") !== (clientCards.get(number)?.trigger ?? ""));

console.log(`带【触发】卡牌：${triggerCards.length}`);
console.log(`DSL trigger：${triggerCards.filter((card) => dsl.has(card.number)).length}`);
console.log(`脚本 OnLifeRevealTrigger：${triggerCards.filter((card) => scripted.has(card.number)).length}`);
console.log(`引擎通用处理：${generic.length}`);
console.log(`本批目标：${batchTargets.size}`);
console.log(`本批剩余缺口：${batchMissing.length}`);
console.log(`全卡池另发现候选：${laterCandidates.length}`);
console.log(`卡图确认字段缺失：${printedTriggerMissing.length}`);
console.log(`反击误填 trigger：${falseTriggerPresent.length}`);
console.log(`本次效果实现缺口：${repairedEffectMissing.length}`);
console.log(`双端 trigger 不一致：${clientParityMismatches.length}`);

if (batchMissing.length > 0) {
  for (const number of batchMissing)
    console.error(`本批未覆盖：${number}`);
  process.exitCode = 1;
}

for (const card of laterCandidates)
  console.warn(`待另行审计：${card.number}｜${card.name}｜${card.trigger}`);

for (const number of printedTriggerMissing)
  console.error(`卡图有【触发】但运行数据为空：${number}`);
for (const number of falseTriggerPresent)
  console.error(`卡图无【触发】但运行数据非空：${number}`);
for (const card of invalidTimingData)
  console.error(`trigger 字段误填其它时机：${card.number}｜${card.trigger}`);
for (const number of repairedEffectMissing)
  console.error(`本次卡牌仍缺生命触发实现：${number}`);
for (const [number, card] of clientParityMismatches)
  console.error(`双端 trigger 不一致：${number}｜服务端=${card.trigger ?? ""}｜客户端=${clientCards.get(number)?.trigger ?? ""}`);

if (printedTriggerMissing.length > 0 || falseTriggerPresent.length > 0
  || invalidTimingData.length > 0 || repairedEffectMissing.length > 0
  || clientParityMismatches.length > 0) {
  process.exitCode = 1;
}
