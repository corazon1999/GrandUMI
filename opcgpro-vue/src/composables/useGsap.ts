import gsap from "gsap";
import { onUnmounted } from "vue";

/**
 * useGsap — GSAP 单例 + reduced-motion 守卫。
 * 返回的 gsap 实例在 reduced-motion 时所有 tween 退化为 0 时长。
 */
export function useGsap() {
  const reduced = typeof window !== "undefined"
    && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

  if (reduced) {
    // 退化：所有 tween 立即完成
    const fastGsap = {
      ...gsap,
      to: (target: gsap.TweenTarget, vars: gsap.TweenVars) => {
        return gsap.to(target, { ...vars, duration: 0 });
      },
      fromTo: (target: gsap.TweenTarget, fromVars: gsap.TweenVars, toVars: gsap.TweenVars) => {
        return gsap.fromTo(target, fromVars, { ...toVars, duration: 0 });
      },
    };
    return fastGsap;
  }

  return gsap;
}

/**
 * useGsapTimeline — 创建并自动清理的 timeline。
 */
export function useGsapTimeline(vars?: gsap.TimelineVars) {
  const anim = useGsap();
  const tl = anim.timeline(vars);
  onUnmounted(() => tl.kill());
  return tl;
}
