<script setup lang="ts">
import { computed, ref, provide } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useStageScale } from "@/composables/useStageScale";
import { CARD_SIZE_OVERRIDE, CARD_FELT } from "@/composables/useResponsive";
import { getCard } from "@/data/CardLoader";
import PlayerMat from "./PlayerMat.vue";
import PhaseTrack from "./PhaseTrack.vue";
import GameLog from "./GameLog.vue";
import GameActions from "./GameActions.vue";
import AnimationLayer from "./AnimationLayer.vue";
import RevealOverlay from "./RevealOverlay.vue";
import GameChatPanel from "./GameChatPanel.vue";
import HandArea from "./HandArea.vue";
import CardItem from "@/components/ui/CardItem.vue";

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
// 设计画布：对齐 redesign/battle.jsx 的 2000×944（比例 2.12）。
// 早期误用 1280×720 导致中栏(领袖+5角色槽)宽度溢出、与左右列重合——
// 画布加宽后中栏 1fr 轨道获得足够宽度，scale-to-fit 再缩放到视口。
const STAGE_W = 1600;
const STAGE_H = 760;

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
// 牌桌内所有卡启用毛毡光泽框（deck-editor 不在此 provide 树内，保持原样）
provide(CARD_FELT, true);

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
  <!-- 暗角 vignette（毛毡牌桌四周压暗，源自 battle.jsx；主题动态背景由 App.vue 全局 AnimatedBackground 提供） -->
  <div
    class="pointer-events-none absolute inset-0 z-[2]"
    style="background: radial-gradient(135% 80% at 50% 46%, transparent 42%, rgba(0, 0, 0, 0.5))"
  />

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
          <section class="panel relative flex min-h-0 flex-1 flex-col overflow-hidden p-4">
            <div class="ticks"><i /><i /><i /><i /></div>
            <div class="kicker" style="font-size: 11px">选中卡</div>
            <div v-if="focusCard" class="mt-3 flex min-h-0 flex-col items-center gap-3 overflow-hidden">
              <CardItem :card="focusCard" size="lg" :lift-on-select="false" />
              <div class="flex w-full flex-col gap-1.5">
                <div class="head truncate" style="font-size: 17px">{{ focusCard.name }}</div>
                <div v-if="focusCard.power != null" class="bf-detail-row">
                  <span class="mono faint">力量</span><span class="mono bf-detail-row__v" style="color: var(--primary)">{{ focusCard.power.toLocaleString() }}</span>
                </div>
                <div v-if="focusCard.cost != null" class="bf-detail-row">
                  <span class="mono faint">费用</span><span class="mono bf-detail-row__v">{{ focusCard.cost }}</span>
                </div>
                <div v-if="focusCard.counter" class="bf-detail-row">
                  <span class="mono faint">对抗</span><span class="mono bf-detail-row__v" style="color: #f0a13a">+{{ focusCard.counter.toLocaleString() }}</span>
                </div>
                <div v-if="focusCard.type" class="bf-detail-row">
                  <span class="mono faint">类型</span><span class="mono bf-detail-row__v">{{ focusCard.type }}</span>
                </div>
              </div>
            </div>
            <div v-else class="bf-empty-hint">悬停任意卡牌<br />查看详情</div>
          </section>
          <section class="panel relative p-4" style="height: 9rem">
            <div class="ticks"><i /><i /><i /><i /></div>
            <div class="kicker" style="font-size: 11px">记录</div>
          </section>
        </aside>

        <!-- 中栏：手牌（毛毡外）+ 毛毡牌桌（对手半场 / 接缝阶段条 / 己方半场） -->
        <main class="relative z-0 flex min-w-0 flex-1 flex-col gap-2">
          <!-- 对手手牌（毛毡外·顶部） -->
          <div class="bf-hand bf-hand--opp">
            <HandArea side="opponent" hidden @hover-card="(c: any) => hoveredCard = c" />
          </div>

          <!-- 毛毡牌桌 -->
          <div class="bf-felt bf-board">
            <div class="bf-board__half">
              <PlayerMat side="opponent" :is-observer="isObserver" :is-playback="isPlayback" @hover-card="(c: any) => hoveredCard = c" />
            </div>
            <div class="bf-board__seam">
              <div class="bf-seam" />
              <div class="bf-board__phase">
                <PhaseTrack :current-turn="currentTurn" :phase="phase" />
              </div>
            </div>
            <div class="bf-board__half">
              <PlayerMat side="my" :is-observer="isObserver" :is-playback="isPlayback" @hover-card="(c: any) => hoveredCard = c" />
            </div>
          </div>

          <!-- 己方手牌（毛毡外·底部） -->
          <div class="bf-hand bf-hand--my">
            <HandArea side="my" :hidden="isObserver" @hover-card="(c: any) => hoveredCard = c" />
          </div>
        </main>

        <!-- 右栏：玩家信息 + 日志 + 操作 -->
        <aside class="relative z-40 flex h-full min-h-0 w-44 shrink-0 flex-col gap-3">
          <section class="panel relative p-4">
            <div class="ticks"><i /><i /><i /><i /></div>
            <div class="kicker" style="font-size: 10px">对手</div>
            <div class="head truncate" style="font-size: 17px; margin-top: 6px">{{ opponentName || '对手' }}</div>
            <div style="height: 1px; background: var(--line); margin: 12px 0" />
            <div class="kicker" style="font-size: 10px">我</div>
            <div class="head truncate" style="font-size: 17px; margin-top: 6px; color: var(--primary)">{{ myName || '我' }}</div>
          </section>
          <!-- 玩家/回放模式显示日志；玩家模式额外显示操作；观战模式完全隐藏 -->
          <template v-if="showPlayerControls || isPlayback">
            <section class="panel relative flex min-h-0 flex-1 flex-col overflow-hidden p-4">
              <div class="ticks"><i /><i /><i /><i /></div>
              <div class="kicker" style="font-size: 10px">操作日志</div>
              <div class="scroll mt-2 min-h-0 flex-1 overflow-y-auto">
                <GameLog />
              </div>
            </section>
            <section v-if="showPlayerControls" class="panel relative p-4">
              <div class="ticks"><i /><i /><i /><i /></div>
              <div class="kicker" style="font-size: 10px; margin-bottom: 8px">操作</div>
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

