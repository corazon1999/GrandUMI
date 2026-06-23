<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useIsDefender } from "@/composables/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem.vue";

const phase = useStore(useGameStore, (s) => s.phase);
const battle = useStore(useGameStore, (s) => s.battle);
const my = useStore(useGameStore, (s) => s.my);
const opp = useStore(useGameStore, (s) => s.opponent);
const isPending = useStore(useGameStore, (s) => s.isPending);
const isDefender = useIsDefender();

const visible = computed(() =>
  !!battle.value && isDefender.value && phase.value === "Block" && !!my.value && !!opp.value,
);

const attackerName = computed(() => {
  if (!battle.value || !opp.value) return "???";
  if (battle.value.attackerCardId === opp.value.leaderId)
    return getCard(opp.value.leaderNumber)?.name ?? "领袖";
  const c = opp.value.fieldCards.find((f) => f.id === battle.value!.attackerCardId);
  return getCard(c?.number ?? "")?.name ?? "角色";
});

const attackerPower = computed(() => {
  const b = battle.value;
  if (!b || !opp.value) return 0;
  const base = b.attackerCardId === opp.value.leaderId
    ? opp.value.leaderPower
    : opp.value.fieldCards.find((f) => f.id === b.attackerCardId)?.powerCurrent ?? 0;
  return base + b.attackerBonus;
});

const targetName = computed(() => {
  const b = battle.value;
  if (!b || !my.value) return "???";
  if (b.targetIsLeader)
    return getCard(my.value.leaderNumber)?.name ?? "领袖";
  const c = my.value.fieldCards.find((f) => f.id === b.targetCardId);
  return getCard(c?.number ?? "")?.name ?? "角色";
});

const targetPower = computed(() => {
  const b = battle.value;
  if (!b || !my.value) return 0;
  const base = b.targetIsLeader
    ? my.value.leaderPower
    : my.value.fieldCards.find((f) => f.id === b.targetCardId)?.powerCurrent ?? 0;
  return base + b.defenderBonus;
});

const willLose = computed(() => attackerPower.value >= targetPower.value);

function hasBlockerKeyword(number: string, gained: string[]) {
  const card = getCard(number);
  return (
    gained.includes("阻挡者") ||
    (card?.abilities?.includes("阻挡者") ?? false) ||
    (card?.keyWords?.includes("阻挡者") ?? false)
  );
}

const blockers = computed(() => {
  if (!my.value) return [];
  return my.value.fieldCards.filter(
    (c) => !c.isTapped && hasBlockerKeyword(c.number, c.gainedKeywords),
  );
});

function declareBlocker(id: string) {
  GameRequest.declareBlocker(id);
}
</script>

<template>
  <Transition name="battle-defense">
    <div
      v-if="visible"
      class="fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-3 border-t border-sky-200/20 bg-slate-950/95 px-6 py-4 shadow-2xl shadow-black/60"
    >
      <div class="flex items-center gap-4">
        <span class="rounded-full bg-red-600/80 px-3 py-1 text-sm font-black text-white">
          阻挡步骤
        </span>
        <span class="text-sm font-bold text-slate-200">
          {{ attackerName }} 攻击 {{ battle?.targetIsLeader ? "你的领袖" : targetName }}
        </span>
      </div>

      <div class="flex items-center gap-3 text-sm font-black">
        <span class="text-red-300">攻击 {{ attackerPower }}</span>
        <span class="text-slate-500">vs</span>
        <span class="text-sky-300">防御 {{ targetPower }}</span>
        <span :class="willLose ? 'text-red-400' : 'text-green-400'">
          {{ willLose ? "（当前会被击败）" : "（当前可挡住）" }}
        </span>
      </div>

      <div class="flex flex-col items-center gap-2">
        <div v-if="blockers.length > 0" class="flex flex-wrap justify-center gap-2">
          <button
            v-for="b in blockers"
            :key="b.id"
            type="button"
            :disabled="isPending"
            class="rounded-md ring-2 ring-transparent transition hover:ring-amber-300 disabled:cursor-not-allowed disabled:opacity-50"
            @click="declareBlocker(b.id)"
          >
            <CardItem :card="getCard(b.number) ?? null" :is-tapped="b.isTapped" size="sm" />
          </button>
        </div>
        <span v-else class="text-xs text-slate-400">没有可用的【阻挡者】</span>
        <button
          type="button"
          :disabled="isPending"
          class="rounded-md bg-slate-700 px-6 py-2 text-sm font-bold text-white transition hover:bg-slate-600 disabled:cursor-not-allowed disabled:opacity-50"
          @click="GameRequest.passBlock()"
        >
          不阻挡
        </button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.battle-defense-enter-active { transition: transform 0.35s ease; }
.battle-defense-leave-active { transition: transform 0.35s ease; }
.battle-defense-enter-from { transform: translateY(100%); }
.battle-defense-leave-to { transform: translateY(100%); }
</style>
