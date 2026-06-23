<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useRouter } from "vue-router";
import { loadAllDecks, deleteDeck, type SavedDeck } from "@/data/DeckMapper";
import { useNetStore } from "@/store/netStore";
import EmblemDecoration from "@/components/ui/EmblemDecoration.vue";

const router = useRouter();

const SELECTED_KEY = "grandumi_selected_deck";

const emit = defineEmits<{ (e: "deckSelected"): void }>();

const decks = ref<Record<string, SavedDeck>>({});
const selected = ref("");
const deleteTarget = ref<string | null>(null);

const deckEntries = computed(() => Object.entries(decks.value));

function syncToGlobal(name: string) {
  const allDecks = loadAllDecks();
  const saved = allDecks[name];
  if (!saved) return;
  const cardsStr = [saved.leader, ...saved.cards].join("\n");
  useNetStore.getState().setSelectedDeck({
    name,
    leader: saved.leader,
    leaderName: saved.leaderName,
    cards: cardsStr,
  });
}

onMounted(() => {
  decks.value = loadAllDecks();
  const saved = localStorage.getItem(SELECTED_KEY);
  if (saved) {
    selected.value = saved;
    syncToGlobal(saved);
  }
});

function handleSelect(name: string) {
  selected.value = name;
  localStorage.setItem(SELECTED_KEY, name);
  syncToGlobal(name);
  emit("deckSelected");
}

function handleEdit(name: string) {
  localStorage.setItem(SELECTED_KEY, name);
  syncToGlobal(name);
  router.push("/deck-editor");
}

function handleDeleteConfirm() {
  if (!deleteTarget.value) return;
  deleteDeck(deleteTarget.value);
  if (selected.value === deleteTarget.value) {
    selected.value = "";
    localStorage.removeItem(SELECTED_KEY);
    useNetStore.getState().setSelectedDeck(null);
  }
  decks.value = loadAllDecks();
  deleteTarget.value = null;
}

function onImgError(e: Event) {
  (e.target as HTMLImageElement).src = "/sprites/CardBack.png";
}
</script>

