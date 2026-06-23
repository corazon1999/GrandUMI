<script setup lang="ts">
import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";

const connState = useStore(useNetStore, (s) => s.connState);
const isOk = computed(() => connState.value === "connected");
const isPending = computed(
  () => connState.value === "connecting" || connState.value === "handshaking",
);

const dotClass = computed(() => {
  if (isOk.value) return "net-dot--ok";
  if (isPending.value) return "net-dot--pending";
  return "net-dot--down";
});
</script>

<template>
  <div class="net-state">
    <span class="net-dot" :class="dotClass" />
    <span class="net-label">
      {{ isOk ? "已连接" : isPending ? "连接中" : "未连接" }}
    </span>
  </div>
</template>

<style scoped>
.net-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.35rem;
  padding: 0.4rem 0.3rem;
  font-family: var(--font-mono);
  font-size: 0.55rem;
  letter-spacing: 0.15em;
  color: var(--color-text-muted);
  border-top: 1px solid var(--color-divider);
  width: 100%;
  transition: color 200ms;
}
.net-state:hover { color: var(--color-primary); }
.net-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  transition: all 300ms;
}
.net-dot--ok {
  background: var(--color-primary);
  box-shadow: 0 0 8px var(--color-primary), 0 0 14px var(--color-primary-glow);
  animation: glow-pulse 2.4s ease-in-out infinite;
}
.net-dot--pending {
  background: var(--color-primary);
  box-shadow: 0 0 8px var(--color-primary-glow);
  animation: pulse 1s ease-in-out infinite;
}
.net-dot--down {
  background: var(--color-secondary);
  box-shadow: 0 0 8px var(--color-secondary-glow);
  animation: pulse 1.2s ease-in-out infinite;
}
.net-label {
  text-transform: uppercase;
  writing-mode: vertical-rl;
  font-size: 0.5rem;
  letter-spacing: 0.25em;
}
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>
