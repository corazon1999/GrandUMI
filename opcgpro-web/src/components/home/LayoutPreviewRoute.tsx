"use client";

import { useEffect, useState, type ReactNode } from "react";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import { useLayoutSettings } from "./LayoutSettingsProvider";
import { resolveGameLayout } from "@/lib/gameLayout";

export default function LayoutPreviewRoute({ children }: { children: ReactNode }) {
  const { mode, setGameOverlayHost } = useLayoutSettings();
  const [isPhonePortrait, setIsPhonePortrait] = useState(false);

  useEffect(() => {
    const query = window.matchMedia(
      "(orientation: portrait) and (max-width: 767px), (orientation: portrait) and (max-width: 1024px) and (pointer: coarse)",
    );
    const update = () => setIsPhonePortrait(query.matches);

    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);

  const layout = resolveGameLayout(mode, isPhonePortrait);

  return (
    <LayoutPreviewFrame
      mode={layout.mode}
      rotateQuarterTurn={layout.rotateQuarterTurn}
      edgeToEdge={layout.edgeToEdge}
    >
      <div className="layout-preview-route relative h-full w-full overflow-hidden">
        {children}
        <div
          ref={setGameOverlayHost}
          className="pointer-events-none absolute inset-0 z-[10000]"
        />
      </div>
    </LayoutPreviewFrame>
  );
}
