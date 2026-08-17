import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("反击事件必须经过二次确认才能发送出牌请求", async () => {
  const [source, modal] = await Promise.all([
    readSource("../src/components/game/HandArea.tsx"),
    readSource("../src/components/game/CounterEventConfirmModal.tsx"),
  ]);

  assert.match(source, /const \[pendingCounterEvent, setPendingCounterEvent\] = useState/);
  assert.match(source, /else if \(isCounterEventPlayable\(c, i\) && c\) \{\s*setPendingCounterEvent/);
  assert.doesNotMatch(source, /else if \(isCounterEventPlayable\(c, i\)\) GameRequest\.playCounterEvent\(i\)/);
  assert.match(source, /<CounterEventConfirmModal/);
  assert.match(source, /onCancel=\{\(\) => setPendingCounterEvent\(null\)\}/);
  assert.match(source, /if \(serverCards\[pending\.handIndex\]\?\.number !== pending\.cardNumber\) return/);
  assert.match(source, /GameRequest\.playCounterEvent\(pending\.handIndex\)/);
  assert.match(modal, /title="确认使用反击事件"/);
  assert.match(modal, /onClose=\{onCancel\}/);
});

test("反击事件确认框兼容手机安全区和最小触控尺寸", async () => {
  const source = await readSource("../src/components/game/CounterEventConfirmModal.tsx");

  assert.match(source, /<Modal[\s\S]*?mobileSheet[\s\S]*?>/);
  assert.ok((source.match(/className="min-h-12/g) ?? []).length >= 2);
  assert.match(source, />\s*取消\s*<\/button>/);
  assert.match(source, />\s*确认打出\s*<\/button>/);
});
