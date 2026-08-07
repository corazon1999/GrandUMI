"use client";

import type { ReactNode } from "react";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import { useLayoutSettings } from "./LayoutSettingsProvider";

export default function LayoutPreviewRoute({ children }: { children: ReactNode }) {
  const { mode } = useLayoutSettings();

  return (
    <LayoutPreviewFrame mode={mode}>
      <div className="layout-preview-route h-full w-full overflow-hidden">{children}</div>
    </LayoutPreviewFrame>
  );
}
