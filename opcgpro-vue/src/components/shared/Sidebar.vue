<script setup lang="ts">
import Ticks from "./Ticks.vue";
import Avatar from "./Avatar.vue";
import { useProfile } from "@/composables/useProfile";

/**
 * 侧边导航栏（CLAUDE.md §5.6）：
 * 84×full panel，头像（点击进个人中心）+ 三项导航 + 底部连接状态。
 */
type NavView = "home" | "lobby" | "deck" | "friends" | "rank" | "history" | "settings" | "profile";

defineProps<{
  username: string;
  level?: number;
  active: NavView;
  connected: boolean;
}>();

defineEmits<{
  (e: "navigate", view: NavView): void;
}>();

const { profile } = useProfile();

// 导航项（对齐 design components.jsx NAV，7 项）
const NAV_ITEMS: { id: Exclude<NavView, "profile">; glyph: string; lbl: string }[] = [
  { id: "home",     glyph: "首", lbl: "主页" },
  { id: "lobby",    glyph: "厅", lbl: "大厅" },
  { id: "deck",     glyph: "组", lbl: "卡组" },
  { id: "friends",  glyph: "友", lbl: "好友" },
  { id: "rank",     glyph: "榜", lbl: "排行" },
  { id: "history",  glyph: "战", lbl: "战绩" },
  { id: "settings", glyph: "设", lbl: "设置" },
];
</script>

<template>
  <aside class="sidebar">
    <Ticks />

    <button
      type="button"
      class="sidebar__avatar"
      :class="{ 'is-active': active === 'profile' }"
      title="个人中心"
      @click="$emit('navigate', 'profile')"
    >
      <span v-if="active === 'profile'" class="sidebar__avatar-mark" />
      <Avatar :src="profile.avatar" :name="profile.name" :size="46" :glow="active === 'profile'" />
      <span class="mono faint sidebar__username">{{ profile.name || username || "guest" }}</span>
    </button>

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
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 4px 0 10px;
  width: 100%;
  background: none;
  border: none;
  cursor: pointer;
}
.sidebar__avatar-mark {
  position: absolute;
  left: 0;
  top: 8px;
  bottom: 6px;
  width: 3px;
  background: var(--primary);
  border-radius: 0 3px 3px 0;
  box-shadow: 0 0 14px var(--primary-glow);
}
.sidebar__avatar.is-active .sidebar__username {
  color: var(--primary);
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
  overflow-y: auto;
  overflow-x: hidden;
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