<script setup lang="ts">
import { computed, ref, provide } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useStageScale } from "@/composables/useStageScale";
import { CARD_SIZE_OVERRIDE } from "@/composables/useResponsive";
import { getCard } from "@/data/CardLoader";
import PlayerMat from "./PlayerMat.vue";
import PhaseTrack from "./PhaseTrack.vue";
import GameLog from "./GameLog.vue";
import GameActions from "./GameActions.vue";
import AnimationLayer from "./AnimationLayer.vue";
import RevealOverlay from "./RevealOverlay.vue";
import GameChatPanel from "./GameChatPanel.vue";

/**
 * GameBoard — 牌桌渲染（对战页与回放页共用）。
 *
 * 纯展示层：从 gameStore 读取镜像状态并按固定 1280×720 画布 scale-to-fit 渲染。
 * 不含玩家专属浮层（轮抽/Prompt/菜单/等待遮罩/结算弹窗等）——那些由 GamePage 叠加。
 * 回放页传 isPlayback 即可复用同一套牌桌。
 *
 * 与 opcgpro-web/src/components/game/GameBoard.tsx 一一对齐：
 * - 1280×720 固定画布 + useStageScale 等比缩放
 * - 三栏（LeftRail + Main + RightRail）布局
 * - 左栏「选中卡」面板的 focusCard 推导逻辑是 Vue 端自创（web 是空占位），
 *   用 @hover-card 事件链接 PlayerMat/HandArea/FieldArea/LeaderCard 的 emit
 */
const STAGE_W = 1280;
const STAGE_H = 720;

const props = defineProps<{
  isObserver: boolean;
  isPlayback: boolean;
}>();

const currentTurn = useStore(useGameStore, (s) => s.currentTurn);
const phase = useStore(useGameStore, (s) => s.phase);
const myName = useStore(useGameStore, (s) => s.myName);
const opponentName = useStore(useGameStore, (s) => s.opponentName);
const my = useStore(useGameStore, (s) => s.my);
const opponent = useStore(useGameStore, (s) => s.opponent);
const selectedHandIndex = useStore(useGameStore, (s) => s.selectedHandIndex);
const selectedFieldId = useStore(useGameStore, (s) => s.selectedFieldId);

// 设计画布等比缩放
const stageScale = useStageScale(STAGE_W, STAGE_H);
// 强制画布内所有 CardItem 用 sm 尺寸（与 web 端 CardSizeOverride.Provider 一致）
provide(CARD_SIZE_OVERRIDE, ref<"sm" | "md" | "lg">("sm"));

const showPlayerControls = computed(() => !props.isObserver && !props.isPlayback);

// ── 左栏「选中卡」面板 ──────────────────────────────────────────────────
// Vue 端自创：依据 hover-card 事件链 + selectedHandIndex + selectedFieldId
// 推导当前需展示详情的卡。web 对应位置是空 5:7 占位，无推导逻辑。
const hoveredCard = ref<ReturnType<typeof getCard> | null>(null);
const focusCard = computed(() => {
  if (hoveredCard.value) return hoveredCard.value;
  if (selectedHandIndex.value !== null && my.value) {
    const n = my.value.handCardNumbers[selectedHandIndex.value];
    if (n) return getCard(n) ?? null;
  }
  if (selectedFieldId.value !== null) {
    if (my.value) {
      if (selectedFieldId.value === my.value.leaderId && my.value.leaderNumber)
        return getCard(my.value.leaderNumber) ?? null;
      const myFc = my.value.fieldCards.find((c) => c.id === selectedFieldId.value);
      if (myFc?.number) return getCard(myFc.number) ?? null;
    }
    if (opponent.value) {
      if (selectedFieldId.value === opponent.value.leaderId && opponent.value.leaderNumber)
        return getCard(opponent.value.leaderNumber) ?? null;
      const oppFc = opponent.value.fieldCards.find((c) => c.id === selectedFieldId.value);
      if (oppFc?.number) return getCard(oppFc.number) ?? null;
    }
  }
  return null;
});
</script>