<style scoped>
/* 性能：牌桌内面板去掉 backdrop-blur（背景为静态渐变时无需每帧重采样模糊），
   提高底色不透明度补偿磨砂质感，避免操作时的合成卡顿。 */
:deep(.panel) {
  backdrop-filter: none;
  background: color-mix(in srgb, var(--surface) 92%, transparent);
}

/* 毛毡牌桌：上下半场各占一半，接缝在中间 */
.bf-board {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.bf-board__half {
  flex: 1;
  display: flex;
  align-items: center;
  min-height: 0;
}
/* 让 PlayerMat 的 .bf-half 网格撑满半场宽度 */
.bf-board__half :deep(.bf-half) {
  flex: 1;
  width: 100%;
}
.bf-board__seam {
  position: relative;
  height: 0;
}
.bf-board__phase {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  z-index: 5;
}

/* 手牌区（毛毡外，上/下） */
.bf-hand {
  display: flex;
  align-items: flex-end;
  justify-content: center;
  flex-shrink: 0;
  min-height: 0;
}
.bf-hand--opp {
  align-items: flex-start;
}

/* 选中卡详情行 */
.bf-detail-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 7px 0;
  border-bottom: 1px solid var(--line);
  font-size: 12px;
}
.bf-detail-row__v {
  font-size: 13px;
  font-weight: 700;
  color: var(--ink);
}
.bf-empty-hint {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  line-height: 2;
  color: var(--ink-faint);
  font-family: var(--font-mono);
  font-size: 12px;
  letter-spacing: 0.1em;
}
</style>