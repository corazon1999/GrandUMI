<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, inject } from "vue";
import type { CardData } from "@/types/card";
import CardFrame from "@/components/ui/CardFrame.vue";
import RarityRing from "@/components/ui/RarityRing.vue";
import { useCardHover } from "@/composables/useCardHover";
import { CARD_IMG_IO } from "./cardImgIO";

const props = defineProps<{
  card: CardData;
  deckCount: number;
  isLeaderMode: boolean;
}>();
const emit = defineEmits<{
  (e: "click"): void;
  (e: "rightClick"): void;
  (e: "mouseEnter", card: CardData, rect: DOMRect, currentSprite: string): void;
  (e: "mouseLeave"): void;
  (e: "spriteChange", sprite: string): void;
}>();

const isFull = computed(() => !props.isLeaderMode && props.deckCount >= 4);
const hasInDeck = computed(() => props.deckCount > 0);

const sprites = computed(() =>
  props.card.sprites?.length ? props.card.sprites : [props.card.sprite ?? "/sprites/CardBack.png"],
);
const hasAlts = computed(() => sprites.value.length > 1);
const spriteIdx = ref(sprites.value.length - 1);
const imgFailed = ref(false);
const currentSrc = computed(() =>
  imgFailed.value ? "/sprites/CardBack.png" : sprites.value[spriteIdx.value] ?? "/sprites/CardBack.png",
);

// 悬浮抬升
const cardRef = ref<HTMLElement | null>(null);
const rootRef = ref<HTMLElement | null>(null);
const { onEnter: onHoverIn, onLeave: onHoverOut } = useCardHover(cardRef);

// 图片懒加载：复用父级共享 observer（见 cardImgIO.ts），仅在进入视口附近才创建 <img>。
// 无 provider 时（例如单测/复用场景）退化为立即显示。
const imgIO = inject(CARD_IMG_IO, null);
const inView = ref(!imgIO);

onMounted(() => {
  // 异画默认取最后一个版本（与原逻辑一致）
  const initialSprite = sprites.value[sprites.value.length - 1];
  if (initialSprite && props.card.sprite !== initialSprite) props.card.sprite = initialSprite;

  if (imgIO && rootRef.value) imgIO.observe(rootRef.value, () => { inView.value = true; });
});

onUnmounted(() => { if (imgIO && rootRef.value) imgIO.unobserve(rootRef.value); });

function goPrev() {
  imgFailed.value = false;
  const next = (spriteIdx.value - 1 + sprites.value.length) % sprites.value.length;
  spriteIdx.value = next;
  emit("spriteChange", sprites.value[next]);
}
function goNext() {
  imgFailed.value = false;
  const next = (spriteIdx.value + 1) % sprites.value.length;
  spriteIdx.value = next;
  emit("spriteChange", sprites.value[next]);
}
function onEnter(e: MouseEvent) {
  emit("mouseEnter", props.card, (e.currentTarget as HTMLElement).getBoundingClientRect(), currentSrc.value);
  onHoverIn();
}
function onLeave() {
  emit("mouseLeave");
  onHoverOut();
}
</script>

<template>
  <div
    ref="rootRef"
    class="group flex flex-col gap-1.5"
  >
    <CardFrame
      ref="cardRef"
      :rarity="card.rarity"
      size="md"
      @click="!isFull && emit('click')"
      @contextmenu.prevent="emit('rightClick')"
      @mouseenter="onEnter"
      @mouseleave="onLeave"
    >
      <div
        :class="[
          'relative aspect-[2/3] w-full select-none',
          isFull ? 'cursor-not-allowed' : 'cursor-pointer',
        ]"
      >
        <img
          v-if="inView"
          :src="currentSrc"
          :alt="card.name"
          class="absolute inset-0 h-full w-full bg-stone-800 object-cover"
          :draggable="false"
          decoding="async"
          @error="imgFailed = true"
        />
        <div v-else class="absolute inset-0 bg-stone-800" />

        <!-- 暗化蒙版（已选/已满/灰显） -->
        <div
          v-if="isFull"
          class="absolute inset-0 bg-red-950/40"
        />
        <div
          v-else-if="hasInDeck && !isLeaderMode"
          class="pointer-events-none absolute inset-0 bg-orange-500/10 mix-blend-overlay"
        />

        <template v-if="hasAlts">
          <button
            class="absolute bottom-0 left-0 top-0 z-10 flex w-5 items-center justify-center bg-black/60 text-sm text-white opacity-0 transition-opacity hover:bg-black/80 group-hover:opacity-100"
            title="上一版本"
            @click.stop="goPrev"
          >
            ‹
          </button>
          <button
            class="absolute bottom-0 right-0 top-0 z-10 flex w-5 items-center justify-center bg-black/60 text-sm text-white opacity-0 transition-opacity hover:bg-black/80 group-hover:opacity-100"
            title="下一版本"
            @click.stop="goNext"
          >
            ›
          </button>
        </template>

        <div v-if="hasAlts" class="absolute bottom-1 left-0 right-0 z-10 flex justify-center gap-1 pb-0.5">
          <span
            v-for="(_, i) in sprites"
            :key="i"
            :class="['block h-1 w-1 rounded-full', i === spriteIdx ? 'bg-[#c8a04a]' : 'bg-white/30']"
          />
        </div>

        <!-- 费用角标 -->
        <div
          v-if="card.type !== 'Leader'"
          class="absolute left-1 top-1 flex h-6 w-6 items-center justify-center rounded-full border border-[#c8a04a] bg-black/80 text-xs font-bold text-white shadow-md"
        >
          {{ card.cost }}
        </div>

        <!-- 稀有度标识（仅 R+ 显示） -->
        <div
          v-if="card.rarity && ['L','SR','SEC','P'].includes(card.rarity)"
          class="absolute right-1 top-1"
        >
          <RarityRing :rarity="card.rarity" size="xs" />
        </div>

        <!-- 战力 -->
        <div
          v-if="card.power > 0"
          class="absolute bottom-1 right-1 flex h-6 w-6 items-center justify-center rounded-full border border-stone-500 bg-black/80 text-xs font-bold text-white shadow-md"
        >
          {{ (card.power / 1000).toFixed(0) }}k
        </div>

        <!-- 角标 -->
        <div
          v-if="card.subscript > 0"
          class="absolute bottom-1 left-1 rounded bg-black/80 px-1.5 text-xs font-bold text-[#d4b876] shadow-md"
        >
          {{ card.subscript }}
        </div>

        <!-- 已选数气泡 -->
        <div
          v-if="hasInDeck && !isLeaderMode"
          class="absolute -right-1 -top-1 flex h-6 w-6 items-center justify-center rounded-full border-2 border-[#c8a04a] bg-orange-500 text-xs font-bold leading-none text-white shadow-lg"
        >
          {{ deckCount }}
        </div>

        <!-- 已满提示 -->
        <div v-if="isFull" class="absolute inset-0 flex items-center justify-center">
          <span class="rounded border border-red-500 bg-black/70 px-2 py-0.5 text-xs font-bold tracking-wider text-red-300 uppercase">
            已满
          </span>
        </div>
      </div>
    </CardFrame>

    <p class="w-full truncate px-0.5 text-center text-xs leading-tight text-gray-400 group-hover:text-[#d4b876] transition-colors">
      {{ card.name }}
    </p>
  </div>
</template>
