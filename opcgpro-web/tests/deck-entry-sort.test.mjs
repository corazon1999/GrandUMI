import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const storeSource = await readFile(
  new URL("../src/store/deckStore.ts", import.meta.url),
  "utf8",
);

function loadDeckComparator() {
  const sourceFile = ts.createSourceFile(
    "deckStore.ts",
    storeSource,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS,
  );
  const declarations = sourceFile.statements.filter((statement) =>
    (ts.isVariableStatement(statement) && statement.getText(sourceFile).includes("DECK_TYPE_ORDER"))
    || (ts.isFunctionDeclaration(statement) && statement.name?.text === "compareDeckCards"),
  );
  assert.equal(declarations.length, 2, "应能读取卡组专用排序实现");

  const snippet = [
    "const compareCards = (a, b) => a.cost - b.cost || b.subscript - a.subscript || a.number.localeCompare(b.number);",
    ...declarations.map((statement) => statement.getText(sourceFile)),
  ].join("\n");
  const compiled = ts.transpileModule(snippet, {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  }).outputText;
  const module = { exports: {} };
  Function("module", "exports", compiled)(module, module.exports);
  return module.exports.compareDeckCards;
}

const compareDeckCards = loadDeckComparator();
const card = (type, cost, number, subscript = 0) => ({ type, cost, number, subscript });

test("卡组构成先按角色、事件、场地分组，再按费用升序", () => {
  const cards = [
    card("Stage", 1, "OP01-001"),
    card("Character", 7, "OP01-002"),
    card("Event", 0, "OP01-003"),
    card("Character", 2, "OP01-004"),
    card("Stage", 0, "OP01-005"),
    card("Event", 5, "OP01-006"),
  ];

  assert.deepEqual(
    cards.sort(compareDeckCards).map(({ type, cost }) => `${type}:${cost}`),
    ["Character:2", "Character:7", "Event:0", "Event:5", "Stage:0", "Stage:1"],
  );
});

test("同类型同费用沿用既有确定性次级顺序，且条目排序使用专用比较器", () => {
  const cards = [
    card("Character", 3, "OP01-003", 0),
    card("Character", 3, "OP01-002", 1),
    card("Character", 3, "OP01-001", 0),
  ];

  assert.deepEqual(
    cards.sort(compareDeckCards).map(({ number }) => number),
    ["OP01-002", "OP01-001", "OP01-003"],
  );
  assert.match(storeSource, /sort\(\(a, b\) => compareDeckCards\(a\.card, b\.card\)\)/);
});
