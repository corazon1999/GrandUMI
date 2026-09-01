import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("GM 面板只在单人测试对局挂载并注册 T 键入口", async () => {
  const [page, panel] = await Promise.all([
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/GMPanel.tsx"),
  ]);

  assert.match(page, /setIsSoloTest\(sessionStorage\.getItem\("isBotMatch"\) === "1"\)/);
  assert.match(page, /\{isSoloTest && !isObserver && !isPlayback && <GMPanel \/>\}/);
  assert.doesNotMatch(page, /\{!isObserver && !isPlayback && <GMPanel \/>\}/);
  assert.match(panel, /if \(e\.key !== "t" && e\.key !== "T"\) return/);
  assert.match(panel, /window\.addEventListener\("keydown", onKeyDown\)/);
});

test("手机竖屏单人测试提供安全区内的 GM 入口和可滚动面板", async () => {
  const panel = await readSource("../src/components/game/GMPanel.tsx");

  assert.match(panel, /const rotateQuarterTurn = useLayoutQuarterTurn\(\)/);
  assert.match(panel, /\{rotateQuarterTurn && !open && \(/);
  assert.match(panel, /min-h-12 min-w-12/);
  assert.match(panel, /--layout-safe-left/);
  assert.match(panel, /--layout-safe-right/);
  assert.match(panel, /--layout-safe-top/);
  assert.match(panel, /--layout-safe-bottom/);
  assert.match(panel, /100cqw/);
  assert.match(panel, /100cqh/);
  assert.match(panel, /overflow-y-auto/);
});