<template>
  <div class="deck-choose-root">
    <!-- 顶栏 -->
    <div class="dc-header">
      <div class="dc-header__left">
        <p class="dc-header__eyebrow">// Card Collection</p>
        <h2 class="dc-header__title">我的卡组</h2>
      </div>
      <div class="dc-header__actions">
        <RouterLink to="/deck-editor?new=1" custom v-slot="{ navigate }">
          <button class="maritime-btn maritime-btn--outline" @click="navigate">
            <span class="maritime-btn__shine" />
            <span class="dc-btn-prefix">+</span>
            <span>新建卡组</span>
          </button>
        </RouterLink>
      </div>
    </div>

    <!-- 卡组列表 / 空状态 -->
    <div class="dc-body">
      <!-- 空状态 -->
      <div v-if="deckEntries.length === 0" class="dc-empty">
        <EmblemDecoration class="dc-empty__emblem" :size="280" :opacity="0.1" variant="watermark" />

        <div class="dc-empty__core">
          <div class="dc-empty__hex">
            <span class="dc-empty__counter">0<span class="dc-empty__counter-sep">/</span>51</span>
          </div>
          <p class="dc-empty__eyebrow">// DECK REGISTRY EMPTY</p>
          <p class="dc-empty__hint">尚未建造任何卡组</p>
          <RouterLink to="/deck-editor?new=1" custom v-slot="{ navigate }">
            <button class="maritime-btn maritime-btn--primary" @click="navigate">
              <span class="maritime-btn__shine" />
              <span class="dc-btn-prefix">▶</span>
              <span>BUILD YOUR FIRST DECK</span>
            </button>
          </RouterLink>
        </div>
      </div>

      <!-- 有卡组时的列表 -->
      <div v-else class="dc-list">
        <div
          v-for="[name, deck] in deckEntries"
          :key="name"
          :class="['dc-item', { 'dc-item--selected': selected === name }]"
          @click="handleSelect(name)"
        >
          <span v-if="selected === name" class="dc-item__highlight" />
          <span v-if="selected === name" class="dc-item__bar" />

          <div class="dc-item__body">
            <!-- 领航卡大图 + 名牌 -->
            <div class="dc-item__portrait">
              <span v-if="selected === name" class="dc-item__portrait-glow" />
              <img
                :src="deck.leaderSprite || '/sprites/CardBack.png'"
                :alt="deck.leaderName"
                class="dc-item__portrait-img"
                loading="lazy"
                @error="onImgError"
              />
              <div class="dc-item__portrait-cap">{{ deck.leaderName }}</div>
            </div>

            <!-- 主信息列 -->
            <div class="dc-item__info">
              <p class="dc-item__name">{{ name }}</p>
              <p class="dc-item__sub">领航 · {{ deck.leader }}</p>
              <div class="dc-item__tags">
                <span class="dc-tag dc-tag--char">角色<em>{{ deck.charCount }}</em></span>
                <span class="dc-tag dc-tag--event">事件<em>{{ deck.eventCount }}</em></span>
                <span class="dc-tag dc-tag--stage">场地<em>{{ deck.stageCount }}</em></span>
              </div>
            </div>

            <!-- 右侧：总张数 + 状态 + 操作 -->
            <div class="dc-item__aside">
              <div class="dc-item__count">
                <span class="dc-item__count-val">{{ deck.charCount + deck.eventCount + deck.stageCount + 1 }}</span>
                <span class="dc-item__count-max">/51 张</span>
              </div>
              <div v-if="selected === name" class="dc-item__active-badge">
                <span class="dc-item__active-dot" />
                使用中
              </div>
              <div class="dc-item__actions">
                <button class="dc-item__edit" @click.stop="handleEdit(name)">编辑</button>
                <button class="dc-item__delete" @click.stop="deleteTarget = name">删除</button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 删除确认弹窗 -->
    <Teleport to="body">
      <div v-if="deleteTarget" class="dc-modal-backdrop" @click.self="deleteTarget = null">
        <div class="dc-modal">
          <span class="dc-modal__corner dc-modal__corner--tl" />
          <span class="dc-modal__corner dc-modal__corner--tr" />
          <span class="dc-modal__corner dc-modal__corner--bl" />
          <span class="dc-modal__corner dc-modal__corner--br" />

          <div class="dc-modal__header">
            <p class="dc-modal__eyebrow">// CONFIRM DECK PURGE</p>
            <p class="dc-modal__title">删除这副卡组？</p>
            <p class="dc-modal__target">「{{ deleteTarget }}」</p>
          </div>
          <div class="dc-modal__body">
            <p class="dc-modal__warning">⚠ 此操作不可撤销</p>
            <div class="dc-modal__actions">
              <button class="maritime-btn maritime-btn--ghost" @click="deleteTarget = null">
                <span class="maritime-btn__shine" />
                <span>取消</span>
              </button>
              <button class="maritime-btn maritime-btn--danger" @click="handleDeleteConfirm">
                <span class="maritime-btn__shine" />
                <span>确认删除</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* ═══ 根容器 ═══════════════════════════════════════════════════════ */
.deck-choose-root {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
  background: transparent;
  font-family: var(--font-body);
  color: var(--color-text-primary);
}

/* ═══ 顶栏 ═════════════════════════════════════════════════════════ */
.dc-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid var(--color-border);
  background: linear-gradient(180deg, var(--color-bg-void) 0%, transparent 100%);
  flex-shrink: 0;
}
.dc-header__left { display: flex; flex-direction: column; gap: 0.15rem; }
.dc-header__eyebrow {
  font-family: var(--font-mono);
  font-size: 0.65rem;
  letter-spacing: 0.3em;
  color: var(--color-text-muted);
  text-transform: uppercase;
}
.dc-header__title {
  font-family: var(--font-display);
  font-size: 1.4rem;
  font-weight: 900;
  letter-spacing: 0.15em;
  color: var(--color-text-primary);
  text-transform: uppercase;
  margin: 0;
  text-shadow: 0 0 8px var(--color-primary-glow);
}
.dc-header__actions { display: flex; gap: 0.6rem; }

