<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { loadAllDecks } from "@/data/DeckMapper";
import { HomeRequest } from "@/net/HomeProtocol";
import { showMessage } from "@/components/ui/MessageBox";
import ScreenHead from "@/components/shared/ScreenHead.vue";
import Ticks from "@/components/shared/Ticks.vue";

const emit = defineEmits<{ (e: "goToDeck"): void }>();

const matchState = useStore(useNetStore, (s) => s.matchState);
const selectedDeck = useStore(useNetStore, (s) => s.selectedDeck);
const opponentName = useStore(useNetStore, (s) => s.opponentName);
const roomCode = useStore(useNetStore, (s) => s.roomCode);

const roomMode = ref<"none" | "create" | "join">("none");
const joinInput = ref("");
const copied = ref(false);
const botGoFirst = ref(true);

// ── TEMP: dev-only 测试入口（验证后删除） ─────────────────────────────
onMounted(() => {
  if (typeof window === "undefined") return;
  const url = new URL(window.location.href);
  if (url.searchParams.get("__test") === "bot") {
    const decks = loadAllDecks();
    const firstName = Object.keys(decks)[0];
    if (firstName) {
      const d = decks[firstName];
      useNetStore.getState().setSelectedDeck({
        name: firstName,
        leader: d.leader,
        leaderName: d.leaderName,
        cards: [d.leader, ...d.cards].join("\n"),
      });
    } else {
      useNetStore.getState().setSelectedDeck({
        name: "测试卡组",
        leader: "OP01-001",
        leaderName: "路飞",
        cards: "OP01-001\nOP01-004",
      });
    }
    useNetStore.getState().setOpponentName("机器人对手");
    useNetStore.getState().setMatchState("bot-matching");
  }
});

const leaderSprite = computed(() => {
  if (!selectedDeck.value) return "";
  const decks = loadAllDecks();
  return decks[selectedDeck.value.name]?.leaderSprite ?? "";
});

function handleMatch() {
  if (!selectedDeck.value) return;
  HomeRequest.enterMatch(selectedDeck.value.cards);
}
function handleBotMatch() {
  if (!selectedDeck.value) return;
  const sent = HomeRequest.enterBotMatch(
    selectedDeck.value.cards,
    botGoFirst.value,
  );
  if (!sent) showMessage("服务器未连接，请稍后重试", "error");
}
function handleCancelMatch() {
  HomeRequest.cancelMatch();
}
function handleCreateRoom() {
  if (!selectedDeck.value) return;
  roomMode.value = "create";
  const sent = HomeRequest.createRoom(selectedDeck.value.cards);
  if (!sent) {
    showMessage("服务器未连接，请稍后重试", "error");
    roomMode.value = "none";
  }
}
function handleJoinRoom() {
  if (!selectedDeck.value) return;
  roomMode.value = "join";
}
function confirmJoinRoom() {
  const code = joinInput.value.trim().toUpperCase();
  if (code.length < 6 || !selectedDeck.value) return;
  HomeRequest.joinRoom(code, selectedDeck.value.cards);
  joinInput.value = "";
}
function handleCancelRoom() {
  HomeRequest.cancelRoom();
  roomMode.value = "none";
  joinInput.value = "";
}
function copyRoomCode() {
  if (!roomCode.value) return;
  navigator.clipboard
    .writeText(roomCode.value)
    .then(() => {
      copied.value = true;
      setTimeout(() => (copied.value = false), 2000);
    })
    .catch(() => {});
}
</script>

