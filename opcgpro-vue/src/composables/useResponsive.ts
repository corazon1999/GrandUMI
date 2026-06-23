import { ref, computed, inject, onMounted, onUnmounted, type InjectionKey, type Ref } from "vue";

export const CARD_SIZE_OVERRIDE: InjectionKey<Ref<"sm" | "md" | "lg">> = Symbol("cardSizeOverride");

export function useResponsive() {
  const override = inject(CARD_SIZE_OVERRIDE, null);
  const size = ref<"sm" | "md" | "lg">("md");

  function update() {
    if (override) return;
    const w = window.innerWidth;
    const h = window.innerHeight;
    if (w < 1100 || h < 780) size.value = "sm";
    else if (w < 1536 || h < 940) size.value = "md";
    else size.value = "lg";
  }

  onMounted(() => {
    update();
    if (!override) window.addEventListener("resize", update);
  });
  onUnmounted(() => {
    if (!override) window.removeEventListener("resize", update);
  });

  return {
    size,
    cardSize: computed(() => override?.value ?? size.value),
    isMobile: computed(() => (override?.value ?? size.value) === "sm"),
  };
}
