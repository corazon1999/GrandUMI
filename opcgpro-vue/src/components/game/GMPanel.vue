<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";

const props = withDefaults(defineProps<{ showButton?: boolean }>(), { showButton: false });

const open = ref(false);
const cardNumber = ref("");
const donCount = ref("9");
const summonNumber = ref("");
const summonTarget = ref<"self" | "opponent">("self");
const inputRef = ref<HTMLInputElement | null>(null);

const showPlayerControls = useStore(useGameStore, (s) => {
  const m = s.mode;
  return m !== "Observer" && m !== "Playback";
});

function onKeyDown(e: KeyboardEvent) {
  if (e.key !== "t" && e.key !== "T") return;
  const el = e.target as HTMLElement | null;
  const tag = el?.tagName;
  if (tag === "INPUT" || tag === "TEXTAREA" || el?.isContentEditable) return;
  e.preventDefault();
  open.value = !open.value;
}

onMounted(() => window.addEventListener("keydown", onKeyDown));
onUnmounted(() => window.removeEventListener("keydown", onKeyDown));

function toggleOpen() {
  open.value = !open.value;
  if (open.value) nextTick(() => inputRef.value?.focus());
}

function submit() {
  const num = cardNumber.value.trim().toUpperCase();
  if (!num) return;
  GameRequest.debugAddCard(num);
  inputRef.value?.focus();
}

function submitDon() {
  const n = parseInt(donCount.value, 10);
  GameRequest.debugAddDon(Number.isFinite(n) && n > 0 ? n : 1);
}

function refreshDon() {
  GameRequest.debugRefreshDon();
}

function submitSummon() {
  const num = summonNumber.value.trim().toUpperCase();
  if (!num) return;
  GameRequest.debugSummon(num, summonTarget.value);
}

function koAll(target: "self" | "opponent") {
  GameRequest.debugKoAll(target);
}

function restAll(target: "self" | "opponent") {
  GameRequest.debugRestAll(target);
}

function leaderAttack() {
  GameRequest.debugLeaderAttack();
}
</script>

<template>
  <template v-if="showPlayerControls">
    <button
      v-if="showButton && !open"
      class="fixed left-4 top-4 z-50 rounded-md border border-amber-400/50 bg-amber-500/90 px-3 py-1.5 text-xs font-black text-slate-950 shadow-lg transition-colors hover:bg-amber-400"
      aria-label="打开 GM 调试面板"
      @click="toggleOpen"
    >
      GM
    </button>

    <Transition name="gm-panel">
      <div
        v-if="open"
        class="fixed right-4 top-4 z-50 flex max-h-[calc(100dvh-2rem)] w-72 flex-col rounded-lg border border-amber-400/40 bg-slate-950/95 shadow-2xl shadow-black/50"
      >
        <div class="flex shrink-0 items-center justify-between border-b border-white/10 px-4 py-2.5">
          <h2 class="text-sm font-black text-amber-300">GM 调试面板</h2>
          <button
            class="rounded px-2 py-0.5 text-xs text-slate-400 transition-colors hover:text-white"
            @click="open = false"
          >
            关闭 (T)
          </button>
        </div>

        <div class="min-h-0 flex-1 overflow-y-auto px-4 py-3">
          <label class="block text-xs font-bold text-slate-300">加牌到手牌</label>
          <div class="mt-1.5 flex gap-2">
            <input
              ref="inputRef"
              v-model="cardNumber"
              placeholder="例：OP01-001"
              class="min-w-0 flex-1 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white placeholder:text-slate-500 focus:border-amber-400 focus:outline-none"
              @keydown.enter="submit"
            />
            <button
              class="shrink-0 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
              @click="submit"
            >
              添加
            </button>
          </div>
          <p class="mt-1.5 text-[11px] text-slate-500">输入卡牌编号后回车或点"添加"，可连续加牌。</p>

          <div class="my-2 h-px bg-white/10" />

          <label class="block text-xs font-bold text-slate-300">加咚（活跃）</label>
          <div class="mt-1.5 flex gap-2">
            <input
              v-model="donCount"
              type="number"
              min="1"
              class="w-20 min-w-0 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white focus:border-amber-400 focus:outline-none"
              @keydown.enter="submitDon"
            />
            <button
              class="flex-1 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
              @click="submitDon"
            >
              加咚
            </button>
          </div>
          <button
            class="mt-1.5 w-full rounded bg-sky-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-sky-500"
            @click="refreshDon"
          >
            刷新咚（全部回费用区并竖直）
          </button>

          <div class="my-2 h-px bg-white/10" />

          <label class="block text-xs font-bold text-slate-300">打出到场上</label>
          <div class="mt-1.5 flex gap-1 rounded border border-slate-600 bg-slate-900 p-0.5">
            <button
              :class="[
                'flex-1 rounded px-2 py-1 text-xs font-bold transition-colors',
                summonTarget === 'self' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white',
              ]"
              @click="summonTarget = 'self'"
            >
              我方场上
            </button>
            <button
              :class="[
                'flex-1 rounded px-2 py-1 text-xs font-bold transition-colors',
                summonTarget === 'opponent' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white',
              ]"
              @click="summonTarget = 'opponent'"
            >
              对方场上
            </button>
          </div>
          <div class="mt-1.5 flex gap-2">
            <input
              v-model="summonNumber"
              placeholder="例：OP01-025"
              class="min-w-0 flex-1 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white placeholder:text-slate-500 focus:border-amber-400 focus:outline-none"
              @keydown.enter="submitSummon"
            />
            <button
              class="shrink-0 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
              @click="submitSummon"
            >
              打出
            </button>
          </div>
          <p class="mt-1.5 text-[11px] text-slate-500">角色/舞台打出到场上，不扣费。</p>

          <div class="my-2 h-px bg-white/10" />

          <label class="block text-xs font-bold text-slate-300">KO 场上角色</label>
          <div class="mt-1.5 flex gap-2">
            <button
              class="flex-1 rounded bg-rose-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-rose-500"
              @click="koAll('self')"
            >
              KO 我方全部
            </button>
            <button
              class="flex-1 rounded bg-rose-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-rose-500"
              @click="koAll('opponent')"
            >
              KO 对方全部
            </button>
          </div>

          <div class="my-2 h-px bg-white/10" />

          <label class="block text-xs font-bold text-slate-300">横置场上角色</label>
          <div class="mt-1.5 flex gap-2">
            <button
              class="flex-1 rounded bg-orange-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-orange-500"
              @click="restAll('self')"
            >
              横置我方全部
            </button>
            <button
              class="flex-1 rounded bg-orange-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-orange-500"
              @click="restAll('opponent')"
            >
              横置对方全部
            </button>
          </div>

          <div class="my-2 h-px bg-white/10" />

          <label class="block text-xs font-bold text-slate-300">对手领袖攻击</label>
          <button
            class="mt-1.5 w-full rounded bg-purple-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-purple-500"
            @click="leaderAttack"
          >
            对手领袖攻击我方领袖
          </button>
        </div>
      </div>
    </Transition>
  </template>
</template>

<style scoped>
.gm-panel-enter-active { transition: all 0.2s ease; }
.gm-panel-leave-active { transition: all 0.2s ease; }
.gm-panel-enter-from { opacity: 0; transform: translateX(24px); }
.gm-panel-leave-to { opacity: 0; transform: translateX(24px); }
</style>
