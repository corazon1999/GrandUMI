<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useRouter } from "vue-router";
import { useStore } from "@/composables/useStore";
import { useDeckStore, FORMAT_RULES, type DeckFormat } from "@/store/deckStore";
import {
  saveDeck,
  loadAllDecks,
  loadDeck,
  deleteDeck,
  deckExists,
  nextDeckName,
  exportDeckString,
  importDeckString,
  type SavedDeck,
} from "@/data/DeckMapper";
import type { CardData } from "@/types/card";
import Ticks from "@/components/shared/Ticks.vue";

type SaveState = "idle" | "saved" | "error";

const router = useRouter();
const store = () => useDeckStore.getState();

const format = useStore(useDeckStore, (s) => s.format);
const leader = useStore(useDeckStore, (s) => s.leader);
const entries = useStore(useDeckStore, (s) => s.entries);
const deckName = useStore(useDeckStore, (s) => s.deckName);
const loadedName = useStore(useDeckStore, (s) => s.loadedName);

const saveState = ref<SaveState>("idle");
const showLoad = ref(false);
const showExport = ref(false);
const showImport = ref(false);
const exportText = ref("");
const importText = ref("");
const importMsg = ref<string | null>(null);
const copied = ref(false);
const savedDecks = ref<Record<string, SavedDeck>>({});
const deleteTarget = ref<string | null>(null);
const overwriteTarget = ref<string | null>(null);

const formatKeys = Object.keys(FORMAT_RULES) as DeckFormat[];
const formatLabel = (f: DeckFormat) =>
  f === "Unrestricted" ? "自由" : f === "OP15-Only" ? "OP15" : "OP16";

const total = computed(() =>
  entries.value.reduce((sum, e) => sum + e.count, 0),
);
const mainSize = computed(() => FORMAT_RULES[format.value].mainSize);
const isValid = computed(() => {
  void entries.value;
  void leader.value;
  void format.value;
  return store().isValid();
});
const savedEntries = computed(() => Object.entries(savedDecks.value));

onMounted(() => {
  savedDecks.value = loadAllDecks();
  const isNew = new URLSearchParams(window.location.search).get("new") === "1";
  if (isNew) {
    store().setDeckName(nextDeckName());
    store().setLoadedName(null);
  } else {
    const sel = localStorage.getItem("grandumi_selected_deck");
    if (sel) {
      store().setDeckName(sel);
      store().setLoadedName(sel);
    }
  }
});

function handleNew() {
  store().clearDeck();
  store().clearNotice();
  store().setDeckName(nextDeckName());
  store().setLoadedName(null);
  showLoad.value = false;
  showExport.value = false;
  showImport.value = false;
}

function doSave(name: string) {
  try {
    const cards = entries.value.flatMap(
      (e) => Array(e.count).fill(e.card) as CardData[],
    );
    saveDeck(name, leader.value!, cards);
    savedDecks.value = loadAllDecks();
    store().setLoadedName(name);
    saveState.value = "saved";
    setTimeout(() => (saveState.value = "idle"), 2000);
  } catch {
    saveState.value = "error";
    setTimeout(() => (saveState.value = "idle"), 2000);
  }
}

function handleSave() {
  if (!isValid.value) return;
  if (
    deckExists(deckName.value) &&
    (loadedName.value === null || deckName.value !== loadedName.value)
  ) {
    overwriteTarget.value = deckName.value;
    return;
  }
  doSave(deckName.value);
}

function handleLoad(name: string) {
  const result = loadDeck(name);
  if (!result) return;
  const s = store();
  s.clearDeck();
  s.setLeader(result.leader);
  result.cards.forEach((c) => s.addCard(c));
  s.setDeckName(name);
  s.setLoadedName(name);
  showLoad.value = false;
}

function handleExport() {
  if (!leader.value) {
    exportText.value = "请先选择领航卡再导出";
  } else {
    const cards = entries.value.flatMap(
      (e) => Array(e.count).fill(e.card) as CardData[],
    );
    exportText.value = exportDeckString(leader.value, cards, deckName.value);
  }
  showExport.value = true;
  showImport.value = false;
  showLoad.value = false;
  copied.value = false;
}

async function handleCopy() {
  try {
    await navigator.clipboard.writeText(exportText.value);
    copied.value = true;
    setTimeout(() => (copied.value = false), 2000);
  } catch {
    copied.value = false;
  }
}

