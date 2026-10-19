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

/** 游戏总开关和系统减少动态效果共同约束持续动画，支持系统设置实时变化。 */
export function useReducedVisualEffects(): boolean {
  const enabled = useSettingsStore((state) => state.visualEffectsEnabled);
  const reducedMotion = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  return !enabled || reducedMotion;
}
