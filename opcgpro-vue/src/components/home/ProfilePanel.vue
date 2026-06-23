<script setup lang="ts">
import { ref, reactive, computed, watch, onMounted } from "vue";
import Avatar from "@/components/shared/Avatar.vue";
import { useProfile, TITLES, type Profile } from "@/composables/useProfile";
import { getAllCachedCards, loadCardSet } from "@/data/CardLoader";
import { DEFAULT_SEARCH_SETS } from "@/data/cardSets";
import type { CardData } from "@/types/card";

/** 个人中心（源自 redesign/profile.jsx ProfileScreen，头像改用项目原有图片头像功能：选领航卡卡图）。 */
const { profile, setProfile } = useProfile();
const draft = reactive<Profile>({ ...profile });

const dirty = computed(() => JSON.stringify(draft) !== JSON.stringify(profile));
watch(
  () => JSON.stringify(profile),
  () => {
    if (!dirty.value) Object.assign(draft, profile);
  },
);

// 领航卡（图片头像候选）
const leaders = ref<CardData[]>([]);
const loading = ref(false);
function spriteOf(c: CardData): string {
  return c.sprites?.length ? c.sprites[c.sprites.length - 1] : c.sprite ?? "";
}
onMounted(async () => {
  loading.value = true;
  if (getAllCachedCards().length === 0) {
    for (const s of DEFAULT_SEARCH_SETS) await loadCardSet(s).catch(() => {});
  }
  leaders.value = getAllCachedCards().filter((c) => c.type === "Leader");
  loading.value = false;
});

// 当前头像：草稿已选则用之，否则回退默认领航卡（路飞优先）
const resolvedAvatar = computed(() => {
  if (draft.avatar) return draft.avatar;
  const luffy = leaders.value.find((c) => c.name.includes("路飞"));
  const fallback = luffy ?? leaders.value[0];
  return fallback ? spriteOf(fallback) : "";
});

function set(patch: Partial<Profile>) {
  Object.assign(draft, patch);
}
function reset() {
  Object.assign(draft, profile);
}
function save() {
  // 未手动选头像时，把当前解析出的默认领航卡写入，保证侧栏与档案一致
  draft.avatar = draft.avatar || resolvedAvatar.value;
  setProfile({ ...draft });
}

const winStats: { k: string; v: string | number }[] = [
  { k: "胜场", v: 38 },
  { k: "胜率", v: "63%" },
  { k: "连胜", v: 4 },
];
</script>

