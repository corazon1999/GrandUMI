<script setup lang="ts">
import { computed, watch } from "vue";
import { useRouter } from "vue-router";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import { useGameInit } from "@/composables/useGameInit";
import { usePlayback } from "@/composables/usePlayback";
import GameBoard from "@/components/game/GameBoard.vue";
import ReconnectOverlay from "@/components/game/ReconnectOverlay.vue";
import OpponentDisconnectBanner from "@/components/game/OpponentDisconnectBanner.vue";
import MulliganOverlay from "@/components/game/MulliganOverlay.vue";
import PromptOverlay from "@/components/game/PromptOverlay.vue";
import BattleDefenseOverlay from "@/components/game/BattleDefenseOverlay.vue";
import GameMenu from "@/components/game/GameMenu.vue";
import GMPanel from "@/components/game/GMPanel.vue";
import FeedbackOverlay from "@/components/game/FeedbackOverlay.vue";

/**
 * GamePage — 对战路由页（薄包装）。
 *
 * 仅负责：
 * - 路由登录态管理（useGameInit 拉快照）
 * - 玩家专属浮层（轮抽/Prompt/菜单/反馈/等待遮罩/结算弹窗/模式标签）
 * - 回放链路（sessionStorage → usePlayback）
 *
 * 牌桌渲染（背景/画布/三栏/聊天）全部委托给 GameBoard 组件，可被对战页与回放页共用。
 * 与 opcgpro-web/src/app/game/page.tsx 一一对齐。
 */

const router = useRouter();

const mode = useStore(useGameStore, (s) => s.mode);
const isPending = useStore(useGameStore, (s) => s.isPending);
const isGameOver = useStore(useGameStore, (s) => s.isGameOver);
const winnerIsMe = useStore(useGameStore, (s) => s.winnerIsMe);
const gameOverReason = useStore(useGameStore, (s) => s.gameOverReason);

const isObserver = computed(() => mode.value === "Observer");
const isPlayback = computed(() => mode.value === "Playback");
const showPlayerControls = computed(() => !isObserver.value && !isPlayback.value);

useGameInit();

const playback = usePlayback(
  isPlayback.value
    ? (() => {
        try {
          const raw = sessionStorage.getItem("grandumi_playback");
          return raw ? JSON.parse(raw) : null;
        } catch {
          return null;
        }
      })()
    : null,
);

watch(
  isPlayback,
  (v) => { if (v && playback.state.value === "idle") playback.play(); },
  { immediate: true },
);

function backToHome() {
  useGameStore.getState().resetGame();
  useNetStore.getState().setMatchState("idle");
  useNetStore.getState().setOpponentName("");
  router.push("/home");
}
</script>

<template>
  <div class="relative h-screen w-screen select-none overflow-hidden text-white">
    <!-- 玩家专属浮层（不随画布缩放） -->
    <template v-if="showPlayerControls">
      <ReconnectOverlay />
      <OpponentDisconnectBanner />
      <MulliganOverlay />
      <PromptOverlay />
      <BattleDefenseOverlay />
      <GameMenu />
      <GMPanel />
    </template>

    <FeedbackOverlay v-if="!isPlayback" />

    <!-- 牌桌（背景 + 三栏 + 聊天） -->
    <GameBoard :is-observer="isObserver" :is-playback="isPlayback" />

    <!-- 模式标签（固定屏幕角） -->
    <div v-if="isObserver" class="pointer-events-none fixed left-4 top-4 z-20 rounded-full bg-purple-600/80 px-3 py-1 text-xs text-white">观战模式</div>
    <div v-if="isPlayback" class="pointer-events-none fixed left-4 top-4 z-20 rounded-full bg-green-600/80 px-3 py-1 text-xs text-white">回放模式</div>

    <!-- 等待遮罩 -->
    <Transition name="fade">
      <div v-if="showPlayerControls && isPending" class="fixed inset-0 z-30 flex cursor-wait items-center justify-center bg-black/30">
        <div class="h-8 w-8 animate-spin rounded-full border-[3px] border-white/40 border-t-white" />
      </div>
    </Transition>

    <!-- 游戏结束弹窗 -->
    <Transition name="fade">
      <div v-if="isGameOver" class="fixed inset-0 z-40 flex flex-col items-center justify-center bg-black/70">
        <h1 :class="['text-5xl font-black', winnerIsMe ? 'text-yellow-400 drop-shadow-[0_0_12px_rgba(250,204,21,0.6)]' : 'text-gray-400 drop-shadow-[0_0_12px_rgba(156,163,175,0.5)]']">
          {{ winnerIsMe ? '你胜利了！' : '你战败了' }}
        </h1>
        <p v-if="gameOverReason" class="mt-3 text-lg text-white/70">结束原因：{{ gameOverReason }}</p>
        <button class="mt-6 rounded-lg bg-orange-500 px-6 py-2 text-white transition-colors hover:bg-orange-400" @click="backToHome">
          返回大厅
        </button>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from,
.fade-leave-to { opacity: 0; }
</style>