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
  assert.match(modal, /title=\{isMain \? "确认使用主要事件" : "确认使用反击事件"\}/);
  assert.match(modal, /onClose=\{onCancel\}/);
});

test("反击事件确认框兼容手机安全区和最小触控尺寸", async () => {
  const source = await readSource("../src/components/game/CounterEventConfirmModal.tsx");

  assert.match(source, /<Modal[\s\S]*?mobileSheet[\s\S]*?>/);
  assert.ok((source.match(/className="min-h-12/g) ?? []).length >= 2);
  assert.match(source, />\s*取消\s*<\/button>/);
  assert.match(source, />\s*确认打出\s*<\/button>/);
});

test("主要阶段拒绝反击专用事件，并在双模式事件出牌前二次确认", async () => {
  const [actions, modal] = await Promise.all([
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/components/game/CounterEventConfirmModal.tsx"),
  ]);

  assert.match(actions, /selectedIsCounterOnlyEvent/);
  assert.match(actions, /selectedHandCard\?\.type === "Event" && !selectedHandCard\.effectTags\.includes\("EventMain"\)/);
  assert.match(actions, /selected\.effectTags\.includes\("EventMain"\)[\s\S]*?selected\.effectTags\.includes\("EventCounter"\)/);
  assert.match(actions, /setPendingMainEvent\(/);
  assert.match(actions, /<CounterEventConfirmModal[\s\S]*?mode="main"/);
  assert.match(actions, /my\?\.handCardNumbers\[pending\.handIndex\] !== pending\.cardNumber/);
  assert.match(actions, /GameRequest\.playCard\(pending\.handIndex\)/);
  assert.match(modal, /确认使用主要事件/);
  assert.match(modal, /mode = "counter"/);
  assert.match(modal, /min-h-12/);
});
