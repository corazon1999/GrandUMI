<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useResponsive } from "@/composables/useResponsive";
import CardItem from "@/components/ui/CardItem.vue";
import { getCard } from "@/data/CardLoader";
import { GameRequest } from "@/net/GameRequest";

const props = defineProps<{ side: "my" | "opponent" }>();
const emit = defineEmits<{ (e: "hover-card", card: ReturnType<typeof getCard> | null): void }>();

const slotSizes = { sm: "w-[4.5rem] h-[6.3rem]", md: "w-[6rem] h-[8.4rem]", lg: "w-[8rem] h-[11.2rem]" } as const;

const player = useStore(useGameStore, (s) => (props.side === "my" ? s.my : s.opponent));
const isPending = useStore(useGameStore, (s) => s.isPending);
const selectedFieldId = useStore(useGameStore, (s) => s.selectedFieldId);
const selectedDonIndex = useStore(useGameStore, (s) => s.selectedDonIndex);
const battle = useStore(useGameStore, (s) => s.battle);
const currentTurn = useStore(useGameStore, (s) => s.currentTurn);
const isSelectingTarget = useStore(useBattleStore, (s) => s.isSelectingTarget);
const { cardSize } = useResponsive();

const dimensions = computed(() => slotSizes[cardSize.value]);
const leader = computed(() => (player.value ? getCard(player.value.leaderNumber) : null));

const attackerSide = computed(() => currentTurn.value ? "my" : "opponent");
const defenderSide = computed(() => currentTurn.value ? "opponent" : "my");
const isAttacker = computed(() => !!battle.value && props.side === attackerSide.value && player.value?.leaderId === battle.value.attackerCardId);
const isBattleTarget = computed(() => !!battle.value && props.side === defenderSide.value && battle.value.targetIsLeader);
const isTargetable = computed(() => isSelectingTarget.value && props.side === "opponent" && !isPending.value);

function handleClick() {
  if (isPending.value) return;
  if (isTargetable.value) { useBattleStore.getState().confirmAttackTarget({ isLeader: true }); return; }
  if (selectedDonIndex.value !== null && props.side === "my") { GameRequest.attachDon("leader", selectedDonIndex.value || 1); useGameStore.getState().setSelectedDon(null); return; }
  if (props.side === "my" && player.value) { useGameStore.getState().setSelectedField(selectedFieldId.value === player.value.leaderId ? null : player.value.leaderId); }
}
</script>

<template>
  <div v-if="!player || !leader" :class="[dimensions, 'relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30']">
    <span class="text-xs font-black text-slate-600">LEADER</span>
  </div>
  <div v-else :class="[dimensions, 'relative']" @mouseenter="emit('hover-card', leader)" @mouseleave="emit('hover-card', null)">
    <div v-if="isAttacker" class="pointer-events-none absolute -inset-1 z-20 animate-pulse rounded-lg shadow-lg shadow-red-500/50 ring-4 ring-red-500" />
    <div v-if="isBattleTarget" class="pointer-events-none absolute -inset-1 z-20 animate-pulse rounded-lg shadow-lg shadow-amber-400/50 ring-4 ring-amber-400" />
    <span v-if="isAttacker" class="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-red-600 px-1.5 text-[10px] font-black text-white shadow">攻击</span>
    <span v-if="isBattleTarget" class="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-amber-500 px-1.5 text-[10px] font-black text-black shadow">目标</span>
    <CardItem :card="leader" :size="cardSize" :is-selected="(side === 'my' && selectedFieldId === player.leaderId) || isTargetable" :is-tapped="player.leaderTapped" :attached-don-count="player.leaderAttachedDon" :power-buff="player.leaderPower - (leader.power ?? 0) - player.leaderAttachedDon * 1000" hide-cost hide-counter :lift-on-select="false" :attack-state="side === 'my' && currentTurn && player.leaderCanAttack ? 'can' : 'none'" @click="handleClick" />
    <div v-if="isTargetable" class="absolute -right-2 -top-2 h-5 w-5 animate-pulse rounded-full bg-red-500 shadow-lg shadow-red-500/50" />
    <div v-if="selectedDonIndex !== null && side === 'my' && !isPending" class="pointer-events-none absolute -left-2 -top-2 flex h-6 min-w-6 animate-pulse items-center justify-center rounded-full bg-yellow-300 px-1 shadow-lg shadow-yellow-300/50">
      <span class="text-[10px] font-black text-black">+{{ selectedDonIndex }}</span>
    </div>
  </div>
</template>
