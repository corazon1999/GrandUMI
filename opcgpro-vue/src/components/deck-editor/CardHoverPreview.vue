<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import { PREVIEW_W, type HoverInfo } from "./CardHoverPreview";
import RarityRing from "@/components/ui/RarityRing.vue";
import { useCardEntrance } from "@/composables/useCardEntrance";

const PREVIEW_H_APPROX = 480;
const TYPE_LABELS: Record<string, string> = {
  Leader: "领航", Character: "角色", Stage: "舞台", Event: "事件",
};

const props = defineProps<{ info: HoverInfo }>();

const imgSrc = ref(props.info.currentSprite ?? props.info.card.sprite ?? "/sprites/CardBack.png");
watch(
  () => [props.info.currentSprite, props.info.card.sprite],
  () => { imgSrc.value = props.info.currentSprite ?? props.info.card.sprite ?? "/sprites/CardBack.png"; },
);

const showRight = computed(() => window.innerWidth - props.info.rect.right >= PREVIEW_W + 16);
const x = computed(() => {
  const raw = showRight.value ? props.info.rect.right + 12 : props.info.rect.left - 12 - PREVIEW_W;
  // 夹在视口内，避免出界/被右栏遮挡（配合 Teleport to body 后 fixed 相对视口生效）
  return Math.max(8, Math.min(raw, window.innerWidth - PREVIEW_W - 8));
});
const y = computed(() => {
  const cardCenterY = props.info.rect.top + props.info.rect.height / 2;
  const rawY = cardCenterY - PREVIEW_H_APPROX / 2;
  return Math.max(8, Math.min(rawY, window.innerHeight - PREVIEW_H_APPROX - 8));
});

const card = computed(() => props.info.card);
const displayColor = computed(() => toDisplayColor(card.value.color));
const colorStyle = computed(() => COLOR_STYLES[primaryDisplayColor(card.value.color)]);

// 弹性入场
const previewRef = ref<HTMLElement | null>(null);
useCardEntrance(previewRef);
</script>

<template>
  <div
    ref="previewRef"
    class="hover-preview fixed z-50 overflow-hidden rounded-xl border border-[#c8a04a] bg-stone-900 shadow-[0_12px_40px_rgba(0,0,0,0.7),0_0_0_1px_rgba(200,160,74,0.4)]"
    :style="{ width: PREVIEW_W + 'px', left: x + 'px', top: y + 'px', pointerEvents: 'none' }"
  >
    <div class="relative bg-black" :style="{ height: PREVIEW_W * 1.4 + 'px' }">
      <img
        :src="imgSrc"
        :alt="card.name"
        class="absolute inset-0 h-full w-full object-cover"
        loading="lazy"
        @error="imgSrc = '/sprites/CardBack.png'"
      />
      <!-- 顶部金色高光（HS 卡顶反光感） -->
      <div class="pointer-events-none absolute inset-x-0 top-0 h-1 bg-gradient-to-b from-[#c8a04a]/60 to-transparent" />
      <!-- 底部色条 -->
      <div v-if="colorStyle" :class="['absolute bottom-0 left-0 right-0 h-1.5', colorStyle.bg]" />
    </div>

    <div class="flex flex-col gap-1.5 bg-gradient-to-b from-stone-900 to-stone-950 p-2.5">
      <p class="font-hs-heading text-sm font-bold leading-tight text-white">
        {{ card.name }}
      </p>

      <div class="flex flex-wrap items-center gap-1.5">
        <span v-if="colorStyle" :class="['rounded px-1.5 py-0.5 text-xs font-bold text-white', colorStyle.bg]">
          {{ displayColor }}
        </span>
        <span class="text-xs text-gray-400">{{ TYPE_LABELS[card.type] ?? card.type }}</span>
        <RarityRing v-if="card.rarity" :rarity="card.rarity" size="xs" />
        <span v-if="card.subscript > 0" class="text-[11px] font-bold text-[#d4b876]">角标{{ card.subscript }}</span>
        <span class="ml-auto font-mono text-[11px] text-gray-600">{{ card.number }}</span>
      </div>

      <div class="flex items-center gap-3 border-y border-stone-700/60 py-1 text-xs">
        <span v-if="card.cost > 0" class="text-gray-300">费 <span class="font-bold text-white">{{ card.cost }}</span></span>
        <span v-if="card.power > 0" class="text-gray-300">力 <span class="font-bold text-white">{{ card.power.toLocaleString() }}</span></span>
        <span v-if="card.counter > 0" class="text-gray-300">反 <span class="font-bold text-white">+{{ card.counter }}</span></span>
        <span v-if="card.property" class="ml-auto rounded border border-stone-600 px-1 text-[11px] text-gray-300">{{ card.property }}</span>
      </div>

      <div v-if="card.keyWords.length > 0" class="flex flex-wrap gap-1">
        <span v-for="k in card.keyWords" :key="k" class="rounded border border-blue-800/60 bg-blue-900/40 px-1.5 py-0.5 text-[11px] text-blue-300">
          {{ k }}
        </span>
      </div>

      <p v-if="card.effectText" class="border-t border-stone-700/60 pt-1.5 text-xs leading-relaxed text-gray-300">
        {{ card.effectText }}
      </p>

      <p v-if="card.trigger" class="rounded border border-[#c8a04a]/40 bg-[#3a2810]/40 px-1.5 py-1 text-[11px] italic leading-relaxed text-[#d4b876]">
        ⚡ {{ card.trigger }}
      </p>
    </div>
  </div>
</template>

<style scoped>
.hover-preview {
  /* 入场由 useCardEntrance 控制（GSAP back.out 弹性） */
}
</style>
