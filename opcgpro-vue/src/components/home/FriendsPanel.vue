<script setup lang="ts">
/**
 * 好友（源自 redesign/friends.jsx FriendsScreen）。
 * 添加好友 + 好友请求 + 好友列表（全部/在线筛选）。当前为静态展示数据。
 */
import { ref, computed } from "vue";
import Avatar from "@/components/shared/Avatar.vue";
import Ticks from "@/components/shared/Ticks.vue";

const emit = defineEmits<{ (e: "navigate", view: "lobby"): void }>();

type Status = "online" | "away" | "offline";
interface Friend { name: string; c: string; title: string; status: Status; note: string }
interface Request { name: string; c: string; title: string; mutual: number }

const FRIENDS: Friend[] = [
  { name: "红发·香克斯", c: "#d6453e", title: "超新星", status: "online", note: "对战中" },
  { name: "猫眼·妮可", c: "#3fa45f", title: "草帽船员", status: "online", note: "在大厅" },
  { name: "光月·御田", c: "#9a5fd0", title: "革命军", status: "online", note: "组建卡组中" },
  { name: "海军·战国", c: "#5b9bd5", title: "海军本部", status: "away", note: "离开 12 分钟" },
  { name: "白胡子·爱德华", c: "#3fb0a8", title: "王下七武海", status: "offline", note: "3 小时前在线" },
  { name: "托尼·乔巴", c: "#e0b13e", title: "见习航海士", status: "offline", note: "昨天在线" },
];
const REQUESTS: Request[] = [
  { name: "罗·特拉法尔加", c: "#5b9bd5", title: "超新星", mutual: 3 },
  { name: "波雅·汉库克", c: "#d6453e", title: "王下七武海", mutual: 1 },
];
const STATUS: Record<Status, { c: string; t: string }> = {
  online: { c: "var(--good)", t: "在线" },
  away: { c: "#e0b13e", t: "离开" },
  offline: { c: "var(--ink-faint)", t: "离线" },
};

const tab = ref<"all" | "online">("all");
const onlineCount = computed(() => FRIENDS.filter((f) => f.status === "online").length);
const list = computed(() => (tab.value === "online" ? FRIENDS.filter((f) => f.status === "online") : FRIENDS));
</script>

<template>
  <div class="screen-root scroll enter">
    <div class="screen-inner" style="max-width: 820px">
      <div class="kicker" style="font-size: 12px">船员名册</div>
      <h1 class="head" style="font-size: 40px; margin: 10px 0 4px">好友</h1>
      <div class="dim" style="font-size: 13px; margin-bottom: 24px">
        共 {{ FRIENDS.length }} 位好友 · {{ onlineCount }} 人在线
      </div>

      <!-- 添加好友 -->
      <div class="panel panel-pad add-row">
        <Ticks />
        <div class="field" style="flex: 1; height: 48px">
          <span class="ic mono">@</span>
          <input placeholder="输入船员 ID 或昵称添加好友…" />
        </div>
        <button class="btn btn--primary" style="min-width: 110px">添加好友</button>
      </div>

      <!-- 好友请求 -->
      <div v-if="REQUESTS.length > 0" style="margin-bottom: 22px">
        <div class="kicker" style="font-size: 11px; margin-bottom: 12px">好友请求 · {{ REQUESTS.length }}</div>
        <div style="display: flex; flex-direction: column; gap: 10px">
          <div v-for="(r, i) in REQUESTS" :key="i" class="panel friend-row">
            <Avatar :name="r.name" :size="44" :ring="false" />
            <div style="flex: 1; min-width: 0">
              <div style="font-size: 15px; color: var(--ink)">{{ r.name }}</div>
              <div class="mono faint" style="font-size: 11px; margin-top: 2px">{{ r.title }} · {{ r.mutual }} 位共同好友</div>
            </div>
            <button class="btn btn--primary" style="padding: 9px 18px; font-size: 13px">接受</button>
            <button class="btn btn--ghost" style="padding: 9px 14px; font-size: 13px">忽略</button>
          </div>
        </div>
      </div>

      <!-- 列表头 + 筛选 -->
      <div class="list-head">
        <div class="kicker" style="font-size: 11px">好友列表</div>
        <div class="seg">
          <button :class="['seg__opt', { 'is-active': tab === 'all' }]" @click="tab = 'all'">全部</button>
          <button :class="['seg__opt', { 'is-active': tab === 'online' }]" @click="tab = 'online'">在线</button>
        </div>
      </div>

      <!-- 好友 -->
      <div style="display: flex; flex-direction: column; gap: 10px">
        <div
          v-for="(f, i) in list"
          :key="i"
          class="panel friend-row"
          :style="{ opacity: f.status === 'offline' ? 0.62 : 1 }"
        >
          <div style="position: relative; flex-shrink: 0">
            <Avatar :name="f.name" :size="46" :ring="false" :glow="f.status === 'online'" />
            <span class="status-dot" :style="{ background: STATUS[f.status].c }" />
          </div>
          <div style="flex: 1; min-width: 0">
            <div style="display: flex; align-items: center; gap: 8px">
              <span class="friend-name">{{ f.name }}</span>
              <span class="mono" style="font-size: 10px" :style="{ color: STATUS[f.status].c }">{{ STATUS[f.status].t }}</span>
            </div>
            <div class="mono faint" style="font-size: 11px; margin-top: 2px">{{ f.title }} · {{ f.note }}</div>
          </div>
          <button
            v-if="f.status === 'online'"
            class="btn btn--primary"
            style="padding: 9px 16px; font-size: 13px"
            @click="emit('navigate', 'lobby')"
          >邀请对战</button>
          <button v-else class="btn btn--ghost" style="padding: 9px 16px; font-size: 13px">留言</button>
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

.add-row {
  position: relative;
  display: flex;
  gap: 12px;
  align-items: center;
  margin-bottom: 20px;
}

.list-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.friend-row {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 18px;
  transition: border-color 0.25s;
}
.friend-row:hover {
  border-color: var(--line-strong);
}
.friend-name {
  font-size: 15px;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.status-dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 13px;
  height: 13px;
  border-radius: 50%;
  border: 2px solid var(--surface);
}

@media (max-width: 700px) {
  .screen-root { padding: 76px 16px 24px; }
}
</style>
