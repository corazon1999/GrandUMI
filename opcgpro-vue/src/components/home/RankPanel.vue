<script setup lang="ts">
/**
 * 天梯排行（源自 redesign/screens2.jsx LeaderboardScreen）。
 * 你的段位横幅 + 前三领奖台 + 排名列表。当前为静态展示数据。
 */
import { computed } from "vue";
import Avatar from "@/components/shared/Avatar.vue";
import Ticks from "@/components/shared/Ticks.vue";
import { useProfile } from "@/composables/useProfile";

const { profile } = useProfile();

interface Row { rank: number; name: string; c: string; title: string; pts: number; wr: number; me?: boolean }

const LADDER: Row[] = [
  { rank: 1, name: "海贼王·罗杰", c: "#e8b04b", title: "四皇候补", pts: 4820, wr: 78 },
  { rank: 2, name: "红发·香克斯", c: "#d6453e", title: "超新星", pts: 4655, wr: 74 },
  { rank: 3, name: "海军·战国", c: "#5b9bd5", title: "海军本部", pts: 4510, wr: 71 },
  { rank: 4, name: "白胡子·爱德华", c: "#3fb0a8", title: "王下七武海", pts: 4322, wr: 69 },
  { rank: 5, name: "光月·御田", c: "#9a5fd0", title: "革命军", pts: 4180, wr: 67 },
  { rank: 6, name: "猫眼·妮可", c: "#3fa45f", title: "草帽船员", pts: 4055, wr: 65 },
  { rank: 28, name: `${profile.name}（你）`, c: "#e8b04b", title: profile.title, pts: 2840, wr: 63, me: true },
];

const top3 = LADDER.slice(0, 3);
// 领奖台顺序：第2名 - 第1名 - 第3名
const podium = computed(() => [top3[1], top3[0], top3[2]]);
const rest = LADDER.slice(3);
</script>

<template>
  <div class="screen-root scroll enter">
    <div class="screen-inner" style="max-width: 900px">
      <div class="kicker" style="font-size: 12px">赛季 S3</div>
      <h1 class="head" style="font-size: 40px; margin: 10px 0 4px">天梯排行</h1>
      <div class="dim" style="font-size: 13px; margin-bottom: 24px">赛季 S3 · 距结算还有 12 天</div>

      <!-- 你的段位横幅 -->
      <div class="panel panel-pad tier-banner">
        <Ticks />
        <Avatar :src="profile.avatar" :name="profile.name" :size="58" glow />
        <div style="flex: 1">
          <div class="mono faint" style="font-size: 11px; letter-spacing: 0.14em">你的排名</div>
          <div style="display: flex; align-items: baseline; gap: 10px; margin-top: 4px">
            <span class="head glow-title" style="font-size: 30px">#28</span>
            <span class="dim" style="font-size: 14px">超新星 II · 2,840 分</span>
          </div>
        </div>
        <div style="text-align: right">
          <div class="head" style="font-size: 22px; color: var(--good)">▲ 12</div>
          <div class="mono faint" style="font-size: 11px">本周变化</div>
        </div>
      </div>

      <!-- 领奖台 -->
      <div class="podium">
        <div v-for="p in podium" :key="p.rank" class="podium-col">
          <Avatar :name="p.name" :size="p.rank === 1 ? 72 : 56" :ring="false" :glow="p.rank === 1" />
          <div class="podium-name">{{ p.name }}</div>
          <div
            class="panel podium-block"
            :class="{ 'podium-block--first': p.rank === 1 }"
            :style="{ height: (p.rank === 1 ? 150 : 116) + 'px' }"
          >
            <div class="head glow-title" :style="{ fontSize: (p.rank === 1 ? 44 : 34) + 'px' }">{{ p.rank }}</div>
            <div class="mono" style="font-size: 13px; color: var(--primary); font-weight: 700">{{ p.pts }}</div>
          </div>
        </div>
      </div>

      <!-- 列表 -->
      <div style="display: flex; flex-direction: column; gap: 8px">
        <div
          v-for="p in rest"
          :key="p.rank"
          class="panel rank-row"
          :class="{ 'rank-row--me': p.me }"
        >
          <div class="head rank-row__num" :style="{ color: p.me ? 'var(--primary)' : 'var(--ink-dim)' }">{{ p.rank }}</div>
          <Avatar :src="p.me ? profile.avatar : ''" :name="p.name" :size="42" :ring="false" />
          <div style="flex: 1; min-width: 0">
            <div class="rank-row__name">{{ p.name }}</div>
            <div class="mono faint" style="font-size: 11px; margin-top: 2px">{{ p.title }} · 胜率 {{ p.wr }}%</div>
          </div>
          <div style="text-align: right">
            <div class="mono" style="font-size: 16px; font-weight: 700; color: var(--primary)">{{ p.pts }}</div>
            <div class="mono faint" style="font-size: 10px; letter-spacing: 0.1em">分</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.screen-root {
  position: absolute;
  inset: 0;
  overflow-y: auto;
  padding: 76px 40px 32px;
  font-family: var(--font-ui);
  color: var(--ink);
}
.screen-inner {
  margin: 0 auto;
}

.tier-banner {
  position: relative;
  display: flex;
  align-items: center;
  gap: 18px;
  margin-bottom: 24px;
  background: radial-gradient(120% 120% at 100% 0%, color-mix(in srgb, var(--primary) 14%, transparent), var(--surface));
}

.podium {
  display: flex;
  align-items: flex-end;
  justify-content: center;
  gap: 18px;
  margin-bottom: 26px;
}
.podium-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  width: 150px;
}
.podium-name {
  font-family: var(--font-ui);
  font-size: 13px;
  color: var(--ink);
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}
.podium-block {
  width: 100%;
  border-radius: var(--radius);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  background: var(--surface);
}
.podium-block--first {
  background: linear-gradient(180deg, color-mix(in srgb, var(--primary) 26%, var(--surface)), var(--surface));
  border-color: var(--primary);
}

.rank-row {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 18px;
  transition: border-color 0.25s;
}
.rank-row:hover {
  border-color: var(--line-strong);
}
.rank-row--me {
  border-color: var(--primary);
  background: color-mix(in srgb, var(--primary) 12%, var(--surface));
}
.rank-row__num {
  font-size: 20px;
  width: 44px;
  text-align: center;
}
.rank-row__name {
  font-size: 15px;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 700px) {
  .screen-root { padding: 76px 16px 24px; }
  .podium-col { width: 100px; }
}
</style>
