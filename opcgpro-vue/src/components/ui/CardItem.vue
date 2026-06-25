<script setup lang="ts">
import { computed, ref, watch, inject } from "vue";
import type { CardData } from "@/types/card";
import { clsx } from "clsx";
import { CARD_FELT } from "@/composables/useResponsive";
import CardZoomOverlay from "./CardZoomOverlay.vue";

const props = withDefaults(
  defineProps<{
    card: CardData | null;
    isSelected?: boolean;
    isTapped?: boolean;
    powerBuff?: number;
    attachedDonCount?: number;
    faceDown?: boolean;
    size?: "sm" | "md" | "lg";
    hidePower?: boolean;
    hideCost?: boolean;
    hideCounter?: boolean;
    liftOnSelect?: boolean;
    attackState?: "can" | "sick" | "none";
    costBuff?: number;
    showBlockerFx?: boolean;
  }>(),
  {
    isSelected: false,
    isTapped: false,
    powerBuff: 0,
    attachedDonCount: 0,
    faceDown: false,
    size: "md",
    hidePower: false,
    hideCost: false,
    hideCounter: false,
    liftOnSelect: true,
    attackState: "none",
    costBuff: 0,
    showBlockerFx: false,
  },
);

const emit = defineEmits<{ (e: "click"): void }>();

const zoomOpen = ref(false);

function handleContextMenu(e: MouseEvent) {
  e.preventDefault();
  if (showFaceDown.value || !props.card) return;
  zoomOpen.value = true;
}

const sizes = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
} as const;

const FALLBACK = "/sprites/CardBack.png";

const showFaceDown = computed(() => props.faceDown || !props.card);
const displayPower = computed(
  () => (props.card?.power ?? 0) + props.powerBuff + props.attachedDonCount * 1000,
);
const powerColor = computed(() => {
  const delta = props.powerBuff + props.attachedDonCount * 1000;
  if (delta > 0) return "text-green-300";
  if (props.powerBuff < 0) return "text-red-300";
  return "text-white";
});

const imgSrc = ref(props.card?.sprite ?? FALLBACK);
watch(
  () => props.card?.sprite,
  (s) => { imgSrc.value = s ?? FALLBACK; },
);

const transformStyle = computed(() => {
  const rotate = props.isTapped ? 90 : 0;
  const lift = props.liftOnSelect !== false;
  const y = props.isSelected && lift ? -12 : 0;
  const scale = props.isSelected && lift ? 1.05 : 1;
  return { transform: `translateY(${y}px) rotate(${rotate}deg) scale(${scale})` };
});

// ── 毛毡光泽框（牌桌内由 GameBoard provide 开启，源自 battle.jsx 厚卡） ──
const felt = inject(CARD_FELT, false);
// 卡色（数据形 炎/风/水/暗/地/光）→ battle.jsx CC 十六进制
const FELT_CC: Record<string, string> = {
  炎: "#e0463d", 风: "#3fb061", 水: "#3f86e6", 暗: "#9a5fe0", 地: "#8a7f96", 光: "#ecc24a",
};
function hexA(hex: string, a: number): string {
  const h = hex.replace("#", "");
  const r = parseInt(h.slice(0, 2), 16);
  const g = parseInt(h.slice(2, 4), 16);
  const b = parseInt(h.slice(4, 6), 16);
  return `rgba(${r},${g},${b},${a})`;
}
const cardColor = computed(() => {
  const first = (props.card?.color ?? "").split("/")[0].trim();
  return FELT_CC[first] ?? "";
});
// 根样式：变换 + （毛毡模式下）卡色边 + 立体底边 + 彩色辉光
// 领航卡更厚：3px 边、更深底边、更大辉光（源自 battle.jsx Leader）
const rootStyle = computed<Record<string, string>>(() => {
  const base: Record<string, string> = { ...transformStyle.value };
  if (felt && !showFaceDown.value && cardColor.value) {
    const c = cardColor.value;
    if (!props.isSelected) base.borderColor = c;
    // 去掉常驻大半径彩色辉光（0 0 40px / 0 0 20px）——这是最贵的每帧重栅格化项；
    // 保留 inset 高光 + 3D 底边 + 收敛投影，立体感不变。
    if (props.card?.type === "Leader") {
      base.borderWidth = "3px";
      base.boxShadow = `inset 0 1px 0 rgba(255,255,255,.3), 0 4px 0 ${hexA(c, 0.6)}, 0 16px 30px -8px rgba(0,0,0,.85)`;
    } else {
      base.boxShadow = `inset 0 1px 0 rgba(255,255,255,.24), 0 3.5px 0 ${hexA(c, 0.5)}, 0 12px 22px -8px rgba(0,0,0,.82)`;
    }
  }
  return base;
});
</script>

