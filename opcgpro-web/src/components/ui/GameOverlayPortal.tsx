"use client";

import { createPortal } from "react-dom";
import { useEffect, useState } from "react";
import { useLayoutSettings } from "@/components/home/LayoutSettingsProvider";

export default function GameOverlayPortal({ children }: { children: React.ReactNode }) {
  const { gameOverlayHost } = useLayoutSettings();
  const [mounted, setMounted] = useState(false);

  useEffect(() => setMounted(true), []);

  if (!mounted) return null;

  return createPortal(children, gameOverlayHost ?? document.body);
}
