import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const sprite = await readFile(new URL("../src/lib/sprite.ts", import.meta.url), "utf8");
const catalog = await readFile(new URL("../src/components/home/CardCatalogPanel.tsx", import.meta.url), "utf8");
const cardBack = await readFile(new URL("../src/components/ui/CardBack.tsx", import.meta.url), "utf8");

test("正式主域图片可回退到 IPv4 直连入口", () => {
  assert.match(sprite, /process\.env\.NEXT_PUBLIC_ASSET_ORIGIN/);
  assert.match(sprite, /const PRODUCTION_DIRECT_ORIGIN = "https:\/\/grand-umi\.com"/);
  assert.match(sprite, /export function assetSrc/);
  assert.match(sprite, /return assetSrc\(mapLocalSource/);
  assert.match(sprite, /window\.location\.hostname !== PRODUCTION_HOST/);
  assert.match(sprite, /imageFallbackSources\(\[/);
});

test("卡牌图鉴会依次尝试派生图、原图与外部图后再显示占位", () => {
  assert.match(catalog, /const CARD_IMAGE_TIMEOUT_MS = 15_000/);
  assert.match(catalog, /const CARD_IMAGE_MAX_RETRIES = 2/);
  assert.match(catalog, /retryCountRef\.current >= CARD_IMAGE_MAX_RETRIES/);
  assert.match(catalog, /window\.setTimeout\(handleImageFailure, CARD_IMAGE_TIMEOUT_MS\)/);
  assert.match(catalog, /图片暂不可用/);
});

test("高清卡图或异画缺失时会回退缩略图与同卡默认画面", () => {
  assert.match(sprite, /const lowResolutionSrc = variant === "display" \? thumbSrc\(rawSrc\) : null/);
  assert.match(sprite, /const baseRawSrc = alternateBaseCardSrc\(rawSrc\)/);
  assert.match(sprite, /const baseLowResolutionSrc = variant === "display" && baseRawSrc/);
  assert.match(sprite, /derivedSrc,\s*lowResolutionSrc,\s*baseDerivedSrc,\s*baseLowResolutionSrc/);
  assert.match(sprite, /return `\?v=\$\{CARD_ASSET_VERSION\}`/);
  assert.match(sprite, /return `\$\{suffix\}&r=\$\{CARD_ASSET_VERSION\}`/);
});

test("自定义卡背超时后仅重试直连入口并回退内置卡背", () => {
  assert.match(cardBack, /const CUSTOM_CARD_BACK_TIMEOUT_MS = 6_000/);
  assert.match(cardBack, /assetSrc\(customImagePath\)/);
  assert.match(cardBack, /directAssetSrc\(customImage\)/);
  assert.match(cardBack, /setCustomImageFailed\(true\)/);
  assert.match(cardBack, /if \(customImage && !customImageFailed\)/);
});