function handleImportApply() {
  const { leader: lead, cards, skipped } = importDeckString(importText.value);
  if (!lead && cards.length === 0) {
    importMsg.value = "没有识别到有效卡牌，请检查卡组码";
    return;
  }
  const s = store();
  s.clearDeck();
  if (lead) s.setLeader(lead);
  cards.forEach((c) => s.addCard(c));
  importMsg.value = `导入完成：${cards.length} 张${skipped > 0 ? `，跳过 ${skipped} 张无效卡号` : ""}${!lead ? "（未识别到领航）" : ""}`;
  setTimeout(() => {
    showImport.value = false;
    importText.value = "";
    importMsg.value = null;
  }, 1800);
}

function handleDeleteDeck(name: string) {
  deleteDeck(name);
  savedDecks.value = loadAllDecks();
}

function onLeaderImgError(e: Event) {
  (e.target as HTMLImageElement).src = "/sprites/CardBack.png";
}

function closeDropdowns() {
  showLoad.value = false;
  showExport.value = false;
  showImport.value = false;
}
</script>

<template>
  <!-- ── 主工具栏 ─────────────────────────────────────────────── -->
  <div class="de-topbar">
    <Ticks />

    <!-- 左：返回 + 页面标题 -->
    <div class="de-topbar__left">
      <button
        class="de-btn de-btn--ghost de-btn--back"
        @click="router.push('/home')">
        ← 返回
      </button>
      <div class="de-topbar__title-group">
        <span class="de-topbar__kicker">卡组编辑器</span>
      </div>
    </div>

    <!-- 中：卡组名称 + 格式 -->
    <div class="de-topbar__center">
      <div class="de-field">
        <input
          :value="deckName"
          class="de-field__input"
          placeholder="卡组名称"
          @input="
            store().setDeckName(($event.target as HTMLInputElement).value)
          " />
      </div>
      <div class="de-seg">
        <button
          v-for="f in formatKeys"
          :key="f"
          :class="['de-seg__opt', { 'is-active': format === f }]"
          :title="FORMAT_RULES[f].label"
          @click="store().setFormat(f)">
          {{ formatLabel(f) }}
        </button>
      </div>
    </div>

    <!-- 右：操作 + 保存 -->
    <div class="de-topbar__right">
      <div class="de-action-group">
        <button class="de-btn de-btn--ghost" @click="handleNew">新建</button>
        <button
          :class="['de-btn de-btn--ghost', { 'is-active': showLoad }]"
          @click="
            showLoad = !showLoad;
            showExport = false;
            showImport = false;
          ">
          读取
        </button>
        <button class="de-btn de-btn--ghost" @click="store().clearDeck()">
          清空
        </button>
        <button
          :class="['de-btn de-btn--ghost', { 'is-active': showExport }]"
          @click="handleExport">
          导出
        </button>
        <button
          :class="['de-btn de-btn--ghost', { 'is-active': showImport }]"
          @click="
            showImport = !showImport;
            showExport = false;
            showLoad = false;
            importMsg = null;
          ">
          导入
        </button>
      </div>

      <div class="de-divider" />

      <!-- 保存按钮 -->
      <button
        v-if="saveState === 'saved'"
        class="de-btn de-btn--success"
        disabled>
        ✓ 已保存
      </button>
      <button
        v-else-if="saveState === 'error'"
        class="de-btn de-btn--danger"
        disabled>
        保存失败
      </button>
      <button
        v-else
        :class="['de-btn de-btn--primary', { 'is-disabled': !isValid }]"
        :disabled="!isValid"
        :title="
          !isValid
            ? !leader
              ? '请选择领航卡'
              : total < mainSize
                ? `还差${mainSize - total}张`
                : `超出${total - mainSize}张`
            : ''
        "
        @click="handleSave">
        保存卡组
      </button>
    </div>
  </div>

  <!-- ── 读取面板 ─────────────────────────────────────────────── -->
  <div v-if="showLoad" class="de-dropdown">
    <p v-if="savedEntries.length === 0" class="de-dropdown__empty">
      暂无保存的卡组
    </p>
    <div v-for="[name, deck] in savedEntries" :key="name" class="de-load-row">
      <img
        :src="deck.leaderSprite || '/sprites/CardBack.png'"
        :alt="deck.leaderName"
        class="de-load-row__img"
        loading="lazy"
        @error="onLeaderImgError" />
      <div class="de-load-row__info">
        <p class="de-load-row__name">{{ name }}</p>
        <p class="de-load-row__leader">{{ deck.leaderName }}</p>
        <div class="de-load-row__counts">
          <span class="de-count de-count--char">角{{ deck.charCount }}</span>
          <span class="de-count de-count--event">事{{ deck.eventCount }}</span>
          <span class="de-count de-count--stage">场{{ deck.stageCount }}</span>
        </div>
      </div>
      <div class="de-load-row__actions">
        <button
          class="de-btn de-btn--sm de-btn--primary"
          @click="handleLoad(name)">
          载入
        </button>
        <button
          class="de-btn de-btn--sm de-btn--danger-ghost"
          @click="deleteTarget = name">
          删除
        </button>
      </div>
    </div>
  </div>

  <!-- ── 导出面板 ─────────────────────────────────────────────── -->
  <div v-if="showExport" class="de-dropdown de-dropdown--compact">
    <textarea
      :value="exportText"
      readonly
      class="de-textarea"
      @click="($event.target as HTMLTextAreaElement).select()" />
    <div class="de-dropdown__footer">
      <button class="de-btn de-btn--primary de-btn--flex" @click="handleCopy">
        {{ copied ? "✓ 已复制" : "复制到剪贴板" }}
      </button>
      <button class="de-btn de-btn--ghost" @click="showExport = false">
        关闭
      </button>
    </div>
  </div>

  <!-- ── 导入面板 ─────────────────────────────────────────────── -->
  <div v-if="showImport" class="de-dropdown de-dropdown--compact">
    <textarea
      v-model="importText"
      placeholder="粘贴卡组码…（领航行 + 每行「数量 卡号」）"
      class="de-textarea" />
    <p v-if="importMsg" class="de-dropdown__msg">{{ importMsg }}</p>
    <div class="de-dropdown__footer">
      <button
        :disabled="!importText.trim()"
        :class="[
          'de-btn de-btn--primary de-btn--flex',
          { 'is-disabled': !importText.trim() },
        ]"
        @click="handleImportApply">
        导入
      </button>
      <button
        class="de-btn de-btn--ghost"
        @click="
          showImport = false;
          importText = '';
          importMsg = null;
        ">
        关闭
      </button>
    </div>
  </div>

  <!-- ── 覆盖确认弹窗 ─────────────────────────────────────────── -->
  <Teleport to="body">
    <div
      v-if="overwriteTarget"
      class="de-modal-backdrop"
      @click.self="overwriteTarget = null">
      <div class="de-modal">
        <span class="de-modal__corner de-modal__corner--tl" />
        <span class="de-modal__corner de-modal__corner--tr" />
        <span class="de-modal__corner de-modal__corner--bl" />
        <span class="de-modal__corner de-modal__corner--br" />
        <p class="de-modal__kicker">同名卡组已存在</p>
        <p class="de-modal__name">「{{ overwriteTarget }}」</p>
        <p class="de-modal__hint">继续保存将覆盖，原内容不可恢复</p>
        <div class="de-modal__actions">
          <button
            class="de-btn de-btn--ghost de-btn--md"
            @click="overwriteTarget = null">
            取消
          </button>
          <button
            class="de-btn de-btn--warn de-btn--md"
            @click="
              doSave(overwriteTarget!);
              overwriteTarget = null;
            ">
            覆盖保存
          </button>
        </div>
      </div>
    </div>

    <div
      v-if="deleteTarget"
      class="de-modal-backdrop"
      @click.self="deleteTarget = null">
      <div class="de-modal de-modal--danger">
        <span class="de-modal__corner de-modal__corner--tl" />
        <span class="de-modal__corner de-modal__corner--tr" />
        <span class="de-modal__corner de-modal__corner--bl" />
        <span class="de-modal__corner de-modal__corner--br" />
        <p class="de-modal__kicker">确认删除卡组</p>
        <p class="de-modal__name">「{{ deleteTarget }}」</p>
        <p class="de-modal__hint">此操作不可撤销</p>
        <div class="de-modal__actions">
          <button
            class="de-btn de-btn--ghost de-btn--md"
            @click="deleteTarget = null">
            取消
          </button>
          <button
            class="de-btn de-btn--danger de-btn--md"
            @click="
              handleDeleteDeck(deleteTarget!);
              deleteTarget = null;
            ">
            确认删除
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* ── 主工具栏 ─────────────────────────────────────────────────── */
.de-topbar {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 0 8px;
  margin: 0 16px;
  height: 52px;
  background: color-mix(in srgb, var(--surface) 82%, transparent);
  border-bottom: 1px solid var(--line);
  backdrop-filter: blur(var(--panel-blur));
  flex-shrink: 0;
  z-index: 10;
}

