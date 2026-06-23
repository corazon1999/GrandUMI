<script setup lang="ts">
import { ref, computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useDeckStore } from "@/store/deckStore";
import HearthButton from "@/components/ui/HearthButton.vue";
import GoldDivider from "@/components/ui/GoldDivider.vue";
import {
  COLOR_DISPLAY_NAMES,
  COLOR_DISPLAY_TO_DATA,
  COLOR_DATA_TO_DISPLAY,
  COLOR_STYLES,
} from "@/lib/colorMap";

const PROPERTIES = ["", "斩", "打", "射", "智", "特"];
const DECK_TYPES = ["", "Character", "Stage", "Event"];
const TYPE_LABELS: Record<string, string> = { Character: "角色", Stage: "场地", Event: "事件" };
const RARITIES = ["", "L", "SR", "R", "UC", "C", "SEC", "P"];
const COL_PRESETS = [4, 5, 6, 7, 8, 9, 10, 12];

const SET_GROUPS: { label: string; sets: string[] }[] = [
  { label: "OP 主弹", sets: ["OP01","OP02","OP03","OP04","OP05","OP06","OP07","OP08","OP09","OP10","OP11","OP12","OP13","OP14","OP15","OP16"] },
  { label: "ST 起始", sets: ["ST01","ST02","ST03","ST04","ST05","ST06","ST07","ST08","ST09","ST10","ST11","ST12","ST13","ST14","ST15","ST16","ST17","ST18","ST19","ST20","ST21","ST22","ST23","ST24","ST25","ST26","ST27","ST28","ST29","ST30"] },
  { label: "EB/PRB", sets: ["EB01","EB02","EB03","EB04","PRB01","PRB02"] },
  { label: "P/其他", sets: ["P","OPD","TY01"] },
];

const searchQuery = useStore(useDeckStore, (s) => s.searchQuery);
const filterColor = useStore(useDeckStore, (s) => s.filterColor);
const filterType = useStore(useDeckStore, (s) => s.filterType);
const filterProperty = useStore(useDeckStore, (s) => s.filterProperty);
const filterRarity = useStore(useDeckStore, (s) => s.filterRarity);
const filterSets = useStore(useDeckStore, (s) => s.filterSets);
const filterShowSub1 = useStore(useDeckStore, (s) => s.filterShowSub1);
const gridColumns = useStore(useDeckStore, (s) => s.gridColumns);

const store = () => useDeckStore.getState();

let searchDebounce: ReturnType<typeof setTimeout> | null = null;
function onSearchInput(val: string) {
  if (searchDebounce) clearTimeout(searchDebounce);
  searchDebounce = setTimeout(() => store().setSearchQuery(val), 150);
}

const showSettings = ref(false);
const setGroupExpanded = ref<number | null>(null);

const isLeaderMode = computed(() => filterType.value === "Leader");
const activeDisplayColor = computed(() => COLOR_DATA_TO_DISPLAY[filterColor.value] ?? "");
const hasFilter = computed(
  () => !!(searchQuery.value || filterColor.value || filterType.value || filterProperty.value || filterRarity.value || filterSets.value.length > 0 || filterShowSub1.value),
);

