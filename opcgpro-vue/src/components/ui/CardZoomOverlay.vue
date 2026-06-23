<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from "vue";
import type { CardData } from "@/types/card";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import { RARITY_STYLES } from "@/components/deck-editor/CardHoverPreview";

const TYPE_LABELS: Record<string, string> = {
  Leader: "领航", Character: "角色", Stage: "舞台", Event: "事件",
};

const props = defineProps<{
  card: CardData;
  sprite: string;
}>();

const emit = defineEmits<{ (e: "close"): void }>();

const imgSrc = ref(props.sprite || props.card.sprite || "/sprites/CardBack.png");

watch(() => props.sprite, (s) => { imgSrc.value = s || props.card.sprite || "/sprites/CardBack.png"; });

function onImgError() {
  if (props.card.image && imgSrc.value !== props.card.image) {
    imgSrc.value = props.card.image;
  } else {
    imgSrc.value = "/sprites/CardBack.png";
  }
}

function onKeyDown(e: KeyboardEvent) {
  if (e.key === "Escape") emit("close");
}

onMounted(() => window.addEventListener("keydown", onKeyDown));
onUnmounted(() => window.removeEventListener("keydown", onKeyDown));

const displayColor = toDisplayColor(props.card.color);
const colorStyle = COLOR_STYLES[primaryDisplayColor(props.card.color)];
</script>

<template>
  <Transition name="zoom-fade">
    <div
      class="fixed inset-0 z-[120] flex items-center justify-center bg-black/75 backdrop-blur-sm"
      @click="emit('close')"
      @contextmenu.prevent="emit('close')"
    >
      <div class="flex flex-col items-center gap-3">
        <!-- 大卡图 -->
        <div
          class="relative overflow-hidden rounded-2xl border border-gray-600 shadow-2xl"
          style="height: min(78vh, 640px); aspect-ratio: 0.717"
          @click.stop
        >
          <img
            :src="imgSrc"
            :alt="card.name"
            class="absolute inset-0 h-full w-full object-cover"
            @error="onImgError"
          />
        </div>

        <!-- 信息条 -->
        <div
          class="max-w-[92vw] rounded-xl bg-gray-900/95 px-4 py-3 shadow-xl ring-1 ring-white/10"
          @click.stop
        >
          <div class="flex flex-wrap items-center gap-2">
            <p class="text-base font-bold leading-tight text-white">{{ card.name }}</p>
            <span v-if="colorStyle" :class="['rounded px-1.5 py-0.5 text-[11px] font-bold text-white', colorStyle.bg]">
              {{ displayColor }}
            </span>
            <span class="text-[11px] text-gray-400">{{ TYPE_LABELS[card.type] ?? card.type }}</span>
            <span v-if="card.property" class="text-[11px] text-gray-400">{{ card.property }}</span>
            <span v-if="card.rarity" :class="['rounded px-1 text-[10px] font-bold', RARITY_STYLES[card.rarity] ?? 'bg-gray-700 text-white']">
              {{ card.rarity }}
            </span>
            <span class="ml-auto text-[11px] text-gray-500">{{ card.number }}</span>
          </div>

          <div class="mt-1.5 flex items-center gap-4 text-[12px]">
            <span v-if="card.cost > 0" class="text-gray-300">费 <span class="font-bold text-white">{{ card.cost }}</span></span>
            <span v-if="card.type === 'Character' || card.type === 'Leader'" class="text-gray-300">力 <span class="font-bold text-white">{{ card.power.toLocaleString() }}</span></span>
            <span v-if="card.counter > 0" class="text-gray-300">反 <span class="font-bold text-white">+{{ card.counter }}</span></span>
          </div>

          <div v-if="card.keyWords.length > 0" class="mt-1.5 flex flex-wrap gap-1">
            <span v-for="k in card.keyWords" :key="k" class="rounded bg-blue-900/60 px-1.5 py-0.5 text-[10px] text-blue-300">
              {{ k }}
            </span>
          </div>

          <div v-if="card.abilities.length > 0" class="mt-1.5 flex flex-wrap gap-1">
            <span v-for="a in card.abilities" :key="a" class="rounded bg-emerald-900/60 px-1.5 py-0.5 text-[10px] text-emerald-300">
              {{ a }}
            </span>
          </div>

          <p v-if="card.trigger" class="mt-2 text-[12px] leading-snug text-amber-200">
            <span class="font-bold">触发</span> {{ card.trigger }}
          </p>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.zoom-fade-enter-active,
.zoom-fade-leave-active { transition: opacity 0.15s ease; }
.zoom-fade-enter-from,
.zoom-fade-leave-to { opacity: 0; }
</style>
