"use client";

import { useEffect, useState } from "react";

/**
 * useResponsive — 响应式尺寸检测
 * 根据视口宽度返回适配的 UI 尺寸档位
 */
export function useResponsive() {
  const [size, setSize] = useState<"sm" | "md" | "lg">("md");

  useEffect(() => {
    function update() {
      const w = window.innerWidth;
      if (w < 640) setSize("sm");
      else if (w < 1024) setSize("md");
      else setSize("lg");
    }
    update();
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  return {
    /** UI 尺寸档位 */
    size,
    /** 卡牌尺寸适配 */
    cardSize: size === "sm" ? "sm" as const : "md" as const,
    /** 是否为移动端（宽度 < 768） */
    isMobile: size === "sm",
  };
}
