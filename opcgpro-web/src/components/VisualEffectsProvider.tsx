"use client";

import { useInsertionEffect, type ReactNode } from "react";
import { MotionConfig, MotionGlobalConfig } from "framer-motion";
import { useReducedVisualEffects } from "@/hooks/useVisualEffects";

/** 在布局动效启动前同步总开关，覆盖弹窗、门户和各页面中的运动组件。 */
export default function VisualEffectsProvider({ children }: { children: ReactNode }) {
  const reduced = useReducedVisualEffects();
  useInsertionEffect(() => {
    MotionGlobalConfig.skipAnimations = reduced;
    document.documentElement.dataset.visualEffects = reduced ? "off" : "on";
    return () => {
      MotionGlobalConfig.skipAnimations = false;
      document.documentElement.dataset.visualEffects = "off";
    };
  }, [reduced]);

  return <MotionConfig reducedMotion={reduced ? "always" : "never"}>{children}</MotionConfig>;
}
