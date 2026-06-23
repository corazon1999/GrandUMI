<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from "vue";

const emit = defineEmits<{
  (e: "logo"): void;
}>();

type Theme = "pirate" | "navy";
type Mood = "a" | "b" | "c";

const STORAGE_THEME = "grandumi-theme";
const STORAGE_MOOD = "grandumi-mood";

const themeKey = ref<Theme>("pirate");
const moodKey = ref<Mood>("b");

const MOOD_LABEL: Record<Mood, string> = { a: "终端", b: "电影", c: "游戏" };

function readTheme(): Theme {
  const t = document.documentElement.dataset.theme;
  return t === "navy" || t === "marine" ? "navy" : "pirate";
}
function readMood(): Mood {
  const m = document.documentElement.dataset.mood;
  return m === "a" || m === "c" ? m : "b";
}

function applyTheme(t: Theme) {
  themeKey.value = t;
  document.documentElement.dataset.theme = t;
  try {
    localStorage.setItem(STORAGE_THEME, t);
  } catch {}
}
function applyMood(m: Mood) {
  moodKey.value = m;
  document.documentElement.dataset.mood = m;
  try {
    localStorage.setItem(STORAGE_MOOD, m);
  } catch {}
}

let obs: MutationObserver | null = null;
onMounted(() => {
  themeKey.value = readTheme();
  moodKey.value = readMood();
  obs = new MutationObserver(() => {
    themeKey.value = readTheme();
    moodKey.value = readMood();
  });
  obs.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ["data-theme", "data-mood"],
  });
});
onBeforeUnmount(() => obs?.disconnect());

const factionLabel = () => (themeKey.value === "pirate" ? "海贼" : "海军");
</script>

<template>
  <header class="top-bar">
    <!-- 左侧：系统状态 -->
    <div class="top-bar__left" @click="emit('logo')"></div>

    <!-- 右侧：气质 + 阵营 + 主题 -->
    <div class="top-bar__right">
      <!-- 主题切换：帽子/锚 -->
      <div class="tg" title="切换主题">
        <button
          :class="['tg__b', { 'is-active': themeKey === 'pirate' }]"
          title="海贼"
          @click="applyTheme('pirate')">
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.7"
            stroke-linecap="round"
            stroke-linejoin="round">
            <path d="M4 16c2 1.6 5 2.4 8 2.4s6-.8 8-2.4" />
            <path d="M7.5 16C7.5 10 9 6 12 6s4.5 4 4.5 10" />
            <path d="M6.5 15.4h11" />
          </svg>
        </button>
        <button
          :class="['tg__b', { 'is-active': themeKey === 'navy' }]"
          title="海军"
          @click="applyTheme('navy')">
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.7"
            stroke-linecap="round"
            stroke-linejoin="round">
            <circle cx="12" cy="5" r="2" />
            <line x1="12" y1="7" x2="12" y2="20" />
            <line x1="8" y1="11" x2="16" y2="11" />
            <path d="M5 14c0 4 3.5 6 7 6s7-2 7-6" />
          </svg>
        </button>
      </div>
    </div>
  </header>
</template>

<style scoped>
.top-bar {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 56px;
  z-index: 30;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 20px;
  pointer-events: none;
}
.top-bar__left,
.top-bar__right {
  display: flex;
  align-items: center;
  gap: 12px;
  pointer-events: auto;
}
.top-bar__left {
  cursor: pointer;
}
.top-bar__sys {
  font-size: 11px;
}
.top-bar__hint {
  font-size: 10px;
  letter-spacing: 0.12em;
}

/* ── 主题/气质切换按钮组 ── */
.tg {
  display: inline-flex;
  padding: 4px;
  gap: 2px;
  background: var(--bg1);
  border: 1px solid var(--line);
  border-radius: var(--radius-pill);
}
.tg__b {
  width: 38px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: transparent;
  color: var(--ink-faint);
  cursor: pointer;
  border-radius: var(--radius-pill);
  transition: all 0.25s;
}
.tg__b:hover {
  color: var(--ink-dim);
}
.tg__b.is-active {
  color: var(--on-primary);
  background: var(--primary);
}
.tg--mood .tg__b {
  width: 34px;
  font-family: var(--font-mono);
  font-size: 13px;
  font-weight: 700;
}
</style>
