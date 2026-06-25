<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import DonCountSlot from "./DonCountSlot.vue";

const props = defineProps<{ side: "my" | "opponent" }>();

const player = useStore(useGameStore, (s) => (props.side === "my" ? s.my : s.opponent));
const currentTurn = useStore(useGameStore, (s) => s.currentTurn);
const isPending = useStore(useGameStore, (s) => s.isPending);
const selectedDonIndex = useStore(useGameStore, (s) => s.selectedDonIndex);

const canInteract = computed(
  () => props.side === "my" && currentTurn.value && !isPending.value && (player.value?.costActive ?? 0) > 0,
);

// 每次点击拟依附数 +1（封顶=活跃咚数），到顶后再点取消；目标点击时一次依附该数量（#144 复数咚依附）
function toggleDon() {
  const cur = selectedDonIndex.value ?? 0;
  const max = player.value?.costActive ?? 0;
  useGameStore.getState().setSelectedDon(cur >= max ? null : cur + 1);
}
</script>

<template>
  <div v-if="player" class="flex min-w-0 items-center gap-2">
    <DonCountSlot label="活跃" :count="player.costActive" state="active" :selected="selectedDonIndex !== null" :staged-count="selectedDonIndex ?? 0" :can-interact="canInteract" @click="toggleDon" />
    <DonCountSlot label="休息" :count="player.costRest" state="rest" />
  </div>
</template>
