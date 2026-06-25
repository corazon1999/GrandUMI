<script setup lang="ts">
import { computed } from "vue";

/**
 * DON 活跃 / 休息格（毛毡牌桌：.bf-box「DON + 数量」，源自 battle.jsx DonCluster）。
 * 活跃且有量时高亮为主题金；可交互时（我方回合选 DON）整体可点。
 */
const props = withDefaults(
  defineProps<{
    label: string;
    count: number;
    state: "active" | "rest";
    selected?: boolean;
    stagedCount?: number;
    canInteract?: boolean;
  }>(),
  { selected: false, stagedCount: 0, canInteract: false },
);
const emit = defineEmits<{ (e: "click"): void }>();

const clickable = computed(() => props.count > 0 && props.canInteract);
const on = computed(() => props.state === "active" && props.count > 0);
</script>

<template>
  <div class="bf-well">
    <span class="kicker">{{ label }}</span>
    <button
      type="button"
      :disabled="count <= 0 || !canInteract"
      :class="['bf-box bf-don-box relative', { 'is-on': on, 'is-selected': selected }]"
      @click="clickable && emit('click')"
    >
      <span class="bf-don-box__t">DON</span>
      <span class="bf-don-box__n">{{ count }}</span>
      <!-- 拟依附张数徽标：再点 +1，达上限后再点取消（#144 复数咚依附） -->
      <span
        v-if="stagedCount > 0"
        class="absolute right-1 top-1 z-30 rounded-full bg-amber-400 px-1.5 py-0.5 text-[10px] font-black leading-none text-black shadow ring-2 ring-amber-200"
      >依附×{{ stagedCount }}</span>
    </button>
  </div>
</template>

<style scoped>
.bf-don-box {
  width: 60px;
  height: 90px;
  cursor: default;
}
.bf-don-box:not(:disabled) {
  cursor: pointer;
}
.bf-don-box__t {
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 11px;
  letter-spacing: 0.04em;
  color: var(--ink-dim);
}
.bf-don-box__n {
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 22px;
  color: var(--ink);
  margin-top: 2px;
}
.bf-don-box.is-on {
  border-color: var(--primary);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.14),
    0 0 24px -4px var(--primary-glow);
}
.bf-don-box.is-on .bf-don-box__t,
.bf-don-box.is-on .bf-don-box__n {
  color: var(--primary);
}
.bf-don-box.is-selected {
  border-color: var(--primary-bright);
  box-shadow:
    0 0 0 2px var(--primary-glow),
    0 0 20px -2px var(--primary-glow);
}
</style>
