import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [admin, main, protocol, store, types] = await Promise.all([
  readFile(new URL("../src/components/home/AdminPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/MainPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/store/netStore.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/types/net.ts", import.meta.url), "utf8"),
]);

test("管理员控制台集中展示服务状态和管理能力", () => {
  assert.match(admin, /data-testid="admin-panel"/);
  assert.match(admin, /服务连接/);
  assert.match(admin, /在线玩家/);
  assert.match(admin, /进行中房间/);
  assert.match(admin, /待审核卡背/);
  assert.match(admin, /<RulesetControlPanel \/>/);
  assert.match(admin, /onOpenCardBackReview/);
  assert.match(admin, /onOpenPlayers/);
});

test("点击在线玩家状态卡可查看近一周和近一月峰值图", () => {
  assert.match(admin, /controls="online-peak-panel"/);
  assert.match(admin, /setShowPeakChart/);
  assert.match(admin, /<PeakChart points=/);
  assert.match(admin, /peakRange === range/);
  assert.match(admin, /每日在线玩家峰值/);
  assert.match(admin, /<table className="sr-only">/);
  assert.match(protocol, /case "MsgAdminOperations"/);
  assert.match(store, /peaks7: \[\]/);
  assert.match(admin, /adminOperations\.onlineCount \?\? "—"/);
  assert.match(types, /interface OnlinePlayerPeakPoint/);
});

test("管理员可提交测试服与正式服最新版本发布任务", () => {
  assert.match(admin, /一键部署测试服到最新/);
  assert.match(admin, /一键发布正式服到最新/);
  assert.match(admin, /window\.confirm/);
  assert.match(admin, /HomeRequest\.deployLatest\(environment\)/);
  assert.match(protocol, /proto: "MsgAdminDeploy", environment/);
  assert.match(types, /AdminDeploymentEnvironment = "test" \| "production"/);
});

test("管理面板展示低频缓存的每日场次与磁盘容量", () => {
  assert.match(admin, /每日完成场次/);
  assert.match(admin, /<MatchChart points=/);
  assert.match(admin, /adminOperations\.matches7/);
  assert.match(admin, /服务器磁盘空间/);
  assert.match(admin, /refreshIntervalHours/);
  assert.match(protocol, /matchesUpdatedAt/);
  assert.match(store, /matches30: \[\]/);
  assert.match(types, /interface AdminStorageSnapshot/);
});

test("所有折线图数据点都支持悬停、键盘和触摸查看具体数值", () => {
  assert.match(admin, /function InteractiveLinePoints/);
  assert.equal([...admin.matchAll(/<InteractiveLinePoints/g)].length, 2);
  assert.match(admin, /data-line-point=/);
  assert.match(admin, /data-line-tooltip=/);
  assert.match(admin, /onPointerEnter=/);
  assert.match(admin, /onFocus=/);
  assert.match(admin, /onClick=\{toggleSelected\}/);
  assert.match(admin, /event\.key === "Enter" \|\| event\.key === " "/);
  assert.match(admin, /r="16" fill="transparent"/);
  assert.match(admin, /日期：\{activePoint\.label\}/);
  assert.match(admin, /\{valueLabel\}：\{activePoint\.value\} \{unit\}/);
  assert.match(admin, /aria-label=\{`日期：\$\{point\.label\}，\$\{valueLabel\}：\$\{point\.value\} \$\{unit\}`\}/);
  assert.match(admin, /valueLabel="场次"/);
  assert.match(admin, /valueLabel="人数"/);
});

test("管理员先搜索并选中玩家再执行改名或密码重置", () => {
  assert.match(admin, /玩家账号管理/);
  assert.match(admin, /HomeRequest\.searchAdminPlayers\(query\)/);
  assert.match(admin, /HomeRequest\.renameAdminPlayer/);
  assert.match(admin, /HomeRequest\.resetAdminPlayerPassword/);
  assert.match(admin, /临时密码（请立即交给玩家）/);
  assert.match(admin, /window\.confirm/);
  assert.match(protocol, /case "MsgAdminPlayerSearch"/);
  assert.match(protocol, /case "MsgAdminPlayerUpdate"/);
  assert.match(types, /interface AdminPlayerSummary/);
});

test("管理入口只对服务端授权账号显示且无权限状态可安全返回", () => {
  assert.match(main, /maintenance\.canManage && \(/);
  assert.match(main, /label="管理中心"/);
  assert.match(main, /view: "admin", label: "管理"/);
  assert.match(admin, /if \(!maintenance\.canManage\)/);
  assert.match(admin, /data-testid="admin-panel-denied"/);
  assert.match(admin, /onReturnToLobby/);
});

test("手机竖屏布局保持单列操作区和至少44px触控尺寸", () => {
  assert.match(admin, /grid-cols-2/);
  assert.match(admin, /@\[720px\]:grid-cols-3/);
  assert.match(admin, /@\[1040px\]:grid-cols-6/);
  assert.match(admin, /@\[760px\]:grid-cols-2/);
  assert.match(admin, /@\[680px\]:grid-cols-/);
  assert.match(admin, /min-h-11/);
  assert.match(admin, /min-h-14/);
  assert.match(admin, /overflow-x-auto/);
  assert.match(main, /repeat\(\$\{mobileNavItems\.length\}, minmax\(44px, 1fr\)\)/);
});
