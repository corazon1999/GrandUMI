import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("云赛事冠军 I 使用金色云纹冠军徽章而不是普通文本回退", async () => {
  const [identity, emblem, styles, fixture] = await Promise.all([
    readSource("../src/components/ui/HunterIdentity.tsx"),
    readSource("../src/components/ui/SeasonTitleEmblem.tsx"),
    readSource("../src/components/ui/HunterIdentity.module.css"),
    readSource("../src/components/home/HuntersLayoutVerification.tsx"),
  ]);
  assert.match(identity, /"云赛事冠军 I": \{ faction: "cloudChampion", variant: "cloudChampion", effect: "solar"/);
  assert.match(identity, /seasonPrefix && <small>\{seasonPrefix\}<\/small>/);
  assert.match(emblem, /variant === "cloudChampion"/);
  assert.match(emblem, /云赛事冠军：奖杯、云纹和 I 级盾牌组合成独立徽章/);
  assert.match(styles, /\.cloudChampion \{ --accent: 245 158 11; --light: 255 241 188; \}/);
  assert.match(styles, /\.cloudChampion \.honorText strong/);
  assert.match(fixture, /"云赛事冠军 I"/);
});
