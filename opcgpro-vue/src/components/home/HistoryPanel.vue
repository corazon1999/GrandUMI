<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useRouter } from "vue-router";
import { listMeta, deleteMatch, clearAll, type MatchMeta } from "@/data/matchHistoryDB";
import { getCard } from "@/data/CardLoader";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import ScreenHead from "@/components/shared/ScreenHead.vue";
import Ticks from "@/components/shared/Ticks.vue";
const router = useRouter();
const list = ref<MatchMeta[]>([]);
const loading = ref(true);
const playerName = useStore(useNetStore, (s) => s.playerName);

// 卡牌颜色 → hex（用于领航圆点光晕）
const LEADER_COLOR_HEX: Record<string, string> = {
  红: "#d6453e", 绿: "#3fa45f", 蓝: "#3f7ad6",
  紫: "#8a5cd0", 黑: "#5b5b66", 黄: "#e0b13e",
};

function relativeTime(ts: number): string {
  const diff = Date.now() - ts;
  const min = Math.floor(diff / 60000);
  if (min < 1) return "刚刚";
  if (min < 60) return `${min} 分钟前`;
  const hrs = Math.floor(min / 60);
  if (hrs < 24) return `${hrs} 小时前`;
  const days = Math.floor(hrs / 24);
  return `${days} 天前`;
}

function leaderLabel(num: string): string {
  if (!num) return "—";
  const card = getCard(num);
  return card?.name ? card.name : num;
}

function leaderColor(num: string): string {
  if (!num) return "var(--primary)";
  const card = getCard(num);
  if (card?.color) {
    // card.color 格式如 "红" 或 "红/绿"，取第一个
    const first = card.color.split("/")[0].trim();
    return LEADER_COLOR_HEX[first] ?? "var(--primary)";
  }
  return "var(--primary)";
}

// ── 统计 ──
const stats = computed(() => {
  const total = list.value.length;
  const wins = list.value.filter((m) => m.winnerIsMe).length;
  const winRate = total > 0 ? Math.round((wins / total) * 100) : 0;
  let streak = 0;
  for (const m of list.value) {
    if (m.winnerIsMe) streak++;
    else break;
  }
  return { total, winRate, streak };
});

async function refresh() {
  loading.value = true;
  try {
    list.value = await listMeta();
  } catch {
    list.value = [];
  } finally {
    loading.value = false;
  }
}

async function handleDelete(id: string) {
  await deleteMatch(id).catch(() => {});
  refresh();
}

async function handleClear() {
  if (!confirm("确定清空全部对局记录？此操作不可恢复。")) return;
  await clearAll().catch(() => {});
  refresh();
}

onMounted(refresh);
</script>

