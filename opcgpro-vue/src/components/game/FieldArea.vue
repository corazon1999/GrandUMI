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

const player = useStore(useGameStore, (s) => (props.side === "my" ? s.my : s.opponent));
const isPending = useStore(useGameStore, (s) => s.isPending);
const selectedFieldId = useStore(useGameStore, (s) => s.selectedFieldId);
const selectedDonIndex = useStore(useGameStore, (s) => s.selectedDonIndex);
const battle = useStore(useGameStore, (s) => s.battle);
const currentTurn = useStore(useGameStore, (s) => s.currentTurn);
const turnCount = useStore(useGameStore, (s) => s.turnCount);
const isSelectingTarget = useStore(useBattleStore, (s) => s.isSelectingTarget);
const { cardSize } = useResponsive();

// 战斗中：攻击方=当前回合方，防守方=另一方
const attackerSide = computed(() => currentTurn.value ? "my" : "opponent");
const defenderSide = computed(() => currentTurn.value ? "opponent" : "my");

function handleCardClick(cardId: string, isTapped: boolean) {
  if (isPending.value) return;

  if (isSelectingTarget.value && props.side === "opponent") {
    // 只有横置(休息)的角色才能被攻击
    if (!isTapped) return;
    useBattleStore.getState().confirmAttackTarget({ isLeader: false, cardId });
    return;
  }

  // 选中了活跃咚 + 点自己角色 -> 贴咚（依附拟选的张数，#144）
  if (selectedDonIndex.value !== null && props.side === "my") {
    GameRequest.attachDon(cardId, selectedDonIndex.value || 1);
    useGameStore.getState().setSelectedDon(null);
    return;
  }

  useGameStore.getState().setSelectedField(selectedFieldId.value === cardId ? null : cardId);
}

function cardOf(number: string) { return getCard(number) ?? null; }
function powerBuff(fc: { powerCurrent: number; attachedDon: number; number: string }) {
  return fc.powerCurrent - (cardOf(fc.number)?.power ?? 0) - fc.attachedDon * 1000;
}

// 始终 5 个槽位（不足补空槽，源自 battle.jsx CharRow）
const slots = computed(() =>
  Array.from({ length: 5 }, (_, i) => player.value?.fieldCards[i] ?? null),
);
const slotDim = computed(
  () =>
    ({ sm: "h-[6.3rem] w-[4.5rem]", md: "h-[8.4rem] w-[6rem]", lg: "h-[11.2rem] w-[8rem]" })[
      cardSize.value
    ],
);
</script>

<template>
  <!-- 角色行：始终 5 槽（源自 battle.jsx CharRow），空槽显示编号 -->
  <div class="bf-row bf-charrow">
    <template v-for="(fc, i) in slots" :key="fc?.id ?? ('empty-' + i)">
      <!-- 有角色 -->
      <div
        v-if="fc"
        class="relative shrink-0"
        @mouseenter="emit('hover-card', cardOf(fc.number))"
        @mouseleave="emit('hover-card', null)"
      >
        <!-- 战斗高亮：攻击者红环 / 被攻击目标琥珀环 -->
        <div v-if="battle && side === attackerSide && fc.id === battle.attackerCardId" class="pointer-events-none absolute -inset-1 z-20 animate-pulse rounded-lg shadow-lg shadow-red-500/50 ring-4 ring-red-500" />
        <div v-if="battle && side === defenderSide && !battle.targetIsLeader && fc.id === battle.targetCardId" class="pointer-events-none absolute -inset-1 z-20 animate-pulse rounded-lg shadow-lg shadow-amber-400/50 ring-4 ring-amber-400" />
        <span v-if="battle && side === attackerSide && fc.id === battle.attackerCardId" class="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-red-600 px-1.5 text-[10px] font-black text-white shadow">攻击</span>
        <span v-if="battle && side === defenderSide && !battle.targetIsLeader && fc.id === battle.targetCardId" class="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-amber-500 px-1.5 text-[10px] font-black text-black shadow">目标</span>

        <CardItem
          :card="cardOf(fc.number)"
          :is-selected="selectedFieldId === fc.id || (isSelectingTarget && side === 'opponent' && fc.isTapped && !isPending)"
          :is-tapped="fc.isTapped"
          :power-buff="powerBuff(fc)"
          :cost-buff="fc.cost - (cardOf(fc.number)?.cost ?? 0)"
          :attached-don-count="fc.attachedDon"
          :size="cardSize"
          hide-counter
          :lift-on-select="false"
          show-blocker-fx
          :attack-state="side === 'my' && currentTurn ? (fc.canAttack ? 'can' : !fc.isTapped && fc.turnPlayed === turnCount ? 'sick' : 'none') : 'none'"
          @click="handleCardClick(fc.id, fc.isTapped)"
        />

        <!-- 选择攻击目标指示器 -->
        <div v-if="isSelectingTarget && side === 'opponent' && fc.isTapped && !isPending" class="absolute -right-2 -top-2 h-5 w-5 animate-pulse rounded-full bg-red-500 shadow-lg shadow-red-500/50" />

        <!-- 锁定标识 -->
        <div v-if="fc.cannotActivateNextReset" title="下个重置阶段不会转为活跃" class="pointer-events-none absolute -bottom-2 -right-2 z-40 flex h-6 w-6 items-center justify-center rounded-full bg-slate-900/90 text-amber-300 shadow-lg ring-2 ring-amber-400/70">
          <span class="text-[12px] leading-none">🔒</span>
        </div>

        <!-- 无法转为休息状态 -->
        <div v-if="fc.cannotBeRested" title="无法被效果转为休息状态" class="pointer-events-none absolute -bottom-2 -left-2 z-40 flex h-6 w-6 items-center justify-center rounded-full bg-slate-900/90 shadow-lg ring-2 ring-rose-400/70">
          <svg viewBox="0 0 24 24" class="h-4 w-4" fill="none"><rect x="3" y="8.5" width="18" height="7" rx="1.5" stroke="#e2e8f0" stroke-width="1.6" /><path d="M5.5 6 L18.5 18 M18.5 6 L5.5 18" stroke="#f43f5e" stroke-width="2.2" stroke-linecap="round" /></svg>
        </div>

        <!-- 咚附着指示器 -->
        <div v-if="selectedDonIndex !== null && side === 'my' && !isPending" class="pointer-events-none absolute -left-2 -top-2 z-40 flex h-6 min-w-6 animate-pulse items-center justify-center rounded-full bg-yellow-300 px-1 shadow-lg shadow-yellow-300/50">
          <span class="text-[10px] font-black text-black">+{{ selectedDonIndex }}</span>
        </div>
      </div>

      <!-- 空槽 -->
      <div v-else :class="[slotDim, 'bf-slot bf-charslot']">
        <span class="bf-slot__n">{{ i + 1 }}</span>
      </div>
    </template>
  </div>
</template>

<style scoped>
.bf-charrow {
  overflow: visible;
}
.bf-charslot .bf-slot__n {
  font-size: 24px;
}
</style>
