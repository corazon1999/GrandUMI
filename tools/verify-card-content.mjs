import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import {
  CARD_ROOT,
  FRONTEND_CARD_ROOT,
  MANIFEST_FILE,
  REGISTRY_FILE,
  ROOT,
  buildEffectRegistry,
  buildIndex,
  buildManifest,
  loadCanonicalCards,
  pretty,
  setFileNames,
} from "./card-content-lib.mjs";

const errors = [];
const loaded = await loadCanonicalCards();
errors.push(...loaded.errors);
const manifest = await buildManifest(loaded);
const registry = await buildEffectRegistry(loaded, manifest);

async function compareGenerated(file, expected) {
  let actual;
  try { actual = await readFile(path.join(CARD_ROOT, file), "utf8"); }
  catch { errors.push(`缺少派生文件 卡牌数据/${file}`); return; }
  if (actual !== pretty(expected)) errors.push(`卡牌数据/${file} 已过期；运行 node tools/generate-card-content.mjs --write`);
}

await compareGenerated(MANIFEST_FILE, manifest);
await compareGenerated(REGISTRY_FILE, registry);
await compareGenerated("_index.json", buildIndex(loaded));

const frontendSets = await setFileNames(FRONTEND_CARD_ROOT);
const canonicalSet = new Set(loaded.files);
for (const extra of frontendSets.filter((file) => !canonicalSet.has(file) && !["allCards.json", "imageManifest.json"].includes(file))) {
  errors.push(`前端卡牌镜像含 canonical 未登记文件：${extra}`);
}
for (const file of loaded.files) {
  try {
    const mirror = await readFile(path.join(FRONTEND_CARD_ROOT, file));
    if (!mirror.equals(loaded.bytesByFile.get(file))) errors.push(`前端卡牌镜像与 canonical 不一致：${file}`);
  } catch { errors.push(`前端卡牌镜像缺少：${file}`); }
}

const scenarioPath = path.join(ROOT, "card-content", "scenario-matrix.v1.json");
const scenario = JSON.parse(await readFile(scenarioPath, "utf8"));
if (scenario.schemaVersion !== "grandumi.card-scenario-matrix.v1" || !Array.isArray(scenario.scenarios)) {
  errors.push("场景矩阵 schemaVersion 或 scenarios 无效");
} else {
  const ids = new Set();
  const cardNumbers = new Set(loaded.cards.map((card) => card.number));
  const testSource = (await Promise.all(
    (await readdir(path.join(ROOT, "服务端WebSocket.Tests"))).filter((file) => file.endsWith(".cs"))
      .map((file) => readFile(path.join(ROOT, "服务端WebSocket.Tests", file), "utf8")),
  )).join("\n");
  for (const entry of scenario.scenarios) {
    if (!entry.id || ids.has(entry.id)) errors.push(`场景 ID 缺失或重复：${entry.id ?? "<空>"}`);
    ids.add(entry.id);
    if (!cardNumbers.has(entry.cardNumber)) errors.push(`场景引用未知卡牌：${entry.id} -> ${entry.cardNumber}`);
    const method = String(entry.automatedTest ?? "").split(".").at(-1);
    if (!method || !new RegExp(`\\b${method}\\s*\\(`).test(testSource)) errors.push(`场景缺少可定位自动化测试：${entry.id}`);
  }
}

if (registry.orphanImplementations.length) errors.push(`registry 含孤儿实现：${registry.orphanImplementations.join("、")}`);
if (registry.duplicateScripted.length) errors.push(`registry 含重复手写实现：${registry.duplicateScripted.join("、")}`);
if (registry.unresolvedTaggedCards.length) errors.push(`registry 含未登记效果标签：${registry.unresolvedTaggedCards.join("、")}`);

if (errors.length) {
  console.error("卡牌内容校验失败：");
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}
console.log(`卡牌内容通过：${manifest.files.length} 个卡集、${manifest.totalCards} 张卡、SHA-256 ${manifest.contentSha256}。`);
console.log(`效果 registry：${registry.implementationCardCount} 张卡有实现登记，内建关键词 ${registry.builtinMetadataCards.length}，未登记标签 ${registry.unresolvedTaggedCards.length}；场景矩阵 ${scenario.scenarios.length} 项。`);
