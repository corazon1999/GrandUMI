<script setup lang="ts">
/**
 * PanelChrome — HS 风格面板包边。
 * - 深底 stone-800
 * - 顶部 1px 金色高光线（inset 阴影）
 * - 底部 1px 暗线
 * - 可选 title 插槽（HS 招牌金色字 Cinzel）
 */
withDefaults(
  defineProps<{
    title?: string;
    bordered?: boolean;
    padding?: "none" | "sm" | "md";
  }>(),
  { bordered: true, padding: "md" },
);

const PAD = {
  none: "",
  sm: "p-2",
  md: "p-3",
} as const;
</script>

<template>
  <section
    :class="[
      'relative rounded-lg bg-stone-800/80',
      bordered
        ? 'border border-stone-700/80 shadow-[inset_0_1px_0_rgba(200,160,74,0.15),inset_0_-1px_0_rgba(0,0,0,0.5)]'
        : '',
    ]"
  >
    <header v-if="title || $slots.title" class="flex shrink-0 items-center justify-between border-b border-stone-700/60 px-3 py-2">
      <h3 v-if="title" class="font-hs-heading text-xs font-bold tracking-widest text-[#c8a04a] uppercase">
        {{ title }}
      </h3>
      <slot name="title" />
      <div v-if="$slots.actions" class="flex items-center gap-1">
        <slot name="actions" />
      </div>
    </header>

    <div :class="PAD[padding]">
      <slot />
    </div>
  </section>
</template>
