<script setup lang="ts">
import { watch, nextTick, ref } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";

const logLines = useStore(useGameStore, (s) => s.logLines);
const bottomRef = ref<HTMLDivElement | null>(null);

watch(() => logLines.value.length, () => nextTick(() => bottomRef.value?.scrollIntoView({ block: "end" })));
</script>

<template>
  <div v-if="logLines.length === 0" class="mt-2 text-[11px] text-slate-600">暂无操作</div>
  <div v-else class="mt-2 flex flex-col gap-1">
    <div v-for="l in logLines" :key="l.id"
      :class="[l.text.startsWith('——') ? 'py-0.5 text-center text-[11px] font-bold text-amber-300/80' : 'text-[11px] leading-snug text-slate-300']">
      {{ l.text }}
    </div>
    <div ref="bottomRef" />
  </div>
</template>
