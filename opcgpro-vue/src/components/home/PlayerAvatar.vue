<script setup lang="ts">
import { ref, watch, nextTick, useTemplateRef, onMounted } from "vue";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";
import { getAllCachedCards } from "@/data/CardLoader";
import type { CardData } from "@/types/card";
import AvatarPicker from "./AvatarPicker.vue";

const AVATAR_KEY = "grandumi_avatar";

function getDefaultAvatar(): string {
  const luffy = getAllCachedCards().find(
    (c) => c.type === "Leader" && c.name.includes("路飞"),
  );
  return luffy?.sprite ?? "";
}
function loadAvatar(): string {
  return localStorage.getItem(AVATAR_KEY) || getDefaultAvatar();
}

const playerName = useStore(useNetStore, (s) => s.playerName);
const account = useStore(useNetStore, (s) => s.account);

const editing = ref(false);
const draft = ref("");
const inputRef = useTemplateRef<HTMLInputElement>("input");
const avatarSrc = ref("");
const showPicker = ref(false);

onMounted(() => {
  avatarSrc.value = loadAvatar();
});

function startEdit() {
  draft.value = playerName.value;
  editing.value = true;
}
watch(editing, async (v) => {
  if (v) {
    await nextTick();
    inputRef.value?.focus();
  }
});

function confirm() {
  const name = draft.value.trim();
  if (!name) {
    editing.value = false;
    return;
  }
  useNetStore.getState().setPlayerName(name);
  if (account.value) localStorage.setItem(`grandumi_nick_${account.value}`, name);
  editing.value = false;
}
function cancel() {
  editing.value = false;
}

function handleSelectAvatar(card: CardData) {
  const sprite = card.sprites?.length ? card.sprites[card.sprites.length - 1] : card.sprite ?? "";
  avatarSrc.value = sprite;
  localStorage.setItem(AVATAR_KEY, sprite);
  showPicker.value = false;
}
</script>

<template>
  <div v-if="editing" class="flex w-14 flex-col items-center gap-1 px-1">
    <input
      ref="input"
      v-model="draft"
      :maxlength="16"
      class="w-full rounded border border-orange-500 bg-gray-800 px-1 py-0.5 text-center text-xs text-white outline-none"
      @keydown.enter="confirm"
      @keydown.escape="cancel"
      @blur="confirm"
    />
    <span class="text-[11px] text-gray-600">Enter确认</span>
  </div>
  <div v-else class="mb-1 flex flex-col items-center gap-0.5">
    <button
      title="点击更换头像"
      class="relative h-10 w-10 shrink-0 overflow-hidden rounded-full border-2 border-gray-700 bg-gray-800 transition-colors hover:border-orange-500"
      @click="showPicker = true"
    >
      <img
        v-if="avatarSrc"
        :src="avatarSrc"
        alt="头像"
        class="h-full w-full rounded-full object-cover object-top"
        style="transform: scale(1.1)"
        loading="lazy"
        :draggable="false"
        @error="avatarSrc = ''"
      />
      <span v-else class="text-xs font-bold text-white">
        {{ playerName ? playerName[0].toUpperCase() : "?" }}
      </span>
    </button>
    <button
      title="点击修改昵称"
      class="w-14 truncate text-center text-[11px] text-gray-500 transition-colors hover:text-gray-300"
      @click="startEdit"
    >
      {{ playerName || "未知" }}
    </button>
  </div>

  <AvatarPicker
    :open="showPicker"
    :current="avatarSrc"
    @close="showPicker = false"
    @select="handleSelectAvatar"
  />
</template>
