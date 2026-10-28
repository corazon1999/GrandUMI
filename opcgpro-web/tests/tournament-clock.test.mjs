import assert from "node:assert/strict";
import test from "node:test";
import { tableDeadline, tableOverdue, tableClockLabel } from "../src/lib/tournamentClock.ts";

const table = (values = {}) => ({ id: "table-1", player0: "p0", player1: "p1", startedAt: 1000, status: "playing", confirmed: false, ...values });
const round = (stage, tables) => ({ stage, status: "live", tables });

test("旧赛事的决赛默认不限时，半决赛和瑞士轮保留默认时限", () => {
  const current = table();
  const final = round("cut", [current]);
  assert.equal(tableDeadline(final, current), null);
  assert.equal(tableOverdue(final, current, 86_400_000), false);
  assert.equal(tableClockLabel(final, current, 86_400_000), "决赛不限时");
  assert.equal(tableDeadline(round("cut", [current, table()]), current), 1_801_000);
  assert.equal(tableDeadline(round("swiss", [current]), current), 1_801_000);
});

test("单桌自定义截止时间优先于原开局时刻，取消时限后不再显示超时", () => {
  const current = table({ timeLimitMs: 60_000, deadlineAt: 2_000_000 });
  const swiss = round("swiss", [current]);
  assert.equal(tableDeadline(swiss, current), 2_000_000);
  assert.equal(tableClockLabel(swiss, current, 1_940_000), "本桌剩余 01:00");
  assert.equal(tableClockLabel(swiss, current, 2_000_000), "已超时，等待管理员处理");
  current.timeLimitMs = 0;
  assert.equal(tableDeadline(swiss, current), null);
  assert.equal(tableClockLabel(swiss, current, 86_400_000), "不限时");
});

test("超时只显示待处理提醒，已结算及整轮人工结算不再列为待处理超时", () => {
  const current = table({ timeoutNotifiedAt: 2_000_000 });
  const swiss = round("swiss", [current]);
  assert.equal(tableOverdue(swiss, current, 2_000_000), true);
  current.confirmed = true;
  assert.equal(tableOverdue(swiss, current, 2_000_000), false);
  assert.equal(tableClockLabel(swiss, current, 2_000_000), "计时已结束");
  current.confirmed = false; swiss.status = "manual";
  assert.equal(tableOverdue(swiss, current, 2_000_000), false);
});

test("落座超时受阻桌显示人工提醒，普通受阻桌和轮空不被误报", () => {
  const current = table({ startedAt: null, status: "blocked", timeoutNotifiedAt: 2000 });
  const swiss = round("swiss", [current]);
  assert.equal(tableOverdue(swiss, current, 3000), true);
  current.timeoutNotifiedAt = null;
  assert.equal(tableOverdue(swiss, current, 3000), false);
  assert.equal(tableClockLabel(swiss, table({ player1: null }), 3000), "本轮轮空");
});
