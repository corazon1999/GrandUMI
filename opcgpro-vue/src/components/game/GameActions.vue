<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useIsDefender } from "@/composables/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";

const currentTurn = useStore(useGameStore, (s) => s.currentTurn);
const phase = useStore(useGameStore, (s) => s.phase);
const isPending = useStore(useGameStore, (s) => s.isPending);
const selectedHandIndex = useStore(useGameStore, (s) => s.selectedHandIndex);
const selectedFieldId = useStore(useGameStore, (s) => s.selectedFieldId);
const turnCount = useStore(useGameStore, (s) => s.turnCount);
const battle = useStore(useGameStore, (s) => s.battle);
const my = useStore(useGameStore, (s) => s.my);
const isDefender = useIsDefender();
const isSelectingTarget = useStore(useBattleStore, (s) => s.isSelectingTarget);

const attackerTapped = computed(() => {
  if (!my.value || selectedFieldId.value === null) return null;
  if (my.value.leaderId === selectedFieldId.value) return my.value.leaderTapped;
  return my.value.fieldCards.find((c) => c.id === selectedFieldId.value)?.isTapped ?? null;
});

const canAttack = computed(() => currentTurn.value && turnCount.value > 1 && !battle.value && !isSelectingTarget.value && attackerTapped.value === false);
const canPlay = computed(() => currentTurn.value && selectedHandIndex.value !== null);
const canPassCounter = computed(() => isDefender.value && phase.value === "Counter");

const selectedNumber = computed(() => {
  if (!my.value || selectedFieldId.value === null) return null;
  if (selectedFieldId.value === my.value.leaderId) return my.value.leaderNumber;
  if (selectedFieldId.value === my.value.stageId) return my.value.stageNumber;
  return my.value.fieldCards.find((c) => c.id === selectedFieldId.value)?.number ?? null;
});
const selectedHasActivated = computed(() => selectedNumber.value ? getCard(selectedNumber.value)?.effectTags?.includes("ActivatedMain") ?? false : false);
const selectedActivatedUsed = computed(() => {
  if (!my.value || selectedFieldId.value === null) return false;
  if (selectedFieldId.value === my.value.leaderId) return my.value.leaderActivatedUsedThisTurn;
  if (selectedFieldId.value === my.value.stageId) return my.value.stageActivatedUsedThisTurn;
  return my.value.fieldCards.find((c) => c.id === selectedFieldId.value)?.activatedUsedThisTurn ?? false;
});
const canActivate = computed(() => currentTurn.value && phase.value === "Main" && !battle.value && !isSelectingTarget.value && selectedFieldId.value !== null && selectedHasActivated.value && !selectedActivatedUsed.value);
const hasAny = computed(() => canAttack.value || isSelectingTarget.value || canPlay.value || canActivate.value || canPassCounter.value || currentTurn.value);

const btn = "w-full rounded-md px-3 py-2 text-sm font-bold text-white shadow transition-colors disabled:cursor-not-allowed disabled:bg-gray-600";

function startAttack() { if (selectedFieldId.value) useBattleStore.getState().startAttack(selectedFieldId.value); }
function cancelAttack() { useBattleStore.getState().cancelAttack(); }
function playCard() { if (selectedHandIndex.value !== null) GameRequest.playCard(selectedHandIndex.value); }
function activateEffect() { if (selectedFieldId.value) { GameRequest.useEffect(selectedFieldId.value, "main"); useGameStore.getState().setSelectedField(null); } }
function endTurn() { useBattleStore.getState().endTurn(); }
function passCounter() { GameRequest.passCounter(); }
</script>

<template>
  <div class="flex flex-col gap-2">
    <button v-if="canAttack" :disabled="isPending" :class="`${btn} bg-red-600 hover:bg-red-500`" @click="startAttack">攻击</button>
    <button v-if="isSelectingTarget" :disabled="isPending" :class="`${btn} bg-slate-700 hover:bg-slate-600`" @click="cancelAttack">取消攻击</button>
    <button v-if="canPlay" :disabled="isPending" :class="`${btn} bg-blue-500 hover:bg-blue-400`" @click="playCard">出牌</button>
    <button v-if="canActivate" :disabled="isPending" :class="`${btn} bg-purple-600 hover:bg-purple-500`" @click="activateEffect">启动效果</button>
    <button v-if="canPassCounter" :disabled="isPending" :class="`${btn} bg-amber-600 hover:bg-amber-500`" @click="passCounter">结束反击</button>
    <button v-if="currentTurn" :disabled="isPending" :class="`${btn} bg-orange-500 hover:bg-orange-400`" @click="endTurn">结束回合</button>
    <p v-if="!hasAny" class="py-1 text-center text-xs text-slate-500">等待对手…</p>
  </div>
</template>
