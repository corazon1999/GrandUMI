<script setup lang="ts">
/**
 * 共享标题块：kicker + title + 菱形分隔条 + 副标题 slot。
 * 用于 LobbyScreen / DeckScreen / HistoryScreen 顶部。
 */
withDefaults(
  defineProps<{
    kicker?: string;
    title: string;
    subtitle?: string;
    /** 标题字号 px（默认 28） */
    titleSize?: number;
  }>(),
  { titleSize: 44, kicker: "", subtitle: "" },
);
</script>

<template>
  <div class="screen-head">
    <span v-if="kicker" class="kicker">{{ kicker }}</span>
    <h1
      v-if="title"
      class="head glow-title screen-head__title"
      :style="{ fontSize: titleSize + 'px' }"
    >
      {{ title }}
    </h1>
    <div v-if="title || $slots.default" class="screen-head__divider">
      <span class="screen-head__bar" />
      <span class="screen-head__diamond" />
      <span class="screen-head__bar" />
    </div>
    <p v-if="subtitle" class="dim screen-head__subtitle">{{ subtitle }}</p>
    <slot />
  </div>
</template>

<style scoped>
.screen-head {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  text-align: center;
}
.screen-head__title {
  margin: 0;
  letter-spacing: 0.02em;
  line-height: 1.15;
}
.screen-head__divider {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 220px;
  margin: 4px 0 2px;
}
.screen-head__bar {
  flex: 1;
  height: 1px;
  background: linear-gradient(to right, transparent, var(--line-strong), transparent);
}
.screen-head__diamond {
  width: 7px;
  height: 7px;
  background: var(--primary);
  transform: rotate(45deg);
  box-shadow: 0 0 8px var(--primary-glow);
}
.screen-head__subtitle {
  margin: 0;
  font-size: 13px;
}
</style>