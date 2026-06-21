<script setup lang="ts">
// 全局壳（等价旧项目 NetProvider）：
//   - 启动连 WS + 注册协议（useNet）
//   - 监听 netStore.navigateTo → router.push（SPA 内导航，避免整页刷新断 WS）
//   - 全局 AnimatedBackground：5 层 canvas 绘制（背景色+雾气+声呐+粒子+水波+暗角）
//   - 跟踪 documentElement.data-theme 切换 canvas palette
import { watch, ref, onMounted, onBeforeUnmount } from "vue";
import { useRouter } from "vue-router";
import { useNet } from "@/composables/useNet";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import MessageBox from "@/components/ui/MessageBox.vue";
import ThemeSwitcher from "@/components/ui/ThemeSwitcher.vue";
import AnimatedBackground from "@/components/ui/AnimatedBackground.vue";

useNet();

const router = useRouter();
const navigateTo = useStore(useNetStore, (s) => s.navigateTo);

watch(navigateTo, (path) => {
  if (path) {
    useNetStore.getState().setNavigateTo(null);
    router.push(path);
  }
});

// 跟随主题切换 canvas palette（ThemeSwitcher 写入 "marine"，但 BG_PALETTE 用 "navy"）
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
  <!-- 全屏动态背景层（CLAUDE.md §4.1：5 层 canvas，z-index 0 铺底） -->
  <div class="global-bg">
    <AnimatedBackground :theme-key="currentTheme" />
  </div>

  <!-- 页面内容层（z-index 1 在背景之上） -->
  <div class="global-content">
    <router-view />
    <ThemeSwitcher />
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
.global-content {
  position: relative;
  z-index: 1;
  min-height: 100vh;
}
</style>
