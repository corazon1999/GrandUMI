import { onMounted, type Ref } from "vue";
import { useGsap } from "./useGsap";

/**
 * useCardEntrance — 卡牌入场动画：
 *   1) scale 0.5 → 1.1 → 1.0（弹性 back.out）
 *   2) 金光闪：box-shadow 0 0 24px #c8a04a 持续 600ms 后回到常态
 *
 * 用法：const cardRef = ref<HTMLElement>(); useCardEntrance(cardRef);
 */
export function useCardEntrance(target: Ref<HTMLElement | null>, opts?: { delay?: number }) {
  const gsap = useGsap();
  const delay = opts?.delay ?? 0;

  onMounted(() => {
    const el = target.value;
    if (!el) return;

    gsap.fromTo(
      el,
      { scale: 0.5, opacity: 0, rotate: -2 },
      {
        scale: 1,
        opacity: 1,
        rotate: 0,
        duration: 0.36,
        delay,
        ease: "back.out(1.7)",
        clearProps: "transform",
        onComplete: () => {
          // 金光闪
          gsap.fromTo(
            el,
            { boxShadow: "0 0 0 0 rgba(200,160,74,0), 0 0 0 1px #c8a04a" },
            {
              boxShadow: "0 0 24px 4px rgba(200,160,74,0.6), 0 0 0 1px #d4b876",
              duration: 0.18,
              yoyo: true,
              repeat: 1,
              ease: "power2.out",
            },
          );
        },
      },
    );
  });
}