<template>
  <div class="profile-root scroll enter">
    <div class="profile-inner">
      <div class="kicker" style="font-size: 12px">船员档案</div>
      <h1 class="head profile-title">个人中心</h1>
      <p class="dim profile-sub">自定义你的头像、昵称与称号 · 仅保存在本设备</p>

      <div class="profile-grid">
        <!-- 预览卡 -->
        <div class="panel panel-pad profile-preview">
          <div class="ticks"><i /><i /><i /><i /></div>
          <Avatar :src="resolvedAvatar" :name="draft.name" :size="132" glow />
          <div class="profile-preview__name">{{ draft.name || "—" }}</div>
          <span class="tag is-active" style="cursor: default">{{ draft.title }}</span>

          <div class="profile-stats">
            <div class="profile-stat">
              <div class="mono faint profile-stat__k">Lv</div>
              <div class="head profile-stat__v" style="color: var(--primary)">{{ draft.lv }}</div>
            </div>
            <div class="profile-stat">
              <div class="mono faint profile-stat__k">ID</div>
              <div class="head profile-stat__v" style="color: var(--primary)">{{ draft.id }}</div>
            </div>
          </div>

          <div class="profile-stats">
            <div v-for="s in winStats" :key="s.k" class="profile-stat">
              <div class="head glow-title profile-stat__v">{{ s.v }}</div>
              <div class="mono faint profile-stat__k">{{ s.k }}</div>
            </div>
          </div>
        </div>

        <!-- 编辑器 -->
        <div class="profile-editor">
          <!-- 选择头像（领航卡卡图） -->
          <div class="panel panel-pad profile-card">
            <div class="ticks"><i /><i /><i /><i /></div>
            <div class="kicker profile-card__h">选择头像 · 领航卡</div>
            <p v-if="loading" class="dim profile-loading">加载领航卡…</p>
            <div v-else class="profile-avatars scroll">
              <button
                v-for="c in leaders"
                :key="c.number"
                :title="c.name"
                :class="['profile-avatar-btn', { 'is-active': resolvedAvatar === spriteOf(c) }]"
                @click="set({ avatar: spriteOf(c) })"
              >
                <Avatar :src="spriteOf(c)" :name="c.name" :size="54" :ring="false" />
              </button>
            </div>
          </div>

          <!-- 昵称 + 称号 -->
          <div class="panel panel-pad profile-nametitle">
            <div class="ticks"><i /><i /><i /><i /></div>
            <div>
              <div class="kicker profile-card__h">昵称</div>
              <div class="field">
                <span class="ic mono">@</span>
                <input v-model="draft.name" :maxlength="16" placeholder="输入昵称" />
              </div>
            </div>
            <div>
              <div class="kicker profile-card__h">称号</div>
              <div class="profile-titles">
                <span
                  v-for="t in TITLES"
                  :key="t"
                  :class="['tag', { 'is-active': draft.title === t }]"
                  @click="set({ title: t })"
                >{{ t }}</span>
              </div>
            </div>
          </div>

          <!-- 操作 -->
          <div class="profile-actions">
            <button class="btn" :disabled="!dirty" @click="reset">重置</button>
            <button class="btn btn--primary" style="min-width: 140px" :disabled="!dirty" @click="save">
              {{ dirty ? "保存修改" : "已保存 ✓" }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.profile-root {
  height: 100%;
  overflow: auto;
  padding: 28px 40px 40px;
}
.profile-inner {
  max-width: 1000px;
  margin: 0 auto;
}
.profile-title {
  font-size: 40px;
  margin: 10px 0 4px;
}
.profile-sub {
  font-size: 13px;
  margin: 0 0 24px;
}
.profile-grid {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 20px;
  align-items: start;
}

/* 预览卡 */
.profile-preview {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  padding: 28px;
}
.profile-preview__name {
  font-family: var(--font-head);
  font-weight: 900;
  font-size: 26px;
  color: var(--ink);
  margin-top: 4px;
}
.profile-stats {
  display: flex;
  gap: 10px;
  width: 100%;
}
.profile-stat {
  flex: 1;
  text-align: center;
  padding: 12px 0;
  background: var(--bg1);
  border-radius: var(--radius);
  border: 1px solid var(--line);
}
.profile-stat__k {
  font-size: 10px;
  letter-spacing: 0.14em;
  margin-top: 3px;
}
.profile-stat__v {
  font-size: 20px;
  margin-top: 3px;
}

/* 编辑器 */
.profile-editor {
  display: flex;
  flex-direction: column;
  gap: 18px;
}
.profile-card {
  position: relative;
}
.profile-card__h {
  font-size: 11px;
  margin-bottom: 14px;
}
.profile-loading {
  font-size: 12px;
  padding: 8px 0;
}
.profile-avatars {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(64px, 1fr));
  gap: 12px;
  max-height: 268px;
  overflow: auto;
  padding-right: 4px;
}
.profile-avatar-btn {
  cursor: pointer;
  background: transparent;
  border: 1px solid var(--line);
  border-radius: var(--radius);
  padding: 8px;
  display: flex;
  justify-content: center;
  transition: all 0.2s;
}
.profile-avatar-btn:hover {
  border-color: var(--line-strong);
}
.profile-avatar-btn.is-active {
  background: var(--surface2);
  border: 1.5px solid var(--primary);
  box-shadow: 0 0 18px -6px var(--primary-glow);
}
.profile-nametitle {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.profile-titles {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.profile-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}
</style>
