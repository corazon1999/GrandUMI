import assert from "node:assert/strict";
import test from "node:test";
import { positionLineChartTooltip } from "../src/lib/lineChartTooltip.ts";

test("折线图首尾数据点的提示框不会越过左右边缘", () => {
  assert.deepEqual(positionLineChartTooltip(32, 24, 720, 220, 168, 58), { x: 4, y: 38 });
  assert.deepEqual(positionLineChartTooltip(688, 24, 720, 220, 168, 58), { x: 548, y: 38 });
});

test("高低数据点的提示框自动选择下方或上方", () => {
  assert.deepEqual(positionLineChartTooltip(360, 24, 720, 220, 168, 58), { x: 276, y: 38 });
  assert.deepEqual(positionLineChartTooltip(360, 196, 720, 220, 168, 58), { x: 276, y: 124 });
});

test("极端坐标也会被约束在图表可视区域内", () => {
  assert.deepEqual(positionLineChartTooltip(-20, -20, 720, 220, 168, 58), { x: 4, y: 4 });
  assert.deepEqual(positionLineChartTooltip(900, 300, 720, 220, 168, 58), { x: 548, y: 158 });
});
