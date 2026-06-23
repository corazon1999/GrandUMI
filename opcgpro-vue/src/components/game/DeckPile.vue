<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/composables/useResponsive";

const props = defineProps<{ side: "my" | "opponent" }>();

const pileSizes = { sm: "h-[6.3rem] w-[4.5rem]", md: "h-[8.4rem] w-[6rem]", lg: "h-[11.2rem] w-[8rem]" } as const;

const count = useStore(useGameStore, (s) =>
  (props.side === "my" ? s.my?.deckCount : s.opponent?.deckCount) ?? 0,
);
const { cardSize } = useResponsive();
const pile = computed(() => pileSizes[cardSize.value]);
</script>

<template>
  <div class="flex flex-col items-center gap-2 rounded-md border border-sky-200/15 bg-black/30 px-2.5 py-2 shadow-lg shadow-black/25">
    <span class="text-xs font-semibold text-slate-300">牌库</span>
    <div :class="['relative', pile]">
      <div class="absolute inset-0 translate-x-2 translate-y-2 rounded-md border border-sky-300/20 bg-slate-950" />
      <div class="absolute inset-0 translate-x-1 translate-y-1 rounded-md border border-sky-300/30 bg-blue-950" />
      <div class="absolute inset-0 flex items-center justify-center rounded-md border-2 border-sky-300/45 bg-gradient-to-br from-sky-950 via-blue-950 to-slate-950 shadow-xl shadow-black/40">
        <span class="text-xs font-black text-sky-300">DECK</span>
      </div>
      <div class="absolute -right-3 -top-3 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
        {{ count }}
      </div>
    </div>
  </div>
</template>
