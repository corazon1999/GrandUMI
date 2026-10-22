"use client";

import { useSyncExternalStore } from "react";
import { useSettingsStore } from "@/store/settingsStore";

const QUERY = "(prefers-reduced-motion: reduce)";
function subscribe(onChange: () => void) {
  const query = window.matchMedia(QUERY);
  query.addEventListener("change", onChange);
  return () => query.removeEventListener("change", onChange);
}
const getSnapshot = () => window.matchMedia(QUERY).matches;
const getServerSnapshot = () => false;

export function usePrefersReducedMotion(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}

/** 默认遵循系统减少动态效果；玩家主动开启后优先使用本浏览器的选择。 */
export function useReducedVisualEffects(): boolean {
  const enabled = useSettingsStore((state) => state.visualEffectsEnabled);
  const override = useSettingsStore((state) => state.visualEffectsOverrideReducedMotion);
  const reducedMotion = usePrefersReducedMotion();
  return !enabled || (reducedMotion && !override);
}
