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
  <div :class="[dimensions, 'relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30']">
    <span class="absolute left-2 top-2 z-10 text-xs font-semibold text-slate-200 drop-shadow">场地</span>
    <CardItem v-if="stageNumber" :card="stageCard" :size="cardSize" :is-selected="selectedFieldId === stageId" :is-tapped="stageTapped" @click="handleClick" />
    <span v-else class="text-xs font-black text-slate-600">STAGE</span>
  </div>
</template>
