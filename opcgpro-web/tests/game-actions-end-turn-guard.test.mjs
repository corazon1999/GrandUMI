import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/game/GameActions.tsx", import.meta.url),
  "utf8",
);

test("结束回合与普通操作区保持视觉隔离", () => {
  assert.match(source, /回合控制/);
  assert.match(source, /border-t border-rose-200\/20 pt-4/);
});

test("存在其他操作时先进入确认状态", () => {
  const guardDeclaration = source.match(
    /const hasOtherAction =([\s\S]*?);\r?\n\r?\n  useEffect/,
  );
  assert.ok(guardDeclaration, "应声明结束回合前的其他可用操作集合");
  for (const action of [
    "canAttack",
    "isSelectingTarget",
    "canPlay",
    "canActivate",
    "canAttachDon",
    "canPassCounter",
  ]) {
    assert.match(guardDeclaration[1], new RegExp(action));
  }
  assert.match(
    source,
    /if \(hasOtherAction\) \{\s*setIsEndTurnConfirming\(true\);\s*return;\s*\}\s*endTurn\(\);/,
  );
});

test("确认操作可取消且会在三秒后自动恢复", () => {
  assert.match(source, /window\.setTimeout\(\(\) => setIsEndTurnConfirming\(false\), 3_000\)/);
  assert.match(source, /仍有可用操作，确认结束？/);
  assert.match(source, /grid grid-cols-2 gap-2/);
  assert.match(source, />\s*取消\s*</);
  assert.match(source, />\s*确认结束\s*</);
});
