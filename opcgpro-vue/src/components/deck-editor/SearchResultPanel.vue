<script setup lang="ts">
import { ref, computed, watch, onUnmounted, provide } from "vue";
import { getAllCachedCards } from "@/data/CardLoader";
import { useStore } from "@/composables/useStore";
import { useDeckStore, FORMAT_RULES } from "@/store/deckStore";
import type { CardData } from "@/types/card";
import CardInfoPanel from "@/components/game/CardInfoPanel.vue";
import CardHoverPreview from "./CardHoverPreview.vue";
import type { HoverInfo } from "./CardHoverPreview";
import CardGridItem from "./CardGridItem.vue";
import { CARD_IMG_IO } from "./cardImgIO";

const HOVER_DELAY = 180;

const format = useStore(useDeckStore, (s) => s.format);
const gridColumns = useStore(useDeckStore, (s) => s.gridColumns);
const searchQuery = useStore(useDeckStore, (s) => s.searchQuery);
const filterColor = useStore(useDeckStore, (s) => s.filterColor);
const filterType = useStore(useDeckStore, (s) => s.filterType);
const filterProperty = useStore(useDeckStore, (s) => s.filterProperty);
const filterRarity = useStore(useDeckStore, (s) => s.filterRarity);
const filterSets = useStore(useDeckStore, (s) => s.filterSets);
const filterShowSub1 = useStore(useDeckStore, (s) => s.filterShowSub1);
const notice = useStore(useDeckStore, (s) => s.notice);
// 触发依赖：entries 变化时需要重算 getCount 显示
const entries = useStore(useDeckStore, (s) => s.entries);

const store = () => useDeckStore.getState();

// ── 卡图懒加载：单个共享 IntersectionObserver（见 cardImgIO.ts） ──
// 在 setup 期同步创建（IO 无需 DOM），子组件 onMounted 时即可 observe。
const imgIoMap = new Map<Element, () => void>();
const sharedImgIO: IntersectionObserver | null =
  typeof IntersectionObserver !== "undefined"
    ? new IntersectionObserver(
        (entries) => {
          for (const e of entries) {
            if (!e.isIntersecting) continue;
            imgIoMap.get(e.target)?.();
            sharedImgIO!.unobserve(e.target);
            imgIoMap.delete(e.target);
          }
        },
        { rootMargin: "600px" },
      )
    : null;
provide(CARD_IMG_IO, {
  observe(el, cb) {
    if (sharedImgIO) { imgIoMap.set(el, cb); sharedImgIO.observe(el); }
    else cb();
  },
  unobserve(el) {
    if (sharedImgIO) { sharedImgIO.unobserve(el); imgIoMap.delete(el); }
  },
});

const modal = ref<CardData | null>(null);
const hover = ref<HoverInfo | null>(null);
let hoverTimer: ReturnType<typeof setTimeout> | null = null;
let noticeTimer: ReturnType<typeof setTimeout> | null = null;

const isLeaderMode = computed(() => filterType.value === "Leader");

watch(notice, (n) => {
  if (!n || n.type !== "error") return;
  if (noticeTimer) clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => store().clearNotice(), 2000);
});

const results = computed(() => {
  const all = getAllCachedCards();
  const rule = FORMAT_RULES[format.value];
  const whitelist = isLeaderMode.value ? rule.leaderSetWhitelist : rule.mainSetWhitelist;
  const q = searchQuery.value.toLowerCase();
  return all.filter((card) => {
    const setCode = card.number.split("-")[0];
    if (whitelist && !whitelist.includes(setCode)) return false;
    if (filterSets.value.length > 0 && !filterSets.value.includes(setCode)) return false;
    if (!filterShowSub1.value && card.subscript === 1) return false;
    if (isLeaderMode.value && card.type !== "Leader") return false;
    if (!isLeaderMode.value && card.type === "Leader") return false;
    if (filterColor.value && !card.color.includes(filterColor.value)) return false;
    if (filterProperty.value && card.property !== filterProperty.value) return false;
    if (filterRarity.value && card.rarity !== filterRarity.value) return false;
    if (!isLeaderMode.value && filterType.value && card.type !== filterType.value) return false;
    if (q) {
      if (
        !card.name.toLowerCase().includes(q) &&
        !card.number.toLowerCase().includes(q) &&
        !card.keyWords.some((k) => k.toLowerCase().includes(q)) &&
        !card.effectText.toLowerCase().includes(q)
      ) return false;
    }
    return true;
  });
});

