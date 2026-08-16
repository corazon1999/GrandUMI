import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { derivedRelativePath } from "../scripts/check-card-image-assets.mjs";

const testDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(testDir, "..", "..");

test("原始 PNG 和 JPEG 会映射到同目录的 WebP", () => {
  assert.equal(derivedRelativePath("st34/ST34-004.png"), "st34/ST34-004.webp");
  assert.equal(derivedRelativePath("promo/P-155.JPEG"), "promo/P-155.webp");
  assert.equal(
    derivedRelativePath("op17/OP17-011.png?v=d3707ea9a4f"),
    "op17/OP17-011.webp",
  );
});

test("卡图生成结束后会执行完整性审计", async () => {
  const generator = await readFile(
    path.join(repoRoot, "opcgpro-web", "scripts", "gen-card-thumbs.mjs"),
    "utf8",
  );
  assert.match(generator, /auditCardImageAssets/);
  assert.match(generator, /生成后校验通过/);
});

test("线上资源同步同时覆盖缩略图和高清展示图", async () => {
  const syncScript = await readFile(path.join(repoRoot, "sync-assets-hk.sh"), "utf8");
  assert.match(syncScript, /public\/cards-thumb/);
  assert.match(syncScript, /public\/cards-webp/);
  assert.match(syncScript, /find \. -type f -printf/);
  assert.match(syncScript, /root@103\.146\.230\.37/);
  assert.match(syncScript, /"\/www\/cards-thumb"/);
  assert.match(syncScript, /"\/www\/cards-webp"/);
});

test("测试服部署会校验全部卡图而不只校验最新异画", async () => {
  const deployScript = await readFile(
    path.join(repoRoot, "ops", "server", "deploy-test.sh"),
    "utf8",
  );
  const deployEntry = await readFile(path.join(repoRoot, "deploy-test.ps1"), "utf8");
  assert.match(deployScript, /node scripts\/check-latest-card-art\.mjs/);
  assert.match(deployScript, /node scripts\/check-card-image-assets\.mjs/);
  assert.match(deployScript, /rsync -au "\$source_dir\/" "\$target_dir\/"/);
  assert.match(
    deployEntry,
    /bash \/opt\/grandumi-test\/ops\/server\/deploy-test\.sh/,
  );
});
