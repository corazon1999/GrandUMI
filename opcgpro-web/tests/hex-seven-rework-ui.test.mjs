import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

async function source(path) {
  return readFile(new URL(path, import.meta.url), "utf8");
}

test("主要阶段使用服务端权威可出牌标记且旧回放保留本地回退", async () => {
  const [actions, store, protocol] = await Promise.all([
    source("../src/components/game/GameActions.tsx"),
    source("../src/store/gameStore.ts"),
    source("../src/types/net.ts"),
  ]);

  assert.match(actions, /my\?\.handCardCanPlay\?\.\[selectedHandIndex\]/);
  assert.match(actions, /\?\? !selectedIsCounterOnlyEvent/);
  assert.match(actions, /rotateQuarterTurn \? "min-h-\[5\.75rem\]" : "min-h-12"/);
  assert.match(store, /handCardCanPlay: \[\.\.\.\(player\.handCardCanPlay \?\? \[\]\)\]/);
  assert.match(store, /handCardCanPlay\.splice\(handIndex, 1\)/);
  assert.match(protocol, /handCardCanPlay\?: boolean\[\]/);
});

test("具有动态反击值的事件在反击步骤只走弃牌反击入口", async () => {
  const handArea = await source("../src/components/game/HandArea.tsx");

  assert.match(
    handArea,
    /effectiveCounter\(c, i\) > 0\) GameRequest\.playCounterFromHand\(i\)/,
  );
  assert.match(
    handArea,
    /effectiveCounter\(c, i\) <= 0[\s\S]*?effectTags\.includes\("EventCounter"\)/,
  );
});