.de-topbar__left {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}
.de-topbar__title-group {
  display: flex;
  flex-direction: column;
}
.de-topbar__kicker {
  font-family: var(--font-mono, monospace);
  font-size: 10px;
  letter-spacing: 0.22em;
  color: var(--ink-faint);
  text-transform: uppercase;
}

.de-topbar__center {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  max-width: 520px;
}

.de-field {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  height: 34px;
  border: 1px solid var(--line);
  border-radius: var(--radius);
  background: var(--bg1);
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}
.de-field:focus-within {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-glow);
}
.de-field__input {
  flex: 1;
  height: 100%;
  padding: 0 10px;
  background: transparent;
  border: none;
  outline: none;
  font-family: var(--font-ui, sans-serif);
  font-size: 13px;
  color: var(--ink);
}
.de-field__input::placeholder {
  color: var(--ink-faint);
}

.de-seg {
  display: flex;
  border: 1px solid var(--line);
  border-radius: var(--radius);
  overflow: hidden;
  flex-shrink: 0;
}
.de-seg__opt {
  padding: 0 12px;
  height: 34px;
  font-family: var(--font-mono, monospace);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.06em;
  color: var(--ink-dim);
  background: transparent;
  border: none;
  border-right: 1px solid var(--line);
  cursor: pointer;
  transition: all 0.18s;
}
.de-seg__opt:last-child {
  border-right: none;
}
.de-seg__opt:hover {
  color: var(--ink);
  background: var(--surface);
}
.de-seg__opt.is-active {
  color: var(--primary);
  background: color-mix(in srgb, var(--primary) 14%, transparent);
  box-shadow: inset 0 0 0 1px var(--primary);
}

