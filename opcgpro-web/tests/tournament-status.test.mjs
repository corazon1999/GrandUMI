import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const status = read("../src/lib/tournamentStatus.ts");
const panel = read("../src/components/home/TournamentPanel.tsx");
const verification = read("../src/components/home/TournamentLayoutVerification.tsx");

test("赛事列表的全部可见状态都有独立颜色语义和中文标签", () => {
  for (const key of ["pending", "rejected", "approved", "swiss", "cut", "completed", "cancelled"]) {
    assert.match(status, new RegExp(`${key}: \\{ label:`));
  }
  assert.match(status, /accent: "#a78bfa"/);
  assert.match(status, /accent: "#38bdf8"/);
  assert.match(status, /accent: "#34d399"/);
  assert.match(status, /accent: "#f59e0b"/);
  assert.match(status, /accent: "#f43f5e"/);
});

test("赛事卡片同时渲染状态色带、徽章和可访问的文字说明", () => {
  assert.match(panel, /data-tournament-status=\{item\.status\}/);
  assert.match(panel, /tournament-status-card__content/);
  assert.match(panel, /tournament-status-badge__dot/);
  assert.match(panel, /赛事状态颜色说明/);
});

test("布局验收页覆盖所有赛事列表状态", () => {
  for (const label of ["周末社区杯·报名审核", "OP18 新手挑战赛", "GrandUMI 周末练习赛", "C.G 积分赛", "OP18 淘汰赛·八强", "疯狂星期四·最终决战", "BLG加油杯（取消）"]) {
    assert.match(verification, new RegExp(label));
  }
});
