<script setup lang="ts">
import { computed } from "vue";
import type { CardData } from "@/types/card";
import type { DeckEntry } from "@/store/deckStore";
import { primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";

const props = defineProps<{ entry: DeckEntry }>();
const emit = defineEmits<{
  (e: "remove", number: string): void;
  (e: "mouseEnter", card: CardData, rect: DOMRect, currentSprite: string): void;
  (e: "mouseLeave"): void;
}>();

const sprite = computed(() => props.entry.card.sprite ?? "/sprites/CardBack.png");
const colorStyle = computed(() => COLOR_STYLES[primaryDisplayColor(props.entry.card.color)]);

function onEnter(e: MouseEvent) {
  emit("mouseEnter", props.entry.card, (e.currentTarget as HTMLElement).getBoundingClientRect(), sprite.value);
}
</script>

<template>
  <div
    class="group relative flex cursor-default items-center gap-1.5 overflow-hidden rounded-md border border-[var(--line)] bg-[var(--surface2)]/60 px-2 py-1.5 transition-all duration-200 hover:border-[var(--line-strong)] hover:bg-[var(--surface2)] hover:shadow-[0_0_12px_var(--primary-glow)]"
    :style="{ backgroundImage: `url(${sprite})`, backgroundSize: 'cover', backgroundPosition: 'center 30%' }"
    @mouseenter="onEnter"
    @mouseleave="emit('mouseLeave')"
  >
    <div class="absolute inset-0 bg-[var(--bg0)]/80 transition-colors group-hover:bg-[var(--bg0)]/65" />

    <div
      :class="[
        'relative z-10 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border border-stone-600',
        colorStyle?.bg ?? 'bg-stone-600',
      ]"
    >
      <span
        class="text-xs font-bold leading-none text-white"
        style="text-shadow: 0 0 2px rgba(0,0,0,0.85), 0 0 1px rgba(0,0,0,1)"
      >
        {{ entry.card.cost }}
      </span>
    </div>
    <span class="relative z-10 min-w-0 flex-1 truncate text-xs font-medium text-white drop-shadow-sm">
      {{ entry.card.name }}
    </span>
    <span class="relative z-10 shrink-0 rounded border border-[var(--line-strong)] bg-[var(--surface)]/70 px-1 text-xs font-bold text-[var(--primary)]">
      ×{{ entry.count }}
    </span>
    <button
      class="relative z-10 w-5 shrink-0 rounded text-xs font-bold text-[var(--ink-dim)] opacity-0 transition-all hover:bg-red-900/40 hover:text-red-300 group-hover:opacity-100"
      @click="emit('remove', entry.card.number)"
    >
      −
    </button>
  </div>
</template>
