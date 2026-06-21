<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/composables/useResponsive";
import CardItem from "@/components/ui/CardItem.vue";
import { getCard } from "@/data/CardLoader";

const props = defineProps<{ side: "my" | "opponent" }>();

const pileSizes = { sm: "h-[6.3rem] w-[4.5rem]", md: "h-[8.4rem] w-[6rem]", lg: "h-[11.2rem] w-[8rem]" } as const;

const player = useStore(useGameStore, (s) => props.side === "my" ? s.my : s.opponent);
const { cardSize } = useResponsive();
const count = computed(() => player.value?.lifeCount ?? 0);
const faceUp = computed(() => player.value?.lifeFaceUp ?? []);
const pile = computed(() => pileSizes[cardSize.value]);
const visibleCards = computed(() => Math.min(Math.max(count.value, 1), 5));
</script>

<template>
  <div :class="['relative', pile]">
    <span class="absolute left-2 top-2 z-20 text-xs font-semibold text-slate-200 drop-shadow">
      {{ side === "my" ? "生命" : "对手生命" }}
    </span>
    <template v-if="count > 0">
      <div v-for="(_, i) in visibleCards" :key="i" class="absolute"
        :style="{ inset: '0', transform: `translate(${i * 4}px, ${i * 4}px)`, zIndex: faceUp[i]?.faceUp && faceUp[i]?.number ? 10 + i : i }">
        <CardItem v-if="faceUp[i]?.faceUp && faceUp[i]?.number" :card="getCard(faceUp[i].number!) ?? null" :size="cardSize" :hide-counter="true" :hide-power="true" :hide-cost="true" />
        <div v-else class="relative h-full w-full rounded-md border-2 border-red-200/35 bg-gradient-to-br from-red-800 via-rose-950 to-slate-950 shadow-xl shadow-black/35">
          <div class="absolute inset-2 rounded border border-red-100/15" />
        </div>
      </div>
    </template>
    <div v-else class="h-full w-full rounded-md border-2 border-dashed border-slate-500/60 bg-slate-950/45" />
    <div class="absolute -right-4 -top-3 z-30 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
      {{ count }}
    </div>
  </div>
</template>
