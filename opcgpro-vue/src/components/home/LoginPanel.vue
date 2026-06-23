<script setup lang="ts">
import { ref, computed } from "vue";
import { HomeRequest } from "@/net/HomeProtocol";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";

const STATE_LABEL: Record<string, string> = {
  disconnected: "未连接",
  connecting: "连接中...",
  handshaking: "握手中...",
  connected: "已连接",
  reconnecting: "重连中...",
  recovering: "恢复中...",
  failed: "连接失败",
};

const account = ref("");
const password = ref("");
const connState = useStore(useNetStore, (s) => s.connState);
const error = useStore(useNetStore, (s) => s.error);
const canLogin = computed(() => connState.value === "connected");

function handleLogin() {
  if (!canLogin.value) return;
  if (!account.value.trim() || !password.value.trim()) return;
  HomeRequest.login(account.value.trim(), password.value.trim());
}
</script>

<template>
  <div class="flex h-screen flex-col items-center justify-center bg-gray-950">
    <div class="login-card w-80 rounded-2xl border border-gray-800 bg-gray-900 p-8 shadow-2xl">
      <h1 class="mb-1 text-center text-2xl font-bold text-white">GrandUMI</h1>
      <p class="mb-6 text-center text-xs text-gray-500">One Piece Card Game Online</p>

      <div class="mb-4 flex flex-col gap-3">
        <input
          v-model="account"
          class="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2.5 text-sm text-white outline-none transition-colors focus:border-orange-500 disabled:opacity-50"
          placeholder="账号"
          :disabled="!canLogin"
          autocomplete="username"
          @keydown.enter="handleLogin"
        />
        <input
          v-model="password"
          type="password"
          class="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2.5 text-sm text-white outline-none transition-colors focus:border-orange-500 disabled:opacity-50"
          placeholder="密码"
          :disabled="!canLogin"
          autocomplete="current-password"
          @keydown.enter="handleLogin"
        />
      </div>

      <p v-if="error" class="mb-3 text-center text-xs text-red-400">{{ error }}</p>

      <button
        :disabled="!canLogin"
        class="w-full rounded-lg bg-orange-500 py-2.5 text-sm font-bold text-white transition-colors hover:bg-orange-400 disabled:cursor-not-allowed disabled:bg-gray-700"
        @click="handleLogin"
      >
        {{ canLogin ? "登录" : STATE_LABEL[connState] }}
      </button>

      <div class="mt-4 flex items-center justify-center gap-1.5">
        <div
          :class="[
            'h-1.5 w-1.5 rounded-full',
            connState === 'connected'
              ? 'bg-green-400'
              : connState === 'connecting' || connState === 'handshaking'
                ? 'bg-yellow-400 animate-pulse'
                : 'bg-red-500',
          ]"
        />
        <span class="text-xs text-gray-500">{{ STATE_LABEL[connState] }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-card {
  animation: login-in 0.4s ease both;
}
@keyframes login-in {
  from { opacity: 0; transform: translateY(24px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>
