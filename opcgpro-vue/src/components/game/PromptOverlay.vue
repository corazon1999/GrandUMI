<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem.vue";

const prompt = useStore(useGameStore, (s) => s.pendingPrompt);
const my = useStore(useGameStore, (s) => s.my);
const opp = useStore(useGameStore, (s) => s.opponent);
const selected = ref<string[]>([]);

watch(() => prompt.value?.promptId, () => { selected.value = []; });

const isLifeTrigger = computed(() => prompt.value?.kind === "LifeTrigger");
const isOption = computed(() => prompt.value?.kind === "Option");
const isReturnDon = computed(() => prompt.value?.kind === "ReturnOwnDon");
const options = computed(() => prompt.value?.extra?.options as string[] | undefined);
const lifeCardNumber = computed(() => prompt.value?.extra?.lifeCardNumber as string | undefined);
const lifeCard = computed(() => (lifeCardNumber.value ? getCard(lifeCardNumber.value) ?? null : null));
const hasRealTrigger = computed(() => prompt.value?.extra?.hasRealTrigger === true);

type DonChoice = { id: string; state: string; attachedToNumber?: string; attachedToName?: string };
const donChoices = computed(() => (prompt.value?.extra?.donChoices as DonChoice[] | undefined) ?? []);
const donChoiceMap = computed(() => new Map(donChoices.value.map((d) => [d.id, d])));

function findCardById(id: string) {
  if (id === "leader") return null;
  // 领袖与舞台不在 fieldCards 里（扁平字段），需单独识别，否则候选卡图加载不出
  if (my.value && id === my.value.leaderId) return my.value.leaderNumber ? getCard(my.value.leaderNumber) ?? null : null;
  if (opp.value && id === opp.value.leaderId) return opp.value.leaderNumber ? getCard(opp.value.leaderNumber) ?? null : null;
  if (my.value && id === my.value.stageId) return my.value.stageNumber ? getCard(my.value.stageNumber) ?? null : null;
  if (opp.value && id === opp.value.stageId) return opp.value.stageNumber ? getCard(opp.value.stageNumber) ?? null : null;
  const all = [...(my.value?.fieldCards ?? []), ...(opp.value?.fieldCards ?? [])];
  const found = all.find((c) => c.id === id);
  return found ? getCard(found.number) ?? null : null;
}

function toggle(id: string) {
  const p = prompt.value!;
  if (selected.value.includes(id)) {
    selected.value = selected.value.filter((x) => x !== id);
  } else if (selected.value.length >= p.maxChoose) {
    selected.value = [id];
  } else {
    selected.value = [...selected.value, id];
  }
}

const canConfirm = computed(() => {
  const p = prompt.value;
  return !!p && selected.value.length >= p.minChoose && selected.value.length <= p.maxChoose;
});

function respond(chosen: string[]) {
  GameRequest.respondPrompt(prompt.value!.promptId, chosen);
}
</script>

