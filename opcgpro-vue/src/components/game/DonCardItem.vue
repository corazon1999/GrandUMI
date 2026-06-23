<script setup lang="ts">
import { computed } from "vue";
import { clsx } from "clsx";
import type { DonState } from "@/types/game";

const props = withDefaults(
  defineProps<{
    state: DonState;
    isSelected?: boolean;
    disabled?: boolean;
    size?: "sm" | "md" | "lg";
  }>(),
  { isSelected: false, disabled: false, size: "md" },
);
const emit = defineEmits<{ (e: "click"): void }>();

const sizeClass = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
} as const;
const textClass = { sm: "text-xs", md: "text-sm", lg: "text-base" } as const;
const stateStyle: Record<DonState, string> = {
  deck: "border-sky-500/70 bg-gradient-to-br from-sky-950 via-blue-950 to-slate-950 text-sky-200",
  active: "border-yellow-100 bg-yellow-300 text-black cursor-pointer hover:shadow-lg hover:shadow-yellow-300/40",
  rest: "border-zinc-500 bg-zinc-700 text-zinc-300 opacity-75",
  attached: "border-amber-200 bg-amber-500 text-black",
};

const interactive = computed(() => props.state === "active" && !props.disabled);
</script>

<template>
  <div
    :class="clsx(
      sizeClass[size],
      'relative shrink-0 overflow-hidden rounded-md border-2 shadow-xl shadow-black/35 transition-all',
      stateStyle[state],
      isSelected && 'ring-2 ring-white',
      disabled && 'pointer-events-none',
      interactive && 'hover:scale-[1.03] active:scale-[0.97]',
    )"
    :title="`DON ${state}`"
    @click="!disabled && emit('click')"
  >
    <div class="absolute inset-2 rounded border border-current/25" />
    <div class="flex h-full w-full items-center justify-center px-2">
      <span :class="clsx('font-black leading-none tracking-normal', textClass[size])">DON</span>
    </div>
  </div>
</template>
