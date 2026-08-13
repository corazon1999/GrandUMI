import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("卡牌组件渲染每回合一次可用标识", () => {
  const source = read("../src/components/ui/CardItem.tsx");
  assert.match(source, /data-once-per-turn-ready="true"/);
  assert.match(source, /每回合1次效果可发动/);
});

test("领袖、角色和舞台均接入服务端权威可用状态", () => {
  const field = read("../src/components/game/FieldArea.tsx");
  const leader = read("../src/components/game/LeaderCard.tsx");
  const stage = read("../src/components/game/StageSlot.tsx");

  assert.match(field, /oncePerTurnEffectAvailable=\{fc\.oncePerTurnEffectAvailable\}/);
  assert.match(leader, /oncePerTurnEffectAvailable=\{player\.leaderOncePerTurnEffectAvailable\}/);
  assert.match(stage, /oncePerTurnEffectAvailable=\{player\?\.stageOncePerTurnEffectAvailable \?\? false\}/);
});

test("开发预览页同时展示有标识与已消耗状态", () => {
  const preview = read("../src/app/dev/once-per-turn-indicator/page.tsx");
  assert.match(preview, /oncePerTurnEffectAvailable/);
  assert.match(preview, /<CardItem card=\{character\} size="md" hideCounter \/>/);
});
