import { createHash } from "node:crypto";
import { readFile, readdir } from "node:fs/promises";
import path from "node:path";

export const ROOT = path.resolve(import.meta.dirname, "..");
export const CARD_ROOT = path.join(ROOT, "卡牌数据");
export const FRONTEND_CARD_ROOT = path.join(ROOT, "opcgpro-web", "public", "data");
export const SCHEMA_FILE = "_schema.v1.json";
export const MANIFEST_FILE = "_manifest.v1.json";
export const REGISTRY_FILE = "_effect-registry.v1.json";

export function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

export function stable(value) {
  if (Array.isArray(value)) return `[${value.map(stable).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stable(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

export function pretty(value) {
  return `${JSON.stringify(value, null, 2)}\n`;
}

export async function setFileNames(root = CARD_ROOT) {
  return (await readdir(root))
    .filter((name) => name.endsWith(".json") && !name.startsWith("_"))
    .sort((left, right) => left.localeCompare(right, "en"));
}

function describe(file, index, message) {
  return `${file}[${index}]：${message}`;
}

export function validateCard(card, file, index, schema) {
  const errors = [];
  const itemSchema = schema.items;
  if (!card || typeof card !== "object" || Array.isArray(card)) return [describe(file, index, "必须是对象")];
  for (const field of itemSchema.required) {
    if (!(field in card)) errors.push(describe(file, index, `缺少字段 ${field}`));
  }
  for (const field of Object.keys(card)) {
    if (!(field in itemSchema.properties)) errors.push(describe(file, index, `未知字段 ${field}`));
  }
  for (const [field, rule] of Object.entries(itemSchema.properties)) {
    if (!(field in card)) continue;
    const value = card[field];
    if (rule.type === "string" && typeof value !== "string") errors.push(describe(file, index, `${field} 必须是字符串`));
    if (rule.type === "array" && !Array.isArray(value)) errors.push(describe(file, index, `${field} 必须是数组`));
    if (rule.enum && !rule.enum.includes(value)) errors.push(describe(file, index, `${field} 包含未登记值 ${JSON.stringify(value)}`));
    if (rule.pattern && typeof value === "string" && !new RegExp(rule.pattern).test(value)) errors.push(describe(file, index, `${field} 格式无效`));
    if (rule.minLength && typeof value === "string" && value.length < rule.minLength) errors.push(describe(file, index, `${field} 不能为空`));
    if (rule.uniqueItems && Array.isArray(value) && new Set(value).size !== value.length) errors.push(describe(file, index, `${field} 含重复项`));
    if (rule.items?.enum && Array.isArray(value)) {
      for (const entry of value) if (!rule.items.enum.includes(entry)) errors.push(describe(file, index, `${field} 含未登记值 ${JSON.stringify(entry)}`));
    }
    if (rule.items?.type === "string" && Array.isArray(value) && value.some((entry) => typeof entry !== "string" || !entry)) {
      errors.push(describe(file, index, `${field} 只能包含非空字符串`));
    }
  }
  if (!(Number.isInteger(card.subscript) && card.subscript >= 0)
      && !(typeof card.subscript === "string" && /^[0-9]*$/.test(card.subscript))) {
    errors.push(describe(file, index, "subscript 必须是非负整数或数字字符串"));
  }
  const expectedPrefix = `${path.basename(file, ".json")}-`;
  if (typeof card.number === "string" && !card.number.startsWith(expectedPrefix)) {
    errors.push(describe(file, index, `卡号 ${card.number} 不属于文件 ${file}`));
  }
  return errors;
}

export async function loadCanonicalCards() {
  const schema = JSON.parse(await readFile(path.join(CARD_ROOT, SCHEMA_FILE), "utf8"));
  const files = await setFileNames();
  const cards = [];
  const errors = [];
  const seen = new Map();
  const bytesByFile = new Map();
  for (const file of files) {
    const bytes = await readFile(path.join(CARD_ROOT, file));
    bytesByFile.set(file, bytes);
    let parsed;
    try { parsed = JSON.parse(bytes.toString("utf8")); }
    catch (error) { errors.push(`${file}：JSON 解析失败：${error.message}`); continue; }
    if (!Array.isArray(parsed)) { errors.push(`${file}：根节点必须是数组`); continue; }
    for (let index = 0; index < parsed.length; index++) {
      const card = parsed[index];
      errors.push(...validateCard(card, file, index, schema));
      if (typeof card?.number === "string") {
        if (seen.has(card.number)) errors.push(`${file}[${index}]：卡号 ${card.number} 与 ${seen.get(card.number)} 重复`);
        else seen.set(card.number, `${file}[${index}]`);
      }
      cards.push({ ...card, __file: file });
    }
  }
  return { schema, files, cards, errors, bytesByFile };
}

export async function buildManifest(loaded) {
  loaded ??= await loadCanonicalCards();
  const schemaBytes = await readFile(path.join(CARD_ROOT, SCHEMA_FILE));
  const entries = loaded.files.map((file) => ({
    path: file,
    sha256: sha256(loaded.bytesByFile.get(file)),
    cardCount: loaded.cards.filter((card) => card.__file === file).length,
  }));
  const contentBytes = entries.map((entry) => `${entry.path}\0${entry.sha256}\0${entry.cardCount}\n`).join("");
  return {
    schemaVersion: "grandumi.card-content-manifest.v1",
    schema: { path: SCHEMA_FILE, sha256: sha256(schemaBytes) },
    totalCards: entries.reduce((sum, entry) => sum + entry.cardCount, 0),
    contentSha256: sha256(contentBytes),
    files: entries,
  };
}

async function recursiveFiles(directory, extension) {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) result.push(...await recursiveFiles(fullPath, extension));
    else if (entry.name.endsWith(extension)) result.push(fullPath);
  }
  return result;
}

function relative(file) {
  return path.relative(ROOT, file).replaceAll("\\", "/");
}

export async function buildEffectRegistry(loaded, manifest) {
  loaded ??= await loadCanonicalCards();
  manifest ??= await buildManifest(loaded);
  const sources = new Map();
  const add = (number, kind, file) => {
    const entry = sources.get(number) ?? { scripted: new Set(), dsl: new Set() };
    entry[kind].add(relative(file));
    sources.set(number, entry);
  };
  const scriptedRoot = path.join(ROOT, "服务端WebSocket", "Effects", "Scripted");
  for (const file of await recursiveFiles(scriptedRoot, ".cs")) {
    const source = await readFile(file, "utf8");
    for (const match of source.matchAll(/(?:CardNumber|Number)\s*=>\s*"([A-Z0-9-]+)"/g)) add(match[1], "scripted", file);
  }
  const definitionRoot = path.join(ROOT, "服务端WebSocket", "Effects", "Definitions");
  for (const file of await recursiveFiles(definitionRoot, ".json")) {
    const definitions = JSON.parse(await readFile(file, "utf8"));
    for (const number of Object.keys(definitions)) add(number, "dsl", file);
  }

  const cardByNumber = new Map(loaded.cards.map(({ __file, ...card }) => [card.number, { ...card, file: __file }]));
  const implementedCards = [...sources.keys()].sort((left, right) => left.localeCompare(right, "en"));
  const groupBySource = (kind) => {
    const groups = new Map();
    for (const [number, source] of sources) {
      for (const file of source[kind]) {
        if (!groups.has(file)) groups.set(file, []);
        groups.get(file).push(number);
      }
    }
    return [...groups].sort(([left], [right]) => left.localeCompare(right, "en"))
      .map(([source, cards]) => ({ source, cards: cards.sort((left, right) => left.localeCompare(right, "en")) }));
  };
  const metadataOnly = loaded.cards.filter((card) => (card.effectTags.length || card.abilities.length) && !sources.has(card.number));
  const builtinMetadataCards = metadataOnly.filter((card) => card.effectTags.length === 0).map((card) => card.number);
  const unresolvedTaggedCards = metadataOnly.filter((card) => card.effectTags.length > 0).map((card) => card.number);
  const orphanImplementations = implementedCards.filter((number) => !cardByNumber.has(number));
  const duplicateScripted = [...sources]
    .filter(([, source]) => source.scripted.size > 1)
    .map(([number]) => number)
    .sort((left, right) => left.localeCompare(right, "en"));
  const payload = {
    schemaVersion: "grandumi.card-effect-registry.v1",
    cardContentSha256: manifest.contentSha256,
    implementationCardCount: implementedCards.length,
    scriptedSources: groupBySource("scripted"),
    dslSources: groupBySource("dsl"),
    builtinMetadataCards,
    unresolvedTaggedCards,
    orphanImplementations,
    duplicateScripted,
  };
  return { ...payload, registrySha256: sha256(stable(payload)) };
}

export function buildIndex(loaded) {
  return loaded.files.map((file) => {
    const cards = loaded.cards.filter((card) => card.__file === file);
    return {
      set: path.basename(file, ".json"),
      count: cards.length,
      file,
      effectTags: [...new Set(cards.flatMap((card) => card.effectTags))].sort(),
      abilities: [...new Set(cards.flatMap((card) => card.abilities))].sort(),
    };
  });
}
