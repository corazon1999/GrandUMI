import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const nextConfig = await readFile(new URL("../next.config.ts", import.meta.url), "utf8");
const deployTest = await readFile(new URL("../../ops/server/deploy-test.sh", import.meta.url), "utf8");
const promote = await readFile(new URL("../../ops/server/promote-approved.sh", import.meta.url), "utf8");
const deployHk = await readFile(new URL("../../deploy-hk.ps1", import.meta.url), "utf8");
const caddy = await readFile(new URL("../../ops/server/assets.grand-umi.com.caddy", import.meta.url), "utf8");
const networkTuning = await readFile(new URL("../../ops/server/apply-grandumi-network.sh", import.meta.url), "utf8");
const networkService = await readFile(new URL("../../ops/server/grandumi-network-tuning.service", import.meta.url), "utf8");
const prewarm = await readFile(new URL("../../ops/server/prewarm-assets.sh", import.meta.url), "utf8");
const sprite = await readFile(new URL("../src/lib/sprite.ts", import.meta.url), "utf8");

test("production build keeps critical Next assets same-origin and routes card assets through the CDN", () => {
  assert.doesNotMatch(nextConfig, /assetPrefix\s*:/);
  assert.match(nextConfig, /Next\.js JS\/CSS 始终使用当前站点同源地址/);
  assert.match(promote, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/assets\.grand-umi\.com'/);
  assert.match(deployHk, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/assets\.grand-umi\.com'/);
});

test("home response is dynamic so releases cannot reuse stale HTML", async () => {
  const homePage = await readFile(new URL("../src/app/home/page.tsx", import.meta.url), "utf8");
  const homeClient = await readFile(new URL("../src/app/home/HomeClient.tsx", import.meta.url), "utf8");
  assert.match(homePage, /export const dynamic = "force-dynamic"/);
  assert.doesNotMatch(homePage, /^"use client"/m);
  assert.match(homeClient, /^"use client"/m);
});

test("test build keeps its assets on the test origin", () => {
  assert.match(deployTest, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/test\.grand-umi\.com'/);
});

test("active deployment entry no longer depends on a candidate environment", async () => {
  const deployEntry = await readFile(new URL("../../deploy-test.ps1", import.meta.url), "utf8");
  assert.doesNotMatch(deployEntry, /deploy-new-hk-candidate|candidate\.grand-umi\.com/);
  assert.match(deployEntry, /bash \/opt\/grandumi-test\/ops\/server\/deploy-test\.sh/);
});

test("asset host exposes only cacheable public resources with cross-origin access", () => {
  assert.match(caddy, /^assets\.grand-umi\.com \{/m);
  assert.match(caddy, /\/_next\/static\/\*/);
  assert.match(caddy, /\/card-back-images\/\*/);
  assert.match(caddy, /Access-Control-Allow-Origin "\*"/);
  assert.match(caddy, /Cache-Control "public, max-age=31536000, immutable"/);
  assert.match(caddy, /root \* \/opt\/grandumi\/opcgpro-web\/public/);
  assert.match(caddy, /root \* \/opt\/grandumi\/opcgpro-web\/\.next\/static/);
  assert.match(caddy, /file_server/);
  assert.match(caddy, /@custom_card_backs/);
  assert.match(caddy, /reverse_proxy 127\.0\.0\.1:8080/);
  assert.match(caddy, /respond 404/);
  assert.doesNotMatch(caddy, /reverse_proxy 127\.0\.0\.1:3000/);
  assert.doesNotMatch(caddy, /\/ws/);
});

test("production promotion persists CDN and source-network protection", () => {
  assert.match(promote, /assets\.grand-umi\.com\.caddy/);
  assert.match(promote, /60-grandumi-network\.conf/);
  assert.match(promote, /grandumi-network-tuning\.service/);
  assert.match(promote, /systemctl (?:reload|start) caddy/);
});

test("network shaping can be applied repeatedly after boot or deployment", () => {
  assert.match(networkTuning, /GRANDUMI_EGRESS_RATE:-60mbit/);
  assert.match(networkTuning, /tc qdisc del dev "\$interface" root 2>\/dev\/null \|\| true/);
  assert.match(networkTuning, /tc qdisc add dev "\$interface" root handle 1: htb/);
  assert.match(networkTuning, /tc class add dev "\$interface" parent 1: classid 1:10/);
  assert.match(networkTuning, /burst 32k cburst 32k/);
  assert.match(networkService, /GRANDUMI_EGRESS_RATE=60mbit/);
});

test("release prewarms new chunks and catalog mode covers card thumbnails", () => {
  assert.match(prewarm, /append_files "\$root\/\.next\/static" "\/_next\/static"/);
  assert.match(prewarm, /append_files "\$root\/public\/cards-thumb" "\/cards-thumb"/);
  assert.match(prewarm, /append_files "\$root\/public\/sprites-thumb" "\/sprites-thumb"/);
  assert.match(prewarm, /\/card-back-images\/\$id/);
  assert.match(prewarm, /GRANDUMI_PREWARM_CONCURRENCY:-1/);
  assert.match(prewarm, /GRANDUMI_PREWARM_RATE:-128K/);
  assert.match(prewarm, /--limit-rate "\$rate"/);
  assert.match(promote, /prewarm-assets\.sh" release/);
});

test("card image URLs carry a recovery revision to bypass stale 404 caches", () => {
  assert.match(sprite, /CARD_ASSET_VERSION = `\$\{DATA_VERSION\}-r4`/);
  assert.match(sprite, /`\?v=\$\{CARD_ASSET_VERSION\}`/);
});
