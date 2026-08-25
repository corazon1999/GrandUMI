import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(here, "..");

async function readSource(relativePath) {
  return fs.readFile(path.join(projectRoot, relativePath), "utf8");
}

test("组卡页返回大厅使用原生链接并在整页刷新前保留登录恢复标记", async () => {
  const source = await readSource("src/components/deck-editor/DeckInfoPanel.tsx");
  const protocol = await readSource("src/net/HomeProtocol.ts");

  assert.match(source, /data-deck-editor-back-link/);
  assert.match(source, /href="\/home"/);
  assert.match(source, /aria-label="返回大厅"/);
  assert.match(source, /onClick=\{\(\) => HomeRequest\.prepareHomeReload\(\)\}/);
  assert.doesNotMatch(source, /useRouter/);
  assert.doesNotMatch(source, /router\.push\("\/home"\)/);
  assert.match(protocol, /HOME_REFRESH_RESUME_KEY = "grandumi_resume_home_after_refresh"/);
  assert.match(protocol, /const resumeHome = sessionStorage\.getItem\(HOME_REFRESH_RESUME_KEY\) === "1"/);
  assert.match(protocol, /prepareHomeReload\(\)[\s\S]*sessionStorage\.setItem\(HOME_REFRESH_RESUME_KEY, "1"\)/);
});

test("手机竖屏主导航常驻返回入口且触控区域不小于 44px", async () => {
  const source = await readSource("src/app/deck-editor/page.tsx");

  assert.match(source, /data-deck-mobile-back/);
  assert.match(source, /grid-cols-\[auto_auto_1fr_1fr\]/);
  assert.match(source, /min-h-11 min-w-11/);
  assert.match(source, /aria-label="返回大厅"/);
  assert.match(source, /onClick=\{\(\) => HomeRequest\.prepareHomeReload\(\)\}/);
});
