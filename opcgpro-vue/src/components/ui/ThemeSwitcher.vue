<script setup lang="ts">
import { ref, onMounted } from "vue";

/**
 * 主题切换器：固定右上角
 * - pirate（默认）：草帽 SVG
 * - marine：海军锚 SVG
 * 持久化：localStorage("grandumi-theme")
 * 全局读取入口：document.documentElement.dataset.theme
 */
type Theme = "pirate" | "marine";
const STORAGE_KEY = "grandumi-theme";
const DEFAULT_THEME: Theme = "pirate";

const current = ref<Theme>(DEFAULT_THEME);

onMounted(() => {
  const saved = (localStorage.getItem(STORAGE_KEY) as Theme) || DEFAULT_THEME;
  current.value = saved === "marine" ? "marine" : "pirate";
  document.documentElement.dataset.theme = current.value;
});

function toggle() {
  const next: Theme = current.value === "pirate" ? "marine" : "pirate";
  current.value = next;
  document.documentElement.dataset.theme = next;
  try {
    localStorage.setItem(STORAGE_KEY, next);
  } catch {}
}
</script>

<template>
  <button
    class="theme-switcher"
    :class="`theme-switcher--${current}`"
    :title="current === 'pirate' ? '切换到海军风' : '切换到海贼风'"
    :aria-label="`当前主题: ${current === 'pirate' ? '海贼' : '海军'}, 点击切换`"
    @click="toggle"
  >
    <span class="theme-switcher__icon theme-switcher__icon--pirate" :class="{ 'is-active': current === 'pirate' }">
      <!-- 草帽 SVG -->
      <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
        <ellipse cx="12" cy="16" rx="9" ry="2.5" fill="currentColor" opacity="0.85" />
        <path d="M5 14.5c0-3 3-6 7-6s7 3 7 6" fill="currentColor" />
        <rect x="5" y="13" width="14" height="1.5" fill="var(--color-secondary)" />
      </svg>
    </span>
    <span class="theme-switcher__divider" />
    <span class="theme-switcher__icon theme-switcher__icon--marine" :class="{ 'is-active': current === 'marine' }">
      <!-- 海军锚 SVG -->
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="12" cy="5" r="2" />
        <path d="M12 7v13" />
        <path d="M8 11h8" />
        <path d="M5 14c0 3 3 6 7 6s7-3 7-6" />
        <path d="M5 14c-1 0-2 0-2-1.5" />
        <path d="M19 14c1 0 2 0 2-1.5" />
      </svg>
    </span>
  </button>
</template>

<style scoped>
.theme-switcher {
  position: fixed;
  top: 1rem;
  right: 1rem;
  z-index: 100;
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.5rem;
  background: var(--color-bg-overlay);
  border: 1px solid var(--color-border);
  border-radius: 999px;
  backdrop-filter: blur(8px);
  cursor: pointer;
  transition: all 200ms var(--ease-maritime);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}
.theme-switcher:hover {
  border-color: var(--color-border-strong);
  box-shadow: var(--shadow-glow-sm);
  transform: translateY(-1px);
}

.theme-switcher__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  transition: all 300ms var(--ease-maritime);
  opacity: 0.45;
}
.theme-switcher__icon svg {
  width: 100%;
  height: 100%;
}
.theme-switcher__icon.is-active {
  opacity: 1;
  transform: scale(1.05);
}
.theme-switcher__icon--pirate {
  color: var(--color-primary); /* 草帽橙 */
}
.theme-switcher__icon--pirate.is-active {
  background: rgba(245, 166, 35, 0.15);
  box-shadow: 0 0 10px var(--color-primary-glow);
}
.theme-switcher__icon--marine {
  color: var(--color-primary); /* 海军风下也是古铜金 */
}
.theme-switcher__icon--marine.is-active {
  background: var(--color-primary-glow);
  box-shadow: 0 0 10px var(--color-primary);
}

.theme-switcher__divider {
  width: 1px;
  height: 18px;
  background: var(--color-divider);
}
</style>
