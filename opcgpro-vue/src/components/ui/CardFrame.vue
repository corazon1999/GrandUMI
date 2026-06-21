<script setup lang="ts">
import { computed } from "vue";
import { getRarityRing } from "@/composables/useRarityColor";

/**
 * CardFrame — 卡牌金属外框包装。
 * - 外金边 1.5px (rarity 色)
 * - 内描边 0.5px rgba(0,0,0,.5) 模拟厚度
 * - box-shadow 双层（常态 / hover 通过 :hover 切换）
 * - 可选 rarity ring（hover 抬升时 glow 增强）
 */
const props = withDefaults(
  defineProps<{
    rarity?: string;
    interactive?: boolean; // 是否可点击/悬浮
    size?: "sm" | "md" | "lg";
  }>(),
  { rarity: "C", interactive: true, size: "md" },
);

const ring = computed(() => getRarityRing(props.rarity));

const SIZE_CLASS = {
  sm: "rounded",
  md: "rounded-lg",
  lg: "rounded-xl",
} as const;
</script>

<template>
  <div
    :class="[
      'card-frame group/card relative overflow-hidden bg-black/40',
      SIZE_CLASS[size],
      interactive ? 'cursor-pointer transition-transform duration-200' : '',
    ]"
    :style="{
      border: `1.5px solid ${ring.border}`,
      boxShadow: `0 4px 12px rgba(0,0,0,0.5), inset 0 0 0 1px rgba(0,0,0,0.5)`,
    }"
  >
    <slot />

    <!-- rarity ring（hover 抬升时的额外辉光） -->
    <div
      v-if="interactive"
      class="pointer-events-none absolute inset-0 rounded-[inherit] opacity-0 transition-opacity duration-200 group-hover/card:opacity-100"
      :style="{ boxShadow: `0 0 16px 2px ${ring.glow}` }"
    />
  </div>
</template>

<style scoped>
.card-frame:hover {
  border-color: v-bind("ring.border");
  filter: brightness(1.05);
}
</style>
