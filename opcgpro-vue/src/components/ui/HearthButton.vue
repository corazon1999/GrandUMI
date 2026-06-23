<script setup lang="ts">
import { computed } from "vue";
import { useGsap } from "@/composables/useGsap";
import { ref } from "vue";

/**
 * HearthButton — HS 风格厚重金边按钮。
 * variant:
 *   - primary  : 金底黑字 (主操作：保存、开始)
 *   - secondary: 深底金边 (次操作：读取、返回)
 *   - ghost    : 透明底金边文字 (筛选/清除)
 * size: sm | md | lg
 */
type Variant = "primary" | "secondary" | "ghost";
type Size = "sm" | "md" | "lg";

const props = withDefaults(
  defineProps<{
    variant?: Variant;
    size?: Size;
    disabled?: boolean;
    type?: "button" | "submit" | "reset";
    title?: string;
  }>(),
  { variant: "secondary", size: "md", disabled: false, type: "button" },
);

const emit = defineEmits<{ (e: "click", ev: MouseEvent): void }>();

const btnRef = ref<HTMLButtonElement | null>(null);
const gsap = useGsap();

const variantClass = computed(() => {
  switch (props.variant) {
    case "primary":
      return "bg-gradient-to-b from-[var(--primary-bright)] to-[var(--primary)] text-[var(--on-primary)] font-bold border border-[var(--primary)]";
    case "secondary":
      return "bg-[var(--surface)] text-[var(--ink-dim)] border border-[var(--line-strong)] hover:border-[var(--primary)] hover:text-[var(--ink)]";
    case "ghost":
      return "bg-transparent text-[var(--ink-dim)] border border-transparent hover:border-[var(--line)] hover:text-[var(--ink)]";
  }
});

const sizeClass = computed(() => {
  switch (props.size) {
    case "sm":
      return "px-2 py-0.5 text-xs";
    case "md":
      return "px-3 py-1.5 text-xs";
    case "lg":
      return "px-4 py-2.5 text-sm";
  }
});

function onClick(ev: MouseEvent) {
  if (props.disabled) return;
  if (btnRef.value) {
    gsap.fromTo(
      btnRef.value,
      { scale: 1 },
      { scale: 0.96, duration: 0.05, yoyo: true, repeat: 1, ease: "power2.inOut" },
    );
  }
  emit("click", ev);
}
</script>

<template>
  <button
    ref="btnRef"
    :type="type"
    :disabled="disabled"
    :title="title"
    :class="[
      'inline-flex items-center justify-center gap-1.5 rounded-md font-bold tracking-wide transition-all duration-150',
      'shadow-[inset_0_1px_0_rgba(255,255,255,0.08),inset_0_-1px_0_rgba(0,0,0,0.4),0_1px_2px_rgba(0,0,0,0.4)]',
      'hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.12),0_0_8px_var(--primary-glow)]',
      'active:translate-y-px',
      variantClass,
      sizeClass,
      disabled ? 'cursor-not-allowed opacity-40' : 'cursor-pointer',
    ]"
    @click="onClick"
  >
    <slot />
  </button>
</template>
