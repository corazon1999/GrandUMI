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
  <div class="bf-well">
    <span class="kicker">{{ side === "my" ? "生命" : "对手生命" }}</span>
    <div :class="['relative', pile]">
      <template v-if="count > 0">
        <div v-for="(_, i) in visibleCards" :key="i" class="absolute inset-0"
          :style="{ transform: `translate(${i * 4}px, ${i * -2}px)`, zIndex: faceUp[i]?.faceUp && faceUp[i]?.number ? 10 + i : i }">
          <CardItem v-if="faceUp[i]?.faceUp && faceUp[i]?.number" :card="getCard(faceUp[i].number!) ?? null" :size="cardSize" :hide-counter="true" :hide-power="true" :hide-cost="true" />
          <div v-else class="bf-life__back" />
        </div>
      </template>
      <div v-else class="bf-life__empty" />
      <span class="bf-life__count">{{ count }}</span>
    </div>
  </div>
</template>

<style scoped>
.bf-life__back {
  position: relative;
  height: 100%;
  width: 100%;
  border-radius: 8px;
  background: linear-gradient(155deg, color-mix(in srgb, var(--accent) 82%, #000), var(--bg0));
  border: 2px solid var(--accent);
  box-shadow:
    0 2px 0 color-mix(in srgb, var(--accent) 45%, #000),
    0 8px 16px -8px rgba(0, 0, 0, 0.7),
    inset 0 1px 0 rgba(255, 255, 255, 0.18);
}
.bf-life__empty {
  height: 100%;
  width: 100%;
  border-radius: 8px;
  border: 1.5px dashed var(--line-strong);
  background: rgba(0, 0, 0, 0.18);
}
.bf-life__count {
  position: absolute;
  top: -10px;
  right: -10px;
  z-index: 30;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  background: radial-gradient(circle at 38% 30%, var(--primary-bright), var(--primary));
  color: var(--on-primary);
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 15px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow:
    0 2px 6px rgba(0, 0, 0, 0.6),
    0 0 14px var(--primary-glow),
    inset 0 1px 0 rgba(255, 255, 255, 0.5);
}
</style>
