import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { derivedRelativePath } from "../scripts/check-card-image-assets.mjs";
import {
  expectedManifestAssetPaths,
  manifestSpriteToWebpPath,
} from "../scripts/check-card-image-manifest.mjs";

const testDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(testDir, "..", "..");
const physicalAssetTest = process.env.GRANDUMI_REPOSITORY_VERIFICATION === "1"
  ? { skip: "仓库验证只校验发布清单；测试服部署由 check-card-image-assets.mjs 校验实体卡图" }
  : {};

test("原始 PNG 和 JPEG 会映射到同目录的 WebP", () => {
  assert.equal(derivedRelativePath("st34/ST34-004.png"), "st34/ST34-004.webp");
  assert.equal(derivedRelativePath("promo/P-155.JPEG"), "promo/P-155.webp");
  assert.equal(
    derivedRelativePath("op17/OP17-011.png?v=d3707ea9a4f"),
    "op17/OP17-011.webp",
  );
});

test("卡图发布清单会展开为缩略图和高清图，并忽略缓存参数", () => {
  assert.equal(
    manifestSpriteToWebpPath("/cards/op17/OP17-046_01.png?v=2617121c4341"),
    "op17/OP17-046_01.webp",
  );
  assert.deepEqual(
    expectedManifestAssetPaths({
      "OP17-046": ["/cards/op17/OP17-046.png", "/cards/op17/OP17-046_01.png?v=1"],
    }),
    [
      "cards-thumb/op17/OP17-046.webp",
      "cards-thumb/op17/OP17-046_01.webp",
      "cards-webp/op17/OP17-046.webp",
      "cards-webp/op17/OP17-046_01.webp",
    ],
  );
});

test("宣传卡数据中的每张主卡图都有缩略图和高清展示图", physicalAssetTest, async () => {
  const publicDir = path.join(repoRoot, "opcgpro-web", "public");
  const [cards, manifest] = await Promise.all([
    readFile(path.join(publicDir, "data", "P.json"), "utf8").then(JSON.parse),
    readFile(path.join(publicDir, "data", "imageManifest.json"), "utf8").then(JSON.parse),
  ]);

  for (const card of cards) {
    const source = manifest[card.number]?.[0] ?? `/cards/p/${card.number}.png`;
    assert.match(source, /^\/cards\//, `${card.number} 的主卡图必须使用本地卡图资源`);
    const relativePath = derivedRelativePath(source.slice("/cards/".length));
    await Promise.all([
      access(path.join(publicDir, "cards-thumb", relativePath)),
      access(path.join(publicDir, "cards-webp", relativePath)),
    ]);
  }
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
  assert.match(syncScript, /check-card-image-manifest\.mjs/);
  assert.match(syncScript, /--list/);
  assert.match(syncScript, /正式服卡图清单校验通过/);
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
    /ops\/server\/deploy-test\.sh' \| bash -s --/,
  );
});
