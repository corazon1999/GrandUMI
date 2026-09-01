import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("导出严格使用玩家当前周期、筛选档位和对应矩阵数据", async () => {
  const [panel, exporter] = await Promise.all([
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/lib/leaderMatchupMatrixExport.ts"),
  ]);

  assert.match(panel, /const currentMatrix = leaderMatchupMatrix\?\.period === period[\s\S]*?leaderMatchupMatrix\.filterTier === filterTier/);
  assert.match(panel, /generateLeaderMatchupMatrixImage\(\{[\s\S]*?period: exportPeriod,[\s\S]*?filterTier: exportFilterTier,[\s\S]*?leaderboardItems: leaderboard\.items,[\s\S]*?matrix: currentMatrix/);
  assert.match(panel, /currentMatrixSelectionRef\.current !== `\$\{exportPeriod\}:\$\{exportFilterTier\}`/);
  assert.match(exporter, /options\.matrix\.period !== options\.period \|\| options\.matrix\.filterTier !== options\.filterTier/);
});

test("正文与文件名共用同一份本地秒级时间戳", async () => {
  const exporter = await readSource("../src/lib/leaderMatchupMatrixExport.ts");

  assert.match(exporter, /text: `\$\{year\}-\$\{month\}-\$\{day\} \$\{hours\}:\$\{minutes\}:\$\{seconds\}`/);
  assert.match(exporter, /filename: `\$\{year\}\$\{month\}\$\{day\}-\$\{hours\}\$\{minutes\}\$\{seconds\}`/);
  assert.match(exporter, /生成时间：\$\{timestamp\.text\}/);
  assert.match(exporter, /GrandUMI-Leader对阵-\$\{PERIOD_LABELS\[options\.period\]\}-\$\{timestamp\.filename\}\.png/);
  assert.match(exporter, /const generatedAt = options\.generatedAt \?\? new Date\(\);[\s\S]*?renderLeaderMatchupMatrixImage\(\{ \.\.\.options, generatedAt \}\)/);
});

test("完整矩阵画布只按 Leader 数量计算，不依赖滚动视口", async () => {
  const exporter = await readSource("../src/lib/leaderMatchupMatrixExport.ts");
  const layoutBlock = exporter.slice(
    exporter.indexOf("export function getLeaderMatchupMatrixExportLayout"),
    exporter.indexOf("function pad"),
  );

  assert.match(exporter, /const LEADER_MATRIX_EXPORT_MIN_WIDTH = 1200;/);
  assert.match(layoutBlock, /width: Math\.max\([\s\S]*?LEADER_MATRIX_EXPORT_MIN_WIDTH,[\s\S]*?normalizedCount \* LEADER_MATRIX_EXPORT_CELL_WIDTH/);
  assert.match(layoutBlock, /normalizedCount \* LEADER_MATRIX_EXPORT_ROW_HEIGHT/);
  assert.doesNotMatch(layoutBlock, /innerWidth|clientWidth|scrollWidth|viewport/);
  assert.match(exporter, /leaders\.forEach\(\(leader, rowIndex\)/);
  assert.match(exporter, /leaders\.forEach\(\(opponent, columnIndex\)/);
});

test("导出按钮覆盖准备、连续点击锁定、成功和错误状态", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");

  assert.match(panel, /if \(matrixExportState === "exporting"\) return;/);
  assert.match(panel, /disabled=\{matrixExportDisabled\}/);
  assert.match(panel, /aria-busy=\{matrixExportState === "exporting" \|\| undefined\}/);
  assert.match(panel, /正在导出…/);
  assert.match(panel, /准备导出数据…/);
  assert.match(panel, /已下载 PNG/);
  assert.match(panel, /重试导出/);
  assert.match(panel, /role="alert"/);
  assert.match(panel, /rankingTab === "leader"[\s\S]*?HomeRequest\.requestLeaderMatchupMatrix\(period, filterTier\)/);
});

test("请求失败可重试，无 Leader 时明确禁用且不伪装成加载", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");

  assert.match(panel, /const matrixExportRequestFailed = failed \|\| currentMatrix\?\.result === false;/);
  assert.match(panel, /const matrixExportUnavailable = !loading[\s\S]*?currentMatrix\?\.result === true[\s\S]*?matrixExportLeaders\.length === 0;/);
  assert.match(panel, /const matrixExportDataLoading = !matrixExportReady[\s\S]*?!matrixExportRequestFailed[\s\S]*?!matrixExportUnavailable;/);
  assert.match(panel, /const matrixExportDisabled = matrixExportState === "exporting"[\s\S]*?matrixExportDataLoading[\s\S]*?matrixExportUnavailable;/);
  assert.match(panel, /if \(matrixExportUnavailable\) return;/);
  assert.match(panel, /if \(loading \|\| failed\) HomeRequest\.requestLeaderLeaderboard\(period, filterTier\);[\s\S]*?HomeRequest\.requestLeaderMatchupMatrix\(period, filterTier\);/);
  assert.match(panel, /matrixExportUnavailable[\s\S]*?暂无可导出数据[\s\S]*?matrixExportRequestFailed[\s\S]*?重试导出/);
});

test("无可导出数据文案保持中英日同步", async () => {
  const i18n = await readSource("../src/i18n/core.mjs");

  assert.match(i18n, /"暂无可导出数据": "No data to export"/);
  assert.match(i18n, /"暂无可导出数据": "出力できるデータがありません"/);
});

test("下载释放对象 URL，卡图失败时仍能输出文字占位", async () => {
  const exporter = await readSource("../src/lib/leaderMatchupMatrixExport.ts");

  assert.match(exporter, /finally \{[\s\S]*?URL\.revokeObjectURL\(url\)/);
  assert.match(exporter, /async function loadLeaderImage[\s\S]*?return null;/);
  assert.match(exporter, /if \(image && image\.naturalWidth > 0[\s\S]*?else \{[\s\S]*?fillText\(leaderNumber/);
  assert.match(exporter, /image\.crossOrigin = "anonymous"/);
  assert.match(exporter, /try \{[\s\S]*?canvas\.toBlob[\s\S]*?catch \(error\)/);
});

test("导出控件在手机竖屏换行且保留 44px 触控目标", async () => {
  const panel = await readSource("../src/components/home/LeaderLeaderboardPanel.tsx");
  const controls = panel.slice(
    panel.indexOf('{rankingTab === "leader" && ('),
    panel.indexOf("</header>"),
  );

  assert.match(controls, /flex w-full flex-col gap-2 @\[900px\]:w-auto @\[900px\]:flex-row/);
  assert.match(controls, /导出一图流[\s\S]*?<\/button>/);
  assert.match(controls, /min-h-11 w-full whitespace-nowrap/);
  assert.ok(controls.indexOf("FILTER_TIERS.map") < controls.indexOf("handleMatrixExport"));
  assert.ok(controls.indexOf("handleMatrixExport") < controls.indexOf("PERIODS.map"));
});
