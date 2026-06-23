<script setup lang="ts">
// MessageBox 宿主 —— 订阅命令式内核 messageBus（@/components/ui/MessageBox.ts）渲染 toast。
// 挂在全局壳里一次即可（Phase 5b 起在 App.vue / 各页面顶层挂载）。
import { ref, onMounted, onUnmounted } from "vue";
import { messageBus, type MessageType } from "@/components/ui/MessageBox";

interface Toast {
  id: number;
  text: string;
  type: MessageType;
}

let seq = 0;
const toasts = ref<Toast[]>([]);

const colorMap: Record<MessageType, string> = {
  info: "bg-gray-800 border-gray-600",
  success: "bg-green-900 border-green-600",
  error: "bg-red-900 border-red-600",
};

function onShow({ text, type }: { text: string; type: MessageType }) {
  const id = ++seq;
  toasts.value.push({ id, text, type });
  setTimeout(() => {
    toasts.value = toasts.value.filter((t) => t.id !== id);
  }, 3000);
}

onMounted(() => messageBus.on("show", onShow));
onUnmounted(() => messageBus.off("show", onShow));
</script>

<template>
  <div
    class="pointer-events-none fixed left-1/2 top-4 z-50 flex -translate-x-1/2 flex-col gap-2"
  >
    <TransitionGroup name="toast">
      <div
        v-for="t in toasts"
        :key="t.id"
        :class="['rounded-lg border px-4 py-2 text-sm text-white shadow-lg', colorMap[t.type]]"
      >
        {{ t.text }}
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition: all 0.2s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}
</style>
