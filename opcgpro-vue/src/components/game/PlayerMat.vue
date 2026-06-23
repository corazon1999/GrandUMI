<script setup lang="ts">
import { computed } from "vue";
import LeaderCard from "./LeaderCard.vue";
import StageSlot from "./StageSlot.vue";
import FieldArea from "./FieldArea.vue";
import LifeArea from "./LifeArea.vue";
import DonArea from "./DonArea.vue";
import DonDeckPile from "./DonDeckPile.vue";
import DeckPile from "./DeckPile.vue";
import TrashPile from "./TrashPile.vue";
import { getCard } from "@/data/CardLoader";

/**
 * PlayerMat — 单侧「半场」（毛毡牌桌内，源自 redesign/battle.jsx 的 Half）。
 * 三列网格（.bf-half = auto 1fr auto）：
 *   左 = 生命 + DON 簇 / 中 = 领袖 + 角色行 / 右 = 场地 + 牌库 + 墓地。
 * 手牌不在此处渲染——已上移到 GameBoard 的毛毡外（上/下）。
 */
const props = defineProps<{ side: "my" | "opponent"; isObserver: boolean; isPlayback: boolean }>();
const emit = defineEmits<{ (e: "hover-card", card: ReturnType<typeof getCard> | null): void }>();

const canShowDon = computed(() => !props.isObserver && !props.isPlayback);
</script>

<template>
  <section class="bf-half">
    <!-- 左：生命 + DON -->
    <div class="bf-col bf-col--left">
      <LifeArea :side="side" />
      <div v-if="canShowDon" class="bf-don-cluster">
        <DonDeckPile :side="side" />
        <DonArea :side="side" />
      </div>
    </div>

    <!-- 中：领袖 + 角色行 -->
    <div class="bf-col bf-col--center">
      <LeaderCard :side="side" @hover-card="(c) => emit('hover-card', c)" />
      <FieldArea :side="side" @hover-card="(c) => emit('hover-card', c)" />
    </div>

    <!-- 右：场地 + 牌库 + 墓地 -->
    <div class="bf-col bf-col--right">
      <StageSlot :side="side" />
      <DeckPile :side="side" />
      <TrashPile :side="side" />
    </div>
  </section>
</template>

<style scoped>
.bf-half {
  width: 100%;
}
.bf-col {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
}
.bf-col--left {
  justify-self: start;
  align-items: flex-end;
}
.bf-col--center {
  justify-content: center;
  min-width: 0;
}
.bf-col--right {
  justify-self: end;
  align-items: flex-end;
  gap: 10px;
}
.bf-don-cluster {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
</style>