<template>
  <div class="lobby-root enter scroll">
    <ScreenHead kicker="集结你的船员" title="对战大厅" />

    <!-- ── 当前卡组卡片 ────────────────────────────────────── -->
    <div v-if="selectedDeck" class="panel panel-pad deck-card">
      <Ticks />
      <div class="deck-card__color-block">
        <img
          v-if="leaderSprite"
          :src="leaderSprite"
          class="deck-card__leader-img"
          :alt="selectedDeck.leaderName"
          @error="($event.target as HTMLImageElement).src = '/sprites/CardBack.png'"
        />
        <span v-else class="head">{{ selectedDeck.name.charAt(0) }}</span>
      </div>
      <div class="deck-card__info">
        <span class="mono faint deck-card__eyebrow">当前卡组 · 领航</span>
        <p class="head deck-card__name">{{ selectedDeck.name }}</p>
        <span class="mono dim deck-card__leader"
          >{{ selectedDeck.leaderName }} · 已就绪</span
        >
      </div>
      <button
        class="btn btn--ghost deck-card__change"
        @click="emit('goToDeck')">
        更换 →
      </button>
    </div>

    <div v-else class="panel deck-empty">
      <Ticks />
      <div class="deck-empty__mark">?</div>
      <p class="dim deck-empty__text">尚未选择卡组，请前往卡组页面创建</p>
    </div>

    <!-- ── 操作按钮区（idle） ──────────────────────────────── -->
    <template v-if="matchState === 'idle' && !roomCode && roomMode === 'none'">
      <div class="lobby-actions">
        <button
          class="btn btn--primary btn--lg btn--match"
          :disabled="!selectedDeck"
          @click="handleMatch">
          <span>▶</span> 开始匹配
        </button>

        <div class="bot-group">
          <button
            class="btn"
            :disabled="!selectedDeck"
            title="与机器人对战"
            @click="handleBotMatch">
            单人测试
          </button>
          <div class="seg bot-seg">
            <button
              :class="['seg__opt', { 'is-active': botGoFirst }]"
              @click="botGoFirst = true">
              先手
            </button>
            <button
              :class="['seg__opt', { 'is-active': !botGoFirst }]"
              @click="botGoFirst = false">
              后手
            </button>
          </div>
        </div>
      </div>

      <div class="rule" style="width: 320px; margin: 4px 0">或与好友对战</div>

      <div class="room-actions">
        <button class="btn" :disabled="!selectedDeck" @click="handleCreateRoom">
          <span>＋</span> 创建房间
        </button>
        <button class="btn" :disabled="!selectedDeck" @click="handleJoinRoom">
          <span>→</span> 加入房间
        </button>
      </div>
    </template>

    <!-- ── 匹配中 ───────────────────────────────────────────── -->
    <div
      v-else-if="matchState === 'matching'"
      class="panel panel-pad state-card">
      <Ticks />
      <div class="state-card__ring"><div class="state-card__ring-spin" /></div>
      <p class="kicker state-card__eyebrow">SEARCHING FOR OPPONENT</p>
      <p class="head state-card__title">寻找对手中</p>
      <p class="dim state-card__hint">系统正在为你匹配水平相近的对手</p>
      <button class="btn btn--ghost" @click="handleCancelMatch">
        取消匹配
      </button>
    </div>

    <!-- ── 单人测试开局 ─────────────────────────────────────── -->
    <div
      v-else-if="matchState === 'bot-matching'"
      class="panel panel-pad state-card">
      <Ticks />
      <div class="state-card__ring"><div class="state-card__ring-spin" /></div>
      <p class="kicker state-card__eyebrow">SOLO · INITIALIZING</p>
      <p class="head state-card__title">对手：机器人对手</p>
      <p class="dim state-card__hint">正在初始化对战...</p>
      <button class="btn btn--ghost" @click="handleCancelMatch">取消</button>
    </div>

    <!-- ── 匹配成功 ─────────────────────────────────────────── -->
    <div
      v-else-if="matchState === 'matched'"
      class="panel panel-pad state-card state-card--success">
      <Ticks />
      <div class="state-card__success-icon">✓</div>
      <p class="kicker state-card__eyebrow">MATCH FOUND</p>
      <p class="head state-card__title">对手：{{ opponentName }}</p>
      <p class="dim state-card__hint">即将进入游戏...</p>
    </div>

    <!-- ── 创建房间中 ───────────────────────────────────────── -->
    <div
      v-else-if="!roomCode && roomMode === 'create'"
      class="panel panel-pad state-card">
      <Ticks />
      <div class="state-card__ring"><div class="state-card__ring-spin" /></div>
      <p class="kicker state-card__eyebrow">CREATING ROOM</p>
      <p class="head state-card__title">房间创建中</p>
      <button class="btn btn--ghost" @click="handleCancelRoom">取消</button>
    </div>

    <!-- ── 房间码展示 ───────────────────────────────────────── -->
    <div
      v-else-if="roomCode && roomMode === 'create'"
      class="panel panel-pad state-card">
      <Ticks />
      <p class="kicker state-card__eyebrow">ROOM CODE</p>
      <p
        class="head state-card__room-code"
        :class="{ 'state-card__room-code--copied': copied }">
        {{ roomCode }}
      </p>
      <div class="state-card__actions">
        <button class="btn" @click="copyRoomCode">
          {{ copied ? "✓ 已复制" : "复制房间码" }}
        </button>
        <button class="btn btn--ghost" @click="handleCancelRoom">取消</button>
      </div>
      <p class="dim state-card__hint">等待好友加入...</p>
    </div>

    <!-- ── 加入房间 ─────────────────────────────────────────── -->
    <div
      v-else-if="roomMode === 'join' && !roomCode"
      class="panel panel-pad state-card">
      <Ticks />
      <p class="kicker state-card__eyebrow">ENTER ROOM CODE</p>
      <div class="field state-card__field">
        <span class="ic mono">›</span>
        <input
          :value="joinInput"
          placeholder="000000"
          :maxlength="6"
          @input="
            joinInput = ($event.target as HTMLInputElement).value.toUpperCase()
          "
          @keydown.enter="confirmJoinRoom" />
      </div>
      <div class="state-card__actions">
        <button
          class="btn btn--primary"
          :disabled="joinInput.trim().length < 6"
          @click="confirmJoinRoom">
          加入
        </button>
        <button class="btn btn--ghost" @click="handleCancelRoom">取消</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.lobby-root {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 26px;
  padding: 24px 40px 40px;
  height: 100%;
  overflow-y: auto;
  font-family: var(--font-ui);
}

