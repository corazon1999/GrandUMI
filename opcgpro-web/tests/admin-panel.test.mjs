import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [admin, main] = await Promise.all([
  readFile(new URL("../src/components/home/AdminPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/MainPanel.tsx", import.meta.url), "utf8"),
]);

test("管理员控制台集中展示现有服务状态和管理能力", () => {
  assert.match(admin, /data-testid="admin-panel"/);
  assert.match(admin, /服务连接/);
  assert.match(admin, /在线玩家/);
  assert.match(admin, /进行中房间/);
  assert.match(admin, /待审核卡背/);
  assert.match(admin, /<RulesetControlPanel \/>/);
  assert.match(admin, /onOpenCardBackReview/);
  assert.match(admin, /onOpenPlayers/);
});

test("管理入口只对服务端授权账号显示且无权限状态可安全返回", () => {
  assert.match(main, /maintenance\.canManage && \(/);
  assert.match(main, /label="管理中心"/);
  assert.match(main, /view: "admin", label: "管理"/);
  assert.match(admin, /if \(!maintenance\.canManage\)/);
  assert.match(admin, /data-testid="admin-panel-denied"/);
  assert.match(admin, /onReturnToLobby/);
});

test("手机竖屏布局保持单列操作区和至少 44px 触控尺寸", () => {
  assert.match(admin, /grid-cols-2/);
  assert.match(admin, /@\[720px\]:grid-cols-4/);
  assert.match(admin, /@\[860px\]:grid-cols-/);
  assert.match(admin, /min-h-11/);
  assert.match(admin, /min-h-14/);
  assert.match(main, /repeat\(\$\{mobileNavItems\.length\}, minmax\(44px, 1fr\)\)/);
});
