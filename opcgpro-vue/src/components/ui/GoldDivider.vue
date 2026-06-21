<script setup lang="ts">
/**
 * GoldDivider — 金色装饰分隔条。
 * 中间 1px 实线 + 上下 1px 高光/暗影，HS 招牌金属质感。
 */
withDefaults(
  defineProps<{
    spacing?: "tight" | "normal" | "loose";
  }>(),
  { spacing: "normal" },
);

const SPACING = {
  tight: "my-1",
  normal: "my-2",
  loose: "my-3",
} as const;
</script>

<template>
  <div :class="['relative h-px w-full', SPACING[spacing]]" role="separator">
    <div class="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-[#c8a04a] to-transparent" />
    <div class="absolute inset-x-0 top-px h-px bg-gradient-to-r from-transparent via-[#8a6d2e]/40 to-transparent" />
  </div>
</template>
