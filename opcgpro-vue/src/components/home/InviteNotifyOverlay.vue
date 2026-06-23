<script setup lang="ts">
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";

const invite = useStore(useNetStore, (s) => s.incomingInvite);

function accept() {
  if (!invite.value) return;
  HomeRequest.respondInvite(invite.value.inviteId, true);
  useNetStore.getState().setIncomingInvite(null);
}

function decline() {
  if (!invite.value) return;
  HomeRequest.respondInvite(invite.value.inviteId, false);
  useNetStore.getState().setIncomingInvite(null);
}
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div
        v-if="invite"
        class="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 backdrop-blur-sm"
      >
        <div class="w-80 rounded-xl border border-orange-700 bg-gray-900 p-6 shadow-2xl">
          <p class="mb-1 text-center text-base text-white">对战邀请</p>
          <p class="mb-1 truncate text-center text-sm font-bold text-orange-400">{{ invite.fromName }}</p>
          <p class="mb-5 text-center text-xs text-gray-400">邀请你进入友谊战房间</p>
          <div class="flex gap-2">
            <button
              class="flex-1 rounded-lg bg-gray-800 py-2 text-sm text-gray-300 transition-colors hover:bg-gray-700"
              @click="decline"
            >
              拒绝
            </button>
            <button
              class="flex-1 rounded-lg bg-orange-500 py-2 text-sm font-bold text-white transition-colors hover:bg-orange-400"
              @click="accept"
            >
              接受
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.modal-enter-active, .modal-leave-active { transition: opacity 0.2s; }
.modal-enter-from, .modal-leave-to { opacity: 0; }
</style>
