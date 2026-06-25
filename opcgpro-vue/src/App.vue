<script setup lang="ts">
// 全局壳（等价旧项目 NetProvider）：
//   - 启动连 WS + 注册协议（useNet）
//   - 监听 netStore.navigateTo → router.push（SPA 内导航，避免整页刷新断 WS）
//   - 全局 AnimatedBackground：5 层 canvas 绘制
//   - 跟踪 documentElement 主题切换 canvas palette
//   - 主题切换 UI 仅在 LoginPage 内提供（App.vue 不再全局挂载）
import { watch, ref, computed, onMounted, onBeforeUnmount } from "vue";
import { useRouter, useRoute } from "vue-router";
import { useNet } from "@/composables/useNet";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import MessageBox from "@/components/ui/MessageBox.vue";
import AnimatedBackground from "@/components/ui/AnimatedBackground.vue";

useNet();

const router = useRouter();
const route = useRoute();
// 对战/观战/回放页禁用全屏动态背景 canvas（牌桌已自带毛毡+vignette），
// 避免「常驻 canvas 动画 + 其上 backdrop-blur 面板」每帧重栅格化导致操作卡顿。
const isBattleRoute = computed(
  () => route.path === "/game" || route.path === "/spectate" || route.path.startsWith("/replay"),
);
const navigateTo = useStore(useNetStore, (s) => s.navigateTo);

watch(navigateTo, (path) => {
  if (path) {
    useNetStore.getState().setNavigateTo(null);
    router.push(path);
  }
});

// 跟随主题切换 canvas palette（BG_PALETTE 用 "navy"）
const currentTheme = ref<"pirate" | "navy">("pirate");
let themeObs: MutationObserver | null = null;
function readTheme(): "pirate" | "navy" {
  const t = document.documentElement.dataset.theme;
  return t === "navy" || t === "marine" ? "navy" : "pirate";
}
onMounted(() => {
  currentTheme.value = readTheme();
  themeObs = new MutationObserver(() => {
    currentTheme.value = readTheme();
  });
  themeObs.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ["data-theme"],
  });
});
onBeforeUnmount(() => { themeObs?.disconnect(); });
</script>

<template>
  <!-- 全屏动态背景层（CLAUDE.md z-index 0 铺底）；对战页改用静态渐变省 GPU -->
  <div class="global-bg">
    <AnimatedBackground v-if="!isBattleRoute" :theme-key="currentTheme" />
    <div v-else class="global-bg__static" />
  </div>

  <!-- 页面内容层（z-index 1） -->
  <div class="global-content">
    <router-view />
    <MessageBox />
  </div>
</template>

<style>
.global-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
}
.global-bg__static {
  position: absolute;
  inset: 0;
  background: radial-gradient(120% 120% at 50% 40%, var(--bg1), var(--bg0));
}
.global-content {
  position: relative;
  z-index: 1;
  min-height: 100vh;
}
</style>
