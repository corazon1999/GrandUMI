import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";
import ts from "typescript";

const webRoot = path.resolve(import.meta.dirname, "..");
const requireFromWeb = createRequire(path.join(webRoot, "package.json"));
const modules = new Map();

// 执行真正的客户端 store 与依赖，保留颜色、格式及卡数校验的实际行为。
function loadTypeScript(filename) {
  if (modules.has(filename)) return modules.get(filename).exports;
  const module = { exports: {} };
  modules.set(filename, module);
  const source = ts.transpileModule(readFileSync(filename, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const resolve = (specifier) => {
    if (!specifier.startsWith("@/") && !specifier.startsWith(".")) return requireFromWeb(specifier);
    const base = specifier.startsWith("@/")
      ? path.join(webRoot, "src", specifier.slice(2))
      : path.resolve(path.dirname(filename), specifier);
    const candidate = [base, `${base}.ts`, `${base}.mjs`].find(existsSync);
    assert.ok(candidate, `无法定位客户端依赖 ${specifier}`);
    return candidate.endsWith(".ts") ? loadTypeScript(candidate) : requireFromWeb(candidate);
  };
  Function("module", "exports", "require", source)(module, module.exports, resolve);
  return module.exports;
}

const { useDeckStore } = loadTypeScript(path.join(webRoot, "src", "store", "deckStore.ts"));
const leader = { number: "OP18-060", name: "军子宫", color: "紫/黑", type: "Leader", subscript: 4, keyWords: "神之骑士团" };
const mma = { number: "OP18-093", name: "MMA", color: "黑", type: "Character", cost: 9, subscript: 5, keyWords: "埃鲁巴夫" };

function reset() {
  useDeckStore.setState({ leader, entries: [], format: "Unrestricted", notice: null });
}

test("客户端组卡可添加超过四张MMA并保留五十张的数量", () => {
  reset();
  for (let count = 0; count < 50; count++) useDeckStore.getState().addCard(mma);
  assert.equal(useDeckStore.getState().entries[0].count, 50);
});

test("普通角色仍最多四张且MMA规则不会绕过领袖颜色校验", () => {
  reset();
  const normal = { ...mma, number: "OP18-084", name: "军子宫", cost: 1 };
  for (let count = 0; count < 5; count++) useDeckStore.getState().addCard(normal);
  assert.equal(useDeckStore.getState().entries[0].count, 4);
  reset();
  useDeckStore.setState({ leader: { ...leader, color: "红" } });
  useDeckStore.getState().addCard(mma);
  assert.equal(useDeckStore.getState().entries.length, 0);
  assert.match(useDeckStore.getState().notice.message, /颜色/);
});
