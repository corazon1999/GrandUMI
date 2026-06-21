<script setup lang="ts">
import { computed } from "vue";
import type { CardData } from "@/types/card";
import Modal from "@/components/ui/Modal.vue";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";

const props = defineProps<{ card: CardData | null }>();
const emit = defineEmits<{ (e: "close"): void }>();

const TYPE_LABELS: Record<string, string> = {
  Leader: "领航", Character: "角色", Stage: "舞台", Event: "事件",
};
const RARITY_BG: Record<string, string> = {
  L: "bg-yellow-500 text-black", SR: "bg-pink-500 text-white", R: "bg-sky-500 text-white",
  UC: "bg-gray-500 text-white", U: "bg-gray-500 text-white", C: "bg-gray-700 text-gray-300",
  SEC: "bg-red-600 text-white", P: "bg-emerald-500 text-white",
};
const RARITY_LABEL: Record<string, string> = {
  L: "领袖", SR: "超稀有", R: "稀有", UC: "罕见", U: "罕见", C: "普通", SEC: "隐藏稀有", P: "宣传",
};

const displayColor = computed(() => (props.card ? toDisplayColor(props.card.color) : ""));
const colorStyle = computed(() => (props.card ? COLOR_STYLES[primaryDisplayColor(props.card.color)] : undefined));

function onImgError(e: Event) {
  (e.target as HTMLImageElement).src = "/sprites/CardBack.png";
}
</script>

<template>
  <Modal v-if="card" :open="!!card" :title="card.name" @close="emit('close')">
    <div class="flex gap-4">
      <div class="relative h-44 w-32 shrink-0 overflow-hidden rounded-lg bg-gray-800">
        <img
          :src="card.sprite ?? '/sprites/CardBack.png'"
          :alt="card.name"
          class="h-full w-full object-cover"
          loading="lazy"
          @error="onImgError"
        />
        <div v-if="colorStyle" :class="['absolute bottom-0 left-0 right-0 h-1', colorStyle.bg]" />
      </div>

      <div class="flex min-w-0 flex-col gap-2 text-sm">
        <div class="flex flex-wrap items-center gap-1.5">
          <span class="text-xs font-bold text-orange-400">{{ card.number }}</span>
          <span :class="['rounded px-1.5 py-0.5 text-xs font-bold text-white', colorStyle?.bg ?? 'bg-gray-700']">
            {{ displayColor }}
          </span>
          <span class="text-xs text-gray-400">{{ TYPE_LABELS[card.type] ?? card.type }}</span>
        </div>

        <div class="flex flex-wrap items-center gap-1.5">
          <span
            v-if="card.rarity"
            :class="['rounded px-1.5 py-0.5 text-xs font-bold', RARITY_BG[card.rarity] ?? 'bg-gray-700 text-white']"
          >
            {{ RARITY_LABEL[card.rarity] ?? card.rarity }}
          </span>
          <span v-if="card.subscript > 0" class="text-xs font-bold text-yellow-400">角标 {{ card.subscript }}</span>
        </div>

        <p v-if="card.property" class="text-xs text-gray-400">
          属性 <span class="text-white">{{ card.property }}</span>
        </p>
        <p v-if="card.power > 0" class="text-xs">
          <span class="mr-1 text-gray-400">威力</span><span class="font-bold text-white">{{ card.power.toLocaleString() }}</span>
        </p>
        <p v-if="card.cost > 0" class="text-xs">
          <span class="mr-1 text-gray-400">费用</span><span class="font-bold text-white">{{ card.cost }}</span>
        </p>
        <p v-if="card.counter > 0" class="text-xs">
          <span class="mr-1 text-gray-400">反击</span><span class="font-bold text-white">+{{ card.counter }}</span>
        </p>

        <div v-if="card.keyWords.length > 0" class="flex flex-wrap gap-1">
          <span v-for="k in card.keyWords" :key="k" class="rounded bg-blue-900/60 px-1.5 py-0.5 text-xs text-blue-300">
            {{ k }}
          </span>
        </div>

        <p v-if="card.effectText" class="max-w-52 text-xs leading-relaxed text-gray-300">
          {{ card.effectText }}
        </p>
      </div>
    </div>
  </Modal>
</template>
