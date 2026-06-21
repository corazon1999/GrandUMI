<script setup lang="ts">
import { ref } from "vue";
import type { PlaybackSpeed } from "@/types/playback";

defineProps<{
  currentTurn: number;
  currentStep: number;
  totalTurns: number;
  totalSteps: number;
  isPlaying: boolean;
  isEnded: boolean;
  speed: PlaybackSpeed;
}>();
const emit = defineEmits<{
  (e: "play"): void;
  (e: "pause"): void;
  (e: "stepForward"): void;
  (e: "stepBackward"): void;
  (e: "speedChange", speed: PlaybackSpeed): void;
}>();

const SPEEDS: PlaybackSpeed[] = [0.5, 1, 2, 4];
const SPEED_LABELS: Record<PlaybackSpeed, string> = { 0.5: "0.5x", 1: "1x", 2: "2x", 4: "4x" };
const collapsed = ref(false);
</script>

<template>
  <div class="playback-in fixed bottom-4 left-1/2 z-50 -translate-x-1/2">
    <button
      class="absolute -top-3 left-1/2 flex h-3 w-6 -translate-x-1/2 items-center justify-center rounded-t-md bg-gray-800"
      @click="collapsed = !collapsed"
    >
      <span class="text-[11px] text-gray-500">{{ collapsed ? "▲" : "▼" }}</span>
    </button>

    <Transition name="collapse">
      <div
        v-if="!collapsed"
        class="flex min-w-[320px] flex-col gap-2 rounded-xl border border-gray-700 bg-gray-900/95 px-4 py-3 shadow-2xl backdrop-blur-sm"
      >
        <div class="flex items-center justify-between text-xs text-gray-400">
          <span>回合 {{ currentTurn + 1 }}/{{ totalTurns }}</span>
          <span>步骤 {{ currentStep }}/{{ totalSteps }}</span>
        </div>

        <div class="h-1 overflow-hidden rounded-full bg-gray-800">
          <div
            class="h-full rounded-full bg-green-500 transition-all duration-200"
            :style="{ width: totalSteps > 0 ? `${(currentStep / totalSteps) * 100}%` : '0%' }"
          />
        </div>

        <div class="flex items-center justify-center gap-2">
          <button
            :disabled="currentStep <= 0"
            class="flex h-8 w-8 items-center justify-center rounded-lg bg-gray-800 text-white transition-colors hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
            title="上一步"
            @click="emit('stepBackward')"
          >
            ⏮
          </button>

          <button v-if="isEnded" class="flex h-10 w-10 items-center justify-center rounded-full bg-green-600 text-white hover:bg-green-500" title="重新播放" @click="emit('play')">
            ↺
          </button>
          <button v-else-if="isPlaying" class="flex h-10 w-10 items-center justify-center rounded-full bg-yellow-600 text-white hover:bg-yellow-500" title="暂停" @click="emit('pause')">
            ⏸
          </button>
          <button v-else class="flex h-10 w-10 items-center justify-center rounded-full bg-green-600 text-white hover:bg-green-500" title="播放" @click="emit('play')">
            ▶
          </button>

          <button
            :disabled="isEnded"
            class="flex h-8 w-8 items-center justify-center rounded-lg bg-gray-800 text-white transition-colors hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
            title="下一步"
            @click="emit('stepForward')"
          >
            ⏭
          </button>
        </div>

        <div class="flex items-center justify-center gap-1">
          <button
            v-for="s in SPEEDS"
            :key="s"
            :class="['rounded px-2 py-0.5 text-xs font-medium transition-colors', speed === s ? 'bg-green-600 text-white' : 'bg-gray-800 text-gray-400 hover:bg-gray-700 hover:text-white']"
            @click="emit('speedChange', s)"
          >
            {{ SPEED_LABELS[s] }}
          </button>
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.playback-in { animation: pb-in 0.3s ease both; }
@keyframes pb-in {
  from { opacity: 0; transform: translate(-50%, 30px); }
  to { opacity: 1; transform: translate(-50%, 0); }
}
.collapse-enter-active,
.collapse-leave-active { transition: opacity 0.15s ease; }
.collapse-enter-from,
.collapse-leave-to { opacity: 0; }
</style>
