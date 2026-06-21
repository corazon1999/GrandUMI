<script setup lang="ts">
import { computed } from "vue";
import { useResponsive } from "@/composables/useResponsive";
import DonCardItem from "./DonCardItem.vue";

const props = withDefaults(
  defineProps<{
    label: string;
    count: number;
    state: "active" | "rest";
    selected?: boolean;
    canInteract?: boolean;
  }>(),
  { selected: false, canInteract: false },
);
const emit = defineEmits<{ (e: "click"): void }>();

const slotSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
} as const;
const { cardSize } = useResponsive();
const slot = computed(() => slotSizes[cardSize.value]);
const clickable = computed(() => props.count > 0 && props.canInteract);
</script>

<template>
  <button
    type="button"
    :disabled="count <= 0 || !canInteract"
    :class="[slot, 'relative shrink-0 rounded-md border border-sky-200/15 bg-black/15 text-left shadow-inner shadow-black/25 disabled:cursor-default']"
    @click="clickable && emit('click')"
  >
    <span :class="['absolute left-2 top-2 z-20 text-xs font-black drop-shadow', state === 'active' ? 'text-yellow-200' : 'text-zinc-200']">
      {{ label }}
    </span>
    <DonCardItem v-if="count > 0" :state="state" :size="cardSize" :is-selected="selected" disabled />
    <div v-else class="h-full w-full rounded-md border-2 border-dashed border-slate-500/50 bg-slate-950/35" />
    <div class="absolute inset-x-0 bottom-1 z-30 flex justify-center">
      <span class="rounded bg-slate-950/90 px-2 py-0.5 text-xs font-black text-white shadow">{{ count }}</span>
    </div>
  </button>
</template>