function clearAll() {
  const s = store();
  s.setSearchQuery(""); s.setFilterColor(""); s.setFilterType(""); s.setFilterProperty(""); s.setFilterRarity("");
  s.clearFilterSets(); s.setFilterShowSub1(false);
}
function onRarity(r: string) {
  store().setFilterRarity(r);
  if (r === "L") store().setFilterType("Leader");
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="shrink-0 border-b border-[var(--line)] bg-[var(--bg0)] px-3 pb-2 pt-3">
      <h2 class="gde-kicker">搜索卡牌</h2>
    </div>

    <div class="flex-1 overflow-y-auto">
      <div class="flex flex-col gap-3 p-3">
        <input
          :value="searchQuery"
          class="w-full rounded-[10px] border border-[var(--line)] bg-[var(--bg1)] px-3 py-2.5 text-xs text-[var(--ink)] outline-none transition-all placeholder:text-[var(--ink-faint)] focus:border-[var(--primary)] focus:shadow-[0_0_0_3px_var(--primary-glow)]"
          placeholder="卡名 / 关键词 / 效果..."
          @input="onSearchInput(($event.target as HTMLInputElement).value)"
        />

        <HearthButton
          :variant="isLeaderMode ? 'primary' : 'secondary'"
          size="md"
          class="w-full"
          @click="store().setFilterType(isLeaderMode ? '' : 'Leader')"
        >
          {{ isLeaderMode ? "✦ 领航模式（退出）" : "选择领航卡" }}
        </HearthButton>

        <GoldDivider spacing="tight" />

        <div v-if="!isLeaderMode" class="flex flex-col gap-1">
          <label class="gde-kicker text-[11px]">类型</label>
          <div class="flex flex-wrap gap-1">
            <button
              v-for="t in DECK_TYPES"
              :key="t"
              :class="[
                'rounded-full border px-2.5 py-0.5 text-[11px] font-mono uppercase tracking-[0.06em] transition-all',
                filterType === t
                  ? 'border-[var(--primary)] bg-[var(--primary)]/15 text-[var(--primary)]'
                  : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
              ]"
              @click="store().setFilterType(t)"
            >
              {{ t ? TYPE_LABELS[t] ?? t : "全部" }}
            </button>
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <label class="gde-kicker text-[11px]">颜色</label>
          <div class="flex flex-wrap gap-1">
            <button
              :class="[
                'rounded-full border px-2.5 py-0.5 text-[11px] font-mono uppercase tracking-[0.06em] transition-all',
                filterColor === ''
                  ? 'border-[var(--primary)] bg-[var(--primary)]/15 text-[var(--primary)]'
                  : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
              ]"
              @click="store().setFilterColor('')"
            >
              全部
            </button>
            <button
              v-for="name in COLOR_DISPLAY_NAMES"
              :key="name"
              :class="[
                'rounded border px-2 py-0.5 text-xs font-bold transition-all',
                activeDisplayColor === name
                  ? `${COLOR_STYLES[name].bg} scale-105 border-[var(--primary)] text-white shadow-[0_0_8px_var(--primary-glow)]`
                  : `bg-[var(--surface)] ${COLOR_STYLES[name].text} border-[var(--line)] hover:scale-105 hover:border-[var(--line-strong)]`,
              ]"
              @click="store().setFilterColor(COLOR_DISPLAY_TO_DATA[name])"
            >
              {{ name }}
            </button>
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <label class="gde-kicker text-[11px]">属性</label>
          <div class="flex flex-wrap gap-1">
            <button
              v-for="p in PROPERTIES"
              :key="p"
              :class="[
                'rounded-full border px-2.5 py-0.5 text-[11px] font-mono uppercase tracking-[0.06em] transition-all',
                filterProperty === p
                  ? 'border-[var(--primary)] bg-[var(--primary)]/15 text-[var(--primary)]'
                  : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
              ]"
              @click="store().setFilterProperty(p)"
            >
              {{ p || "全部" }}
            </button>
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <label class="gde-kicker text-[11px]">稀有度</label>
          <div class="flex flex-wrap gap-1">
            <button
              v-for="r in RARITIES"
              :key="r"
              :class="[
                'rounded-full border px-2.5 py-0.5 text-[11px] font-mono uppercase tracking-[0.06em] transition-all',
                filterRarity === r
                  ? 'border-[var(--primary)] bg-[var(--primary)]/15 text-[var(--primary)]'
                  : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
              ]"
              @click="onRarity(r)"
            >
              {{ r || "全部" }}
            </button>
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <div class="flex items-center justify-between">
            <label class="gde-kicker text-[11px]">弹数</label>
            <button v-if="filterSets.length > 0" class="text-[11px] text-[var(--ink-faint)] transition-colors hover:text-[var(--primary)]" @click="store().clearFilterSets()">
              清空
            </button>
          </div>
          <div class="flex flex-col gap-1">
            <div v-for="(g, gi) in SET_GROUPS" :key="g.label" class="flex flex-col gap-1">
              <button
                :class="[
                  'flex w-full items-center justify-between rounded border px-2 py-1 text-left text-xs font-bold transition-colors',
                  g.sets.filter((s) => filterSets.includes(s)).length > 0
                    ? 'border-[var(--primary)]/40 bg-[var(--surface2)]/60 text-[var(--primary)]'
                    : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
                ]"
                @click="setGroupExpanded = setGroupExpanded === gi ? null : gi"
              >
                <span>
                  {{ g.label }}
                  <template v-if="g.sets.filter((s) => filterSets.includes(s)).length > 0">
                    ({{ g.sets.filter((s) => filterSets.includes(s)).length }})
                  </template>
                </span>
                <span class="text-[var(--ink-faint)]">{{ setGroupExpanded === gi ? "▾" : "▸" }}</span>
              </button>
              <div v-if="setGroupExpanded === gi" class="flex flex-wrap gap-0.5 pl-1">
                <button
                  v-for="s in g.sets"
                  :key="s"
                  :class="[
                    'rounded-full border px-1.5 py-0.5 text-[11px] font-mono transition-all',
                    filterSets.includes(s)
                      ? 'border-[var(--primary)] bg-[var(--primary)]/20 text-[var(--primary)]'
                      : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
                  ]"
                  @click="store().toggleFilterSet(s)"
                >
                  {{ s }}
                </button>
              </div>
            </div>
          </div>
        </div>

        <button
          :class="[
            'w-full rounded border py-1.5 text-xs font-bold transition-all',
            filterShowSub1
              ? 'border-sky-500 bg-sky-500/15 text-sky-300 shadow-[inset_0_0_0_1px_rgba(56,189,248,0.3)]'
              : 'border-[var(--line)] bg-transparent text-[var(--ink-faint)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
          ]"
          title="角标=1 通常是旧环境/早期版本卡，默认隐藏"
          @click="store().setFilterShowSub1(!filterShowSub1)"
        >
          {{ filterShowSub1 ? "✓ 显示角标 1 卡" : "已隐藏角标 1 卡" }}
        </button>

        <button v-if="hasFilter" class="text-center text-xs text-[var(--ink-faint)] transition-colors hover:text-[var(--primary)]" @click="clearAll">
          清除筛选
        </button>
      </div>
    </div>

    <div class="shrink-0 border-t border-[var(--line)]">
      <div v-if="showSettings" class="border-b border-[var(--line)] bg-[var(--surface)]/50 px-3 py-2.5">
        <p class="gde-kicker mb-2 text-[11px]">每行卡牌数</p>
        <div class="mb-2 flex flex-wrap gap-1">
          <button
            v-for="n in COL_PRESETS"
            :key="n"
            :class="[
              'h-6 w-7 rounded border text-xs font-mono font-bold transition-all',
              gridColumns === n
                ? 'border-[var(--primary)] bg-[var(--primary)]/15 text-[var(--primary)]'
                : 'border-[var(--line)] bg-transparent text-[var(--ink-dim)] hover:border-[var(--line-strong)] hover:text-[var(--ink)]',
            ]"
            @click="store().setGridColumns(n)"
          >
            {{ n }}
          </button>
        </div>
        <div class="flex items-center gap-2">
          <button
            :disabled="gridColumns <= 4"
            class="h-7 w-7 rounded border border-[var(--line)] bg-[var(--surface)] text-base font-bold text-[var(--ink)] transition-colors hover:border-[var(--primary)] disabled:opacity-30"
            @click="store().setGridColumns(gridColumns - 1)"
          >
            −
          </button>
          <span class="flex-1 text-center text-sm font-bold text-[var(--ink)]">{{ gridColumns }} 列</span>
          <button
            :disabled="gridColumns >= 16"
            class="h-7 w-7 rounded border border-[var(--line)] bg-[var(--surface)] text-base font-bold text-[var(--ink)] transition-colors hover:border-[var(--primary)] disabled:opacity-30"
            @click="store().setGridColumns(gridColumns + 1)"
          >
            +
          </button>
        </div>
      </div>

      <button
        :class="[
          'flex w-full items-center gap-1.5 px-3 py-2 text-xs transition-all',
          showSettings
            ? 'border-t border-[var(--primary)]/40 bg-[var(--surface)] text-[var(--primary)]'
            : 'text-[var(--ink-faint)] hover:bg-[var(--surface)]/50 hover:text-[var(--ink)]',
        ]"
        @click="showSettings = !showSettings"
      >
        <span class="text-xs">⚙</span>
        <span>显示设置</span>
        <span class="ml-auto text-[var(--ink-faint)]">{{ gridColumns }} 列</span>
      </button>
    </div>
  </div>
</template>
