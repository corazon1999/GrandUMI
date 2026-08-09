import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/CardBackPlazaPanel.tsx", import.meta.url),
  "utf8",
);

test("卡背广场提供热门与我的投稿两个管理视图", () => {
  assert.match(source, /type GalleryView = "popular" \| "mine"/);
  assert.match(source, /role="tablist" aria-label="卡背广场分类"/);
  assert.match(source, />\s*热门卡背\s*</);
  assert.match(source, /我发布的卡背/);
  assert.match(source, /galleryView === "mine" \? ownedCardBacks : gallery/);
});

test("删除入口只在我的投稿视图为本人投稿显示", () => {
  assert.match(source, /galleryView === "mine" && item\.owned && \(/);
  assert.match(source, /HomeRequest\.deleteCardBack\(cardBackId\)/);
  assert.doesNotMatch(source, /galleryView === "popular" && item\.owned && \(/);
});
