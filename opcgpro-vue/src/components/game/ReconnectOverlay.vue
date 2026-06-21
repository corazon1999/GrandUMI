<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { useRouter } from "vue-router";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";

const router = useRouter();
const connState = useStore(useNetStore, (s) => s.connState);
const reconnectCountdown = useStore(useNetStore, (s) => s.reconnectCountdown);
const remaining = ref(0);
let timer: ReturnType<typeof setInterval> | null = null;

watch(
  [reconnectCountdown, connState],
  () => {
    if (timer) { clearInterval(timer); timer = null; }
    if (connState.value !== "reconnecting") return;
    remaining.value = reconnectCountdown.value;
    timer = setInterval(() => { remaining.value = Math.max(0, remaining.value - 1); }, 1000);
  },
  { immediate: true },
);

const visible = computed(() => connState.value === "reconnecting" || connState.value === "recovering");
</script>

<template>
  <Transition name="fade">
    <div
      v-if="visible"
      class="fixed inset-0 z-50 flex flex-col items-center justify-center bg-black/80 backdrop-blur-sm"
    >
      <div class="mb-4 h-12 w-12 animate-spin rounded-full border-4 border-orange-400 border-t-transparent" />
      <template v-if="connState === 'reconnecting'">
        <p class="text-xl font-bold text-white">连接已断开</p>
        <p class="mt-2 text-gray-400">{{ remaining > 0 ? `${remaining} 秒后重试...` : "正在重连..." }}</p>
      </template>
      <p v-else-if="connState === 'recovering'" class="text-xl font-bold text-white">正在恢复游戏状态...</p>
    </div>
  </Transition>

  <div
    v-if="connState === 'failed'"
    class="fixed inset-0 z-50 flex flex-col items-center justify-center bg-black/90"
  >
    <p class="mb-4 text-2xl font-bold text-red-400">连接失败</p>
    <p class="mb-6 text-gray-400">无法重新连接到服务器，游戏已结束</p>
    <button class="rounded-lg bg-orange-500 px-6 py-2 text-white transition-colors hover:bg-orange-400" @click="router.push('/home')">
      返回大厅
    </button>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from,
.fade-leave-to { opacity: 0; }
</style>
