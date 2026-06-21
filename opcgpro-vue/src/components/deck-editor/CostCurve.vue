<script setup lang="ts">
import { computed } from "vue";
import type { DeckEntry } from "@/store/deckStore";

const props = defineProps<{ entries: DeckEntry[] }>();

/* HS-现代：低费=冷绿渐变（安全），高费=暖金/红（昂贵） */
const BAR_COLORS = [
  "bg-gradient-to-t from-emerald-700 to-emerald-400",
  "bg-gradient-to-t from-emerald-600 to-emerald-300",
  "bg-gradient-to-t from-teal-600 to-teal-300",
  "bg-gradient-to-t from-cyan-600 to-cyan-300",
  "bg-gradient-to-t from-sky-600 to-sky-300",
  "bg-gradient-to-t from-indigo-600 to-indigo-300",
  "bg-gradient-to-t from-violet-600 to-violet-300",
  "bg-gradient-to-t from-orange-600 to-orange-300",
  "bg-gradient-to-t from-red-600 to-red-300",
  "bg-gradient-to-t from-[#8a6d2e] to-[#d4b876]",
];
function barColor(cost: number) {
  return BAR_COLORS[Math.min(cost, BAR_COLORS.length - 1)] ?? BAR_COLORS[BAR_COLORS.length - 1];
}

const CHART_H = 64;
const LABEL_H = 16;
const NUM_H = 14;

const costMap = computed(() => {
  const m: Record<number, number> = {};
  props.entries.forEach((e) => { m[e.card.cost] = (m[e.card.cost] ?? 0) + e.count; });
  return m;
});
const costs = computed(() => Object.keys(costMap.value).map(Number).sort((a, b) => a - b));
const hasCards = computed(() => costs.value.length > 0);
const maxCost = computed(() => (hasCards.value ? Math.max(10, costs.value[costs.value.length - 1]) : 10));
const maxCount = computed(() => Math.max(1, ...Object.values(costMap.value)));
const allCosts = computed(() => Array.from({ length: maxCost.value + 1 }, (_, i) => i));

function bar(i: number) {
  const count = costMap.value[i] ?? 0;
  const ratio = maxCount.value > 0 ? count / maxCount.value : 0;
  const barMax = CHART_H - LABEL_H - (count > 0 ? NUM_H : 0);
  const barH = count > 0 ? Math.max(4, Math.round(ratio * barMax)) : 0;
  const padTop = CHART_H - LABEL_H - barH - (count > 0 ? NUM_H : 0);
  return { count, barH, padTop };
}
</script>

<template>
  <div v-if="!hasCards" class="flex flex-col gap-1">
    <span class="gde-kicker text-[11px]">费用曲线</span>
    <div class="flex h-12 items-center justify-center rounded-lg border border-[var(--line)] bg-[var(--surface)]/50">
      <span class="text-xs text-[var(--ink-faint)]">暂无卡牌</span>
    </div>
  </div>
  <div v-else class="flex flex-col gap-2">
    <span class="gde-kicker text-[11px]">费用曲线</span>
    <div class="flex h-16 gap-px rounded-lg border border-[var(--line)] bg-[var(--surface)]/40 px-1 shadow-[inset_0_1px_0_var(--primary-glow)/10]">
      <div
        v-for="i in allCosts"
        :key="i"
        class="flex flex-1 flex-col items-center"
        :style="{ paddingTop: bar(i).padTop + 'px' }"
      >
        <span :class="['mb-0.5 text-[11px] font-bold leading-none', bar(i).count > 0 ? 'text-white/90' : 'text-transparent']">
          {{ bar(i).count > 0 ? bar(i).count : " " }}
        </span>
        <div
          :class="['w-full rounded-t transition-all duration-200', bar(i).count > 0 ? barColor(i) : 'bg-transparent']"
          :style="{ height: bar(i).barH + 'px', minHeight: bar(i).count > 0 ? '4px' : '0' }"
        />
        <span :class="['mt-0.5 text-[11px] leading-none', bar(i).count > 0 ? 'text-gray-300' : 'text-gray-700']">{{ i }}</span>
      </div>
    </div>
  </div>
</template>
