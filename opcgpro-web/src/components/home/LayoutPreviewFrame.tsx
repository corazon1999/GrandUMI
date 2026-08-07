"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ContainerResponsiveProvider } from "@/components/ui/ResponsiveScope";

export type LayoutPreviewMode = "desktop" | "mobile-landscape" | "mobile-portrait";

export const LAYOUT_PREVIEW_STORAGE_KEY = "grandumi_home_layout_preview";

export const LAYOUT_PREVIEW_OPTIONS: Array<{
  value: LayoutPreviewMode;
  label: string;
  description: string;
  width?: number;
  height?: number;
}> = [
  {
    value: "desktop",
    label: "电脑",
    description: "占满当前浏览器窗口",
  },
  {
    value: "mobile-landscape",
    label: "手机横屏",
    description: "844 × 390",
    width: 844,
    height: 390,
  },
  {
    value: "mobile-portrait",
    label: "手机竖屏",
    description: "390 × 844",
    width: 390,
    height: 844,
  },
];

export function isLayoutPreviewMode(value: string | null): value is LayoutPreviewMode {
  return LAYOUT_PREVIEW_OPTIONS.some((option) => option.value === value);
}

export function useLayoutPreviewMode() {
  const [mode, setModeState] = useState<LayoutPreviewMode>("desktop");

  useEffect(() => {
    try {
      const saved = localStorage.getItem(LAYOUT_PREVIEW_STORAGE_KEY);
      if (isLayoutPreviewMode(saved)) setModeState(saved);
    } catch {
      // 本地存储不可用时使用默认电脑布局。
    }
  }, []);

  const setMode = useCallback((next: LayoutPreviewMode) => {
    setModeState(next);
    try {
      localStorage.setItem(LAYOUT_PREVIEW_STORAGE_KEY, next);
    } catch {
      // 布局切换本身仍然生效，仅不保留到下次访问。
    }
  }, []);

  return [mode, setMode] as const;
}

export default function LayoutPreviewFrame({
  mode,
  children,
}: {
  mode: LayoutPreviewMode;
  children: React.ReactNode;
}) {
  const hostRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(1);
  const option = LAYOUT_PREVIEW_OPTIONS.find((item) => item.value === mode) ?? LAYOUT_PREVIEW_OPTIONS[0];
  const isDesktop = mode === "desktop";

  useEffect(() => {
    if (isDesktop) {
      setScale(1);
      return;
    }

    const host = hostRef.current;
    if (!host || !option.width || !option.height) return;

    const updateScale = () => {
      const availableWidth = Math.max(1, host.clientWidth - 32);
      const availableHeight = Math.max(1, host.clientHeight - 32);
      setScale(Math.min(1, availableWidth / option.width!, availableHeight / option.height!));
    };

    updateScale();
    const observer = new ResizeObserver(updateScale);
    observer.observe(host);
    return () => observer.disconnect();
  }, [isDesktop, option.height, option.width]);

  return (
    <div ref={hostRef} className="relative h-[100dvh] w-full overflow-hidden bg-black">
      <div
        data-layout-preview={mode}
        className={`@container overflow-hidden bg-gray-950 ${
          isDesktop
            ? "h-full w-full"
            : "absolute left-1/2 top-1/2 rounded-2xl ring-1 ring-gray-700 shadow-2xl"
        }`}
        style={
          isDesktop
            ? { containerType: "size", transform: "translateZ(0)" }
            : {
                containerType: "size",
                width: option.width,
                height: option.height,
                transform: `translate(-50%, -50%) scale(${scale})`,
                transformOrigin: "center",
              }
        }
      >
        <ContainerResponsiveProvider>{children}</ContainerResponsiveProvider>
      </div>
    </div>
  );
}
