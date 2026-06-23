<script setup lang="ts">
import { ref } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal.vue";

const open = ref(false);
const isPending = useStore(useGameStore, (s) => s.isPending);

function handleSurrender() {
  open.value = false;
  GameRequest.surrender();
}
</script>

<template>
  <button
    :disabled="isPending"
    class="absolute right-4 top-4 z-20 h-9 w-9 rounded-md bg-slate-800 text-lg leading-none text-slate-300 transition-colors hover:bg-slate-700 disabled:cursor-not-allowed disabled:bg-gray-600"
    aria-label="打开游戏菜单"
    @click="open = true"
  >
    ≡
  </button>

  <Modal :open="open" title="游戏菜单" @close="open = false">
    <div class="flex flex-col gap-2">
      <button class="w-full rounded-lg bg-gray-700 py-2 text-sm text-white transition-colors hover:bg-gray-600" @click="open = false">
        继续游戏
      </button>
      <button class="w-full rounded-lg py-2 text-sm text-red-400 transition-colors hover:bg-gray-700" @click="handleSurrender">
        投降
      </button>
    </div>
  </Modal>
</template>