<template>
  <!-- 背景层（画布外，全屏铺底） -->
  <div class="absolute inset-0 bg-[radial-gradient(circle_at_center,_rgba(33,92,145,0.26),_transparent_62%),linear-gradient(135deg,_#0b1a2c,_#07111f_48%,_#0a1524)]" />
  <div class="absolute inset-0 opacity-[0.08] [background-image:linear-gradient(rgba(255,255,255,.6)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,.6)_1px,transparent_1px)] [background-size:18px_18px]" />

  <!-- 动画 / 揭示（不随画布缩放） -->
  <AnimationLayer />
  <RevealOverlay />

  <!-- 固定设计画布 + 整体等比缩放居中（scale-to-fit） -->
  <div class="absolute inset-0 z-10 flex items-center justify-center">
    <div
      class="relative shrink-0"
      :style="{
        width: `${STAGE_W}px`,
        height: `${STAGE_H}px`,
        transform: `scale(${stageScale})`,
        transformOrigin: 'center',
      }"
    >
      <div class="absolute inset-3 flex gap-3">
        <!-- 左栏：选中卡 + 记录 -->
        <aside class="flex h-full min-h-0 w-52 shrink-0 flex-col gap-3">
          <section class="min-h-0 flex-1 rounded-md border border-sky-200/15 bg-slate-950/55 p-3 shadow-inner shadow-black/30">
            <h2 class="text-xs font-black text-slate-300">选中卡</h2>
            <div v-if="focusCard" class="mt-2 flex flex-col gap-2">
              <div class="truncate text-sm font-bold text-sky-100">{{ focusCard.name }}</div>
              <div v-if="focusCard.power" class="text-xs text-sky-300">力量 {{ focusCard.power.toLocaleString() }}</div>
              <div v-if="focusCard.counter" class="text-xs text-amber-300">反击 +{{ focusCard.counter }}</div>
              <div v-if="focusCard.cost != null" class="text-xs text-slate-400">费用 {{ focusCard.cost }}</div>
              <div v-if="focusCard.type" class="text-xs text-slate-500">{{ focusCard.type }}</div>
            </div>
            <div v-else class="mt-3 aspect-[5/7] rounded-md border border-dashed border-slate-600/70 bg-black/20" />
          </section>
          <section class="h-36 rounded-md border border-sky-200/15 bg-slate-950/55 p-3 shadow-inner shadow-black/30 xl:h-44">
            <h2 class="text-xs font-black text-slate-300">记录</h2>
          </section>
        </aside>

        <!-- 中栏：牌桌（对手区 + 阶段条 + 己方区） -->
        <main class="relative z-0 flex min-w-0 flex-1 flex-col gap-2">
          <PlayerMat side="opponent" :is-observer="isObserver" :is-playback="isPlayback" @hover-card="(c: any) => hoveredCard = c" />
          <PhaseTrack :current-turn="currentTurn" :phase="phase" />
          <PlayerMat side="my" :is-observer="isObserver" :is-playback="isPlayback" @hover-card="(c: any) => hoveredCard = c" />
        </main>

        <!-- 右栏：玩家信息 + 日志 + 操作 -->
        <aside class="relative z-40 flex h-full min-h-0 w-44 shrink-0 flex-col gap-3">
          <section class="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
            <p class="text-xs font-black text-slate-300">对手</p>
            <p class="mt-1 truncate text-sm font-black text-white">{{ opponentName || '对手' }}</p>
            <div class="my-3 h-px bg-white/10" />
            <p class="text-xs font-black text-slate-300">我</p>
            <p class="mt-1 truncate text-sm font-black text-sky-100">{{ myName || '我' }}</p>
          </section>
          <!-- 玩家/回放模式显示日志；玩家模式额外显示操作；观战模式完全隐藏 -->
          <template v-if="showPlayerControls || isPlayback">
            <section class="relative min-h-0 flex-1 overflow-y-auto rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
              <h2 class="text-xs font-black text-slate-300">操作日志</h2>
              <GameLog />
            </section>
            <section v-if="showPlayerControls" class="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
              <h2 class="mb-2 text-xs font-black text-slate-300">操作</h2>
              <GameActions />
            </section>
          </template>
        </aside>
      </div>
    </div>
  </div>

  <!-- 局内聊天（固定屏幕角，不随画布缩放；回放模式内部自隐） -->
  <GameChatPanel :is-playback="isPlayback" />
</template>