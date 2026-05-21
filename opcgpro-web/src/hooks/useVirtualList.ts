"use client";

import { useCallback, useEffect, useRef, useState } from "react";

interface VirtualListOptions {
  itemCount: number;
  itemHeight: number;
  rowWidth: number;
  columns: number;
  gap: number;
  overscan?: number;
}

interface VirtualListResult {
  containerRef: React.RefObject<HTMLDivElement | null>;
  totalHeight: number;
  startIndex: number;
  endIndex: number;
  visibleItems: { index: number; row: number; col: number }[];
}

/**
 * useVirtualList — 虚拟网格列表
 * 只渲染可视区域内的项目，大幅减少 DOM 节点数
 */
export function useVirtualList({
  itemCount,
  itemHeight,
  rowWidth,
  columns,
  gap,
  overscan = 2,
}: VirtualListOptions): VirtualListResult {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [containerWidth, setContainerWidth] = useState(rowWidth);

  // 根据容器宽度动态计算列数
  const effectiveColumns = Math.max(1, Math.floor((containerWidth + gap) / (rowWidth + gap)));
  const rowCount = Math.ceil(itemCount / effectiveColumns);
  const cellHeight = itemHeight + gap;
  const totalHeight = rowCount * cellHeight + gap;

  // 计算可视行范围
  const startRow = Math.max(0, Math.floor(scrollTop / cellHeight) - overscan);
  const visibleRows = Math.ceil((containerRef.current?.clientHeight ?? 600) / cellHeight) + overscan * 2;
  const endRow = Math.min(rowCount, startRow + visibleRows);

  const startIndex = startRow * effectiveColumns;
  const endIndex = Math.min(itemCount, endRow * effectiveColumns);

  // 生成可见项列表
  const visibleItems: { index: number; row: number; col: number }[] = [];
  for (let i = startIndex; i < endIndex; i++) {
    const row = Math.floor(i / effectiveColumns);
    const col = i % effectiveColumns;
    visibleItems.push({ index: i, row, col });
  }

  const handleScroll = useCallback(() => {
    if (containerRef.current) {
      setScrollTop(containerRef.current.scrollTop);
    }
  }, []);

  // 监听容器宽度变化
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setContainerWidth(entry.contentRect.width);
      }
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  return {
    containerRef,
    totalHeight,
    startIndex,
    endIndex,
    visibleItems,
  };
}
