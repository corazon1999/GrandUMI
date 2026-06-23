<script setup lang="ts">
import { ref, computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/composables/useResponsive";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem.vue";

const props = defineProps<{ side: "my" | "opponent" }>();

const pileSizes = { sm: "h-[6.3rem] w-[4.5rem]", md: "h-[8.4rem] w-[6rem]", lg: "h-[11.2rem] w-[8rem]" } as const;

const trash = useStore(useGameStore, (s) =>
  (props.side === "my" ? s.my?.trashNumbers : s.opponent?.trashNumbers) ?? [],
);
const { cardSize } = useResponsive();
const pile = computed(() => pileSizes[cardSize.value]);
const open = ref(false);

const count = computed(() => trash.value.length);
</script>

<template>
  <div class="bf-well">
    <span class="kicker">墓地</span>
    <div
      :class="[pile, 'bf-area bf-trash']"
      :title="count > 0 ? `查看墓地（${count} 张）` : '墓地为空'"
      @click="open = true"
    >
      <span class="bf-trash__lbl">TRASH</span>
      <span class="bf-trash__n">{{ count }}</span>
    </div>

    <!-- 墓地弹窗 -->
    <Teleport to="body">
      <Transition name="fade">
        <div v-if="open" class="fixed inset-0 z-50 flex flex-col items-center justify-center gap-4 bg-black/80 p-8" @click="open = false">
          <div class="flex items-center gap-4">
            <p class="text-lg font-bold text-white">
              {{ side === "my" ? "我方" : "对手" }}墓地（{{ count }} 张）
            </p>
            <button class="rounded-lg bg-gray-600 px-4 py-1 text-sm font-bold text-white hover:bg-gray-500" @click="open = false">
              关闭
            </button>
          </div>
          <div class="flex max-h-[75vh] max-w-5xl flex-wrap justify-center gap-2 overflow-y-auto p-2" @click.stop>
            <span v-if="count === 0" class="text-sm text-gray-400">墓地为空</span>
            <!-- 反序展示：最近送入的排最前 -->
            <CardItem v-for="(num, i) in [...trash].reverse()" :key="`${num}-${i}`" :card="getCard(num) ?? null" size="md" />
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
.bf-trash {
  cursor: pointer;
}
.bf-trash__lbl {
  position: absolute;
  top: 7px;
  font-family: var(--font-mono);
  font-size: 9px;
  letter-spacing: 0.12em;
  color: var(--ink-faint);
}
.bf-trash__n {
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 26px;
  color: var(--ink-dim);
}
.fade-enter-active,
.fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from,
.fade-leave-to { opacity: 0; }
</style>