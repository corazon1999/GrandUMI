<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem.vue";

const my = useStore(useGameStore, (s) => s.my);
const opp = useStore(useGameStore, (s) => s.opponent);
const isFirst = useStore(useGameStore, (s) => s.currentTurn || s.firstPlayer === 0);
const mulliganBothDone = useStore(useGameStore, (s) => s.mulliganBothDone);

const visible = computed(() => !!my.value && !mulliganBothDone.value);
const myDone = computed(() => my.value?.mulliganDone ?? false);
const oppDone = computed(() => opp.value?.mulliganDone ?? false);
const choosing = computed(() => !myDone.value);
const handCards = computed(() => (my.value ? my.value.handCardNumbers.map((n) => getCard(n) ?? null) : []));
// 浮层不占桌面空间，统一用最大尺寸 lg（≈ 设计稿 132px）让起手牌更清晰
const mulliganCardSize = "lg" as const;
</script>

<template>
  <Transition name="fade">
    <div v-if="visible" class="fixed inset-0 z-50 flex flex-col items-center justify-center gap-6 bg-black/80">
      <div class="text-center">
        <p class="mb-1 text-lg font-bold text-white">{{ isFirst ? "你是先手" : "你是后手" }}</p>
        <p class="text-sm text-gray-400">
          {{ choosing ? "是否要更换起始手牌？" : oppDone ? "进入对局..." : "等待对手选择..." }}
        </p>
      </div>

      <div class="flex items-center justify-center gap-2 px-4">
        <CardItem v-for="(card, i) in handCards" :key="`mulligan-${card?.number ?? i}-${i}`" :card="card" :size="mulliganCardSize" />
      </div>

      <div v-if="choosing" class="flex gap-4">
        <button class="rounded-lg bg-blue-600 px-8 py-3 text-base font-bold text-white transition-colors hover:bg-blue-500" @click="GameRequest.mulligan(true)">
          更换
        </button>
        <button class="rounded-lg bg-orange-500 px-8 py-3 text-base font-bold text-white transition-colors hover:bg-orange-400" @click="GameRequest.mulligan(false)">
          保留
        </button>
      </div>

      <div v-else class="flex items-center gap-3">
        <div class="h-5 w-5 animate-spin rounded-full border-2 border-white/40 border-t-white" />
        <span class="text-sm text-gray-300">等待对手完成选择...</span>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from,
.fade-leave-to { opacity: 0; }
</style>
