import type { ReactNode } from "react";
import LayoutPreviewRoute from "@/components/home/LayoutPreviewRoute";

export default function GameLayout({ children }: { children: ReactNode }) {
  return <LayoutPreviewRoute>{children}</LayoutPreviewRoute>;
}
