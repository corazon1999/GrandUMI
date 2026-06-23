<script setup lang="ts">
import { ref, watch } from "vue";

/** 图片头像（领航卡卡图）。无图时回退昵称首字母。边框/辉光用主题金。 */
const props = withDefaults(
  defineProps<{
    src?: string;
    name?: string;
    size?: number;
    ring?: boolean;
    glow?: boolean;
  }>(),
  { src: "", name: "", size: 44, ring: true, glow: false },
);

const broken = ref(false);
watch(
  () => props.src,
  () => {
    broken.value = false;
  },
);
</script>

<template>
  <div
    class="gd-avatar"
    :style="{
      width: size + 'px',
      height: size + 'px',
      border: ring ? '2px solid var(--primary)' : '1px solid var(--line)',
      boxShadow: glow
        ? '0 0 24px -4px var(--primary-glow), inset 0 1px 0 rgba(255,255,255,.3)'
        : 'inset 0 1px 0 rgba(255,255,255,.2)',
    }"
  >
    <img
      v-if="src && !broken"
      :src="src"
      alt="头像"
      class="gd-avatar__img"
      :draggable="false"
      loading="lazy"
      @error="broken = true"
    />
    <span v-else class="gd-avatar__fallback" :style="{ fontSize: size * 0.4 + 'px' }">
      {{ name ? name[0].toUpperCase() : "?" }}
    </span>
  </div>
</template>

<style scoped>
.gd-avatar {
  border-radius: 50%;
  flex-shrink: 0;
  position: relative;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--surface2);
}
.gd-avatar__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: top;
  transform: scale(1.08);
}
.gd-avatar__fallback {
  font-family: var(--font-head);
  font-weight: 900;
  color: var(--primary);
}
</style>
