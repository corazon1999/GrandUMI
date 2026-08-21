import assert from "node:assert/strict";
import test from "node:test";
import {
  elapsedMillisecondsFromServerSync,
  remainingSecondsFromServer,
} from "../src/lib/serverCountdown.mjs";

test("服务端时间锚点不受玩家设备快慢二十分钟影响", () => {
  const deadline = "2026-08-18T12:01:00.000Z";
  const serverNow = "2026-08-18T12:00:00.000Z";

  // 计算只依赖服务端锚点与单调耗时，设备墙上时钟偏差不会参与结果。
  assert.equal(remainingSecondsFromServer(deadline, serverNow, 0), 60);
  assert.equal(remainingSecondsFromServer(deadline, serverNow, 2_500), 58);
  assert.equal(remainingSecondsFromServer(deadline, serverNow, 60_000), 0);
});

test("缺少新字段时仍兼容旧服务端快照", () => {
  const originalNow = Date.now;
  Date.now = () => Date.parse("2026-08-18T12:00:30.000Z");
  try {
    assert.equal(
      remainingSecondsFromServer("2026-08-18T12:01:00.000Z", null, 0),
      30,
    );
  } finally {
    Date.now = originalNow;
  }
});

test("操作棋钟只使用服务端时间差和单调流逝时间", () => {
  assert.equal(elapsedMillisecondsFromServerSync(
    "2026-08-18T12:00:00.000Z",
    "2026-08-18T12:00:00.250Z",
    1_500,
  ), 1_750);
  assert.equal(elapsedMillisecondsFromServerSync(null, null, 1_500), 1_500);
  assert.equal(elapsedMillisecondsFromServerSync(
    "2026-08-18T12:00:01.000Z",
    "2026-08-18T12:00:00.000Z",
    -100,
  ), 0);
});
