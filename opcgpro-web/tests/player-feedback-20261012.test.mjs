import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";
import ts from "typescript";

const webRoot = path.resolve(import.meta.dirname, "..");
const requireFromWeb = createRequire(path.join(webRoot, "package.json"));
const modules = new Map();

// 执行真实加载器和组卡状态，覆盖资料解析到玩家组卡操作的完整路径。
function loadTypeScript(filename) {
  if (modules.has(filename)) return modules.get(filename).exports;
  const module = { exports: {} };
  modules.set(filename, module);
  const compiled = ts.transpileModule(readFileSync(filename, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const resolve = (specifier) => {
    if (!specifier.startsWith("@/") && !specifier.startsWith(".")) return requireFromWeb(specifier);
    const base = specifier.startsWith("@/")
      ? path.join(webRoot, "src", specifier.slice(2)) : path.resolve(path.dirname(filename), specifier);
    const candidate = [base, `${base}.ts`, `${base}.mjs`].find(existsSync);
    assert.ok(candidate, `无法定位客户端依赖 ${specifier}`);
    return candidate.endsWith(".ts") ? loadTypeScript(candidate) : requireFromWeb(candidate);
  };
  Function("module", "exports", "require", compiled)(module, module.exports, resolve);
  return module.exports;
}

const { loadAllCards, loadCardSet, getCard, getAllCachedCards } = loadTypeScript(path.join(webRoot, "src", "data", "CardLoader.ts"));
const { useDeckStore } = loadTypeScript(path.join(webRoot, "src", "store", "deckStore.ts"));
const originalFetch = globalThis.fetch;
const localFetch = async (url) => ({
  ok: true,
  json: async () => JSON.parse(readFileSync(path.join(webRoot, "public", url.split("?")[0]), "utf8")),
});
let cards;
try {
  globalThis.fetch = localFetch;
  await loadAllCards();
  cards = getAllCachedCards();
} finally {
  globalThis.fetch = originalFetch;
}
const leader = getCard("ST37-001");
const scissors = getCard("OP15-013");
const reset = () => useDeckStore.setState({ leader, entries: [], format: "Unrestricted", notice: null });

test("真实剪刀卡的动物和阿拉巴斯坦特征分别进入规则列表", () => {
  assert.deepEqual(scissors.keyWords, ["动物", "阿拉巴斯坦王国"]);
});

test("路飞可添加真实剪刀卡且切换领航时不会误删", () => {
  reset();
  useDeckStore.getState().addCard(scissors);
  assert.deepEqual(useDeckStore.getState().entries.map(entry => entry.card.number), ["OP15-013"]);
  useDeckStore.setState({ leader: getCard("OP15-001"), entries: [{ card: scissors, count: 4 }] });
  useDeckStore.getState().setLeader(leader);
  assert.equal(useDeckStore.getState().entries.length, 1);
  assert.equal(useDeckStore.getState().notice, null);
});

test("含四张剪刀的真实五十张阿拉巴斯坦卡组通过客户端最终校验", () => {
  reset();
  const other = cards.filter(card => card.type !== "Leader" && card.number !== scissors.number
    && card.subscript !== 1 && card.keyWords.includes("阿拉巴斯坦王国")
    && card.color.split("/").some(color => leader.color.split("/").includes(color)))
    .flatMap(card => Array(4).fill(card)).slice(0, 46);
  assert.equal(other.length, 46);
  const entries = new Map();
  for (const card of [...Array(4).fill(scissors), ...other]) {
    const entry = entries.get(card.number) ?? { card, count: 0 };
    entry.count++;
    entries.set(card.number, entry);
  }
  useDeckStore.setState({ entries: [...entries.values()] });
  const result = useDeckStore.getState().validate();
  assert.equal(result.ok, true, result.reason);
});

test("特征加载兼容斜线中英文逗号和分隔后的空白", async () => {
  try {
    for (const keyWords of ["动物/阿拉巴斯坦王国", "动物,阿拉巴斯坦王国", "动物，阿拉巴斯坦王国", "动物, 阿拉巴斯坦王国"]) {
      globalThis.fetch = async (url) => ({
        ok: true,
        json: async () => JSON.parse(readFileSync(path.join(webRoot, "public", url.split("?")[0]), "utf8"))
          .map(card => card.number === "OP15-013" ? { ...card, keyWords } : card),
      });
      const loaded = await loadCardSet("OP15");
      assert.deepEqual(loaded.find(card => card.number === "OP15-013").keyWords, ["动物", "阿拉巴斯坦王国"]);
    }
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("P116罗宾从实际卡牌包加载为黑色", () => {
  assert.equal(getCard("P-116").color, "黑");
});
