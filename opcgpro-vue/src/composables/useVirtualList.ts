import { ref, computed, onMounted, onUnmounted, type Ref } from "vue";

interface VirtualListOptions {
  itemCount: Ref<number> | (() => number);
  itemHeight: number;
  rowWidth: number;
  gap: number;
  overscan?: number;
}

/**
 * useVirtualList — 虚拟网格列表（Vue 版）
 * 只渲染可视区域内的项目，大幅减少 DOM 节点数。
 * 用法：把返回的 containerRef 绑到滚动容器，并监听其 @scroll="onScroll"。
 */
export function useVirtualList(opts: VirtualListOptions) {
  const { itemHeight, rowWidth, gap, overscan = 2 } = opts;
  const itemCountSrc = opts.itemCount;
  const getItemCount = typeof itemCountSrc === "function" ? itemCountSrc : () => itemCountSrc.value;

  const containerRef = ref<HTMLDivElement | null>(null);
  const scrollTop = ref(0);
  const containerWidth = ref(rowWidth);
  const containerHeight = ref(600);

  const effectiveColumns = computed(() =>
    Math.max(1, Math.floor((containerWidth.value + gap) / (rowWidth + gap))),
  );
  const cellHeight = itemHeight + gap;
  const rowCount = computed(() => Math.ceil(getItemCount() / effectiveColumns.value));
  const totalHeight = computed(() => rowCount.value * cellHeight + gap);

  const startIndex = computed(() => {
    const startRow = Math.max(0, Math.floor(scrollTop.value / cellHeight) - overscan);
    return startRow * effectiveColumns.value;
  });
  const endIndex = computed(() => {
    const startRow = Math.max(0, Math.floor(scrollTop.value / cellHeight) - overscan);
    const visibleRows = Math.ceil(containerHeight.value / cellHeight) + overscan * 2;
    const endRow = Math.min(rowCount.value, startRow + visibleRows);
    return Math.min(getItemCount(), endRow * effectiveColumns.value);
  });

  const visibleItems = computed(() => {
    const items: { index: number; row: number; col: number }[] = [];
    const cols = effectiveColumns.value;
    for (let i = startIndex.value; i < endIndex.value; i++) {
      items.push({ index: i, row: Math.floor(i / cols), col: i % cols });
    }
    return items;
  });

  function onScroll() {
    if (containerRef.value) scrollTop.value = containerRef.value.scrollTop;
  }

  let ro: ResizeObserver | null = null;
  onMounted(() => {
    const el = containerRef.value;
    if (!el) return;
    containerHeight.value = el.clientHeight || 600;
    ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        containerWidth.value = entry.contentRect.width;
        containerHeight.value = entry.contentRect.height || containerHeight.value;
      }
    });
    ro.observe(el);
  });
  onUnmounted(() => ro?.disconnect());

  return { containerRef, totalHeight, startIndex, endIndex, visibleItems, effectiveColumns, onScroll };
}
