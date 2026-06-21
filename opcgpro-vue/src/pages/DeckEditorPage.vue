<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { loadCardSet } from "@/data/CardLoader";
import { loadDeck } from "@/data/DeckMapper";
import { DEFAULT_SEARCH_SETS, ALL_SET_NAMES } from "@/data/cardSets";
import { useDeckStore } from "@/store/deckStore";
import SearchPanel from "@/components/deck-editor/SearchPanel.vue";
import SearchResultPanel from "@/components/deck-editor/SearchResultPanel.vue";
import DeckInfoPanel from "@/components/deck-editor/DeckInfoPanel.vue";
import DeckEditorTopBar from "@/components/deck-editor/DeckEditorTopBar.vue";

const GRID_COLS_KEY = "deckEditor_gridCols";

type LoadState = "loading" | "done" | "error";
const loadState = ref<LoadState>("loading");
const loaded = ref(0);
const total = DEFAULT_SEARCH_SETS.length;
const pct = computed(() => Math.round((loaded.value / total) * 100));

onMounted(async () => {
  const saved = parseInt(localStorage.getItem(GRID_COLS_KEY) ?? "", 10);
  if (!isNaN(saved)) useDeckStore.getState().setGridColumns(saved);

  try {
    for (const setName of DEFAULT_SEARCH_SETS) {
      await loadCardSet(setName);
      loaded.value++;
    }
    loadState.value = "done";

    const isNew = new URLSearchParams(window.location.search).get("new") === "1";
    if (isNew) {
      useDeckStore.getState().clearDeck();
    } else {
      const selectedDeck = localStorage.getItem("grandumi_selected_deck");
      if (selectedDeck) {
        const deck = loadDeck(selectedDeck);
        if (deck) {
          const store = useDeckStore.getState();
          store.clearDeck();
          store.setLeader(deck.leader);
          deck.cards.forEach((c) => store.addCard(c));
        }
      }
    }

    const remaining = ALL_SET_NAMES.filter((s) => !DEFAULT_SEARCH_SETS.includes(s));
    for (const setName of remaining) {
      await loadCardSet(setName).catch(() => {});
    }
  } catch {
    loadState.value = "error";
  }
});
</script>

<template>
  <div v-if="loadState === 'loading'" class="flex h-screen flex-col items-center justify-center gap-4 bg-[var(--bg0)]">
    <p class="text-lg font-bold text-[var(--ink)]">加载卡牌数据...</p>
    <div class="h-2 w-64 overflow-hidden rounded-full bg-[var(--surface)]">
      <div class="h-full rounded-full bg-gradient-to-r from-[var(--primary-bright)] to-[var(--primary)] transition-all duration-300" :style="{ width: pct + '%' }" />
    </div>
    <p class="text-sm text-[var(--ink-faint)]">{{ loaded }} / {{ total }} 个卡集</p>
  </div>

  <div v-else-if="loadState === 'error'" class="flex h-screen items-center justify-center bg-[var(--bg0)]">
    <p class="text-[var(--bad)]">卡牌数据加载失败，请刷新页面重试</p>
  </div>

  <!-- 顶栏 + 三栏编辑区 -->
  <div v-else class="deck-editor-root">
    <DeckEditorTopBar />
    <div class="deck-editor-body">
      <div class="panel deck-col deck-col--filter">
        <SearchPanel />
      </div>
      <div class="panel deck-col deck-col--grid">
        <SearchResultPanel />
      </div>
      <div class="panel deck-col deck-col--info">
        <DeckInfoPanel />
      </div>
    </div>
  </div>
</template>

<style scoped>
.deck-editor-root {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  padding-top: 56px; /* 跳过全局 TopBar */
  background: transparent;
  overflow: hidden;
}
.deck-editor-body {
  flex: 1;
  display: flex;
  gap: 14px;
  padding: 12px 16px 16px;
  min-height: 0;
  overflow: hidden;
}
.deck-col {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.deck-col--filter { width: 230px; flex-shrink: 0; }
.deck-col--grid   { flex: 1; min-width: 0; }
.deck-col--info   { width: 290px; flex-shrink: 0; }

@media (max-width: 1100px) {
  .deck-col--filter { width: 200px; }
  .deck-col--info   { width: 250px; }
}
</style>