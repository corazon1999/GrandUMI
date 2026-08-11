import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const nextConfig = await readFile(new URL("../next.config.ts", import.meta.url), "utf8");
const deployTest = await readFile(new URL("../../ops/server/deploy-test.sh", import.meta.url), "utf8");
const promote = await readFile(new URL("../../ops/server/promote-approved.sh", import.meta.url), "utf8");
const caddy = await readFile(new URL("../../ops/server/assets.grand-umi.com.caddy", import.meta.url), "utf8");
const networkTuning = await readFile(new URL("../../ops/server/apply-grandumi-network.sh", import.meta.url), "utf8");

test("production build routes hashed Next assets through the CDN origin", () => {
  assert.match(nextConfig, /assetPrefix: assetOrigin \|\| undefined/);
  assert.match(promote, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/assets\.grand-umi\.com'/);
});

test("test build keeps its assets on the test origin", () => {
  assert.match(deployTest, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/test\.grand-umi\.com'/);
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
  assert.match(networkTuning, /tc qdisc del dev "\$interface" root 2>\/dev\/null \|\| true/);
  assert.match(networkTuning, /tc qdisc add dev "\$interface" root handle 1: htb/);
  assert.match(networkTuning, /tc class add dev "\$interface" parent 1: classid 1:10/);
});
