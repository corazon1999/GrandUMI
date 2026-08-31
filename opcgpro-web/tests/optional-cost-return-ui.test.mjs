import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("可选效果的必选成本提供返回是否发动按钮", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /prompt\.extra\?\.canReturnToEffectConfirm === true/);
  assert.match(source, /const returnChoiceIds =/);
  assert.match(source, /prompt\.validChoices\.filter\(\(id\) => !returnChoiceSet\.has\(id\)\)/);
  assert.equal(source.match(/onClick=\{handleReturnToEffectConfirm\}/g)?.length, 2);
  assert.equal(source.match(/aria-label="取消支付并返回是否发动"/g)?.length, 2);
  assert.ok((source.match(/取消支付并返回/g) ?? []).length >= 2);
  assert.match(source, /min-h-12/);
  assert.match(source, /py-\[calc\(2rem\+var\(--layout-safe-top,0px\)\)\]/);
  assert.match(source, /padding-bottom:calc\(2rem\+var\(--layout-safe-bottom,0px\)\)/);
  assert.equal(source.match(/max-md:sticky max-md:bottom-\[calc\(0\.75rem\+var\(--layout-safe-bottom,0px\)\)\]/g)?.length, 2);
  assert.equal(source.match(/max-md:mb-12/g)?.length, 2);
  assert.match(source, /prompt\.minChoose === 0 && !allowDefaultOrder/);
  assert.match(source, /aria-label="不选择任何目标并继续结算"/);
  assert.match(source, /不选择并继续/);
});
