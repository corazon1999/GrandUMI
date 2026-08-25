export type LineChartTooltipPosition = { x: number; y: number };

export function positionLineChartTooltip(
  pointX: number,
  pointY: number,
  chartWidth: number,
  chartHeight: number,
  tooltipWidth: number,
  tooltipHeight: number,
  gap = 14,
): LineChartTooltipPosition {
  const margin = 4;
  const x = Math.max(margin, Math.min(chartWidth - tooltipWidth - margin, pointX - tooltipWidth / 2));
  const preferredY = pointY > chartHeight / 2
    ? pointY - tooltipHeight - gap
    : pointY + gap;
  const y = Math.max(margin, Math.min(chartHeight - tooltipHeight - margin, preferredY));
  return { x, y };
}
