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

test("峰值在线玩家标题统一且可查看近一周和近一月趋势", () => {
  assert.match(admin, /controls="online-peak-panel"/);
  assert.match(admin, /toggleTrend\("peak"\)/);
  assert.match(admin, /<PlayerCountChart/);
  assert.match(admin, /peakRange === range/);
  assert.match(admin, /chartName="峰值在线玩家"/);
  assert.match(admin, /aria-label="峰值在线玩家"/);
  assert.doesNotMatch(admin, /每日在线玩家峰值/);
  assert.match(admin, /<table className="sr-only">/);
  assert.match(protocol, /case "MsgAdminOperations"/);
  assert.match(store, /peaks7: \[\]/);
  assert.match(admin, /adminOperations\.onlineCount \?\? "—"/);
  assert.match(types, /interface OnlinePlayerPeakPoint/);
});

test("统计区移除当前在线玩家卡并保留峰值趋势与快捷入口", () => {
  assert.doesNotMatch(admin, /label="在线玩家"/);
  assert.doesNotMatch(admin, /detail="正式服当前在线人数"/);
  assert.match(admin, /label="峰值在线玩家"/);
  assert.match(admin, /controls="online-peak-panel"/);
  assert.match(admin, /<span className="block text-sm font-black text-white">在线玩家<\/span>/);
  assert.match(admin, /typeof authoritativeOnlineCount === "number"/);
  assert.match(admin, /className="col-span-2 @\[720px\]:col-span-3 @\[1040px\]:col-span-2"/);
});

test("三个趋势统计卡互斥展开且再次点击当前卡可收起", () => {
  assert.match(admin, /type ExpandedTrend = "peak" \| "dailyActive" \| "matches" \| null/);
  assert.equal([...admin.matchAll(/useState<ExpandedTrend>\(null\)/g)].length, 1);
  assert.match(admin, /setExpandedTrend\(\(current\) => current === trend \? null : trend\)/);
  assert.match(admin, /expandedTrend === "peak" &&/);
  assert.match(admin, /expandedTrend === "dailyActive" &&/);
  assert.match(admin, /expandedTrend === "matches" &&/);
  assert.doesNotMatch(admin, /setShow(?:Peak|DailyActive|Match)Chart/);
});

test("趋势统计卡明确显示选中与收起状态并关联对应面板", () => {
  assert.match(admin, /aria-expanded=\{expanded\}/);
  assert.match(admin, /aria-controls=\{controls\}/);
  assert.match(admin, /data-selected=\{expanded \? "true" : "false"\}/);
  assert.match(admin, /expanded \? "已展开" : "点击查看"/);
  assert.match(admin, /expanded \? "▲" : "▼"/);
  assert.match(admin, /border-white\/80 bg-gray-800\/90 shadow-/);
  assert.equal([...admin.matchAll(/controls="(?:online-peak|daily-active|daily-match)-panel"/g)].length, 3);
});

test("日活玩家按正式服口径提供今日值和近一周一月趋势", () => {
  assert.match(admin, /label="日活玩家"/);
  assert.match(admin, /controls="daily-active-panel"/);
  assert.match(admin, /当天至少成功登录一次的去重玩家/);
  assert.match(admin, /测试服登录不会计入/);
  assert.match(admin, /<DailyActiveChart points=/);
  assert.match(admin, /dailyActiveRange === range/);
  assert.match(protocol, /dailyActive7:/);
  assert.match(store, /dailyActive30: \[\]/);
  assert.match(types, /interface DailyActivePlayerPoint/);
  assert.match(types, /playerTrafficUpdatedAt/);
});

test("管理员发布测试服与正式服前必须申请一次性凭证并二次确认", () => {
  assert.match(admin, /申请测试服部署凭证/);
  assert.match(admin, /申请正式服发布凭证/);
  assert.match(admin, /二次确认并部署测试服/);
  assert.match(admin, /二次确认并发布正式服/);
  assert.match(admin, /HomeRequest\.requestAdminApproval\(operation, environment\)/);
  assert.match(admin, /HomeRequest\.deployLatest\(environment, adminApproval\)/);
  assert.doesNotMatch(admin, /HomeRequest\.deployLatest\(environment\)\)/);
  assert.match(protocol, /proto: "MsgAdminDeploy",[\s\S]{0,80}environment,[\s\S]{0,160}challengeId: approval\?\.challengeId/);
  assert.match(protocol, /proto: "MsgAdminApproval"/);
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
  assert.match(admin, /chartName="日活玩家"/);
});

test("管理员先搜索并选中玩家再执行改名或密码重置", () => {
  assert.match(admin, /玩家账号管理/);
  assert.match(admin, /HomeRequest\.searchAdminPlayers\(query, playerSearchBy\)/);
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
  assert.match(admin, /@\[1040px\]:grid-cols-4/);
  assert.match(admin, /@\[760px\]:grid-cols-2/);
  assert.match(admin, /@\[680px\]:grid-cols-/);
  assert.match(admin, /min-h-11/);
  assert.match(admin, /min-h-14/);
  assert.match(admin, /overflow-x-auto/);
  assert.match(main, /repeat\(\$\{mobileNavItems\.length\}, minmax\(44px, 1fr\)\)/);
});
