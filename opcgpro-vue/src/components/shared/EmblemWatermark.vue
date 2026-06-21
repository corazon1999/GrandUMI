<script setup lang="ts">
withDefaults(
  defineProps<{
    themeKey?: "pirate" | "navy";
    size?: number;
    opacity?: number;
  }>(),
  { themeKey: "pirate", size: 640, opacity: 0.06 },
);
</script>

<template>
  <div class="emblem-watermark" :style="{ opacity }">
    <!-- 海贼：路飞骷髅旗（草帽+交叉骨）-->
    <img
      v-if="themeKey === 'pirate'"
      src="/emblems/jolly-roger.png"
      class="emblem-img emblem-pirate"
      :width="size * 0.72"
      :height="size * 0.72"
      aria-hidden="true" />

    <!-- 海军：MARINE 海鸥标志 -->
    <img
      v-else
      src="/emblems/marine.svg"
      class="emblem-img emblem-navy"
      :width="size"
      :height="size * 0.5"
      aria-hidden="true" />
  </div>
</template>

<style scoped>
.emblem-watermark {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  pointer-events: none;
  animation: grandumi-breathe 9s ease-in-out infinite;
}

/* 用 invert + mix-blend-mode: screen 实现深色背景上的彩色水印：
   invert(1) 把深色线条变白，grey 背景变暗（消失）；
   screen 在深色游戏背景上让亮色可见、暗色透明 */
.emblem-img {
  mix-blend-mode: screen;
  object-fit: contain;
}

/* 海贼主题：金色 (#e8b04b) 骷髅旗 */
.emblem-pirate {
  filter: invert(1) sepia(1) saturate(4) hue-rotate(350deg) brightness(0.9);
  transform: translateY(-8%);
}

/* 海军主题：蓝色 (#5b9bd5) MARINE 标志 */
.emblem-navy {
  filter: invert(1) sepia(1) saturate(5) hue-rotate(190deg) brightness(0.85);
  margin-top: -6%;
}
</style>
