<script setup lang="ts">
import { ref, onMounted, computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { loadAllDecks, type SavedDeck } from "@/data/DeckMapper";

const room = useStore(useNetStore, (s) => s.friendlyRoom);
const account = useStore(useNetStore, (s) => s.account);
const decks = ref<Record<string, SavedDeck>>({});
const picking = ref(false);

onMounted(() => { decks.value = loadAllDecks(); });

const myIndex = computed(() => room.value?.players.findIndex((p) => p.account === account.value) ?? -1);
const oppIndex = computed(() => (myIndex.value === 0 ? 1 : 0));
const me = computed(() => room.value?.players[myIndex.value]);
const opp = computed(() => room.value?.players[oppIndex.value]);
const myScore = computed(() => room.value?.scores[myIndex.value] ?? 0);
const oppScore = computed(() => room.value?.scores[oppIndex.value] ?? 0);

function selectDeck(name: string) {
  const saved = loadAllDecks()[name];
  if (!saved) return;
  const cards = [saved.leader, ...saved.cards].join("\n");
  HomeRequest.friendlySelectDeck(cards, name);
  picking.value = false;
}

function toggleReady() { HomeRequest.friendlyReady(!me.value?.ready); }
function leave() { HomeRequest.friendlyLeave(); }
</script>

<template>
  <div v-if="room" class="flex h-screen flex-col items-center justify-center gap-6 bg-gray-950 p-8">
    <h2 class="text-2xl font-bold text-white">友谊战房间</h2>

    <div class="flex items-center gap-4">
      <span class="max-w-[8rem] truncate text-right text-sm text-gray-400">{{ me?.name ?? "我" }}</span>
      <span class="text-3xl font-black text-orange-400">{{ myScore }} : {{ oppScore }}</span>
      <span class="max-w-[8rem] truncate text-sm text-gray-400">{{ opp?.name ?? "对方" }}</span>
    </div>

    <div class="flex gap-6">
      <div class="flex w-44 flex-col items-center gap-2 rounded-xl border-2 border-orange-500/60 bg-orange-500/5 p-4">
        <span class="text-[10px] text-gray-500">我</span>
        <p class="w-full truncate text-center text-sm font-bold text-white">{{ me?.name ?? "?" }}</p>
        <p class="w-full truncate text-center text-xs text-gray-400">{{ me?.deckName ?? "未选卡组" }}</p>
        <span :class="['text-[11px] font-bold', me?.ready ? 'text-green-400' : 'text-gray-600']">
          {{ me?.ready ? "已准备" : "未准备" }}
        </span>
      </div>
      <div class="flex w-44 flex-col items-center gap-2 rounded-xl border-2 border-gray-700 bg-gray-900 p-4">
        <span class="text-[10px] text-gray-500">对手</span>
        <p class="w-full truncate text-center text-sm font-bold text-white">{{ opp?.name ?? "?" }}</p>
        <p class="w-full truncate text-center text-xs text-gray-400">{{ opp?.deckName ?? "未选卡组" }}</p>
        <span :class="['text-[11px] font-bold', opp?.ready ? 'text-green-400' : 'text-gray-600']">
          {{ opp?.ready ? "已准备" : "未准备" }}
        </span>
      </div>
    </div>

    <div class="flex w-full max-w-sm flex-col items-center gap-3">
      <button
        class="w-full rounded-lg border border-gray-700 bg-gray-800 py-2 text-sm text-white transition-colors hover:border-orange-500"
        @click="picking = !picking"
      >
        {{ me?.deckName ? `已选：${me.deckName}（点击更换）` : "选择卡组" }}
      </button>

      <div
        v-if="picking"
        class="flex w-full max-h-48 flex-col gap-1 overflow-y-auto rounded-lg border border-gray-800 bg-gray-900 p-2"
      >
        <p v-if="Object.keys(decks).length === 0" class="py-3 text-center text-xs text-gray-600">
          还没有卡组，去「卡组」面板创建
        </p>
        <button
          v-for="(d, name) in decks"
          :key="name"
          class="flex items-center gap-2 rounded px-2 py-1.5 text-left hover:bg-gray-800"
          @click="selectDeck(name)"
        >
          <img
            :src="d.leaderSprite || '/sprites/CardBack.png'"
            :alt="d.leaderName"
            class="h-10 w-7 shrink-0 rounded border border-gray-700 object-cover"
            @error="(e) => ((e.target as HTMLImageElement).src = '/sprites/CardBack.png')"
          />
          <div class="min-w-0">
            <p class="truncate text-xs text-white">{{ name }}</p>
            <p class="truncate text-[10px] text-gray-500">{{ d.leaderName }}</p>
          </div>
        </button>
      </div>

      <button
        :disabled="!me?.deckName"
        :class="[
          'w-full rounded-xl py-2.5 text-sm font-bold transition-all',
          !me?.deckName
            ? 'cursor-not-allowed bg-gray-800 text-gray-600'
            : me?.ready
              ? 'bg-green-600 text-white hover:bg-green-500'
              : 'bg-orange-500 text-white hover:bg-orange-400',
        ]"
        @click="toggleReady"
      >
        {{ me?.ready ? "✓ 已准备（点击取消）" : "准备" }}
      </button>

      <p v-if="me?.ready && opp?.ready" class="text-xs text-green-400">双方已准备，即将开始对战…</p>

      <button class="mt-2 text-xs text-gray-500 transition-colors hover:text-red-400" @click="leave">
        退出房间
      </button>
    </div>
  </div>
</template>
