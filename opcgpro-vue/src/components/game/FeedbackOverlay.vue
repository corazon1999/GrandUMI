<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick } from "vue";
import { NetManager } from "@/net/NetManager";
import { eventBus } from "@/net/eventBus";
import { useGameStore } from "@/store/gameStore";
import type { MsgBase, MsgBugReport } from "@/types/net";

type SubmitState =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "ok"; path?: string }
  | { kind: "fail"; error?: string };

const open = ref(false);
const description = ref("");
const submit = ref<SubmitState>({ kind: "idle" });
const textRef = ref<HTMLTextAreaElement | null>(null);

function onKeyDown(e: KeyboardEvent) {
  if (e.key !== "F2") return;
  e.preventDefault();
  open.value = !open.value;
}

onMounted(() => {
  window.addEventListener("keydown", onKeyDown);

  // 订阅服务端回执
  const handler = (msg: MsgBase) => {
    if (msg.proto !== "MsgBugReport") return;
    const m = msg as MsgBugReport;
    if (m.result) submit.value = { kind: "ok", path: m.path };
    else submit.value = { kind: "fail", error: m.error };
  };
  eventBus.on("message", handler);
  onUnmounted(() => eventBus.off("message", handler));
});

onUnmounted(() => window.removeEventListener("keydown", onKeyDown));

// 打开时聚焦
function onOpenChange(v: boolean) {
  if (v) {
    submit.value = { kind: "idle" };
    nextTick(() => textRef.value?.focus());
  }
}

watch(open, onOpenChange);

function handleSubmit() {
  const desc = description.value.trim();
  if (!desc || submit.value.kind === "sending") return;

  const s = useGameStore.getState();
  const clientInfo = JSON.stringify({
    meta: {
      ts: new Date().toISOString(),
      url: typeof window !== "undefined" ? window.location.href : "",
      userAgent: typeof navigator !== "undefined" ? navigator.userAgent : "",
      mode: s.mode,
      phase: s.phase,
      turnCount: s.turnCount,
      currentTurn: s.currentTurn,
      myName: s.myName,
      opponentName: s.opponentName,
    },
    gameStore: s,
  });

  submit.value = { kind: "sending" };
  const sent = NetManager.send({
    proto: "MsgBugReport",
    description: desc,
    clientInfo,
  } as MsgBugReport);
  if (!sent) submit.value = { kind: "fail", error: "未连接服务器" };
}
</script>

<template>
  <Transition name="modal">
    <div
      v-if="open"
      class="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4"
      @click.self="open = false"
    >
      <div class="w-full max-w-md rounded-lg border border-rose-400/40 bg-slate-950/95 p-5 shadow-2xl shadow-black/60">
        <div class="mb-3 flex items-center justify-between">
          <h2 class="text-sm font-black text-rose-300">反馈 Bug（F2）</h2>
          <button
            class="rounded px-2 py-0.5 text-xs text-slate-400 transition-colors hover:text-white"
            @click="open = false"
          >
            关闭
          </button>
        </div>

        <label class="block text-xs font-bold text-slate-300">问题描述</label>
        <textarea
          ref="textRef"
          v-model="description"
          rows="5"
          placeholder="描述触发 bug 的操作、现象、期望结果……提交时会自动附带当前对局全量信息。"
          class="mt-1.5 w-full resize-none rounded border border-slate-600 bg-slate-900 px-2.5 py-2 text-sm text-white placeholder:text-slate-500 focus:border-rose-400 focus:outline-none"
        />

        <div class="mt-3 flex items-center justify-between gap-3">
          <div class="min-w-0 flex-1 text-[11px]">
            <span v-if="submit.kind === 'ok'" class="text-emerald-400">
              已提交并保存{{ submit.path ? `：${submit.path}` : "" }}
            </span>
            <span v-else-if="submit.kind === 'fail'" class="text-rose-400">
              提交失败{{ submit.error ? `：${submit.error}` : "" }}
            </span>
            <span v-else-if="submit.kind === 'sending'" class="text-slate-400">提交中……</span>
          </div>
          <button
            :disabled="!description.trim() || submit.kind === 'sending'"
            class="shrink-0 rounded bg-rose-500 px-4 py-1.5 text-sm font-bold text-white transition-colors hover:bg-rose-400 disabled:cursor-not-allowed disabled:opacity-50"
            @click="handleSubmit"
          >
            提交反馈
          </button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.modal-enter-active,
.modal-leave-active { transition: opacity 0.2s ease; }
.modal-enter-from,
.modal-leave-to { opacity: 0; }
</style>
