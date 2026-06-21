<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick } from "vue";
import { eventBus } from "@/net/eventBus";
import { GameRequest } from "@/net/GameRequest";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";

const props = defineProps<{ isPlayback: boolean }>();

const PRESETS = ["你好", "好牌！", "谢谢", "手下留情", "该你了", "认输吧", "GG", "网络卡了，稍等"];
const COOLDOWN_MS = 1300;

interface ChatItem { id: number; text: string; fromName: string; isSelf: boolean; fromRole: "player" | "spectator"; }

const myAccount = useStore(useNetStore, (s) => s.account);
const open = ref(false);
const muted = ref(false);
const input = ref("");
const messages = ref<ChatItem[]>([]);
const coolingDown = ref(false);
const unread = ref(0);
const toast = ref<ChatItem | null>(null);

let idCounter = 0;
let toastTimer: ReturnType<typeof setTimeout> | null = null;
let cooldownTimer: ReturnType<typeof setTimeout> | null = null;
const listRef = ref<HTMLDivElement | null>(null);

watch(open, (v) => { if (v) unread.value = 0; });

function handler(m: { text: string; fromAccount?: string; fromName: string; fromRole: "player" | "spectator" }) {
  const isSelf = !!m.fromAccount && m.fromAccount === myAccount.value;
  if (muted.value && !isSelf) return;
  const item: ChatItem = { id: ++idCounter, text: m.text, fromName: m.fromName, isSelf, fromRole: m.fromRole };
  messages.value = [...messages.value.slice(-49), item];
  if (!open.value) { if (!isSelf) unread.value++; if (!isSelf) { toast.value = item; if (toastTimer) clearTimeout(toastTimer); toastTimer = setTimeout(() => { toast.value = null; }, 4000); } }
}

watch(messages, async () => { if (open.value && listRef.value) { await nextTick(); listRef.value.scrollTop = listRef.value.scrollHeight; } });

onMounted(() => { eventBus.on("gameChat", handler); });
onUnmounted(() => { eventBus.off("gameChat", handler); if (toastTimer) clearTimeout(toastTimer); if (cooldownTimer) clearTimeout(cooldownTimer); });

function fireCooldown() { coolingDown.value = true; if (cooldownTimer) clearTimeout(cooldownTimer); cooldownTimer = setTimeout(() => { coolingDown.value = false; }, COOLDOWN_MS); }
function sendPreset(text: string) { if (coolingDown.value) return; GameRequest.sendGameChat(text, "preset"); fireCooldown(); }
function sendFree() { const t = input.value.trim(); if (!t || coolingDown.value) return; GameRequest.sendGameChat(t); input.value = ""; fireCooldown(); }
</script>

<template>
  <div v-if="!isPlayback" class="pointer-events-none fixed bottom-3 left-3 z-40 flex flex-col items-start gap-2">
    <div v-if="!open && toast" class="pointer-events-none max-w-[220px] rounded-lg bg-black/80 px-3 py-1.5 text-xs text-white shadow-lg ring-1 ring-white/15">
      <span class="font-bold text-amber-300">{{ toast.fromName }}：</span>{{ toast.text }}
    </div>

    <div v-if="open" class="pointer-events-auto flex w-72 flex-col overflow-hidden rounded-xl bg-slate-900/95 shadow-2xl ring-1 ring-white/15">
      <div class="flex items-center justify-between border-b border-white/10 px-3 py-1.5">
        <span class="text-xs font-bold text-slate-200">局内聊天</span>
        <div class="flex items-center gap-2">
          <button :class="['text-xs', muted ? 'text-rose-400' : 'text-slate-400 hover:text-slate-200']" :title="muted ? '已静音对手（点击取消）' : '静音对手'" @click="muted = !muted">{{ muted ? '🔇 已静音' : '🔊' }}</button>
          <button class="text-slate-400 hover:text-white" title="收起" @click="open = false">✕</button>
        </div>
      </div>
      <div ref="listRef" class="h-40 overflow-y-auto px-3 py-2 text-xs">
        <div v-if="messages.length === 0" class="text-slate-500">还没有消息。发个招呼吧～</div>
        <div v-for="m in messages" :key="m.id" class="mb-1 leading-snug">
          <span :class="['font-bold', m.isSelf ? 'text-sky-300' : m.fromRole === 'spectator' ? 'text-slate-400' : 'text-amber-300']">{{ m.isSelf ? '你' : m.fromName }}{{ m.fromRole === 'spectator' ? '(观战)' : '' }}：</span>
          <span class="text-slate-100">{{ m.text }}</span>
        </div>
      </div>
      <div class="flex flex-wrap gap-1 border-t border-white/10 px-2 py-1.5">
        <button v-for="p in PRESETS" :key="p" :disabled="coolingDown" class="rounded-full bg-slate-700/80 px-2 py-0.5 text-[11px] text-slate-100 hover:bg-slate-600 disabled:opacity-40" @click="sendPreset(p)">{{ p }}</button>
      </div>
      <div class="flex items-center gap-1 border-t border-white/10 p-2">
        <input v-model="input" maxlength="100" placeholder="输入消息…" class="min-w-0 flex-1 rounded-md bg-slate-800 px-2 py-1 text-xs text-white outline-none ring-1 ring-white/10 focus:ring-sky-400" @keydown.enter="sendFree" />
        <button :disabled="coolingDown || !input.trim()" class="rounded-md bg-sky-600 px-3 py-1 text-xs font-bold text-white hover:bg-sky-500 disabled:bg-slate-700 disabled:opacity-50" @click="sendFree">发送</button>
      </div>
    </div>

    <button class="pointer-events-auto relative flex h-10 w-10 items-center justify-center rounded-full bg-slate-800/90 text-lg shadow-lg ring-1 ring-white/15 hover:bg-slate-700" title="局内聊天" @click="open = !open">
      💬
      <span v-if="!open && unread > 0" class="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">{{ unread > 9 ? '9+' : unread }}</span>
    </button>
  </div>
</template>