.dc-btn-prefix {
  display: inline-block;
  margin-right: 0.4rem;
  font-size: 0.85em;
  opacity: 0.85;
}
.dc-btn--secondary {
  color: var(--color-secondary) !important;
  border-color: var(--color-secondary) !important;
  box-shadow:
    0 0 0 1px var(--color-secondary),
    0 0 8px var(--color-secondary-glow),
    inset 0 1px 0 var(--color-secondary-glow) !important;
}

/* ═══ 主体 ═════════════════════════════════════════════════════════ */
.dc-body {
  flex: 1;
  overflow: hidden;
  position: relative;
}

/* ═══ 空状态 ═══════════════════════════════════════════════════════ */
.dc-empty {
  position: relative;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}
.dc-empty__emblem {
  position: absolute;
  inset: 0;
  z-index: 0;
  pointer-events: none;
}
.dc-empty__core {
  position: relative;
  z-index: 2;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1.25rem;
  padding: 2rem;
  animation: page-in 0.6s var(--ease-maritime);
}
@keyframes page-in {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
}

.dc-empty__hex {
  position: relative;
  width: 160px;
  height: 160px;
  display: flex;
  align-items: center;
  justify-content: center;
  clip-path: polygon(50% 0, 100% 25%, 100% 75%, 50% 100%, 0 75%, 0 25%);
  background: linear-gradient(135deg, var(--color-primary-glow) 0%, var(--color-secondary-glow) 100%);
  box-shadow:
    inset 0 0 40px var(--color-primary-glow),
    0 0 40px var(--color-primary-glow);
}
.dc-empty__hex::before {
  content: "";
  position: absolute;
  inset: 4px;
  clip-path: polygon(50% 0, 100% 25%, 100% 75%, 50% 100%, 0 75%, 0 25%);
  background: var(--color-bg-void);
}
.dc-empty__counter {
  position: relative;
  font-family: var(--font-mono);
  font-size: 2.5rem;
  font-weight: 900;
  color: var(--color-primary);
  text-shadow:
    0 0 12px var(--color-primary-glow),
    0 0 24px var(--color-primary-glow);
  letter-spacing: 0.05em;
  z-index: 1;
}
.dc-empty__counter-sep {
  color: var(--color-secondary);
  margin: 0 0.1em;
  text-shadow: 0 0 8px var(--color-secondary-glow);
}

.dc-empty__eyebrow {
  font-family: var(--font-mono);
  font-size: 0.75rem;
  letter-spacing: 0.4em;
  color: var(--color-primary);
  text-shadow: 0 0 6px var(--color-primary-glow);
  margin: 0;
}
.dc-empty__hint {
  font-size: 0.85rem;
  color: var(--color-text-muted);
  margin: 0;
}

/* ═══ 卡组列表 ═════════════════════════════════════════════════════ */
.dc-list {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding: 1rem 1.25rem;
  height: 100%;
  overflow-y: auto;
}

.dc-item {
  position: relative;
  overflow: hidden;
  border-radius: 0.75rem;
  border: 1px solid var(--color-border);
  background: var(--color-bg-deep);
  cursor: pointer;
  transition: all 200ms var(--ease-maritime);
}
.dc-item:hover {
  border-color: var(--color-border-strong);
  background: var(--color-bg-raised);
  box-shadow: 0 0 16px var(--color-primary-glow);
}
.dc-item--selected {
  border-color: var(--color-border-strong);
  background: linear-gradient(135deg, var(--color-primary-glow) 0%, color-mix(in srgb, var(--color-primary-glow) 30%, transparent) 100%);
  box-shadow:
    0 0 24px var(--color-primary-glow),
    var(--shadow-panel);
}

