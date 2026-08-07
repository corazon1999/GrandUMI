import type { ReactNode } from "react";
import LayoutPreviewRoute from "@/components/home/LayoutPreviewRoute";

export default function ReplayLayout({ children }: { children: ReactNode }) {
  return <LayoutPreviewRoute>{children}</LayoutPreviewRoute>;
}
