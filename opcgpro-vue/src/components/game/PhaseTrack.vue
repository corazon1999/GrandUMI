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
  <div class="flex shrink-0 items-center justify-center gap-2 py-0.5">
    <span :class="['shrink-0 rounded-md px-2.5 py-1 text-[11px] font-black', currentTurn ? 'bg-sky-500/20 text-sky-200 ring-1 ring-sky-400/40' : 'bg-red-500/20 text-red-200 ring-1 ring-red-400/40']">
      {{ currentTurn ? "我的回合" : "对手回合" }}{{ inBattle ? " · 战斗中" : "" }}
    </span>
    <div class="flex items-center gap-1.5">
      <div v-for="p in flow" :key="p"
        :class="['rounded-md px-2.5 py-1 text-[11px] font-black transition-colors', p === phase ? (currentTurn ? 'bg-sky-500 text-white shadow shadow-sky-500/40' : 'bg-red-500 text-white shadow shadow-red-500/40') : 'border border-white/10 bg-slate-800/60 text-slate-400']">
        {{ (PHASE_LABELS as Record<string, string>)[p] ?? p }}
      </div>
    </div>
  </div>
</template>
