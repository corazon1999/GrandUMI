<script setup lang="ts">
import { ref, onMounted } from "vue";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { loadAllDecks } from "@/data/DeckMapper";
import Sidebar from "@/components/shared/Sidebar.vue";
import Ticks from "@/components/shared/Ticks.vue";
import LobbyPanel from "./LobbyPanel.vue";
import DeckChoosePanel from "./DeckChoosePanel.vue";
import HistoryPanel from "./HistoryPanel.vue";
import ProfilePanel from "./ProfilePanel.vue";
import HomePanel from "./HomePanel.vue";
import FriendsPanel from "./FriendsPanel.vue";
import RankPanel from "./RankPanel.vue";
import SettingsPanel from "./SettingsPanel.vue";
import ChatPanel from "./ChatPanel.vue";
import PlayerListPanel from "./PlayerListPanel.vue";
import FriendlyRoomPanel from "./FriendlyRoomPanel.vue";
import InviteNotifyOverlay from "./InviteNotifyOverlay.vue";
import PlayerAvatar from "./PlayerAvatar.vue";
import NetStatePanel from "@/components/ui/NetStatePanel.vue";

const SELECTED_DECK_KEY = "grandumi_selected_deck";
const ONLINE_BADGE_POS_KEY = "grandumi_online_badge_pos";

type View = "home" | "lobby" | "deck" | "friends" | "rank" | "history" | "settings" | "profile";
const view = ref<View>("home");
const showPlayerList = ref(false);

const onlineCount = useStore(useNetStore, (s) => s.onlineCount);
const connState = useStore(useNetStore, (s) => s.connState);
const friendlyRoom = useStore(useNetStore, (s) => s.friendlyRoom);
const playerName = useStore(useNetStore, (s) => s.playerName);

const isConnected = () => connState.value === "connected";

onMounted(() => {
  const name = localStorage.getItem(SELECTED_DECK_KEY);
  if (!name) return;
  const saved = loadAllDecks()[name];
  if (!saved) return;
  useNetStore.getState().setSelectedDeck({
    name,
    leader: saved.leader,
    leaderName: saved.leaderName,
    cards: [saved.leader, ...saved.cards].join("\n"),
  });
});

// ── 在线人数徽标拖动 ──────────────────────────────────────────────────────
const badgePos = ref<{ x: number; y: number } | null>(null);
const badgeRef = ref<HTMLButtonElement | null>(null);
const drag = ref<{ startX: number; startY: number; baseX: number; baseY: number; moved: boolean } | null>(null);
const lastDragPos = ref<{ x: number; y: number } | null>(null);

onMounted(() => {
  try {
    const saved = localStorage.getItem(ONLINE_BADGE_POS_KEY);
    if (saved) badgePos.value = JSON.parse(saved);
  } catch {}
});

