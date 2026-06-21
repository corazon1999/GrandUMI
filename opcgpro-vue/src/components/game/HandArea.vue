<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/composables/useResponsive";
import { useIsDefender } from "@/composables/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import CardItem from "@/components/ui/CardItem.vue";
import { getCard } from "@/data/CardLoader";

const props = withDefaults(defineProps<{ side: "my" | "opponent"; hidden?: boolean }>(), { hidden: false });
const emit = defineEmits<{ (e: "hover-card", card: ReturnType<typeof getCard> | null): void }>();

const player = useStore(useGameStore, (s) => (props.side === "my" ? s.my : s.opponent));
const currentTurn = useStore(useGameStore, (s) => s.currentTurn);
const phase = useStore(useGameStore, (s) => s.phase);
const isPending = useStore(useGameStore, (s) => s.isPending);
const selectedHandIndex = useStore(useGameStore, (s) => s.selectedHandIndex);
const { cardSize } = useResponsive();
const isDefender = useIsDefender();

const wrapRef = ref<HTMLDivElement | null>(null);
const wrapW = ref(0);
let ro: ResizeObserver | null = null;
onMounted(() => {
  if (wrapRef.value) { wrapW.value = wrapRef.value.clientWidth; ro = new ResizeObserver(() => { wrapW.value = wrapRef.value?.clientWidth ?? 0; }); ro.observe(wrapRef.value); }
});
onUnmounted(() => ro?.disconnect());

const cards = computed(() => {
  if (!player.value) return [];
  return props.side === "my" ? player.value.handCardNumbers.map((n) => getCard(n) ?? null) : Array.from({ length: player.value.handCount }, () => null);
});

const stableKeys = computed(() => {
  const seen: Record<string, number> = {};
  return cards.value.map((card, i) => {
    const base = props.side === "my" ? (player.value?.handCardNumbers[i] ?? "null") : "back";
    const occ = (seen[base] = (seen[base] ?? 0) + 1);
    return `${base}#${occ}`;
  });
});

const isCounterStep = computed(() => !props.hidden && props.side === "my" && phase.value === "Counter" && isDefender.value);
const myActiveDon = computed(() => props.side === "my" && player.value ? player.value.costActive : 0);

function isCounterEventPlayable(card: ReturnType<typeof getCard> | null, i: number): boolean {
  if (!isCounterStep.value || !card) return false;
  if ((card.counter ?? 0) > 0) return false;
  if (!card.effectTags?.includes("EventCounter")) return false;
  const effectiveCost = player.value?.handCardCosts?.[i] ?? card.cost;
  return effectiveCost <= myActiveDon.value;
}

const marginLeft = computed(() => {
  const n = cards.value.length;
  const cardW = cardSize.value === "sm" ? 72 : cardSize.value === "md" ? 96 : 128;
  const GAP = 8;
  const PAD = 24;
  const minStep = Math.round(cardW * 0.3);
  const avail = Math.max(0, wrapW.value - PAD);
  let step = cardW + GAP;
  if (n > 1 && avail > 0) { const fitStep = (avail - cardW) / (n - 1); step = Math.max(minStep, Math.min(cardW + GAP, fitStep)); }
  return step - cardW;
});

function handleClick(i: number) {
  if (props.hidden || isPending.value) return;
  if (isCounterStep.value) { const c = cards.value[i]; if (c && (c.counter ?? 0) > 0) GameRequest.playCounterFromHand(i); else if (isCounterEventPlayable(c, i)) GameRequest.playCounterEvent(i); return; }
  if (props.side !== "my" || !currentTurn.value) return;
  useGameStore.getState().setSelectedHand(selectedHandIndex.value === i ? null : i);
}
</script>

<template>
  <div v-if="!player" class="min-h-20" />
  <div v-else ref="wrapRef" class="-my-5 flex min-h-24 w-full min-w-0 items-end justify-center overflow-x-auto px-3 py-6 lg:min-h-32">
    <TransitionGroup :name="side === 'my' ? 'hand-my' : 'hand-opp'">
      <div v-for="(card, i) in cards" :key="stableKeys[i]"
        :class="['relative hover:z-20', (isCounterStep && ((card?.counter ?? 0) > 0 || isCounterEventPlayable(card, i))) ? 'rounded-md ring-2 ring-amber-400 animate-pulse' : '']"
        :style="{ marginLeft: i === 0 ? '0' : `${marginLeft}px` }"
        @mouseenter="emit('hover-card', card)" @mouseleave="emit('hover-card', null)">
        <CardItem :card="card" :is-selected="!hidden && selectedHandIndex === i" :face-down="hidden || card === null" :size="cardSize" hide-power :cost-buff="side === 'my' && card && player?.handCardCosts?.[i] != null ? player.handCardCosts[i] - card.cost : 0" @click="handleClick(i)" />
      </div>
    </TransitionGroup>
    <span v-if="cards.length === 0" class="text-xs text-gray-700">{{ hidden ? "对手手牌" : "手牌为空" }}</span>
  </div>
</template>

<style scoped>
.hand-my-enter-active,
.hand-opp-enter-active,
.hand-my-leave-active,
.hand-opp-leave-active,
.hand-my-move-active,
.hand-opp-move-active { transition: all 0.25s ease; }
.hand-my-enter-from { transform: translateY(36px); opacity: 0; }
.hand-my-leave-to   { transform: translateY(-24px); opacity: 0; }
.hand-opp-enter-from { transform: translateY(-24px); opacity: 0; }
.hand-opp-leave-to  { transform: translateY(24px); opacity: 0; }
</style>