.dc-item__highlight {
  position: absolute;
  inset-x: 0; top: 0;
  height: 1px;
  background: linear-gradient(to right, transparent, var(--color-primary), transparent);
  box-shadow: 0 0 6px var(--color-primary-glow);
}
.dc-item__bar {
  position: absolute;
  left: 0; top: 0; bottom: 0;
  width: 3px;
  background: var(--color-secondary);
  box-shadow: 0 0 8px var(--color-secondary-glow), 0 0 16px var(--color-secondary);
}

/* ── 行体 ────────────────────────────────────────────────────── */
.dc-item__body {
  display: flex;
  align-items: stretch;
  gap: 1rem;
  padding: 0.75rem 1.25rem 0.75rem 0.75rem;
  min-height: 100px;
}

/* ── 领航卡大图 ───────────────────────────────────────────────── */
.dc-item__portrait {
  position: relative;
  flex-shrink: 0;
  width: 62px;
  border-radius: 0.4rem;
  overflow: hidden;
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.5);
  transition: transform 200ms var(--ease-maritime), box-shadow 200ms;
}
.dc-item:hover .dc-item__portrait {
  transform: translateY(-3px) scale(1.02);
  box-shadow: 0 10px 28px rgba(0, 0, 0, 0.65);
}
.dc-item--selected .dc-item__portrait {
  box-shadow: 0 0 0 2px var(--color-primary), 0 6px 24px var(--color-primary-glow);
}
.dc-item__portrait-glow {
  position: absolute;
  inset: 0;
  z-index: 2;
  border-radius: inherit;
  box-shadow: inset 0 0 16px var(--color-primary-glow);
  pointer-events: none;
}
.dc-item__portrait-img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: top center;
}
.dc-item__portrait-cap {
  position: absolute;
  bottom: 0;
  inset-x: 0;
  z-index: 3;
  padding: 0.2rem 0.25rem;
  background: linear-gradient(transparent, rgba(0, 0, 0, 0.82));
  font-family: var(--font-mono);
  font-size: 0.52rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  color: rgba(255, 255, 255, 0.88);
  text-align: center;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 主信息列 ─────────────────────────────────────────────────── */
.dc-item__info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.3rem;
}
.dc-item__name {
  font-size: 1rem;
  font-weight: 700;
  color: var(--color-text-primary);
  letter-spacing: 0.03em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin: 0;
  transition: color 200ms;
}
.dc-item--selected .dc-item__name {
  color: var(--color-primary);
  text-shadow: 0 0 12px var(--color-primary-glow);
}
.dc-item__sub {
  font-family: var(--font-mono);
  font-size: 0.68rem;
  color: var(--color-text-muted);
  letter-spacing: 0.06em;
  margin: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.dc-item__tags {
  display: flex;
  gap: 0.3rem;
  flex-wrap: wrap;
  margin-top: 0.15rem;
}
.dc-tag {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.15rem 0.5rem 0.15rem 0.4rem;
  font-family: var(--font-mono);
  font-size: 0.6rem;
  letter-spacing: 0.05em;
  border-radius: 0.2rem;
  border: 1px solid;
}
.dc-tag em {
  font-style: normal;
  font-weight: 900;
  font-size: 0.72rem;
}
.dc-tag--char {
  color: var(--color-primary);
  background: color-mix(in srgb, var(--color-primary) 10%, transparent);
  border-color: color-mix(in srgb, var(--color-primary) 30%, transparent);
}
.dc-tag--event {
  color: var(--color-secondary);
  background: color-mix(in srgb, var(--color-secondary) 10%, transparent);
  border-color: color-mix(in srgb, var(--color-secondary) 30%, transparent);
}
.dc-tag--stage {
  color: var(--color-accent);
  background: color-mix(in srgb, var(--color-accent) 10%, transparent);
  border-color: color-mix(in srgb, var(--color-accent) 30%, transparent);
}

/* ── 右侧：总量 + 状态 + 操作 ───────────────────────────────── */
.dc-item__aside {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  justify-content: space-between;
  flex-shrink: 0;
  min-width: 72px;
}
.dc-item__count {
  display: flex;
  align-items: baseline;
  gap: 0.15rem;
  line-height: 1;
}
.dc-item__count-val {
  font-family: var(--font-mono);
  font-size: 1.5rem;
  font-weight: 900;
  color: var(--color-text-primary);
  letter-spacing: -0.02em;
}
.dc-item__count-max {
  font-family: var(--font-mono);
  font-size: 0.62rem;
  color: var(--color-text-muted);
  letter-spacing: 0.04em;
}
.dc-item__active-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.15rem 0.5rem;
  font-family: var(--font-mono);
  font-size: 0.55rem;
  font-weight: 700;
  letter-spacing: 0.18em;
  color: var(--color-primary);
  background: color-mix(in srgb, var(--color-primary) 12%, transparent);
  border: 1px solid color-mix(in srgb, var(--color-primary) 35%, transparent);
  border-radius: 999px;
}
.dc-item__active-dot {
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: var(--color-primary);
  box-shadow: 0 0 6px var(--color-primary);
  animation: glow-pulse 1.6s ease-in-out infinite;
  flex-shrink: 0;
}
.dc-item__actions {
  display: flex;
  gap: 0.3rem;
  opacity: 0;
  transition: opacity 200ms var(--ease-maritime);
}
.dc-item:hover .dc-item__actions {
  opacity: 1;
}

