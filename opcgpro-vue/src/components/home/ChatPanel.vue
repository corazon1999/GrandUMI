<script setup lang="ts">
import { ref, watch, nextTick, useTemplateRef, computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";

/**
 * Comms 通讯面板（CLAUDE.md §5.2）。
 * 注意：外层 .panel 由 MainPanel 提供，本组件只负责内部结构。
 */

const chatMessages = useStore(useNetStore, (s) => s.chatMessages);
const playerName = useStore(useNetStore, (s) => s.playerName);
const onlineCount = useStore(useNetStore, (s) => s.onlineCount);
const input = ref("");
const bottomRef = useTemplateRef<HTMLDivElement>("bottom");

const onlineText = computed(() => {
  const n = onlineCount.value ?? 0;
  if (n >= 1000) return (n / 1000).toFixed(1) + "k";
  return String(n);
});

watch(chatMessages, async () => {
  await nextTick();
  bottomRef.value?.scrollIntoView({ behavior: "smooth" });
});

function send() {
  const text = input.value.trim();
  if (!text) return;
  HomeRequest.sendChat(text, playerName.value);
  input.value = "";
}
</script>

<template>
  <div class="chat-root">
    <!-- 头部 -->
    <div class="chat-header">
      <span class="kicker chat-header__title">聊天</span>
      <span class="chat-header__line" />
      <span class="mono dim chat-header__online">
        <span class="dot dot--live" /> 在线 {{ onlineText }}
      </span>
    </div>

    <!-- 消息列表 -->
    <div class="chat-list">
      <div
        v-for="(msg, i) in chatMessages"
        :key="i"
        :class="[
          'chat-msg',
          {
            'chat-msg--self': msg.Name && msg.Name === playerName,
            'chat-msg--other': msg.Name && msg.Name !== playerName,
            'chat-msg--system': !msg.Name,
          },
        ]">
        <template v-if="!msg.Name">
          <span class="chat-msg__line" />
          <span class="chat-msg__system">{{ msg.Msg }}</span>
          <span class="chat-msg__line" />
        </template>
        <template v-else>
          <span class="mono faint chat-msg__name">{{ msg.Name }}</span>
          <span class="chat-msg__bubble">{{ msg.Msg }}</span>
        </template>
      </div>
      <div ref="bottom" />
    </div>

    <!-- 输入区 -->
    <div class="chat-input">
      <div class="field chat-input__field">
        <input
          v-model="input"
          placeholder="// 输入消息..."
          :maxlength="100"
          @keydown.enter="send" />
      </div>
      <button
        class="btn btn--primary chat-input__send"
        :disabled="!input.trim()"
        @click="send">
        发
      </button>
    </div>
  </div>
</template>

<style scoped>
.chat-root {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  font-family: var(--font-ui);
  color: var(--ink);
}

/* ── 头部 ── */
.chat-header {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 18px;
  border-bottom: 1px solid var(--line);
}
.chat-header__title {
  font-size: 12px;
  flex-shrink: 0;
}
.chat-header__line {
  flex: 1;
  height: 1px;
  background: var(--line);
}
.chat-header__online {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  letter-spacing: 0.1em;
  flex-shrink: 0;
}

/* ── 消息列表 ── */
.chat-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.chat-msg {
  display: flex;
  flex-direction: column;
  gap: 3px;
  max-width: 85%;
  animation: msg-in 0.25s var(--ease-maritime);
}
.chat-msg--self {
  align-self: flex-end;
  align-items: flex-end;
}
.chat-msg--other {
  align-self: flex-start;
  align-items: flex-start;
}
.chat-msg--system {
  align-self: stretch;
  max-width: 100%;
  flex-direction: row;
  align-items: center;
  gap: 8px;
  margin: 6px 0;
  font-family: var(--font-mono);
  font-size: 10px;
  color: var(--ink-faint);
  letter-spacing: 0.18em;
  text-transform: uppercase;
}
.chat-msg__line {
  flex: 1;
  height: 1px;
  background: var(--line);
}
.chat-msg__system {
  white-space: nowrap;
}
.chat-msg__name {
  font-size: 9px;
  letter-spacing: 0.1em;
}
.chat-msg__bubble {
  font-size: 13px;
  padding: 9px 12px;
  border-radius: var(--radius);
  word-break: break-word;
  line-height: 1.4;
}
.chat-msg--self .chat-msg__bubble {
  background: linear-gradient(180deg, var(--primary-bright), var(--primary));
  color: var(--on-primary);
  border: 1px solid var(--primary);
  box-shadow: 0 6px 18px -8px var(--primary-glow);
}
.chat-msg--other .chat-msg__bubble {
  background: var(--surface2);
  border: 1px solid var(--line);
  color: var(--ink);
}
@keyframes msg-in {
  from {
    opacity: 0;
    transform: translateY(4px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* ── 输入区 ── */
.chat-input {
  display: flex;
  gap: 8px;
  padding: 12px 14px;
  border-top: 1px solid var(--line);
}
.chat-input__field {
  flex: 1;
  height: 44px;
}
.chat-input__field input {
  font-size: 13px;
}
.chat-input__send {
  padding: 0 18px;
  height: 44px;
  flex-shrink: 0;
}
</style>
