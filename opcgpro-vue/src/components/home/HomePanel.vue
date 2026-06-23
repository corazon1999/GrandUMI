<script setup lang="ts">
/**
 * 主页 / 指挥室（源自 redesign/screens2.jsx HomeScreen）。
 * 欢迎条 + 等级进度 + 快速对战入口 + 公告。公告为静态展示。
 */
import Avatar from "@/components/shared/Avatar.vue";
import Ticks from "@/components/shared/Ticks.vue";
import { useProfile } from "@/composables/useProfile";

const emit = defineEmits<{ (e: "navigate", view: "lobby"): void }>();

const { profile } = useProfile();

// 等级进度（暂无经验系统，占位 64%，与设计稿一致）
const lvPct = 64;

const NEWS: { tag: string; title: string; date: string }[] = [
  { tag: "版本", title: "OP-16 新觉醒 · 卡池现已上线", date: "06-21" },
  { tag: "活动", title: "周末双倍金币 · 限时开启", date: "06-20" },
  { tag: "赛事", title: "夏季冠军杯报名通道开放", date: "06-18" },
];
</script>

<template>
  <div class="screen-root scroll enter">
    <div class="screen-inner" style="max-width: 1120px">
      <div class="kicker" style="font-size: 12px">欢迎回来</div>
      <h1 class="head" style="font-size: 40px; margin: 10px 0 4px">指挥室</h1>
      <div class="dim" style="font-size: 13px; margin-bottom: 24px">欢迎回来，{{ profile.name }}</div>

      <!-- 欢迎 / 等级条 -->
      <div class="panel panel-pad welcome-strip">
        <Ticks />
        <Avatar :src="profile.avatar" :name="profile.name" :size="72" glow />
        <div style="flex: 1; min-width: 0">
          <div style="display: flex; align-items: baseline; gap: 12px">
            <span class="head" style="font-size: 24px; color: var(--ink)">{{ profile.name }}</span>
            <span class="tag is-active" style="cursor: default">{{ profile.title }}</span>
          </div>
          <div style="display: flex; align-items: center; gap: 10px; margin-top: 10px">
            <span class="mono faint" style="font-size: 11px">LV {{ profile.lv }}</span>
            <div class="lv-track">
              <div class="lv-fill" :style="{ width: lvPct + '%' }" />
            </div>
            <span class="mono faint" style="font-size: 11px">{{ lvPct }}%</span>
          </div>
        </div>
      </div>

      <div class="home-grid">
        <!-- 左：快速对战 -->
        <div style="display: flex; flex-direction: column; gap: 20px">
          <button class="panel quick-play" @click="emit('navigate', 'lobby')">
            <Ticks />
            <div class="kicker" style="font-size: 11px">立即开战</div>
            <div class="head glow-title" style="font-size: 44px; line-height: 1">快速对战</div>
            <div class="dim" style="font-size: 14px; margin-top: 4px">匹配一位对手，立即开始一局</div>
            <span class="btn btn--primary btn--lg quick-play__cta">▶ 进入大厅</span>
          </button>
        </div>

        <!-- 右：公告 -->
        <div style="display: flex; flex-direction: column; gap: 20px">
          <div class="panel panel-pad" style="position: relative">
            <Ticks />
            <div class="kicker" style="font-size: 11px; margin-bottom: 14px">公告</div>
            <div style="display: flex; flex-direction: column">
              <div
                v-for="(n, i) in NEWS"
                :key="i"
                class="news-row"
                :style="{ borderBottom: i < NEWS.length - 1 ? '1px solid var(--line)' : 'none' }"
              >
                <span class="tag" style="flex-shrink: 0">{{ n.tag }}</span>
                <span class="news-row__title">{{ n.title }}</span>
                <span class="mono faint" style="font-size: 11px; flex-shrink: 0">{{ n.date }}</span>
              </div>
            </div>
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

.welcome-strip {
  position: relative;
  display: flex;
  align-items: center;
  gap: 22px;
  margin-bottom: 20px;
}
.lv-track {
  flex: 1;
  max-width: 320px;
  height: 7px;
  border-radius: 4px;
  background: var(--bg1);
  overflow: hidden;
  border: 1px solid var(--line);
}
.lv-fill {
  height: 100%;
  background: linear-gradient(90deg, var(--primary), var(--primary-bright));
}

.home-grid {
  display: grid;
  grid-template-columns: 1.3fr 1fr;
  gap: 20px;
  align-items: start;
}

.quick-play {
  position: relative;
  text-align: left;
  cursor: pointer;
  overflow: hidden;
  border: 1px solid var(--line-strong);
  padding: 30px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: radial-gradient(120% 120% at 0% 0%, color-mix(in srgb, var(--primary) 22%, transparent), var(--surface));
  transition: transform 0.2s, box-shadow 0.2s;
}
.quick-play:hover {
  transform: translateY(-2px);
  box-shadow: 0 16px 40px -16px var(--primary-glow);
}
.quick-play__cta {
  margin-top: 14px;
  align-self: flex-start;
  pointer-events: none;
}

.news-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 11px 0;
  cursor: pointer;
}
.news-row__title {
  flex: 1;
  font-size: 13px;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 900px) {
  .home-grid { grid-template-columns: 1fr; }
  .screen-root { padding: 76px 16px 24px; }
}
</style>
