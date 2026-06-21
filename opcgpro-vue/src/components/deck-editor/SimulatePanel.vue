<script setup lang="ts">
import { ref } from "vue";
import { useStore } from "@/composables/useStore";
import { useDeckStore } from "@/store/deckStore";
import CardItem from "@/components/ui/CardItem.vue";
import type { CardData } from "@/types/card";

const leader = useStore(useDeckStore, (s) => s.leader);
const entries = useStore(useDeckStore, (s) => s.entries);

const hand = ref<CardData[]>([]);
const drawPile = ref<CardData[]>([]);

function initSimulate() {
  const deck: CardData[] = entries.value.flatMap((e) => Array(e.count).fill(e.card));
  const shuffled = [...deck].sort(() => Math.random() - 0.5);
  hand.value = shuffled.slice(0, 5);
  drawPile.value = shuffled.slice(5);
}
function draw() {
  if (drawPile.value.length === 0) return;
  hand.value = [...hand.value, drawPile.value[0]];
  drawPile.value = drawPile.value.slice(1);
}
function reset() {
  hand.value = [];
  drawPile.value = [];
}
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-4">
    <h2 class="text-sm font-bold text-white">模拟抽卡</h2>

    <div class="flex gap-2">
      <button
        class="flex-1 rounded-lg bg-blue-600 py-2 text-xs font-bold text-white transition-colors hover:bg-blue-500"
        @click="initSimulate"
      >
        开始模拟
      </button>
      <button
        :disabled="drawPile.length === 0"
        class="flex-1 rounded-lg bg-gray-700 py-2 text-xs text-white transition-colors hover:bg-gray-600 disabled:opacity-50"
        @click="draw"
      >
        抽一张
      </button>
      <button
        class="rounded-lg bg-gray-800 px-3 py-2 text-xs text-red-400 transition-colors hover:bg-gray-700"
        @click="reset"
      >
        重置
      </button>
    </div>

    <div v-if="leader" class="flex items-center gap-2">
      <span class="text-xs text-gray-400">领航</span>
      <CardItem :card="leader" size="sm" />
    </div>

    <div>
      <p class="mb-2 text-xs text-gray-400">手牌 ({{ hand.length }}) · 剩余 {{ drawPile.length }} 张</p>
      <div class="flex flex-wrap gap-2">
        <CardItem v-for="(card, i) in hand" :key="`${card.number}-${i}`" :card="card" size="sm" />
        <p v-if="hand.length === 0" class="text-xs text-gray-600">点击"开始模拟"洗牌抽手</p>
      </div>
    </div>
  </div>
</template>
