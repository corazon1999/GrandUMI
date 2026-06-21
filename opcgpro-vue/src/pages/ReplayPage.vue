<script setup lang="ts">
import { ref, watch, onMounted } from "vue";
import { useRoute } from "vue-router";
import { useGameStore } from "@/store/gameStore";

/**
 * 回放页面（M6 stub）
 * 通过 fetch 拉取服务端 jsonl，逐 tick 应用 MsgGameState。
 */
const route = useRoute();
const id = route.params.id as string;
const steps = ref<unknown[]>([]);
const idx = ref(0);

onMounted(() => {
  if (!id) return;
  fetch(`/api/replay/${id}`)
    .then((r) => (r.ok ? r.text() : Promise.reject(new Error("not found"))))
    .then((text) => {
      const lines = text.split("\n").filter(Boolean);
      steps.value = lines
        .map((l) => { try { return JSON.parse(l); } catch { return null; } })
        .filter(Boolean) as unknown[];
    })
    .catch(() => { steps.value = []; });
});

watch([idx, steps], () => {
  if (idx.value >= steps.value.length) return;
  const step = steps.value[idx.value] as { kind: string; snapshot?: object };
  if (step.kind === "state" && step.snapshot) {
    useGameStore.getState().syncFromServer(step.snapshot as never);
  }
});
</script>

<template>
  <div v-if="!id" class="text-white">无效的回放 ID</div>
  <div v-else-if="steps.length === 0" class="p-6 text-white">加载中…</div>
  <div
    v-else
    class="fixed bottom-4 left-1/2 z-50 flex -translate-x-1/2 items-center gap-3 rounded-xl bg-gray-900 px-4 py-2 text-white"
  >
    <button class="px-2" @click="idx = Math.max(0, idx - 1)">⏮</button>
    <span>{{ idx + 1 }} / {{ steps.length }}</span>
    <button class="px-2" @click="idx = Math.min(steps.length - 1, idx + 1)">⏭</button>
  </div>
</template>
