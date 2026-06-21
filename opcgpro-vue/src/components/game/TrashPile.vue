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
const topNumber = computed(() => (count.value > 0 ? trash.value[count.value - 1] : null));
const topCard = computed(() => (topNumber.value ? getCard(topNumber.value) ?? null : null));
</script>

<template>
  <div class="flex flex-col items-center gap-2 rounded-md border border-zinc-300/15 bg-black/30 px-2.5 py-2 shadow-lg shadow-black/25">
    <span class="text-xs font-semibold text-slate-300">墓地</span>
    <div
      :class="['relative cursor-pointer', pile]"
      :title="count > 0 ? `查看墓地（${count} 张）` : '墓地为空'"
      @click="open = true"
    >
      <!-- 封面 = 最近送入的卡 -->
      <CardItem v-if="topCard" :card="topCard" :size="cardSize" />
      <div v-else class="flex h-full w-full items-center justify-center rounded-md border-2 border-dashed border-zinc-400/35 bg-zinc-950/60">
        <span class="text-xs font-black text-zinc-500">TRASH</span>
      </div>
      <div class="absolute -right-3 -top-3 z-30 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
        {{ count }}
      </div>
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
.fade-enter-active,
.fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from,
.fade-leave-to { opacity: 0; }
</style>