<template>
  <Transition name="fade">
    <div v-if="prompt" class="fixed inset-0 z-50 flex flex-col items-center justify-center gap-6 bg-black/75">
      <p class="text-lg font-bold text-white">{{ prompt.text }}</p>

      <div v-if="isLifeTrigger" class="flex flex-col items-center gap-4">
        <CardItem v-if="lifeCard && hasRealTrigger" :card="lifeCard" size="lg" />
        <div v-if="!hasRealTrigger" class="flex h-40 w-28 items-center justify-center rounded-lg bg-gradient-to-br from-blue-900 to-blue-950">
          <span class="text-xs font-bold text-blue-400">??</span>
        </div>
        <div class="flex gap-3">
          <button class="rounded-lg bg-orange-500 px-6 py-2 font-bold text-white hover:bg-orange-400" @click="respond(['trigger'])">
            发动触发
          </button>
          <button class="rounded-lg bg-gray-600 px-6 py-2 font-bold text-white hover:bg-gray-500" @click="respond(['hand'])">
            加入手牌
          </button>
        </div>
      </div>

      <div v-else-if="isOption && options" class="flex flex-col gap-2">
        <button
          v-for="(opt, i) in options"
          :key="i"
          class="rounded-lg bg-blue-600 px-6 py-2 text-white hover:bg-blue-500"
          @click="respond([i.toString()])"
        >
          {{ opt }}
        </button>
      </div>

      <!-- ReturnDon：专用咚放回选择 -->
      <template v-else-if="isReturnDon">
        <div class="flex max-w-2xl flex-wrap justify-center gap-3">
          <div
            v-for="d in donChoices"
            :key="d.id"
            :class="[
              'relative flex w-20 cursor-pointer flex-col items-center gap-1 rounded-lg border-2 p-1.5 transition',
              selected.includes(d.id) ? 'border-orange-400 bg-orange-400/20' : 'border-white/20 bg-black/40 hover:border-white/50',
            ]"
            @click="toggle(d.id)"
          >
            <div :class="['flex h-12 w-12 items-center justify-center rounded-full bg-gradient-to-br from-yellow-300 to-amber-500 text-[10px] font-black text-black shadow', d.state === 'Rest' ? 'rotate-90' : '']">
              DON!!
            </div>
            <span class="text-[10px] font-bold text-yellow-100">{{ d.state === "Active" ? "活跃" : d.state === "Rest" ? "休息" : "附着" }}</span>
            <span v-if="d.attachedToName || d.attachedToNumber" class="max-w-full truncate text-[9px] text-amber-200">
              贴：{{ d.attachedToName ?? d.attachedToNumber }}
            </span>
            <span v-else-if="d.state === 'Attached'" class="text-[9px] text-amber-200">附着中</span>
            <span v-if="selected.includes(d.id)" class="absolute -right-1.5 -top-1.5 z-10 flex h-5 w-5 items-center justify-center rounded-full bg-orange-500 text-[11px] font-bold text-white ring-2 ring-white">✓</span>
          </div>
          <span v-if="donChoices.length === 0" class="text-sm text-gray-400">无可放回的咚</span>
        </div>
        <div class="flex gap-3">
          <button
            :disabled="!canConfirm"
            class="rounded-lg bg-orange-500 px-6 py-2 font-bold text-white hover:bg-orange-400 disabled:cursor-not-allowed disabled:bg-gray-700"
            @click="respond(selected)"
          >
            确认放回（{{ selected.length }} / {{ prompt.maxChoose }}）
          </button>
        </div>
      </template>

      <template v-else>
        <div class="flex max-w-2xl flex-wrap justify-center gap-2">
          <template v-for="id in prompt.validChoices" :key="id">
            <!-- 咚 token（混合在卡牌列表中，如 OP16-033） -->
            <div
              v-if="donChoiceMap.get(id)"
              :class="[
                'relative flex h-28 w-20 cursor-pointer flex-col items-center justify-center gap-1 rounded-lg border-2 p-1.5 transition',
                selected.includes(id) ? 'border-orange-400 bg-orange-400/20' : 'border-white/20 bg-black/40 hover:border-white/50',
              ]"
              @click="toggle(id)"
            >
              <div :class="['flex h-12 w-12 items-center justify-center rounded-full bg-gradient-to-br from-yellow-300 to-amber-500 text-[10px] font-black text-black shadow', donChoiceMap.get(id)!.state === 'Rest' ? 'rotate-90' : '']">
                DON!!
              </div>
              <span class="text-[10px] font-bold text-yellow-100">{{ donChoiceMap.get(id)!.state === "Active" ? "活跃" : donChoiceMap.get(id)!.state === "Rest" ? "休息" : "附着" }}咚</span>
              <span v-if="selected.includes(id)" class="absolute -right-1.5 -top-1.5 z-10 flex h-5 w-5 items-center justify-center rounded-full bg-orange-500 text-[11px] font-bold text-white ring-2 ring-white">✓</span>
            </div>
            <!-- 普通卡牌 -->
            <div v-else class="cursor-pointer" @click="toggle(id)">
              <CardItem :card="findCardById(id)" size="md" :is-selected="selected.includes(id)" />
            </div>
          </template>
          <span v-if="prompt.validChoices.length === 0" class="text-sm text-gray-400">无可选目标</span>
        </div>

        <div class="flex gap-3">
          <button v-if="prompt.minChoose === 0" class="rounded-lg bg-gray-600 px-6 py-2 text-white hover:bg-gray-500" @click="respond([])">
            跳过
          </button>
          <button
            :disabled="!canConfirm"
            class="rounded-lg bg-orange-500 px-6 py-2 font-bold text-white hover:bg-orange-400 disabled:cursor-not-allowed disabled:bg-gray-700"
            @click="respond(selected)"
          >
            确认（已选 {{ selected.length }} / {{ prompt.maxChoose }}）
          </button>
        </div>
      </template>
    </div>
  </Transition>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from,
.fade-leave-to { opacity: 0; }
</style>