function onBadgePointerDown(e: PointerEvent) {
  const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
  drag.value = { startX: e.clientX, startY: e.clientY, baseX: rect.left, baseY: rect.top, moved: false };
  (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
}

function onBadgePointerMove(e: PointerEvent) {
  const d = drag.value;
  if (!d) return;
  const dx = e.clientX - d.startX;
  const dy = e.clientY - d.startY;
  if (!d.moved && Math.abs(dx) < 4 && Math.abs(dy) < 4) return;
  d.moved = true;
  const bw = (e.currentTarget as HTMLElement).offsetWidth;
  const bh = (e.currentTarget as HTMLElement).offsetHeight;
  const x = Math.min(Math.max(0, d.baseX + dx), window.innerWidth - bw);
  const y = Math.min(Math.max(0, d.baseY + dy), window.innerHeight - bh);
  badgePos.value = { x, y };
  lastDragPos.value = { x, y };
}

function onBadgePointerUp(e: PointerEvent) {
  const d = drag.value;
  drag.value = null;
  try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
  if (!d) return;
  if (!d.moved) {
    showPlayerList.value = true;
  } else if (lastDragPos.value) {
    try { localStorage.setItem(ONLINE_BADGE_POS_KEY, JSON.stringify(lastDragPos.value)); } catch {}
  }
}

function onNavigate(v: View) {
  view.value = v;
}

function goTo(v: View) {
  view.value = v;
}
</script>

<template>
  <template v-if="friendlyRoom">
    <FriendlyRoomPanel />
    <InviteNotifyOverlay />
  </template>

  <div v-else class="main-root">
    <!-- 顶部 TopBar 已由 App.vue 全局提供 -->

    <!-- 左侧 Sidebar -->
    <Sidebar
      :username="playerName || 'guest'"
      :active="view"
      :connected="isConnected()"
      @navigate="onNavigate"
    />

    <!-- 主内容区（左 116 + 右 0） -->
    <main class="main-content">
      <div class="main-content__inner">
        <!-- 在线浮标（可拖动） -->
        <button
          ref="badgeRef"
          type="button"
          title="查看在线玩家（可拖动）"
          :style="badgePos
            ? { position: 'fixed', left: `${badgePos.x}px`, top: `${badgePos.y}px` }
            : { position: 'fixed', right: '1.25rem', bottom: '4.5rem' }"
          class="online-badge"
          @pointerdown="onBadgePointerDown"
          @pointermove="onBadgePointerMove"
          @pointerup="onBadgePointerUp"
        >
          <span class="online-badge__dot" />
          <span class="online-badge__text">在线</span>
          <span class="online-badge__count">{{ onlineCount }}</span>
        </button>

        <Transition name="view-fade" mode="out-in">
          <!-- 主页 / 指挥室 -->
          <HomePanel v-if="view === 'home'" key="home" class="solo-panel enter" @navigate="goTo" />

          <!-- 大厅视图：双栏（中 Lobby + 右 Chat） -->
          <div v-else-if="view === 'lobby'" key="lobby" class="lobby-stack enter">
            <div class="lobby-stack__main">
              <LobbyPanel @go-to-deck="view = 'deck'" />
            </div>
            <aside class="lobby-stack__chat">
              <div class="panel lobby-chat-panel">
                <Ticks />
                <ChatPanel />
              </div>
            </aside>
          </div>

          <DeckChoosePanel
            v-else-if="view === 'deck'"
            key="deck"
            class="solo-panel enter"
            @deck-selected="view = 'lobby'"
          />
          <FriendsPanel v-else-if="view === 'friends'" key="friends" class="solo-panel enter" @navigate="goTo" />
          <RankPanel v-else-if="view === 'rank'" key="rank" class="solo-panel enter" />
          <HistoryPanel v-else-if="view === 'history'" key="history" class="solo-panel enter" />
          <SettingsPanel v-else-if="view === 'settings'" key="settings" class="solo-panel enter" />
          <ProfilePanel v-else-if="view === 'profile'" key="profile" class="solo-panel enter" />
        </Transition>
      </div>
    </main>

    <PlayerListPanel :open="showPlayerList" @close="showPlayerList = false" />
    <InviteNotifyOverlay />
  </div>
</template>

<style scoped>
.main-root {
  position: relative;
  display: block;
  height: 100vh;
  width: 100vw;
  overflow: hidden;
  background: transparent; /* 全局 AnimatedBackground 在 App.vue 根布局（z:0） */
  color: var(--ink);
  font-family: var(--font-ui);
}

/* ── 内容区：左 116(top:72 + 16 + width:84 + gap:16) 右 0 ── */
.main-content {
  position: absolute;
  top: 56px; /* 跳过 TopBar */
  left: 116px; /* 跳过 Sidebar(84 + left:16 + gap:16) */
  right: 0;
  bottom: 0;
  overflow: hidden;
}
.main-content__inner {
  position: absolute;
  inset: 0;
  display: flex;
}

/* ── 大厅双栏 ── */
.lobby-stack {
  flex: 1;
  display: flex;
  gap: 14px;
  padding: 14px 14px 14px 0;
  min-height: 0;
}
.lobby-stack__main {
  flex: 1;
  min-width: 0;
  overflow: auto;
  padding-top: 14px;
}
.lobby-stack__chat {
  width: 320px;
  flex-shrink: 0;
  padding: 14px 14px 14px 0;
}
.lobby-chat-panel {
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow: hidden;
}

/* ── 单栏（卡组/战绩） ── */
/* 不在此处设 overflow：各面板根元素自带 overflow-y:auto（HomePanel/Friends/
   History/Rank/Settings/Profile）或由内部容器滚动（DeckChoose）。
   这里若设 overflow:hidden 会与面板根元素同级冲突并盖掉其滚动条；
   外层 .main-content 已 overflow:hidden 负责裁剪。min-height:0 让 flex 子项
   可收缩，保证内部滚动容器生效。 */
.solo-panel {
  flex: 1;
  min-width: 0;
  min-height: 0;
}

/* ── 在线浮标 ── */
.online-badge {
  z-index: 30;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0.85rem;
  font-family: var(--font-mono);
  font-size: 0.7rem;
  letter-spacing: 0.15em;
  color: var(--ink);
  background: color-mix(in srgb, var(--bg0) 78%, transparent);
  border: 1px solid var(--line);
  border-radius: var(--radius-pill);
  backdrop-filter: blur(8px);
  cursor: grab;
  user-select: none;
  transition: all 200ms ease;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}
.online-badge:hover {
  border-color: var(--line-strong);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4), 0 0 12px var(--primary-glow);
}
.online-badge:active {
  cursor: grabbing;
}
.online-badge__dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--primary);
  box-shadow: 0 0 6px var(--primary), 0 0 12px var(--primary-glow);
  animation: grandumi-dot-pulse 2s ease-in-out infinite;
}
.online-badge__text {
  color: var(--ink-dim);
  text-transform: uppercase;
}
.online-badge__count {
  color: var(--primary);
  font-weight: 700;
  text-shadow: 0 0 6px var(--primary-glow);
}

/* ── 视图切换动画 ── */
.view-fade-enter-active,
.view-fade-leave-active {
  transition: opacity 0.22s ease, transform 0.22s ease;
}
.view-fade-enter-from {
  opacity: 0;
  transform: translateX(12px);
}
.view-fade-leave-to {
  opacity: 0;
  transform: translateX(-12px);
}

/* ── 移动端：折叠 Sidebar ── */
@media (max-width: 900px) {
  .main-content { left: 0; top: 56px; }
  .lobby-stack__chat { display: none; }
}
</style>