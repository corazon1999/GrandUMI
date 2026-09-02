import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(path, import.meta.url), "utf8");
const [panel, qqWhitelist, layoutPage, layoutFixture] = await Promise.all([
  read("../src/components/home/AdminPanel.tsx"),
  read("../src/components/home/QqWhitelistImportPanel.tsx"),
  read("../src/app/layout-verification/admin-panel/page.tsx"),
  read("../src/components/home/AdminPanelLayoutVerification.tsx"),
]);

const panelSlice = (id, nextId) => {
  const start = panel.indexOf(`id="admin-panel-${id}"`);
  const end = nextId ? panel.indexOf(`id="admin-panel-${nextId}"`, start) : panel.indexOf("<aside", start);
  assert.ok(start >= 0 && end > start, `无法定位 ${id} 分页面板`);
  return panel.slice(start, end);
};

test("管理员面板按四类完整且唯一归档现有功能", () => {
  assert.match(panel, /\{ id: "overview", label: "概览" \}/);
  assert.match(panel, /\{ id: "content", label: "内容管理" \}/);
  assert.match(panel, /\{ id: "operations", label: "运营与安全" \}/);
  assert.match(panel, /\{ id: "rules", label: "规则与发布" \}/);

  const overview = panelSlice("overview", "content");
  const content = panelSlice("content", "operations");
  const operations = panelSlice("operations", "rules");
  const rules = panelSlice("rules");
  assert.match(overview, /管理概览/);
  assert.match(overview, /online-peak-panel/);
  assert.match(overview, /daily-active-panel/);
  assert.match(overview, /daily-match-panel/);
  assert.match(content, /全服滚动公告/);
  assert.match(content, /卡背审核/);
  assert.match(content, /<QqWhitelistImportPanel previewOnly=\{layoutVerification\} \/>/);
  assert.match(operations, /<OperationsWorkbench previewState=/);
  assert.match(operations, /服务器磁盘空间/);
  assert.match(operations, /玩家账号管理/);
  assert.match(rules, /<RulesetControlPanel \/>/);
  assert.match(rules, /<AdminHexCatalogPanel previewState=/);
  assert.match(rules, /版本发布/);

  for (const marker of ["QqWhitelistImportPanel", "OperationsWorkbench", "RulesetControlPanel", "AdminHexCatalogPanel"]) {
    assert.equal([...panel.matchAll(new RegExp(`<${marker}(?:\\s|\\/)`, "g"))].length, 1, `${marker} 必须只挂载一次`);
  }
});

test("分页默认进入概览并通过隐藏面板保活草稿状态", () => {
  assert.match(panel, /useState<AdminTab>\("overview"\)/);
  assert.equal([...panel.matchAll(/role="tabpanel"/g)].length, 4);
  assert.equal([...panel.matchAll(/hidden=\{activeTab !== "/g)].length, 4);
  assert.match(panel, /value=\{announcement\}/);
  assert.doesNotMatch(panel, /activeTab === [^\n]+\? \(/);
});

test("分页具备完整 tab 语义和键盘切换", () => {
  assert.match(panel, /role="tablist"/);
  assert.match(panel, /role="tab"/);
  assert.match(panel, /aria-selected=\{selected\}/);
  assert.match(panel, /aria-controls=\{`admin-panel-\$\{tab\.id\}`\}/);
  assert.match(panel, /aria-labelledby="admin-tab-overview"/);
  assert.match(panel, /tabIndex=\{selected \? 0 : -1\}/);
  assert.match(panel, /event\.key === "ArrowRight"/);
  assert.match(panel, /event\.key === "ArrowLeft"/);
  assert.match(panel, /event\.key === "Home"/);
  assert.match(panel, /event\.key === "End"/);
  assert.match(panel, /tabRefs\.current\[nextIndex\]\?\.focus\(\)/);
});

test("窄屏分页可横向滚动且触控、安全区尺寸合规", () => {
  assert.match(panel, /overflow-x-auto overscroll-x-contain/);
  assert.match(panel, /min-w-\[32rem\]/);
  assert.match(panel, /min-h-11 min-w-28/);
  assert.match(panel, /top-\[var\(--layout-safe-top,env\(safe-area-inset-top\)\)\]/);
  assert.match(panel, /var\(--layout-safe-bottom,env\(safe-area-inset-bottom\)\)/);
});

test("布局验证夹具只使用断开连接的假状态且生产默认返回404", () => {
  assert.match(layoutPage, /export const dynamic = "force-dynamic"/);
  assert.match(layoutPage, /process\.env\.GRANDUMI_LAYOUT_VERIFICATION !== "1"\) notFound\(\)/);
  assert.match(layoutFixture, /data-admin-panel-layout-verification/);
  assert.match(layoutFixture, /connState: "disconnected"/);
  assert.match(layoutFixture, /deploymentAvailable: false/);
  assert.match(layoutFixture, /layoutVerification/);
  assert.doesNotMatch(layoutFixture, /HomeRequest/);
  assert.match(panel, /QqWhitelistImportPanel previewOnly=\{layoutVerification\}/);
  assert.match(panel, /OperationsWorkbench previewState=\{layoutVerification/);
  assert.match(panel, /AdminHexCatalogPanel previewState=\{layoutVerification/);
  assert.match(qqWhitelist, /if \(previewOnly\) return;/);
  assert.match(qqWhitelist, /disabled=\{previewOnly \|\| !preview/);
});