.de-topbar__right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
  margin-left: auto;
}
.de-action-group {
  display: flex;
  align-items: center;
  gap: 2px;
}
.de-divider {
  width: 1px;
  height: 22px;
  background: var(--line);
}

/* ── 按钮系统 ─────────────────────────────────────────────────── */
.de-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0 12px;
  height: 34px;
  font-family: var(--font-ui, sans-serif);
  font-size: 12px;
  font-weight: 600;
  border-radius: var(--radius);
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.18s;
  white-space: nowrap;
}
.de-btn--back {
  color: var(--ink-dim);
  border-color: var(--line);
  background: transparent;
  letter-spacing: 0.04em;
  padding: 0 20px;
}
.de-btn--back:hover {
  color: var(--ink);
  border-color: var(--line-strong);
  background: var(--surface);
}
.de-btn--ghost {
  color: var(--ink-dim);
  background: transparent;
  border-color: transparent;
}
.de-btn--ghost:hover {
  color: var(--ink);
  background: var(--surface);
  border-color: var(--line);
}
.de-btn--ghost.is-active {
  color: var(--primary);
  background: color-mix(in srgb, var(--primary) 12%, transparent);
  border-color: color-mix(in srgb, var(--primary) 30%, transparent);
}
.de-btn--primary {
  color: var(--on-primary);
  background: linear-gradient(160deg, var(--primary-bright), var(--primary));
  border-color: var(--primary);
  box-shadow: 0 4px 14px -4px var(--primary-glow);
}
.de-btn--primary:hover:not(:disabled):not(.is-disabled) {
  box-shadow: 0 6px 20px -4px var(--primary-glow);
  filter: brightness(1.08);
}
.de-btn--success {
  color: #fff;
  background: linear-gradient(160deg, #4ade80, #16a34a);
  border-color: #4ade80;
}
.de-btn--danger {
  color: #fff;
  background: linear-gradient(160deg, #f87171, #dc2626);
  border-color: #f87171;
}
.de-btn--danger-ghost {
  color: var(--bad);
  background: transparent;
  border-color: color-mix(in srgb, var(--bad) 30%, transparent);
}
.de-btn--danger-ghost:hover {
  background: color-mix(in srgb, var(--bad) 12%, transparent);
  border-color: var(--bad);
}
.de-btn--warn {
  color: #fff;
  background: linear-gradient(160deg, #fb923c, #ea580c);
  border-color: #fb923c;
}
.de-btn--warn:hover {
  filter: brightness(1.08);
}
.de-btn--sm {
  height: 28px;
  padding: 0 10px;
  font-size: 11px;
}
.de-btn--md {
  height: 38px;
  padding: 0 20px;
  font-size: 13px;
}
.de-btn--flex {
  flex: 1;
}
.de-btn:disabled,
.de-btn.is-disabled {
  opacity: 0.35;
  cursor: not-allowed;
  box-shadow: none;
  filter: none;
}

/* ── 下拉面板 ─────────────────────────────────────────────────── */
.de-dropdown {
  border-bottom: 1px solid var(--line);
  background: color-mix(in srgb, var(--surface) 80%, transparent);
  backdrop-filter: blur(var(--panel-blur));
  max-height: 260px;
  overflow-y: auto;
  animation: slideDown 0.18s ease;
  flex-shrink: 0;
}
.de-dropdown--compact {
  padding: 12px 16px;
  max-height: none;
  overflow: visible;
}
@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateY(-6px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
.de-dropdown__empty {
  padding: 20px;
  text-align: center;
  font-size: 12px;
  color: var(--ink-faint);
  margin: 0;
}
.de-dropdown__footer {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}
.de-dropdown__msg {
  margin: 6px 0 0;
  font-size: 11px;
  color: var(--good);
}

/* ── 读取卡组列表行 ───────────────────────────────────────────── */
.de-load-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 16px;
  border-bottom: 1px solid var(--line);
  transition: background 0.15s;
}
.de-load-row:last-child {
  border-bottom: none;
}
.de-load-row:hover {
  background: var(--surface);
}
.de-load-row__img {
  width: 32px;
  height: 45px;
  border-radius: 3px;
  border: 1px solid color-mix(in srgb, var(--primary) 40%, transparent);
  object-fit: cover;
  flex-shrink: 0;
}
.de-load-row__info {
  flex: 1;
  min-width: 0;
}
.de-load-row__name {
  font-size: 12px;
  font-weight: 600;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin: 0;
}
.de-load-row__leader {
  font-size: 11px;
  color: var(--ink-faint);
  margin: 1px 0 3px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.de-load-row__counts {
  display: flex;
  gap: 8px;
}
.de-count {
  font-family: var(--font-mono, monospace);
  font-size: 10px;
}
.de-count--char {
  color: var(--primary);
}
.de-count--event {
  color: #4ade80;
}
.de-count--stage {
  color: #a78bfa;
}
.de-load-row__actions {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
}

/* ── 文本区域 ─────────────────────────────────────────────────── */
.de-textarea {
  width: 100%;
  height: 110px;
  resize: none;
  border-radius: var(--radius);
  border: 1px solid var(--line);
  background: var(--bg1);
  padding: 8px 10px;
  font-family: var(--font-mono, monospace);
  font-size: 11px;
  color: var(--ink);
  outline: none;
  box-sizing: border-box;
}
.de-textarea:focus {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-glow);
}
.de-textarea::placeholder {
  color: var(--ink-faint);
}

/* ── 弹窗 ─────────────────────────────────────────────────────── */
.de-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.7);
  backdrop-filter: blur(8px);
  animation: fadeIn 0.2s;
}
@keyframes fadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}
.de-modal {
  position: relative;
  width: 300px;
  padding: 28px 24px 24px;
  border-radius: var(--radius-lg);
  background: var(--surface);
  border: 1px solid var(--line-strong);
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.8);
  text-align: center;
  animation: scaleIn 0.25s ease;
}
@keyframes scaleIn {
  from {
    opacity: 0;
    transform: scale(0.92) translateY(8px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}
.de-modal--danger {
  border-color: color-mix(in srgb, var(--bad) 50%, transparent);
}
.de-modal__corner {
  position: absolute;
  width: 14px;
  height: 14px;
}
.de-modal__corner--tl {
  top: 0;
  left: 0;
  border-top: 2px solid var(--primary);
  border-left: 2px solid var(--primary);
}
.de-modal__corner--tr {
  top: 0;
  right: 0;
  border-top: 2px solid var(--primary);
  border-right: 2px solid var(--primary);
}
.de-modal__corner--bl {
  bottom: 0;
  left: 0;
  border-bottom: 2px solid var(--primary);
  border-left: 2px solid var(--primary);
}
.de-modal__corner--br {
  bottom: 0;
  right: 0;
  border-bottom: 2px solid var(--primary);
  border-right: 2px solid var(--primary);
}
.de-modal--danger .de-modal__corner {
  border-color: var(--bad);
}
.de-modal__kicker {
  font-family: var(--font-mono, monospace);
  font-size: 10px;
  letter-spacing: 0.24em;
  color: var(--ink-faint);
  text-transform: uppercase;
  margin: 0 0 8px;
}
.de-modal__name {
  font-size: 14px;
  font-weight: 700;
  color: var(--ink);
  margin: 0 0 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.de-modal__hint {
  font-size: 12px;
  color: var(--ink-faint);
  margin: 0 0 20px;
}
.de-modal__actions {
  display: flex;
  gap: 8px;
}
</style>