<template>
  <div
    :class="clsx(
      sizes[size],
      'relative shrink-0 cursor-pointer overflow-hidden rounded-md border-2 shadow-md shadow-black/30',
      'transform-gpu backface-hidden transition-transform duration-200 ease-out',
      !isSelected && 'hover:scale-[1.03]',
      isSelected
        ? 'border-yellow-300 shadow-yellow-300/40'
        : 'border-slate-500/70 hover:border-slate-200',
    )"
    :style="rootStyle"
    @click="emit('click')"
    @contextmenu="handleContextMenu"
  >
    <div
      v-if="showFaceDown"
      class="flex h-full w-full items-center justify-center bg-gradient-to-br from-sky-950 via-blue-950 to-slate-950 ring-1 ring-inset ring-sky-300/20"
    >
      <span class="text-xs font-black tracking-normal text-sky-300">CARD</span>
    </div>
    <template v-else>
      <img
        :src="imgSrc"
        :alt="card!.name"
        class="absolute inset-0 h-full w-full object-cover"
        :draggable="false"
        loading="lazy"
        decoding="async"
        @error="imgSrc = FALLBACK"
      />
      <!-- 毛毡光泽高光（牌桌厚卡，源自 battle.jsx .bf-card::after） -->
      <div
        v-if="felt"
        class="pointer-events-none absolute inset-0 z-[2]"
        style="background: linear-gradient(157deg, rgba(255, 255, 255, 0.24), rgba(255, 255, 255, 0) 46%)"
      />
      <div
        class="absolute inset-x-0 bottom-0 flex justify-between gap-1 bg-gradient-to-t from-black/90 via-black/50 to-transparent px-1.5 pb-1 pt-6 text-xs font-bold"
      >
        <span
          v-if="!hideCost"
          :class="clsx('rounded px-1.5 text-xs ring-1 ring-white/15', costBuff < 0 ? 'bg-green-700 text-green-100' : costBuff > 0 ? 'bg-red-700 text-red-100' : 'bg-black/85 text-white')"
        >
          {{ (card!.cost + (costBuff ?? 0)) }}
        </span>
        <div class="flex items-center gap-1">
          <span
            v-if="attachedDonCount > 0"
            class="rounded bg-yellow-300 px-1 text-xs font-black leading-tight text-black"
          >
            DONx{{ attachedDonCount }}
          </span>
          <span
            v-if="!hidePower && (card!.type === 'Character' || card!.type === 'Leader')"
            :class="clsx('rounded bg-black/85 px-1.5 text-xs ring-1 ring-white/15', powerColor)"
          >
            {{ displayPower.toLocaleString() }}
          </span>
        </div>
      </div>
      <!-- 反击值徽标 -->
      <span
        v-if="card!.counter > 0 && !hideCounter"
        class="absolute bottom-1 right-1 z-10 rounded bg-amber-500/90 px-1 text-[10px] font-black leading-tight text-black shadow ring-1 ring-black/20"
      >反{{ card!.counter }}</span>
      <!-- 可攻击指示器 -->
      <div
        v-if="attackState === 'can'"
        class="pointer-events-none absolute inset-0 rounded-md ring-2 ring-inset ring-orange-400/80"
      />
      <span
        v-if="attackState === 'can'"
        class="pointer-events-none absolute left-0 top-1/2 z-30 flex -translate-y-1/2 items-center rounded-r bg-gradient-to-r from-emerald-400 to-green-600 px-0.5 py-1 shadow-[0_0_6px_rgba(16,185,129,0.95)] ring-1 ring-emerald-100/70 animate-pulse"
        title="可攻击"
      >
        <svg viewBox="0 0 24 24" class="h-3 w-3 text-white drop-shadow-[0_0_1px_rgba(0,0,0,0.6)]" fill="currentColor" aria-hidden>
          <path d="M12 4l6 7h-4v7h-4v-7H6z" />
        </svg>
      </span>
      <!-- 召唤眩晕标识 -->
      <span
        v-if="attackState === 'sick'"
        class="pointer-events-none absolute left-0 top-1/2 z-30 flex -translate-y-1/2 items-center rounded-r bg-slate-800/85 px-1 py-0.5 ring-1 ring-slate-400/50"
        title="本回合登场，不可攻击"
      >
        <span class="text-[8px] font-black leading-none tracking-tight text-sky-300/90">Zzz</span>
      </span>
    </template>
  </div>
  <Teleport to="body">
    <CardZoomOverlay v-if="zoomOpen && card" :card="card" :sprite="imgSrc" @close="zoomOpen = false" />
  </Teleport>
</template>
