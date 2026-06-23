<script setup lang="ts">
import { computed } from "vue";
import LeaderCard from "./LeaderCard.vue";
import StageSlot from "./StageSlot.vue";
import FieldArea from "./FieldArea.vue";
import HandArea from "./HandArea.vue";
import LifeArea from "./LifeArea.vue";
import DonArea from "./DonArea.vue";
import DonDeckPile from "./DonDeckPile.vue";
import DeckPile from "./DeckPile.vue";
import TrashPile from "./TrashPile.vue";
import { getCard } from "@/data/CardLoader";

const props = defineProps<{ side: "my" | "opponent"; isObserver: boolean; isPlayback: boolean }>();
const emit = defineEmits<{ (e: "hover-card", card: ReturnType<typeof getCard> | null): void }>();

const isOpponent = computed(() => props.side === "opponent");
const canShowDon = computed(() => !props.isObserver && !props.isPlayback);
</script>

<template>
  <section
    :class="[
      'relative min-h-0 min-w-0 flex-1 rounded-md border border-sky-200/15 shadow-inner shadow-black/35',
      isOpponent ? 'bg-red-950/[0.07]' : 'bg-sky-950/[0.16]',
    ]"
  >
    <div :class="['absolute inset-x-0', isOpponent ? 'bottom-0 h-px bg-red-300/20' : 'top-0 h-px bg-sky-300/20']" />

    <!-- 牌库 + 墓地（右侧固定） -->
    <div :class="['absolute right-3 z-20 flex flex-col gap-2', isOpponent ? 'top-1/2 -translate-y-1/2' : 'top-[45%] -translate-y-1/2']">
      <DeckPile :side="side" />
      <TrashPile :side="side" />
    </div>

    <!-- 三行网格 -->
    <div class="grid h-full min-w-0 grid-rows-[1fr_auto_1fr] gap-2 p-3 pr-32 md:pr-36">
      <!-- 第一行 -->
      <div v-if="isOpponent" class="relative min-h-0 min-w-0 pl-24 md:pl-28">
        <HandArea side="opponent" hidden @hover-card="(c) => emit('hover-card', c)" />
      </div>
      <div v-else class="relative flex min-h-0 min-w-0 items-stretch gap-4 pl-24 md:pl-28">
        <div class="absolute left-0 top-0 z-20"><LifeArea :side="side" /></div>
        <div class="min-w-0 flex-1 self-stretch"><FieldArea :side="side" @hover-card="(c) => emit('hover-card', c)" /></div>
      </div>

      <!-- 中间行 -->
      <div class="grid min-h-0 min-w-0 grid-cols-[minmax(14rem,0.9fr)_minmax(16rem,1.1fr)] items-center gap-4">
        <div class="min-w-0">
          <template v-if="canShowDon">
            <div class="flex w-full max-w-[32rem] items-center gap-2">
              <DonDeckPile :side="side" />
              <div class="min-w-0 flex-1"><DonArea :side="side" /></div>
            </div>
          </template>
        </div>
        <div class="justify-self-center">
          <div class="flex items-end justify-center gap-4 md:gap-5">
            <LeaderCard :side="side" @hover-card="(c) => emit('hover-card', c)" />
            <StageSlot :side="side" />
          </div>
        </div>
      </div>

      <!-- 第三行 -->
      <div v-if="isOpponent" class="relative flex min-h-0 min-w-0 items-stretch gap-4 pl-24 md:pl-28">
        <div class="absolute left-0 top-0 z-20"><LifeArea :side="side" /></div>
        <div class="min-w-0 flex-1 self-stretch"><FieldArea :side="side" @hover-card="(c) => emit('hover-card', c)" /></div>
      </div>
      <div v-else class="relative min-h-0 min-w-0 pl-24 md:pl-28">
        <HandArea side="my" :hidden="isObserver" @hover-card="(c) => emit('hover-card', c)" />
      </div>
    </div>
  </section>
</template>