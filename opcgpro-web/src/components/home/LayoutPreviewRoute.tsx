"use client";

import { useEffect, useState, type ReactNode } from "react";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import { useLayoutSettings } from "./LayoutSettingsProvider";

export default function LayoutPreviewRoute({ children }: { children: ReactNode }) {
  const { mode } = useLayoutSettings();
  const [isNarrowPortrait, setIsNarrowPortrait] = useState(false);

  useEffect(() => {
    const query = window.matchMedia("(orientation: portrait) and (max-width: 767px)");
    const update = () => setIsNarrowPortrait(query.matches);

    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);

  // 实际设备保持竖屏时，牌桌临时使用手机横屏画布；不覆盖大厅保存的布局偏好。
  const effectiveMode = isNarrowPortrait ? "mobile-landscape" : mode;

  return (
    <LayoutPreviewFrame mode={effectiveMode}>
      <div className="layout-preview-route h-full w-full overflow-hidden">{children}</div>
    </LayoutPreviewFrame>
  );
}
