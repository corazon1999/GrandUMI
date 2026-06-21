<script setup lang="ts">
import { onMounted, onUnmounted, watch } from "vue";
import { useRouter } from "vue-router";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import { HomeRequest } from "@/net/HomeProtocol";

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ (e: "close"): void }>();

const players = useStore(useNetStore, (s) => s.playerList);
const account = useStore(useNetStore, (s) => s.account);
const router = useRouter();

const STATUS_LABEL: Record<string, { text: string; cls: string }> = {
  idle:     { text: "空闲",   cls: "text-green-400" },
  matching: { text: "匹配中", cls: "text-yellow-400" },
  playing:  { text: "对战中", cls: "text-red-400" },
};

let timer: ReturnType<typeof setInterval> | null = null;

function startPolling() {
  HomeRequest.requestPlayerList();
  timer = setInterval(() => HomeRequest.requestPlayerList(), 4000);
}

function stopPolling() {
  if (timer) { clearInterval(timer); timer = null; }
}

watch(() => props.open, (v) => {
  if (v) startPolling(); else stopPolling();
}, { immediate: true });

onUnmounted(stopPolling);

function handleInvite(p: { account: string }) {
  HomeRequest.invitePlayer(p.account);
}

function handleSpectate(p: { account: string; roomId?: string | null }) {
  if (!p.roomId) return;
  HomeRequest.spectateRoom(p.roomId);
  useGameStore.getState().setMode("Observer");
  emit("close");
  router.push("/game");
}
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div
        v-if="open"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
        @click.self="emit('close')"
      >
        <div class="w-80 rounded-xl border border-gray-700 bg-gray-900 p-4 shadow-2xl">
          <div class="mb-3 flex items-center justify-between">
            <h3 class="text-sm font-bold text-white">在线玩家</h3>
            <button class="text-gray-500 hover:text-white" @click="emit('close')">✕</button>
          </div>

          <div class="flex max-h-96 flex-col gap-1 overflow-y-auto">
            <p v-if="players.length === 0" class="py-8 text-center text-xs text-gray-600">暂无在线玩家</p>
            <div
              v-for="p in players"
              :key="p.account"
              class="flex items-center gap-2 rounded-lg border border-gray-800 bg-gray-800/60 px-3 py-2"
            >
              <div class="min-w-0 flex-1">
                <p class="truncate text-sm font-medium text-white">
                  {{ p.name }}
                  <span v-if="p.account === account" class="ml-1 text-[10px] text-orange-400">（我）</span>
                </p>
                <p :class="['text-[10px]', (STATUS_LABEL[p.status] ?? STATUS_LABEL.idle).cls]">
                  {{ (STATUS_LABEL[p.status] ?? STATUS_LABEL.idle).text }}
                </p>
              </div>
              <template v-if="p.account !== account">
                <button
                  v-if="p.status === 'playing' && p.roomId"
                  class="rounded-lg bg-purple-600 px-3 py-1 text-xs font-bold text-white transition-colors hover:bg-purple-500"
                  @click="handleSpectate(p)"
                >
                  观战
                </button>
                <button
                  v-else
                  :disabled="p.status !== 'idle'"
                  :class="[
                    'rounded-lg px-3 py-1 text-xs font-bold transition-colors',
                    p.status === 'idle'
                      ? 'bg-orange-500 text-white hover:bg-orange-400'
                      : 'cursor-not-allowed bg-gray-700 text-gray-500',
                  ]"
                  @click="handleInvite(p)"
                >
                  邀请对战
                </button>
              </template>
            </div>
          </div>

          <p class="mt-2 text-center text-[10px] text-gray-500">邀请对方接受后，双方进入友谊战房间再选卡组</p>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.modal-enter-active, .modal-leave-active { transition: opacity 0.2s; }
.modal-enter-from, .modal-leave-to { opacity: 0; }
</style>
