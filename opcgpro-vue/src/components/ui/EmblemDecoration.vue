<script setup lang="ts">
import { computed, ref, onMounted, onBeforeUnmount } from "vue";

/**
 * 主题 emblem 装饰组件
 * - 根据当前 :root[data-theme] 自动渲染
 * - pirate：草帽 + 骷髅 + 交叉骨
 * - marine：罗盘 + 海军锚
 * - 用 SVG 矢量绘制，主题色绑定 CSS variable
 */
interface Props {
  size?: number;
  opacity?: number;
  /** 装饰样式 */
  variant?: "center" | "corner" | "watermark";
}

const props = withDefaults(defineProps<Props>(), {
  size: 400,
  opacity: 0.08,
  variant: "center",
});

const theme = ref<"pirate" | "marine">("pirate");
let observer: MutationObserver | null = null;

onMounted(() => {
  // 初始值
  const initial = document.documentElement.dataset.theme;
  theme.value = initial === "marine" ? "marine" : "pirate";

  // 监听主题切换
  observer = new MutationObserver(() => {
    const next = document.documentElement.dataset.theme;
    theme.value = next === "marine" ? "marine" : "pirate";
  });
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ["data-theme"],
  });
});

onBeforeUnmount(() => {
  if (observer) observer.disconnect();
});

const isMarine = computed(() => theme.value === "marine");
</script>

<template>
  <!-- 海贼风：草帽 + 骷髅 + 交叉骨 -->
  <svg
    v-if="!isMarine"
    class="emblem emblem--pirate"
    :class="`emblem--${variant}`"
    :width="size"
    :height="size"
    viewBox="0 0 200 200"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    aria-hidden="true"
  >
    <g :style="{ opacity: opacity }">
      <!-- 头骨轮廓 -->
      <path
        d="M100 20c-30 0-50 22-58 48-3 12 0 28 8 36 3 3 3 8 0 12-4 4-8 14-3 22 4 8 14 8 20 4 4-4 10-4 14 0 8 6 22 6 32 0 4-4 10-4 14 0 6 4 16 4 20-4 5-8 1-18-3-22-3-4-3-9 0-12 8-8 11-24 8-36-8-26-28-48-58-48z"
        fill="currentColor"
      />
      <!-- 眼窝 -->
      <ellipse cx="82" cy="78" rx="11" ry="13" fill="var(--color-bg-void)" />
      <ellipse cx="118" cy="78" rx="11" ry="13" fill="var(--color-bg-void)" />
      <!-- 眼窝内红点 -->
      <circle cx="82" cy="78" r="3" fill="var(--color-secondary)" />
      <circle cx="118" cy="78" r="3" fill="var(--color-secondary)" />
      <!-- 鼻孔 -->
      <path d="M95 100h10l-2 6h-6z" fill="var(--color-bg-void)" />
      <!-- 牙齿 -->
      <g stroke="var(--color-bg-void)" stroke-width="3" stroke-linecap="round">
        <line x1="84" y1="118" x2="116" y2="118" />
        <line x1="86" y1="124" x2="90" y2="124" />
        <line x1="94" y1="124" x2="98" y2="124" />
        <line x1="102" y1="124" x2="106" y2="124" />
        <line x1="110" y1="124" x2="114" y2="124" />
      </g>
      <!-- 帽檐（草帽） -->
      <ellipse cx="100" cy="32" rx="56" ry="10" fill="var(--color-primary)" />
      <path d="M60 32c0-8 18-14 40-14s40 6 40 14H60z" fill="var(--color-primary)" />
      <rect x="56" y="28" width="88" height="6" fill="var(--color-secondary)" />
    </g>
    <!-- 交叉骨（X 形） -->
    <g :style="{ opacity: opacity }" stroke="currentColor" stroke-width="10" stroke-linecap="round" fill="none">
      <line x1="35" y1="155" x2="100" y2="115" />
      <line x1="165" y1="155" x2="100" y2="115" />
      <line x1="35" y1="115" x2="100" y2="155" />
      <line x1="165" y1="115" x2="100" y2="155" />
      <circle cx="35" cy="155" r="6" fill="currentColor" />
      <circle cx="35" cy="115" r="6" fill="currentColor" />
      <circle cx="165" cy="155" r="6" fill="currentColor" />
      <circle cx="165" cy="115" r="6" fill="currentColor" />
    </g>
  </svg>

  <!-- 海军风：罗盘 + 海军锚 -->
  <svg
    v-else
    class="emblem emblem--marine"
    :class="`emblem--${variant}`"
    :width="size"
    :height="size"
    viewBox="0 0 200 200"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    aria-hidden="true"
  >
    <g :style="{ opacity: opacity }">
      <!-- 罗盘外环 -->
      <circle cx="100" cy="100" r="88" stroke="currentColor" stroke-width="2" fill="none" />
      <circle cx="100" cy="100" r="76" stroke="currentColor" stroke-width="0.8" fill="none" />
      <!-- 罗盘刻度（8 个方位） -->
      <g stroke="currentColor" stroke-width="1.5">
        <line x1="100" y1="14" x2="100" y2="24" />
        <line x1="100" y1="176" x2="100" y2="186" />
        <line x1="14" y1="100" x2="24" y2="100" />
        <line x1="176" y1="100" x2="186" y2="100" />
        <line x1="40" y1="40" x2="46" y2="46" />
        <line x1="154" y1="46" x2="160" y2="40" />
        <line x1="40" y1="160" x2="46" y2="154" />
        <line x1="154" y1="154" x2="160" y2="160" />
      </g>
      <!-- 方位字母 -->
      <g font-family="serif" font-size="11" font-weight="bold" fill="currentColor" text-anchor="middle">
        <text x="100" y="34">N</text>
        <text x="100" y="178">S</text>
        <text x="32" y="105">W</text>
        <text x="168" y="105">E</text>
      </g>
      <!-- 内圆（罗盘面） -->
      <circle cx="100" cy="100" r="48" fill="var(--color-bg-deep)" />
      <circle cx="100" cy="100" r="48" stroke="currentColor" stroke-width="1" fill="none" />
      <!-- 锚在中央 -->
      <g transform="translate(100, 100) scale(0.5)" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" fill="none">
        <circle cx="0" cy="-50" r="14" />
        <line x1="0" y1="-36" x2="0" y2="40" />
        <line x1="-18" y1="-22" x2="18" y2="-22" />
        <path d="M-28 16c0 14 12 26 28 26s28-12 28-26" />
        <path d="M-28 16c-4 0-6-2-6-6" />
        <path d="M28 16c4 0 6-2 6-6" />
      </g>
      <circle cx="100" cy="100" r="4" fill="var(--color-primary)" />
    </g>
  </svg>
</template>

<style scoped>
.emblem {
  pointer-events: none;
  color: var(--color-primary);
  display: block;
  flex-shrink: 0;
}
.emblem--center {
  margin: auto;
  animation: emblem-float 8s ease-in-out infinite;
}
.emblem--watermark {
  position: absolute;
  inset: 0;
  margin: auto;
}
@keyframes emblem-float {
  0%, 100% { transform: translateY(0) rotate(0deg); }
  50% { transform: translateY(-12px) rotate(2deg); }
}
</style>
