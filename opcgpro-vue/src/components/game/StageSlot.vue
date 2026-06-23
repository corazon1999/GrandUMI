<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/composables/useResponsive";
import CardItem from "@/components/ui/CardItem.vue";
import { getCard } from "@/data/CardLoader";

const props = defineProps<{ side: "my" | "opponent" }>();

const slotSizes = { sm: "w-[4.5rem] h-[6.3rem]", md: "w-[6rem] h-[8.4rem]", lg: "w-[8rem] h-[11.2rem]" } as const;

const player = useStore(useGameStore, (s) => (props.side === "my" ? s.my : s.opponent));
const isPending = useStore(useGameStore, (s) => s.isPending);
const selectedFieldId = useStore(useGameStore, (s) => s.selectedFieldId);
const { cardSize } = useResponsive();

const dimensions = computed(() => slotSizes[cardSize.value]);
const stageNumber = computed(() => player.value?.stageNumber ?? null);
const stageId = computed(() => player.value?.stageId ?? null);
const stageTapped = computed(() => player.value?.stageTapped ?? false);
const stageCard = computed(() => (stageNumber.value ? getCard(stageNumber.value) ?? null : null));
const clickable = computed(() => props.side === "my" && !!stageId.value && !isPending.value);

function handleClick() {
  if (!clickable.value || !stageId.value) return;
  useGameStore.getState().setSelectedField(selectedFieldId.value === stageId.value ? null : stageId.value);
}
</script>

<template>
  <div class="bf-well">
    <span class="kicker">场地</span>
    <div :class="[dimensions, 'bf-area']">
      <CardItem v-if="stageNumber" :card="stageCard" :size="cardSize" :is-selected="selectedFieldId === stageId" :is-tapped="stageTapped" @click="handleClick" />
      <span v-else class="mono faint bf-area__txt">STAGE</span>
    </div>
  </div>
</template>

<style scoped>
.bf-area__txt {
  font-size: 11px;
  letter-spacing: 0.14em;
}
</style>
