<script setup lang="ts">
import { ref, watch } from "vue";
import Modal from "@/components/ui/Modal.vue";
import { getAllCachedCards, loadCardSet } from "@/data/CardLoader";
import { DEFAULT_SEARCH_SETS } from "@/data/cardSets";
import type { CardData } from "@/types/card";

const props = defineProps<{ open: boolean; current: string }>();
const emit = defineEmits<{ (e: "close"): void; (e: "select", card: CardData): void }>();

const leaders = ref<CardData[]>([]);
const loading = ref(false);

function spriteOf(card: CardData): string {
  return card.sprites?.length ? card.sprites[card.sprites.length - 1] : card.sprite ?? "";
}

watch(
  () => props.open,
  async (open) => {
    if (!open) return;
    loading.value = true;
    if (getAllCachedCards().length === 0) {
      for (const setName of DEFAULT_SEARCH_SETS) {
        await loadCardSet(setName).catch(() => {});
      }
    }
    leaders.value = getAllCachedCards().filter((c) => c.type === "Leader");
    loading.value = false;
  },
);
</script>

<template>
  <Modal :open="open" title="选择头像" @close="emit('close')">
    <div class="flex max-h-80 flex-col gap-2 overflow-y-auto">
      <p class="text-xs text-gray-500">所有领航卡</p>
      <p v-if="loading" class="py-8 text-center text-xs text-gray-600">加载中...</p>
      <div v-else class="grid grid-cols-5 gap-2">
        <button
          v-for="card in leaders"
          :key="card.number"
          :title="card.name"
          :class="[
            'relative h-14 w-14 overflow-hidden rounded-full border-2 transition-all',
            spriteOf(card) === current
              ? 'border-orange-500 ring-2 ring-orange-500/40'
              : 'border-gray-700 hover:border-gray-400',
          ]"
          @click="emit('select', card)"
        >
          <img
            :src="spriteOf(card)"
            :alt="card.name"
            class="h-full w-full rounded-full object-cover object-top"
            style="transform: scale(1.1)"
            :draggable="false"
            loading="lazy"
          />
        </button>
      </div>
    </div>
  </Modal>
</template>