.dc-item__edit,
.dc-item__delete {
  padding: 0.2rem 0.55rem;
  font-size: 0.65rem;
  font-family: var(--font-mono);
  background: transparent;
  border: 1px solid transparent;
  border-radius: 0.25rem;
  cursor: pointer;
  transition: all 160ms var(--ease-maritime);
}
.dc-item__edit {
  color: var(--color-primary);
  border-color: color-mix(in srgb, var(--color-primary) 35%, transparent);
}
.dc-item__edit:hover {
  background: color-mix(in srgb, var(--color-primary) 12%, transparent);
  border-color: var(--color-primary);
}
.dc-item__delete {
  color: var(--color-text-muted);
}
.dc-item__delete:hover {
  color: var(--color-secondary);
  background: color-mix(in srgb, var(--color-secondary) 12%, transparent);
  border-color: var(--color-secondary);
}

/* ═══ 删除确认弹窗 ═════════════════════════════════════════════════ */
.dc-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.7);
  backdrop-filter: blur(8px);
  animation: fade-in 0.2s;
}
@keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }

.dc-modal {
  position: relative;
  width: 22rem;
  padding: 1.75rem;
  border-radius: 1rem;
  background: var(--color-bg-overlay);
  border: 1px solid var(--color-secondary);
  box-shadow:
    0 0 40px var(--color-secondary-glow),
    0 24px 64px rgba(0, 0, 0, 0.8),
    var(--shadow-panel);
  animation: modal-in 0.3s var(--ease-maritime);
}
@keyframes modal-in {
  from { opacity: 0; transform: scale(0.92) translateY(8px); }
  to { opacity: 1; transform: scale(1) translateY(0); }
}

.dc-modal__corner {
  position: absolute;
  width: 16px;
  height: 16px;
}
.dc-modal__corner--tl { top: 0; left: 0; border-top: 2px solid var(--color-secondary); border-left: 2px solid var(--color-secondary); }
.dc-modal__corner--tr { top: 0; right: 0; border-top: 2px solid var(--color-secondary); border-right: 2px solid var(--color-secondary); }
.dc-modal__corner--bl { bottom: 0; left: 0; border-bottom: 2px solid var(--color-secondary); border-left: 2px solid var(--color-secondary); }
.dc-modal__corner--br { bottom: 0; right: 0; border-bottom: 2px solid var(--color-secondary); border-right: 2px solid var(--color-secondary); }

