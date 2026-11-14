import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("云赛事冠军分级文本共用金色云纹冠军徽章", async () => {
  const [identity, emblem, styles, fixture] = await Promise.all([
    readSource("../src/components/ui/HunterIdentity.tsx"),
    readSource("../src/components/ui/SeasonTitleEmblem.tsx"),
    readSource("../src/components/ui/HunterIdentity.module.css"),
    readSource("../src/components/home/HuntersLayoutVerification.tsx"),
  ]);
  for (const tier of ["I", "II", "III"]) {
    assert.match(identity, new RegExp(`"云赛事冠军 ${tier}": \\{ faction: "cloudChampion", variant: "cloudChampion", effect: "solar"`));
  }
  assert.match(identity, /seasonPrefix && <small>\{seasonPrefix\}<\/small>/);
  assert.match(emblem, /variant === "cloudChampion"/);
  assert.match(emblem, /云赛事冠军共用同一枚奖杯、云纹和盾牌徽章，等级只由称号文本描述/);
  assert.doesNotMatch(emblem, /<text[^>]*>I<\/text>/);
  assert.match(styles, /\.cloudChampion \{ --accent: 245 158 11; --light: 255 241 188; \}/);
  assert.match(styles, /\.cloudChampion \.honorText strong/);
  // 布局夹具保留单个云赛事冠军样本，避免改变排行榜海域筛选的固定条目数量。
  assert.match(fixture, /"云赛事冠军 I"/);
});
