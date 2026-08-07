import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const definitionsDir = path.join(root, "服务端WebSocket", "Effects", "Definitions");
const scriptedDir = path.join(root, "服务端WebSocket", "Effects", "Scripted");

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

function loadCards() {
  const cards = new Map();
  // 含原文数据优先；ST36 等仅存在于运行时数据的系列由第二个目录补齐。
  for (const dirName of ["卡牌数据", "卡牌数据_含原文"]) {
    const dir = path.join(root, dirName);
    if (!fs.existsSync(dir)) continue;
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
  }
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
    for (const match of text.matchAll(/CardNumber\s*=>\s*"([A-Z0-9-]+)"/g)) {
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

console.log(`带【触发】卡牌：${triggerCards.length}`);
console.log(`DSL trigger：${triggerCards.filter((card) => dsl.has(card.number)).length}`);
console.log(`脚本 OnLifeRevealTrigger：${triggerCards.filter((card) => scripted.has(card.number)).length}`);
console.log(`引擎通用处理：${generic.length}`);
console.log(`本批目标：${batchTargets.size}`);
console.log(`本批剩余缺口：${batchMissing.length}`);
console.log(`全卡池另发现候选：${laterCandidates.length}`);

if (batchMissing.length > 0) {
  for (const number of batchMissing)
    console.error(`本批未覆盖：${number}`);
  process.exitCode = 1;
}

for (const card of laterCandidates)
  console.warn(`待另行审计：${card.number}｜${card.name}｜${card.trigger}`);