.dc-modal__header { text-align: center; margin-bottom: 1.25rem; }
.dc-modal__eyebrow {
  font-family: var(--font-mono);
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.3em;
  color: var(--color-secondary);
  text-shadow: 0 0 6px var(--color-secondary-glow);
  margin: 0 0 0.6rem;
}
.dc-modal__title {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--color-text-primary);
  margin: 0 0 0.3rem;
}
.dc-modal__target {
  font-size: 0.85rem;
  color: var(--color-text-muted);
  font-family: var(--font-mono);
  margin: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.dc-modal__body { display: flex; flex-direction: column; align-items: stretch; gap: 1rem; }
.dc-modal__warning {
  text-align: center;
  font-size: 0.75rem;
  color: var(--color-text-muted);
  margin: 0;
  letter-spacing: 0.1em;
}
.dc-modal__actions {
  display: flex;
  gap: 0.5rem;
}

/* ═══ 航海风按钮（局部） ═════════════════════════════════════════════ */
.maritime-btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  padding: 0.55rem 1.25rem;
  font-family: var(--font-mono);
  font-size: 0.8rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  border-radius: 0.4rem;
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 200ms var(--ease-maritime);
  overflow: hidden;
}
.maritime-btn__shine {
  position: absolute;
  inset-inline: 0.5rem;
  top: 0;
  height: 1px;
  background: linear-gradient(to right, transparent, rgba(255, 255, 255, 0.6), transparent);
  pointer-events: none;
}
.maritime-btn--primary {
  background: linear-gradient(135deg, var(--color-primary) 0%, var(--color-primary-dim) 100%);
  color: var(--color-bg-void);
  box-shadow:
    0 0 0 1px var(--color-border-strong),
    0 4px 16px var(--color-primary-glow),
    inset 0 1px 0 rgba(255, 255, 255, 0.3);
}
.maritime-btn--primary:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow:
    0 0 0 1px var(--color-primary),
    0 6px 24px var(--color-primary),
    inset 0 1px 0 rgba(255, 255, 255, 0.4);
}
.maritime-btn--primary:active:not(:disabled) { transform: scale(0.98); }

.maritime-btn--outline {
  background: transparent;
  color: var(--color-primary);
  border-color: var(--color-primary);
  box-shadow:
    0 0 0 1px var(--color-primary),
    0 0 8px var(--color-primary-glow),
    inset 0 1px 0 var(--color-primary-glow);
}
.maritime-btn--outline:hover:not(:disabled) {
  background: var(--color-primary-glow);
  box-shadow:
    0 0 12px var(--color-primary),
    0 0 24px var(--color-primary-glow);
  transform: translateY(-1px);
}
.maritime-btn--outline:active:not(:disabled) { transform: scale(0.98); }

.maritime-btn--ghost {
  background: transparent;
  color: var(--color-text-muted);
  border-color: var(--color-border);
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.05em;
}
.maritime-btn--ghost:hover:not(:disabled) {
  color: var(--color-text-primary);
  background: var(--color-bg-raised);
  border-color: var(--color-border-strong);
}

.maritime-btn--danger {
  background: linear-gradient(135deg, var(--color-secondary) 0%, var(--color-secondary-dim) 100%);
  color: #fff;
  box-shadow:
    0 0 0 1px var(--color-secondary),
    0 4px 16px var(--color-secondary-glow),
    inset 0 1px 0 rgba(255, 255, 255, 0.3);
}
.maritime-btn--danger:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow:
    0 0 0 1px var(--color-secondary),
    0 6px 24px var(--color-secondary),
    inset 0 1px 0 rgba(255, 255, 255, 0.4);
}
.maritime-btn--danger:active:not(:disabled) { transform: scale(0.98); }

.maritime-btn:disabled {
  cursor: not-allowed;
  opacity: 0.3;
  filter: grayscale(0.6);
  transform: none !important;
  box-shadow: none !important;
}
</style>
