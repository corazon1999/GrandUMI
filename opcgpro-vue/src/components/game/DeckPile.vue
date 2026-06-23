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
  <div class="bf-well">
    <span class="kicker">牌库</span>
    <div :class="[pile, 'bf-deck']">
      <span class="bf-deck__txt">DECK</span>
      <span class="bf-deck__count">{{ count }}</span>
    </div>
  </div>
</template>

<style scoped>
.bf-deck {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  border: 2px solid var(--primary);
  background: linear-gradient(158deg, var(--primary-bright), var(--bg1));
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.4),
    0 4px 0 var(--primary-glow),
    0 12px 22px -8px rgba(0, 0, 0, 0.8);
}
.bf-deck__txt {
  font-family: var(--font-mono);
  font-size: 13px;
  letter-spacing: 0.08em;
  font-weight: 700;
  color: var(--on-primary);
}
.bf-deck__count {
  position: absolute;
  top: -10px;
  right: -10px;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: radial-gradient(circle at 38% 30%, var(--surface2), var(--bg1));
  border: 1.5px solid var(--primary);
  color: var(--ink);
  font-family: var(--font-mono);
  font-weight: 700;
  font-size: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 0 12px var(--primary-glow);
}
</style>
