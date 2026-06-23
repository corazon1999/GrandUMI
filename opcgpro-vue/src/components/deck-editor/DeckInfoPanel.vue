<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from "vue";
import { useStore } from "@/composables/useStore";
import { useDeckStore, FORMAT_RULES } from "@/store/deckStore";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import type { CardData } from "@/types/card";
import CardHoverPreview from "./CardHoverPreview.vue";
import type { HoverInfo } from "./CardHoverPreview";
import CostCurve from "./CostCurve.vue";
import DeckEntryRow from "./DeckEntryRow.vue";
import GoldDivider from "@/components/ui/GoldDivider.vue";

const HOVER_DELAY = 180;

const store   = () => useDeckStore.getState();
const format  = useStore(useDeckStore, (s) => s.format);
const leader  = useStore(useDeckStore, (s) => s.leader);
const entries = useStore(useDeckStore, (s) => s.entries);
const notice  = useStore(useDeckStore, (s) => s.notice);

const hover = ref<HoverInfo | null>(null);
let hoverTimer: ReturnType<typeof setTimeout> | null = null;
let noticeTimer: ReturnType<typeof setTimeout> | null = null;

const total    = computed(() => entries.value.reduce((sum, e) => sum + e.count, 0));
const mainSize = computed(() => FORMAT_RULES[format.value].mainSize);
const remaining = computed(() => mainSize.value - total.value);

const leaderColorText = computed(() =>
  leader.value
    ? COLOR_STYLES[primaryDisplayColor(leader.value.color)]?.text ?? "text-gray-400"
    : "text-gray-400",
);

watch(notice, (n) => {
  if (!n || n.type !== "info") return;
  if (noticeTimer) clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => store().clearNotice(), 3000);
});

watch(entries, () => {
  if (hover.value && !entries.value.some((e) => e.card.number === hover.value!.card.number)) {
    if (hoverTimer) clearTimeout(hoverTimer);
    hover.value = null;
  }
});

onUnmounted(() => {
  if (hoverTimer) clearTimeout(hoverTimer);
  if (noticeTimer) clearTimeout(noticeTimer);
});

function handleMouseEnter(card: CardData, rect: DOMRect, currentSprite: string) {
  if (hoverTimer) clearTimeout(hoverTimer);
  hoverTimer = setTimeout(() => (hover.value = { card, rect, currentSprite }), HOVER_DELAY);
}
function handleMouseLeave() {
  if (hoverTimer) clearTimeout(hoverTimer);
  hover.value = null;
}
function onLeaderImgError(e: Event) {
  (e.target as HTMLImageElement).src = "/sprites/CardBack.png";
}
</script>

<template>
  <div class="flex h-full flex-col">

    <!-- 通知 -->
    <div v-if="notice?.type === 'info'" class="mx-3 mt-3 rounded-md border border-sky-700/60 bg-sky-900/40 px-2 py-1.5 shadow-[inset_0_0_0_1px_rgba(56,189,248,0.2)]">
      <p class="text-center text-xs text-sky-300">{{ notice.message }}</p>
    </div>

    <div class="px-3 pb-3 pt-3">
      <!-- 领航卡 -->
      <div class="flex items-center gap-2 mb-3">
        <span class="gde-kicker shrink-0 text-[11px]">领航</span>
        <div v-if="leader" class="flex min-w-0 flex-1 items-center gap-2 rounded-[var(--radius)] border border-[var(--primary)]/40 bg-[var(--surface)]/50 p-1.5">
          <img
            :src="leader.sprite ?? '/sprites/CardBack.png'"
            :alt="leader.name"
            class="h-14 w-10 shrink-0 rounded border border-[var(--primary)]/60 object-cover"
            loading="lazy"
            @error="onLeaderImgError"
          />
          <div class="min-w-0">
            <p class="truncate text-xs font-medium text-[var(--ink)]">{{ leader.name }}</p>
            <p :class="['text-xs font-bold', leaderColorText]">
              {{ toDisplayColor(leader.color) }} · {{ leader.number }}
            </p>
          </div>
          <button class="ml-auto shrink-0 text-xs text-[var(--ink-faint)] hover:text-[var(--bad)]" @click="store().setLeader(null)">✕</button>
        </div>
        <div v-else class="flex h-14 flex-1 items-center justify-center rounded-[var(--radius)] border border-dashed border-[var(--line)] bg-[var(--surface)]/30">
          <p class="text-xs text-[var(--ink-faint)]">← 切换领航卡模式选择</p>
        </div>
      </div>
    </div>

    <GoldDivider spacing="tight" />

    <!-- 费用曲线 -->
    <div class="px-3 pb-2">
      <CostCurve :entries="entries" />
    </div>

    <!-- 张数进度条 -->
    <div class="flex items-center gap-2 px-3 pb-2">
      <div class="h-1.5 flex-1 overflow-hidden rounded-full border border-[var(--line)] bg-[var(--surface)] shadow-[inset_0_1px_0_rgba(0,0,0,0.4)]">
        <div
          :class="['h-full rounded-full transition-all duration-300',
            total === mainSize ? 'bg-gradient-to-r from-emerald-600 to-emerald-400'
            : total > mainSize ? 'bg-gradient-to-r from-red-600 to-red-400'
            : 'bg-gradient-to-r from-[var(--primary-bright)] to-[var(--primary)]']"
          :style="{ width: Math.min(100, (total / mainSize) * 100) + '%' }"
        />
      </div>
      <span :class="['shrink-0 text-xs font-bold',
        total === mainSize ? 'text-emerald-400'
        : total > mainSize ? 'text-red-400'
        : 'text-[var(--primary)]']">
        {{ total }}/{{ mainSize }}
      </span>
      <span v-if="remaining > 0" class="shrink-0 text-xs text-[var(--ink-faint)]">差{{ remaining }}张</span>
    </div>

    <!-- 卡牌列表 -->
    <div class="flex min-h-0 flex-1 flex-col gap-0.5 overflow-y-auto px-3 pb-3">
      <p v-if="entries.length === 0" class="py-6 text-center text-xs text-[var(--ink-faint)]">从搜索结果点击卡牌添加</p>
      <DeckEntryRow
        v-for="e in entries"
        :key="e.card.number"
        :entry="e"
        @remove="store().removeCard($event)"
        @mouse-enter="handleMouseEnter"
        @mouse-leave="handleMouseLeave"
      />
    </div>

    <Teleport to="body">
      <CardHoverPreview v-if="hover" :info="hover" />
    </Teleport>
  </div>
</template>