function getCount(number: string): number {
  void entries.value;
  return store().getCount(number);
}

function handleCardClick(card: CardData) {
  isLeaderMode.value ? store().setLeader(card) : store().addCard(card);
}
function handleMouseEnter(card: CardData, rect: DOMRect, currentSprite: string) {
  if (hoverTimer) clearTimeout(hoverTimer);
  hoverTimer = setTimeout(() => (hover.value = { card, rect, currentSprite }), HOVER_DELAY);
}
function handleMouseLeave() {
  if (hoverTimer) clearTimeout(hoverTimer);
  hover.value = null;
}
function onSpriteChange(card: CardData, sprite: string) {
  card.sprite = sprite;
  if (hover.value) hover.value = { ...hover.value, currentSprite: sprite };
}

onUnmounted(() => {
  if (hoverTimer) clearTimeout(hoverTimer);
  if (noticeTimer) clearTimeout(noticeTimer);
  sharedImgIO?.disconnect();
  imgIoMap.clear();
});
</script>

<template>
  <div class="result-root">
    <div class="result-head">
      <p class="kicker result-head__title">
        {{ isLeaderMode ? "选择领航卡" : "搜索结果" }}
        <span class="result-head__sub">· 共 {{ results.length }} 张</span>
      </p>
      <span class="mono faint result-head__cols">{{ gridColumns }} 列</span>
    </div>

    <Transition name="notice">
      <div v-if="notice?.type === 'error'" class="result-notice">
        <p>{{ notice.message }}</p>
      </div>
    </Transition>

    <div class="result-grid-wrap">
      <div v-if="results.length === 0" class="result-empty">
        <p class="mono result-empty__text">// 没有找到匹配的卡牌</p>
      </div>
      <div v-else class="result-grid" :style="{ gridTemplateColumns: `repeat(${gridColumns}, minmax(0, 1fr))` }">
        <div
          v-for="card in results"
          :key="card.number"
          v-memo="[isLeaderMode, getCount(card.number)]"
        >
          <CardGridItem
            :card="card"
            :deck-count="isLeaderMode ? 0 : getCount(card.number)"
            :is-leader-mode="isLeaderMode"
            @click="handleCardClick(card)"
            @right-click="modal = card"
            @mouse-enter="handleMouseEnter"
            @mouse-leave="handleMouseLeave"
            @sprite-change="(s) => onSpriteChange(card, s)"
          />
        </div>
      </div>
    </div>

    <Teleport to="body">
      <CardHoverPreview v-if="hover" :info="hover" />
    </Teleport>
    <CardInfoPanel :card="modal" @close="modal = null" />
  </div>
</template>

<style scoped>
.result-root {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
  font-family: var(--font-ui);
  color: var(--ink);
}

/* ── 头部：结果数 · 列数 ── */
.result-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 18px;
  border-bottom: 1px solid var(--line);
  flex-shrink: 0;
}
.result-head__title {
  font-size: 11px;
}
.result-head__sub {
  margin-left: 8px;
  font-family: var(--font-ui);
  text-transform: none;
  letter-spacing: 0;
  color: var(--ink-faint);
  font-size: 11px;
}
.result-head__cols {
  font-size: 10px;
  letter-spacing: 0.14em;
}

/* ── 通知 ── */
.result-notice {
  padding: 8px 14px;
  border-bottom: 1px solid color-mix(in srgb, var(--bad) 30%, transparent);
  background: color-mix(in srgb, var(--bad) 10%, transparent);
  font-size: 12px;
  color: var(--bad);
  text-align: center;
}
.notice-enter-active,
.notice-leave-active { transition: all 0.15s ease; }
.notice-enter-from,
.notice-leave-to     { opacity: 0; transform: translateY(-6px); }

/* ── 网格区 ── */
.result-grid-wrap {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 14px;
}
.result-empty {
  height: 12rem;
  display: flex;
  align-items: center;
  justify-content: center;
}
.result-empty__text {
  font-size: 12px;
  letter-spacing: 0.2em;
  color: var(--ink-faint);
  text-transform: uppercase;
  margin: 0;
}
.result-grid {
  display: grid;
  gap: 12px;
}
/* 离屏卡牌跳过渲染（布局/绘制），大幅降低长列表初始绘制与滚动开销；
   contain-intrinsic-size 预留高度避免滚动条跳动（auto 会记住实测高度）。 */
.result-grid > * {
  content-visibility: auto;
  contain-intrinsic-size: auto 180px;
}
</style>