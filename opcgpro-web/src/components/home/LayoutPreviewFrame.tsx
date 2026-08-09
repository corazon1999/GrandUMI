"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  ContainerResponsiveProvider,
  LayoutQuarterTurnProvider,
} from "@/components/ui/ResponsiveScope";
import { calculateLayoutScale } from "@/lib/gameLayout";
import {
  LAYOUT_CANVAS_SIZES,
  LAYOUT_PREVIEW_OPTIONS,
  LAYOUT_PREVIEW_STORAGE_KEY,
  normalizeStoredLayoutPreviewMode,
  type LayoutPreviewMode,
  type SelectableLayoutPreviewMode,
} from "@/lib/layoutSettings";

export {
  LAYOUT_PREVIEW_OPTIONS,
  LAYOUT_PREVIEW_STORAGE_KEY,
  type LayoutPreviewMode,
  type SelectableLayoutPreviewMode,
} from "@/lib/layoutSettings";

export function useLayoutPreviewMode() {
  const [mode, setModeState] = useState<SelectableLayoutPreviewMode>("desktop");

  useEffect(() => {
    try {
      const saved = localStorage.getItem(LAYOUT_PREVIEW_STORAGE_KEY);
      const normalized = normalizeStoredLayoutPreviewMode(saved);
      setModeState(normalized);
      if (saved && saved !== normalized) {
        localStorage.setItem(LAYOUT_PREVIEW_STORAGE_KEY, normalized);
      }
    } catch {
      // 本地存储不可用时使用默认电脑布局。
    }
  }, []);

  const setMode = useCallback((next: SelectableLayoutPreviewMode) => {
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
  rotateQuarterTurn = false,
  edgeToEdge = false,
  children,
}: {
  mode: LayoutPreviewMode;
  rotateQuarterTurn?: boolean;
  edgeToEdge?: boolean;
  children: React.ReactNode;
}) {
  const hostRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(1);
  const option = LAYOUT_CANVAS_SIZES[mode];
  const isDesktop = mode === "desktop";

  useEffect(() => {
    if (isDesktop) {
      setScale(1);
      return;
    }

    const host = hostRef.current;
    if (!host || !option.width || !option.height) return;

    const updateScale = () => {
      setScale(calculateLayoutScale({
        hostWidth: host.clientWidth,
        hostHeight: host.clientHeight,
        canvasWidth: option.width!,
        canvasHeight: option.height!,
        rotateQuarterTurn,
        edgeToEdge,
      }));
    };

    updateScale();
    const observer = new ResizeObserver(updateScale);
    observer.observe(host);
    return () => observer.disconnect();
  }, [edgeToEdge, isDesktop, option.height, option.width, rotateQuarterTurn]);

  const transformed = !isDesktop || rotateQuarterTurn;
  const safeAreaStyle = {
    "--layout-safe-top": rotateQuarterTurn
      ? "env(safe-area-inset-right)"
      : "env(safe-area-inset-top)",
    "--layout-safe-right": rotateQuarterTurn
      ? "env(safe-area-inset-bottom)"
      : "env(safe-area-inset-right)",
    "--layout-safe-bottom": rotateQuarterTurn
      ? "env(safe-area-inset-left)"
      : "env(safe-area-inset-bottom)",
    "--layout-safe-left": rotateQuarterTurn
      ? "env(safe-area-inset-top)"
      : "env(safe-area-inset-left)",
  } as React.CSSProperties;

  return (
    <div ref={hostRef} className="relative h-[100dvh] w-full overflow-hidden bg-black">
      <div
        data-layout-preview={mode}
        data-layout-rotated={rotateQuarterTurn ? "true" : "false"}
        className={`@container overflow-hidden bg-gray-950 ${
          !transformed
            ? "h-full w-full"
            : `absolute left-1/2 top-1/2 ${edgeToEdge ? "" : "rounded-2xl ring-1 ring-gray-700 shadow-2xl"}`
        }`}
        style={
          !transformed
            ? { ...safeAreaStyle, containerType: "size", transform: "translateZ(0)" }
            : {
                ...safeAreaStyle,
                containerType: "size",
                width: option.width,
                height: option.height,
                transform: `translate(-50%, -50%) ${rotateQuarterTurn ? "rotate(90deg) " : ""}scale(${scale})`,
                transformOrigin: "center",
              }
        }
      >
        <LayoutQuarterTurnProvider rotateQuarterTurn={rotateQuarterTurn}>
          <ContainerResponsiveProvider>{children}</ContainerResponsiveProvider>
        </LayoutQuarterTurnProvider>
      </div>
    </div>
  );
}
