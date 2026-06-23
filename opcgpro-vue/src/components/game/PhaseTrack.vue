<script setup lang="ts">
import { computed } from "vue";
import { PHASE_LABELS } from "@/data/gameLabels";

const props = defineProps<{ currentTurn: boolean; phase: string }>();

const TURN_FLOW = ["Reset", "Draw", "Don", "Main", "End"] as const;
const BATTLE_FLOW = ["Attack", "Block", "Counter", "Damage"] as const;
const BATTLE_PHASES: ReadonlySet<string> = new Set(BATTLE_FLOW);

const inBattle = computed(() => BATTLE_PHASES.has(props.phase));
const flow = computed(() => (inBattle.value ? BATTLE_FLOW : TURN_FLOW));
</script>

<template>
  <!-- 阶段条（饱和毛毡牌桌：浮于中央接缝，源自 battle.jsx .bf-phase） -->
  <div class="flex shrink-0 items-center justify-center">
    <div class="bf-phase">
      <span
        class="bf-phase__turn"
        :style="currentTurn ? {} : {
          color: 'var(--accent)',
          background: 'color-mix(in srgb, var(--accent) 14%, transparent)',
        }"
      >
        {{ currentTurn ? "我的回合" : "对手回合" }}{{ inBattle ? " · 战斗中" : "" }}
      </span>
      <span
        v-for="p in flow"
        :key="p"
        :class="['bf-phase__opt', { 'is-active': p === phase }]"
      >
        {{ (PHASE_LABELS as Record<string, string>)[p] ?? p }}
      </span>
    </div>
  </div>
</template>
