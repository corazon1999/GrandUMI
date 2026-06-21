import type { Ref } from "vue";
import { useGsap } from "./useGsap";

/**
 * useCardHover — 卡牌悬浮抬升：
 *   - y: 0 → -6, scale: 1 → 1.05
 *   - box-shadow: 常态 → 金光 hover shadow
 *   - 离开时回弹（弹性）
 *
 * 用法：useCardHover(cardRef);
 */
export function useCardHover(target: Ref<HTMLElement | null>) {
  const gsap = useGsap();
  const goldShadow = "0 8px 24px rgba(200,160,74,0.25), 0 0 0 1px #d4b876";
  const baseShadow = "0 4px 12px rgba(0,0,0,0.5), 0 0 0 1px #c8a04a";

  function onEnter() {
    const el = target.value;
    if (!el) return;
    gsap.to(el, {
      y: -6,
      scale: 1.05,
      boxShadow: goldShadow,
      duration: 0.18,
      ease: "power2.out",
      overwrite: "auto",
    });
  }

  function onLeave() {
    const el = target.value;
    if (!el) return;
    gsap.to(el, {
      y: 0,
      scale: 1,
      boxShadow: baseShadow,
      duration: 0.22,
      ease: "back.out(1.4)",
      overwrite: "auto",
    });
  }

  return { onEnter, onLeave };
}
