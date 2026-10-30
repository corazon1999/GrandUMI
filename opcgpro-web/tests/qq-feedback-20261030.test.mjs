import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";
import ts from "typescript";

const root = path.resolve(import.meta.dirname, "..");
const require = createRequire(path.join(root, "package.json"));
const modules = new Map();
function load(file) {
  if (modules.has(file)) return modules.get(file).exports;
  const module = { exports: {} }; modules.set(file, module);
  const compiled = ts.transpileModule(readFileSync(file, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const resolve = specifier => {
    if (!specifier.startsWith(".") && !specifier.startsWith("@/")) return require(specifier);
    const base = specifier.startsWith("@/") ? path.join(root, "src", specifier.slice(2)) : path.resolve(path.dirname(file), specifier);
    const found = [base, `${base}.ts`, `${base}.mjs`].find(existsSync);
    assert.ok(found, `未找到客户端依赖：${specifier}`);
    return found.endsWith(".ts") ? load(found) : require(found);
  };
  Function("module", "exports", "require", compiled)(module, module.exports, resolve);
  return module.exports;
}
const loader = load(path.join(root, "src/data/CardLoader.ts"));
const previousFetch = globalThis.fetch;
try {
  globalThis.fetch = async url => ({ ok: true,
    json: async () => JSON.parse(readFileSync(path.join(root, "public", url.split("?")[0]), "utf8")),
  });
  await loader.loadAllCards();
} finally { globalThis.fetch = previousFetch; }

test("卡鲁默认及可选卡图仅保留经过核实的卡鲁图", () => {
  assert.deepEqual(loader.getCard("EB02-001").sprites, ["/cards/eb02/EB02-001_alt2.png"]);
  assert.equal(loader.getGameCard("EB02-001").sprite, "/cards/eb02/EB02-001_alt2.png");
});
for (const sprite of ["/cards/eb02/EB02-001.png", "/cards/eb02/EB02-001_01.png?v=old"]) {
  test(`旧卡组错误映射可恢复正确卡鲁图：${sprite}`, () => {
    assert.equal(loader.getGameCard("EB02-001", { "EB02-001": sprite }).sprite, "/cards/eb02/EB02-001_alt2.png");
    loader.applyCachedSprite("EB02-001", sprite);
    assert.equal(loader.getCard("EB02-001").sprite, "/cards/eb02/EB02-001_alt2.png");
  });
}
test("贝莱特王子没有生命触发且相邻甚平的合法触发保留", () => {
  assert.equal(loader.getCard("EB01-026").trigger, "");
  assert.match(loader.getCard("EB01-025").trigger, /生命区/);
});