/* ── 当前卡组卡片 ───────────────────────────────────────── */
.deck-card {
  width: min(560px, 80%);
  display: flex;
  align-items: center;
  gap: 20px;
  padding: 22px;
}
.deck-card__color-block {
  width: 64px;
  height: 90px;
  border-radius: var(--radius);
  background: linear-gradient(160deg, var(--accent), var(--bg1));
  border: 1.5px solid var(--accent);
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 22px;
  color: #fff;
  flex-shrink: 0;
  overflow: hidden;
}
.deck-card__leader-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: top center;
  display: block;
}
.deck-card__info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.deck-card__eyebrow {
  font-size: 10px;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}
.deck-card__name {
  font-size: 22px;
  margin: 0;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.deck-card__leader {
  font-size: 12px;
}
.deck-card__change {
  flex-shrink: 0;
}

/* ── 空卡组卡片 ─────────────────────────────────────────── */
.deck-empty {
  width: min(560px, 80%);
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 22px;
}
.deck-empty__mark {
  width: 64px;
  height: 64px;
  border-radius: 50%;
  border: 1.5px dashed var(--line-strong);
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 28px;
  color: var(--primary);
  box-shadow: 0 0 18px -6px var(--primary-glow);
}
.deck-empty__text {
  margin: 0;
  font-size: 14px;
}

/* ── 操作按钮行 ─────────────────────────────────────────── */
.lobby-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
  justify-content: center;
}
.btn--match {
  min-width: 220px;
  font-size: 18px;
}
.bot-group {
  display: flex;
  align-items: center;
  gap: 10px;
}
.bot-seg {
  height: 48px;
}
.bot-seg .seg__opt {
  padding: 8px 18px;
  font-size: 14px;
}

.room-actions {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  justify-content: center;
}

/* ── 状态卡片（匹配中/房间码/输入码） ──────────────────── */
.state-card {
  width: min(480px, 80%);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  padding: 28px;
  text-align: center;
}
.state-card__ring {
  width: 56px;
  height: 56px;
  border: 2px solid var(--line);
  border-top-color: var(--primary);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 0 18px -6px var(--primary-glow);
}
.state-card__ring-spin {
  width: 100%;
  height: 100%;
  border-radius: 50%;
  border: 2px solid transparent;
  border-top-color: var(--primary);
  animation: spin 1.1s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.state-card__eyebrow {
  font-size: 11px;
  letter-spacing: 0.24em;
}
.state-card__title {
  font-size: 26px;
  margin: 0;
  color: var(--ink);
}
.state-card__hint {
  font-size: 13px;
  margin: 0;
}
.state-card__success-icon {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  background: linear-gradient(180deg, var(--primary-bright), var(--primary));
  color: var(--on-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 32px;
  box-shadow: 0 0 24px var(--primary-glow);
}
.state-card--success .state-card__title {
  color: var(--good);
}
.state-card__room-code {
  font-family: var(--font-mono);
  font-size: 38px;
  letter-spacing: 0.4em;
  color: var(--primary);
  margin: 4px 0;
  text-shadow: 0 0 24px var(--primary-glow);
  padding: 6px 18px;
  border: 1px dashed var(--line-strong);
  border-radius: var(--radius);
  background: var(--bg1);
  transition: all 0.3s;
}
.state-card__room-code--copied {
  color: var(--good);
  border-color: var(--good);
  box-shadow: 0 0 18px color-mix(in srgb, var(--good) 50%, transparent);
}
.state-card__field {
  width: 100%;
  max-width: 260px;
  margin-top: 4px;
}
.state-card__actions {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  justify-content: center;
}
</style>
