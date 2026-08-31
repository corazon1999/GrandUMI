import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("真人匹配成功复用统一音效并遵守静音和浏览器解锁边界", async () => {
  const [provider, engine, types] = await Promise.all([
    readSource("../src/components/audio/AudioProvider.tsx"),
    readSource("../src/audio/AudioEngine.ts"),
    readSource("../src/audio/types.ts"),
  ]);

  assert.match(provider, /message\.proto === "MsgMatchFound" && message\.queueKind/);
  assert.match(provider, /play\("matchStart", \{ allowWhenHidden: true \}\)/);
  assert.match(provider, /eventBus\.on\("message", onProtocolMessage\)/);
  assert.match(provider, /eventBus\.off\("message", onProtocolMessage\)/);
  assert.match(engine, /if \(!this\.unlocked \|\| this\.muted \|\| this\.volume <= 0\) return/);
  assert.match(engine, /options\.allowWhenHidden === true && this\.context\?\.state === "running"/);
  assert.match(types, /allowWhenHidden\?: boolean/);
});
