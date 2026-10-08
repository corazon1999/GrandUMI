import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import ts from "typescript";

// 编译真实纯函数及其依赖，直接验证图鉴和组卡共用的筛选入口。
const sourceRoot = path.resolve(import.meta.dirname, "../src");
const cache = new Map();
function loadModule(file) {
  if (cache.has(file)) return cache.get(file);
  const module = { exports: {} };
  cache.set(file, module.exports);
  const compiled = ts.transpileModule(readFileSync(file, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const require = (specifier) => loadModule(path.join(sourceRoot, `${specifier.slice(2)}.ts`));
  Function("module", "exports", "require", compiled)(module, module.exports, require);
  return module.exports;
}
const { CARD_PROPERTIES, filterAndSortCards } = loadModule(path.join(sourceRoot, "lib/cardSearch.ts"));
const filters = { searchQuery: "", filterColors: [], filterType: "", filterProperty: "知",
  filterRarity: "", filterCost: null, filterSets: [], filterShowSub1: true };
const card = (number, property) => ({ number, property, name: number, color: "黄",
  type: "Character", rarity: "C", cost: 1, subscript: 2, keyWords: [], trigger: "" });

test("属性列表使用知，筛选包含标准知属性、历史智别名和双属性", () => {
  assert.ok(CARD_PROPERTIES.includes("知"));
  assert.ok(!CARD_PROPERTIES.includes("智"));
  const cards = [card("OP01-001", "知"), card("OP01-002", "智"),
    card("OP01-003", "打/知"), card("OP01-004", "打")];
  assert.deepEqual(filterAndSortCards(cards, filters).map(c => c.number),
    ["OP01-001", "OP01-002", "OP01-003"]);
  assert.equal(filterAndSortCards(cards, { ...filters, filterProperty: "智" }).length, 3);
});

test("清除属性筛选保留全部卡牌，其他筛选条件仍正常生效", () => {
  const cards = [card("OP01-001", "知"), card("OP01-002", "打")];
  assert.equal(filterAndSortCards(cards, { ...filters, filterProperty: "" }).length, 2);
  assert.equal(filterAndSortCards(cards, { ...filters, searchQuery: "OP01-002" }).length, 0);
});