<template>
  <div class="history-root enter scroll">
    <div class="history-inner">
      <!-- 顶部标题 -->
      <div class="kicker" style="font-size: 12px">最近战绩</div>
      <h1 class="head" style="font-size: 40px; margin: 10px 0 4px">对局记录</h1>
      <div class="dim" style="font-size: 13px; margin-bottom: 24px">
        仅保留本设备最近 {{ list.length }} 局
      </div>

      <!-- 三列统计 -->
      <div class="stats-grid">
        <div class="panel panel-pad stat-card">
          <Ticks />
          <p class="head glow-title stat-card__num">{{ stats.total }}</p>
          <span class="mono faint stat-card__label">总场次</span>
        </div>
        <div class="panel panel-pad stat-card">
          <Ticks />
          <p class="head glow-title stat-card__num">
            {{ stats.winRate }}<span class="stat-card__pct">%</span>
          </p>
          <span class="mono faint stat-card__label">胜率</span>
        </div>
        <div class="panel panel-pad stat-card">
          <Ticks />
          <p class="head glow-title stat-card__num">{{ stats.streak }}</p>
          <span class="mono faint stat-card__label">当前连胜</span>
        </div>
      </div>

      <!-- 对局列表 -->
      <div class="match-list" style="display: flex; flex-direction: column; gap: 10px">
        <p v-if="loading" class="history-empty">加载中…</p>
        <p v-else-if="list.length === 0" class="history-empty">
          暂无对局记录，打一局后会出现在这里
        </p>

        <div
          v-for="m in list"
          :key="m.id"
          class="panel match-row"
        >
          <!-- W/L 徽章 -->
          <div
            :class="[
              'match-row__badge',
              m.winnerIsMe ? 'match-row__badge--win' : 'match-row__badge--lose',
            ]"
          >
            {{ m.winnerIsMe ? "W" : "L" }}
          </div>

          <!-- 领航颜色圆点 -->
          <span
            class="match-row__leader-dot"
            :style="{
              background: leaderColor(m.myLeader),
              boxShadow: `0 0 10px ${leaderColor(m.myLeader)}`,
            }"
          />

          <!-- 对局信息 -->
          <div class="match-row__info">
            <div class="match-row__opponent">
              {{ m.opponentName || "—" }}
            </div>
            <div class="mono faint match-row__detail">
              {{ leaderLabel(m.myLeader) }}
            </div>
          </div>

          <!-- 时间 -->
          <span class="mono dim match-row__time">{{ relativeTime(m.startedAt) }}</span>

          <!-- 回放按钮 -->
          <button
            class="btn btn--ghost match-row__btn"
            @click="router.push(`/replay/${encodeURIComponent(m.id)}`)"
          >
            回放
          </button>

          <!-- 清空全部（仅第一条可见） -->
          <button
            v-if="list.length > 0"
            class="match-row__clear"
            title="清空全部记录"
            @click="handleClear"
          >
            清空
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── 根容器：inset:0 + overflow:auto + padding 76px top 对齐 TopBar ── */
.history-root {
  position: absolute;
  inset: 0;
  overflow-y: auto;
  padding: 76px 40px 24px;
  font-family: var(--font-ui);
  color: var(--ink);
}

.history-inner {
  max-width: 880px;
  margin: 0 auto;
}

/* ── 三列统计 ── */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
  margin-bottom: 26px;
}
.stat-card {
  text-align: center;
  padding: 20px;
}
.stat-card__num {
  font-size: 38px;
  margin: 0;
  line-height: 1;
}
.stat-card__pct {
  font-size: 22px;
  margin-left: 2px;
}
.stat-card__label {
  font-size: 11px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  margin-top: 6px;
}

/* ── 对局列表 ── */
.match-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.history-empty {
  padding: 48px 0;
  text-align: center;
  font-size: 13px;
  color: var(--ink-faint);
  margin: 0;
}

/* ── 对局行 ── */
.match-row {
  display: flex;
  align-items: center;
  gap: 18px;
  padding: 14px 20px;
  transition: border-color 0.25s;
}
.match-row:hover {
  border-color: var(--line-strong);
}

.match-row__badge {
  width: 44px;
  height: 44px;
  border-radius: var(--radius);
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 20px;
  flex-shrink: 0;
}
.match-row__badge--win {
  background: linear-gradient(180deg, var(--primary-bright), var(--primary));
  color: var(--on-primary);
}
.match-row__badge--lose {
  background: var(--accent);
  color: #fff;
}

.match-row__leader-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.match-row__info {
  flex: 1;
  min-width: 0;
}
.match-row__opponent {
  font-size: 15px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.match-row__detail {
  font-size: 11px;
  margin-top: 2px;
}

.match-row__time {
  font-size: 12px;
  flex-shrink: 0;
}

.match-row__btn {
  padding: 8px 14px;
  font-size: 13px;
  flex-shrink: 0;
}

.match-row__clear {
  background: transparent;
  border: none;
  color: var(--ink-faint);
  cursor: pointer;
  padding: 6px;
  font-size: 13px;
  border-radius: var(--radius);
  transition: color 0.2s, background 0.2s;
  flex-shrink: 0;
}
.match-row__clear:hover {
  color: var(--bad);
  background: color-mix(in srgb, var(--bad) 12%, transparent);
}

@media (max-width: 700px) {
  .stats-grid { grid-template-columns: 1fr; }
  .history-root { padding: 76px 16px 24px; }
}
</style>
