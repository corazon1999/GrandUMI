import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("卡组编辑器保存同名卡组时直接交给当前账号的保存流程覆盖", async () => {
  const source = await readSource("../src/components/deck-editor/DeckInfoPanel.tsx");
  const handleSave = source.match(/const handleSave = \(\) => \{([\s\S]*?)\n  \};/);

  assert.ok(handleSave, "应找到卡组保存处理函数");
  assert.match(handleSave[1], /if \(!isValid\(\)\) return;/, "仍需保留卡组合法性检查");
  assert.match(handleSave[1], /doSave\(deckName\);/, "合法卡组应直接按目标名称保存");
  assert.doesNotMatch(handleSave[1], /deckExists|overwrite|loadedName/, "不得再做同名或来源名称判断");
  assert.doesNotMatch(source, /已存在同名卡组|覆盖保存|overwriteTarget/);
});

test("新建卡组仍使用不重复的默认名称", async () => {
  const source = await readSource("../src/components/deck-editor/DeckInfoPanel.tsx");

  assert.match(source, /setDeckName\(nextDeckName\(\)\)/);
});
