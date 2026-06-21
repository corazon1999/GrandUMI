<script setup lang="ts">
import Ticks from "./Ticks.vue";

/**
 * 侧边导航栏（CLAUDE.md §5.6）：
 * 84×full panel，头像 + 三项导航 + 底部连接状态。
 */
defineProps<{
  username: string;
  level?: number;
  active: "lobby" | "deck" | "history";
  connected: boolean;
}>();

defineEmits<{
  (e: "navigate", view: "lobby" | "deck" | "history"): void;
}>();

const NAV_ITEMS: { id: "lobby" | "deck" | "history"; glyph: string; lbl: string }[] = [
  { id: "lobby",   glyph: "厅", lbl: "大厅" },
  { id: "deck",    glyph: "组", lbl: "卡组" },
  { id: "history", glyph: "战", lbl: "战绩" },
];
</script>

<template>
  <aside class="sidebar">
    <Ticks />

    <div class="sidebar__avatar">
      <div class="sidebar__avatar-circle">
        <span>{{ level ?? "?" }}</span>
      </div>
      <span class="mono faint sidebar__username">{{ username || "guest" }}</span>
    </div>

    <span class="sidebar__divider" />

    <nav class="sidebar__nav">
      <button
        v-for="it in NAV_ITEMS"
        :key="it.id"
        :class="['nav-item', { 'is-active': active === it.id }]"
        :title="it.lbl"
        @click="$emit('navigate', it.id)"
      >
        <span class="glyph">{{ it.glyph }}</span>
        <span class="lbl">{{ it.lbl }}</span>
      </button>
    </nav>

    <div class="sidebar__status">
      <span class="dot" :class="connected ? 'dot--live' : 'dot--down'" />
      <span class="mono faint sidebar__status-text">{{ connected ? "已连接" : "未连接" }}</span>
    </div>
  </aside>
</template>

<style scoped>
.sidebar {
  position: absolute;
  left: 16px;
  top: 72px;
  bottom: 16px;
  width: 84px;
  z-index: 20;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 14px 0 12px;
  background: color-mix(in srgb, var(--surface) 82%, transparent);
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  backdrop-filter: blur(var(--panel-blur));
}

.sidebar__avatar {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 4px 0 10px;
  width: 100%;
}
.sidebar__avatar-circle {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  border: 2px solid var(--primary);
  background: var(--surface2);
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 18px;
  color: var(--primary);
  box-shadow: 0 0 18px -4px var(--primary-glow);
}
.sidebar__username {
  font-size: 10px;
  letter-spacing: 0.06em;
  text-align: center;
  max-width: 70px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.sidebar__divider {
  width: 40px;
  height: 1px;
  background: var(--line);
  margin: 4px 0 8px;
}

.sidebar__nav {
  flex: 1;
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.sidebar__status {
  padding-bottom: 4px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
}
.sidebar__status-text {
  font-size: 9px;
  writing-mode: vertical-rl;
  letter-spacing: 0.1em;
  color: var(--ink-faint);
}
</style>