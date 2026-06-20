"use client";

import { useEffect, useState } from "react";

/**
 * 计算固定设计画布(baseW×baseH)等比铺满当前视口的缩放系数。
 * 用于对战页 scale-to-fit：内容按固定基准尺寸布局，整体 transform: scale() 缩放居中，
 * 保证任何宽高比下比例恒定、永不裁切（超宽/超窄屏会有少量黑边）。
 *
 * 移动端适配要点：
 *   - 优先用 window.visualViewport 测量（移动端工具栏伸缩时它才是真实可视尺寸）。
 *   - orientationchange 触发瞬间 iOS/夸克会返回旋转前的旧尺寸，故延迟 + 双 rAF 重测。
 */
export function useStageScale(baseW: number, baseH: number) {
  const [scale, setScale] = useState(1);

  useEffect(() => {
    const vv = window.visualViewport;

    function measure() {
      const w = vv?.width ?? window.innerWidth;
      const h = vv?.height ?? window.innerHeight;
      setScale(Math.min(w / baseW, h / baseH));
    }

    // 旋转后 iOS 尺寸有延迟：立即测一次 + 双 rAF + 兜底 setTimeout 各再测一次
    let raf1 = 0;
    let raf2 = 0;
    let timer = 0;
    function remeasureDeferred() {
      measure();
      raf1 = requestAnimationFrame(() => {
        raf2 = requestAnimationFrame(measure);
      });
      timer = window.setTimeout(measure, 250);
    }

    measure();
    window.addEventListener("resize", measure);
    window.addEventListener("orientationchange", remeasureDeferred);
    vv?.addEventListener("resize", measure);

    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("orientationchange", remeasureDeferred);
      vv?.removeEventListener("resize", measure);
      cancelAnimationFrame(raf1);
      cancelAnimationFrame(raf2);
      clearTimeout(timer);
    };
  }, [baseW, baseH]);

  return scale;
}
