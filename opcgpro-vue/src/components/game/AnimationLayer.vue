<script setup lang="ts">
import { ref, watch } from "vue";
import { useGameAnimation } from "@/composables/useGameAnimation";
import { useGameAudio } from "@/composables/useGameAudio";

/**
 * AnimationLayer — 根据服务端 lastAction 驱动战斗动画特效（纯视觉层）。
 * 震动 / 红闪 / 横幅用 CSS 类 + Vue <Transition> 实现（替代 framer-motion）。
 */
const anim = useGameAnimation();
useGameAudio(anim);

const shake = ref(false);
const flash = ref(false);
const banner = ref<{ text: string; color: string } | null>(null);

watch(anim, (e) => {
  switch (e.type) {
    case "damage":
      flash.value = true;
      shake.value = true;
      setTimeout(() => (flash.value = false), 200);
      setTimeout(() => (shake.value = false), 500);
      break;
    case "koUnit":
      flash.value = true;
      setTimeout(() => (flash.value = false), 150);
      break;
    case "turnStart":
      banner.value = {
        text: e.side === "my" ? "我的回合！" : "对手回合",
        color: e.side === "my" ? "bg-orange-500" : "bg-blue-500",
      };
      setTimeout(() => (banner.value = null), 2000);
      break;
    case "gameOver":
      banner.value = {
        text: e.isWin ? "胜利！" : "失败",
        color: e.isWin ? "bg-yellow-500" : "bg-red-600",
      };
      break;
  }
});
</script>

<template>
  <div :class="['pointer-events-none fixed inset-0 z-20', shake && 'anim-shake']" />

  <Transition name="flash">
    <div v-if="flash" class="pointer-events-none fixed inset-0 z-20 bg-red-500/20" />
  </Transition>

  <Transition name="banner">
    <div
      v-if="banner"
      class="pointer-events-none fixed left-1/2 top-1/4 z-30 -translate-x-1/2 -translate-y-1/2"
    >
      <div :class="[banner.color, 'rounded-xl px-8 py-3 text-2xl font-bold text-white shadow-2xl']">
        {{ banner.text }}
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.anim-shake {
  animation: shake 0.4s ease;
}
@keyframes shake {
  0%, 100% { transform: translate(0, 0); }
  20% { transform: translate(-6px, 3px); }
  40% { transform: translate(6px, -3px); }
  60% { transform: translate(-4px, 2px); }
  80% { transform: translate(4px, -2px); }
}
.flash-enter-active,
.flash-leave-active { transition: opacity 0.15s ease; }
.flash-enter-from,
.flash-leave-to { opacity: 0; }
.banner-enter-active { transition: all 0.25s cubic-bezier(0.34, 1.56, 0.64, 1); }
.banner-leave-active { transition: all 0.2s ease; }
.banner-enter-from { opacity: 0; transform: translate(-50%, calc(-50% - 20px)) scale(0.5); }
.banner-leave-to { opacity: 0; transform: translate(-50%, calc(-50% - 10px)) scale(0.8); }
</style>
