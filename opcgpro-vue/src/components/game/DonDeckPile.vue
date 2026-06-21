<script setup lang="ts">
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/composables/useResponsive";
import DonCardItem from "./DonCardItem.vue";

const props = defineProps<{ side: "my" | "opponent" }>();

const count = useStore(useGameStore, (s) =>
  (props.side === "my" ? s.my?.donDeckCount : s.opponent?.donDeckCount) ?? 0,
);
const { cardSize } = useResponsive();
const pileSizes = { sm: "h-[6.3rem] w-[4.5rem]", md: "h-[8.4rem] w-[6rem]", lg: "h-[11.2rem] w-[8rem]" } as const;
</script>

<template>
  <div :class="[pileSizes[cardSize], 'relative shrink-0']">
    <span class="absolute left-2 top-2 z-20 text-xs font-semibold text-slate-200 drop-shadow">DON 卡堆</span>
    <DonCardItem v-if="count > 0" state="deck" :size="cardSize" disabled />
    <div v-else class="h-full w-full rounded-md border-2 border-dashed border-slate-500/60 bg-slate-950/35" />
    <div class="absolute inset-x-0 bottom-1 z-30 flex justify-center">
      <span class="rounded bg-slate-950/90 px-2 py-0.5 text-xs font-black text-white shadow">{{ count }}</span>
    </div>
  </div>
</template>
