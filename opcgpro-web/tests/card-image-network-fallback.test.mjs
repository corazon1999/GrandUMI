import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const sprite = await readFile(new URL("../src/lib/sprite.ts", import.meta.url), "utf8");
const catalog = await readFile(new URL("../src/components/home/CardCatalogPanel.tsx", import.meta.url), "utf8");
const cardBack = await readFile(new URL("../src/components/ui/CardBack.tsx", import.meta.url), "utf8");

test("正式主域图片可回退到 IPv4 直连入口", () => {
  assert.match(sprite, /const DIRECT_ASSET_ORIGIN = "https:\/\/direct\.grand-umi\.com"/);
  assert.match(sprite, /window\.location\.hostname !== PRODUCTION_HOST/);
  assert.match(sprite, /imageFallbackSources\(\[/);
});

test("卡牌图鉴图片超时会推进到下一候选并最终显示占位", () => {
  assert.match(catalog, /const CARD_IMAGE_TIMEOUT_MS = 5_000/);
  assert.match(catalog, /const CARD_IMAGE_MAX_RETRIES = 1/);
  assert.match(catalog, /retryCountRef\.current >= CARD_IMAGE_MAX_RETRIES/);
  assert.match(catalog, /window\.setTimeout\(handleImageFailure, CARD_IMAGE_TIMEOUT_MS\)/);
  assert.match(catalog, /图片暂不可用/);
});

test("自定义卡背超时后仅重试直连入口并回退内置卡背", () => {
  assert.match(cardBack, /const CUSTOM_CARD_BACK_TIMEOUT_MS = 6_000/);
  assert.match(cardBack, /directAssetSrc\(customImage\)/);
  assert.match(cardBack, /setCustomImageFailed\(true\)/);
  assert.match(cardBack, /if \(customImage && !customImageFailed\)/);
});
