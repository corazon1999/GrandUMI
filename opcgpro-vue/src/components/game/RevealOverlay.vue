<script setup lang="ts">
import { ref, watch } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem.vue";
import type { CardData } from "@/types/card";

const REVEAL_DURATION = 2500;

const reveal = useStore(useGameStore, (s) => s.reveal);

interface Shown { cards: CardData[]; label: string }
const shown = ref<Shown | null>(null);
let timer: ReturnType<typeof setTimeout> | null = null;

watch(
  () => reveal.value?.nonce,
  (nonce) => {
    if (reveal.value && nonce != null) {
      const cards = reveal.value.cardNumbers
        .map((n) => getCard(n))
        .filter((c): c is CardData => !!c);
      if (cards.length === 0) {
        useGameStore.getState().clearReveal();
        return;
      }
      shown.value = {
        cards,
        label: reveal.value.side === "my" ? "你公开了" : "对方公开了",
      };
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => {
        shown.value = null;
        useGameStore.getState().clearReveal();
      }, REVEAL_DURATION);
    }
  },
);
</script>

<template>
  <Transition name="reveal">
    <div
      v-if="shown"
      class="pointer-events-none fixed inset-0 z-40 flex flex-col items-center justify-center gap-4"
    >
      <div class="absolute inset-0 bg-black/45" />
      <div class="relative flex flex-col items-center gap-4">
        <div class="rounded-full bg-orange-500/90 px-5 py-1.5 text-base font-bold text-white shadow-lg">
          {{ shown.label }}
        </div>
        <div class="flex flex-wrap justify-center gap-3">
          <CardItem
            v-for="(card, i) in shown.cards"
            :key="i"
            :card="card"
            size="lg"
          />
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.reveal-enter-active { transition: opacity 0.2s ease; }
.reveal-leave-active { transition: opacity 0.2s ease; }
.reveal-enter-from { opacity: 0; }
.reveal-leave-to { opacity: 0; }
</style>
