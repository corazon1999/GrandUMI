<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from "vue";
import { eventBus } from "@/net/eventBus";

const countdown = ref<number | null>(null);
let timer: ReturnType<typeof setInterval> | null = null;

function onDisconnect(payload: { gracePeriodSeconds: number }) {
  countdown.value = payload.gracePeriodSeconds;
}
function onReconnect() {
  countdown.value = null;
}

onMounted(() => {
  eventBus.on("opponentDisconnected", onDisconnect);
  eventBus.on("opponentReconnected", onReconnect);
});
onUnmounted(() => {
  eventBus.off("opponentDisconnected", onDisconnect);
  eventBus.off("opponentReconnected", onReconnect);
  if (timer) clearInterval(timer);
});

watch(countdown, (n) => {
  if (timer) { clearInterval(timer); timer = null; }
  if (n === null || n <= 0) return;
  timer = setInterval(() => {
    countdown.value = countdown.value !== null && countdown.value > 0 ? countdown.value - 1 : null;
  }, 1000);
});
</script>

<template>
  <div
    v-if="countdown !== null"
    class="absolute left-1/2 top-4 z-40 -translate-x-1/2 animate-pulse rounded-lg bg-yellow-500/90 px-6 py-2 text-sm font-bold text-black shadow-lg"
  >
    对手已断线，等待重连 {{ countdown }}s
  </div>
</template>
