<script setup lang="ts">
import { computed } from "vue";
import { getRarityRing } from "@/composables/useRarityColor";

/**
 * RarityRing — 稀有度小标识。
 * 用法：<RarityRing rarity="SR" /> 显示带描边的稀有度文字
 */
const props = defineProps<{ rarity: string; size?: "xs" | "sm" | "md" }>();
const ring = computed(() => getRarityRing(props.rarity));
const sizeClass = computed(() => {
  switch (props.size) {
    case "xs": return "text-[10px] px-1 py-px";
    case "sm": return "text-xs px-1.5 py-0.5";
    case "md": return "text-sm px-2 py-0.5";
  }
});
</script>

<template>
  <span
    :class="[
      'inline-block rounded font-bold tracking-wider uppercase',
      sizeClass,
    ]"
    :style="{
      color: ring.border,
      border: `1px solid ${ring.border}`,
      background: 'rgba(0,0,0,0.4)',
      textShadow: `0 0 4px ${ring.glow}`,
    }"
    :title="ring.label"
  >
    {{ rarity }}
  </span>
</template>
