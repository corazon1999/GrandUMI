import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";
import ts from "typescript";

const webRoot = path.resolve(import.meta.dirname, "..");
const requireFromWeb = createRequire(path.join(webRoot, "package.json"));
const modules = new Map();

// 执行真实组卡状态逻辑，验证添加、切换领航和最终校验都执行同一特征规则。
function loadTypeScript(filename) {
  if (modules.has(filename)) return modules.get(filename).exports;
  const module = { exports: {} };
  modules.set(filename, module);
  const source = ts.transpileModule(readFileSync(filename, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const resolve = (specifier) => {
    if (!specifier.startsWith("@/") && !specifier.startsWith(".")) return requireFromWeb(specifier);
    const base = specifier.startsWith("@/") ? path.join(webRoot, "src", specifier.slice(2)) : path.resolve(path.dirname(filename), specifier);
    const candidate = [base, `${base}.ts`, `${base}.mjs`].find(existsSync);
    assert.ok(candidate, `无法定位客户端依赖 ${specifier}`);
    return candidate.endsWith(".ts") ? loadTypeScript(candidate) : requireFromWeb(candidate);
  };
  Function("module", "exports", "require", source)(module, module.exports, resolve);
  return module.exports;
}

const { useDeckStore } = loadTypeScript(path.join(webRoot, "src", "store", "deckStore.ts"));
const { CARD_SET_PATHS, DEFAULT_SEARCH_SETS } = loadTypeScript(path.join(webRoot, "src", "data", "cardSets.ts"));
const { loadAllCards, getCard } = loadTypeScript(path.join(webRoot, "src", "data", "CardLoader.ts"));
// 使用真实单包和加载器，避免手写资料与运行时字段类型不一致。
const originalFetch = globalThis.fetch;
try {
  globalThis.fetch = async (url) => ({
    ok: true,
    json: async () => JSON.parse(readFileSync(path.join(webRoot, "public", url.split("?")[0]), "utf8")),
  });
  await loadAllCards();
} finally {
  globalThis.fetch = originalFetch;
}
const luffy = getCard("ST37-001");
const crocodile = getCard("ST38-001");
const valid = getCard("OP18-003");
const invalid = getCard("OP15-007");
const reset = () => useDeckStore.setState({ leader: luffy, entries: [], format: "Unrestricted", notice: null });

test("卡组搜索注册两个新预组系列", () => {
  for (const set of ["ST37", "ST38"]) {
    assert.equal(CARD_SET_PATHS[set], `/data/${set}.json`);
    assert.ok(DEFAULT_SEARCH_SETS.includes(set));
  }
});

test("路飞可以添加阿拉巴斯坦卡但拒绝同颜色的其他特征卡", () => {
  reset();
  useDeckStore.getState().addCard(valid);
  useDeckStore.getState().addCard(invalid);
  assert.deepEqual(useDeckStore.getState().entries.map(entry => entry.card.number), [valid.number]);
  assert.match(useDeckStore.getState().notice.message, /阿拉巴斯坦王国/);
});

test("换为路飞领航时移除不合特征卡且最终校验仍拒绝非法导入", () => {
  reset();
  useDeckStore.setState({ leader: { ...luffy, number: "OP15-002" }, entries: [{ card: valid, count: 4 }, { card: invalid, count: 4 }] });
  useDeckStore.getState().setLeader(luffy);
  assert.deepEqual(useDeckStore.getState().entries.map(entry => entry.card.number), [valid.number]);
  assert.match(useDeckStore.getState().notice.message, /领航规则/);
  const cards = Array.from({ length: 13 }, (_, index) => ({ card: { ...valid, number: `TS01-${index + 1}` }, count: index === 12 ? 2 : 4 }));
  cards.at(-1).card = invalid;
  useDeckStore.setState({ entries: cards });
  const result = useDeckStore.getState().validate();
  assert.equal(result.ok, false);
  assert.match(result.reason, /阿拉巴斯坦王国/);
});

test("克洛克达尔双色领航允许蓝黑卡且不继承路飞特征限制", () => {
  reset();
  useDeckStore.getState().setLeader(crocodile);
  useDeckStore.getState().addCard({ ...invalid, color: "蓝" });
  useDeckStore.getState().addCard({ ...invalid, number: "OP15-084", color: "黑" });
  assert.equal(useDeckStore.getState().entries.length, 2);
});
