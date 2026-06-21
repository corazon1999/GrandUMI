<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import { NetManager } from "@/net/NetManager";
import { useGameStore } from "@/store/gameStore";

const roomId = ref("");
const router = useRouter();

function handleSpectate() {
  if (!roomId.value.trim()) return;
  NetManager.send({ proto: "MsgSpectateRoom", roomId: roomId.value.trim() } as never);
  useGameStore.getState().setMode("Observer");
  router.push("/game");
}
</script>

<template>
  <div class="flex h-screen items-center justify-center bg-gray-950">
    <div class="w-96 rounded-xl bg-gray-800 p-8 shadow-2xl">
      <h1 class="mb-6 text-center text-2xl font-bold text-white">观战</h1>
      <input
        v-model="roomId"
        class="mb-4 w-full rounded-lg bg-gray-700 px-4 py-2 text-white outline-none"
        placeholder="输入房间 ID"
        @keydown.enter="handleSpectate"
      />
      <button
        class="w-full rounded-lg bg-purple-600 py-2 font-bold text-white transition-colors hover:bg-purple-500"
        @click="handleSpectate"
      >
        开始观战
      </button>
      <button
        class="mt-2 w-full rounded-lg bg-gray-700 py-2 text-gray-300 transition-colors hover:text-white"
        @click="router.push('/home')"
      >
        返回大厅
      </button>
    </div>
  </div>
</template>
