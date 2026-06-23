import { ref, onMounted, onUnmounted } from "vue";

/**
 * 计算固定设计画布 (baseW × baseH) 等比铺满当前视口的缩放系数。
 * 用于对战页 scale-to-fit：内容按固定基准尺寸布局，整体 transform: scale() 缩放居中，
 * 保证任何宽高比下比例恒定、永不裁切。
 *
 * 移动端适配要点：
 *   - 优先用 window.visualViewport 测量（移动端工具栏伸缩时它才是真实可视尺寸）。
 *   - orientationchange 触发瞬间 iOS/夸克会返回旋转前的旧尺寸，故延迟 + 双 rAF 重测。
 */
export function useStageScale(baseW: number, baseH: number) {
  const scale = ref(1);

  let raf1 = 0;
  let raf2 = 0;
  let timer = 0;

  function measure() {
    const vv = window.visualViewport;
    const w = vv?.width ?? window.innerWidth;
    const h = vv?.height ?? window.innerHeight;
    scale.value = Math.min(w / baseW, h / baseH);
  }

  function remeasureDeferred() {
    measure();
    raf1 = requestAnimationFrame(() => {
      raf2 = requestAnimationFrame(measure);
    });
    timer = window.setTimeout(measure, 250);
  }

  onMounted(() => {
    measure();
    const vv = window.visualViewport;
    window.addEventListener("resize", measure);
    window.addEventListener("orientationchange", remeasureDeferred);
    vv?.addEventListener("resize", measure);
  });

  onUnmounted(() => {
    const vv = window.visualViewport;
    window.removeEventListener("resize", measure);
    window.removeEventListener("orientationchange", remeasureDeferred);
    vv?.removeEventListener("resize", measure);
    cancelAnimationFrame(raf1);
    cancelAnimationFrame(raf2);
    clearTimeout(timer);
  });

  return scale;
}