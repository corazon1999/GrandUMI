<script setup lang="ts">
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";

/** DON 卡堆（毛毡牌桌：抽象 .bf-box「DON + 数量」，源自 battle.jsx DonCluster 卡堆格） */
const props = defineProps<{ side: "my" | "opponent" }>();

const count = useStore(useGameStore, (s) =>
  (props.side === "my" ? s.my?.donDeckCount : s.opponent?.donDeckCount) ?? 0,
);
</script>

<template>
  <div class="bf-well">
    <span class="kicker">卡堆</span>
    <div class="bf-box bf-don-box">
      <span class="bf-don-box__t">DON</span>
      <span class="bf-don-box__n">{{ count }}</span>
    </div>
  </div>
</template>

<style scoped>
.bf-don-box {
  width: 60px;
  height: 90px;
}
.bf-don-box__t {
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 11px;
  letter-spacing: 0.04em;
  color: var(--ink-dim);
}
.bf-don-box__n {
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 22px;
  color: var(--ink);
  margin-top: 2px;
}
</style>
