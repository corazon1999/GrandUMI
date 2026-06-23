<script setup lang="ts">
/**
 * 设置（源自 redesign/screens2.jsx SettingsScreen）。
 * 阵营/主题、游戏、音效画面、关于、退出登录。尽量接入真实 store：
 *   - 阵营 → documentElement.dataset.theme + localStorage("grandumi-theme")
 *   - 出牌二次确认 → settingsStore.alwaysPromptOnLifeReveal
 *   - 音效/音量 → audioStore
 *   - 退出登录 → netStore.setLoggedIn(false) + 路由 /login
 * 其余偏好（入场动画/战场特效）本地持久化。
 */
import { ref, onMounted } from "vue";
import { useRouter } from "vue-router";
import { useStore } from "@/composables/useStore";
import { useAudioStore } from "@/store/audioStore";
import { useSettingsStore } from "@/store/settingsStore";
import { useNetStore } from "@/store/netStore";
import Ticks from "@/components/shared/Ticks.vue";

const router = useRouter();

// ── 主题 / 阵营 ──
const themeKey = ref<"pirate" | "navy">("pirate");
onMounted(() => {
  const t = document.documentElement.dataset.theme;
  themeKey.value = t === "navy" || t === "marine" ? "navy" : "pirate";
});
function setTheme(k: "pirate" | "navy") {
  themeKey.value = k;
  const stored = k === "navy" ? "marine" : "pirate"; // 与现有 ThemeSwitcher 持久化一致
  document.documentElement.dataset.theme = stored;
  try { localStorage.setItem("grandumi-theme", stored); } catch { /* ignore */ }
}

// ── 出牌二次确认（真实设置）──
const confirmPlay = useStore(useSettingsStore, (s) => s.alwaysPromptOnLifeReveal);
function toggleConfirm() {
  useSettingsStore.getState().setAlwaysPromptOnLifeReveal(!confirmPlay.value);
}

// ── 音效 / 音量（真实设置）──
const muted = useStore(useAudioStore, (s) => s.isMuted);
const bgmVolume = useStore(useAudioStore, (s) => s.bgmVolume);
function toggleSound() { useAudioStore.getState().toggleMute(); }
function onVolume(e: Event) {
  const v = Number((e.target as HTMLInputElement).value) / 100;
  useAudioStore.getState().setBgmVolume(v);
}

// ── 本地偏好（入场动画 / 战场特效）──
function loadPref(k: string, d: boolean): boolean {
  try { const v = localStorage.getItem(k); return v === null ? d : v === "1"; } catch { return d; }
}
function savePref(k: string, v: boolean) { try { localStorage.setItem(k, v ? "1" : "0"); } catch { /* ignore */ } }
const anim = ref(loadPref("grandumi-pref-anim", true));
const fx = ref(loadPref("grandumi-pref-fx", true));
function toggleAnim() { anim.value = !anim.value; savePref("grandumi-pref-anim", anim.value); }
function toggleFx() { fx.value = !fx.value; savePref("grandumi-pref-fx", fx.value); }

// ── 关于 / 数据 ──
function clearCache() {
  if (!confirm("确定清除本地保存的偏好？此操作不可恢复。")) return;
  try {
    ["grandumi-pref-anim", "grandumi-pref-fx", "grandumi_settings"].forEach((k) => localStorage.removeItem(k));
  } catch { /* ignore */ }
  anim.value = true;
  fx.value = true;
}

// ── 退出登录 ──
function logout() {
  useNetStore.getState().setLoggedIn(false);
  router.push("/login");
}
</script>

<template>
  <div class="screen-root scroll enter">
    <div class="screen-inner" style="max-width: 720px">
      <div class="kicker" style="font-size: 12px">偏好</div>
      <h1 class="head" style="font-size: 40px; margin: 10px 0 4px">设置</h1>
      <div class="dim" style="font-size: 13px; margin-bottom: 24px">所有偏好仅保存在本设备</div>

      <div style="display: flex; flex-direction: column; gap: 18px">
        <!-- 账户 -->
        <section class="panel panel-pad sect">
          <Ticks />
          <div class="kicker" style="font-size: 11px; margin-bottom: 8px">账户</div>
          <div class="item">
            <div class="item__txt">
              <div class="item__label">阵营</div>
              <div class="dim item__desc">切换后界面主题与背景随之变化</div>
            </div>
            <div class="tg" title="切换阵营">
              <button :class="['tg__b', { 'is-active': themeKey === 'pirate' }]" title="海贼" @click="setTheme('pirate')">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M4 16c2 1.6 5 2.4 8 2.4s6-.8 8-2.4" />
                  <path d="M7.5 16C7.5 10 9 6 12 6s4.5 4 4.5 10" />
                  <path d="M6.5 15.4h11" />
                </svg>
              </button>
              <button :class="['tg__b', { 'is-active': themeKey === 'navy' }]" title="海军" @click="setTheme('navy')">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="12" cy="5" r="2" />
                  <line x1="12" y1="7" x2="12" y2="20" />
                  <line x1="8" y1="11" x2="16" y2="11" />
                  <path d="M5 14c0 4 3.5 6 7 6s7-2 7-6" />
                </svg>
              </button>
            </div>
          </div>
        </section>

        <!-- 游戏 -->
        <section class="panel panel-pad sect">
          <Ticks />
          <div class="kicker" style="font-size: 11px; margin-bottom: 8px">游戏</div>
          <div class="item">
            <div class="item__txt">
              <div class="item__label">出牌二次确认</div>
              <div class="dim item__desc">打出关键卡前弹出确认</div>
            </div>
            <button class="toggle" :class="{ 'is-on': confirmPlay }" @click="toggleConfirm"><span class="toggle__knob" /></button>
          </div>
          <div class="item">
            <div class="item__txt"><div class="item__label">界面入场动画</div></div>
            <button class="toggle" :class="{ 'is-on': anim }" @click="toggleAnim"><span class="toggle__knob" /></button>
          </div>
        </section>

        <!-- 音效与画面 -->
        <section class="panel panel-pad sect">
          <Ticks />
          <div class="kicker" style="font-size: 11px; margin-bottom: 8px">音效与画面</div>
          <div class="item">
            <div class="item__txt"><div class="item__label">音效</div></div>
            <button class="toggle" :class="{ 'is-on': !muted }" @click="toggleSound"><span class="toggle__knob" /></button>
          </div>
          <div class="item">
            <div class="item__txt">
              <div class="item__label">主音量</div>
              <div class="dim item__desc">{{ Math.round(bgmVolume * 100) }}%</div>
            </div>
            <input type="range" min="0" max="100" :value="Math.round(bgmVolume * 100)" class="vol" @input="onVolume" />
          </div>
          <div class="item">
            <div class="item__txt">
              <div class="item__label">战场特效</div>
              <div class="dim item__desc">攻击/受击的光效与抖动</div>
            </div>
            <button class="toggle" :class="{ 'is-on': fx }" @click="toggleFx"><span class="toggle__knob" /></button>
          </div>
        </section>

        <!-- 关于 -->
        <section class="panel panel-pad sect">
          <Ticks />
          <div class="kicker" style="font-size: 11px; margin-bottom: 8px">关于</div>
          <div class="item">
            <div class="item__txt">
              <div class="item__label">版本</div>
              <div class="dim item__desc">GrandUMI Web · v2.0.0 (CYP_2026)</div>
            </div>
            <span class="mono faint" style="font-size: 12px">已是最新</span>
          </div>
          <div class="item">
            <div class="item__txt">
              <div class="item__label">数据</div>
              <div class="dim item__desc">清除本地保存的偏好</div>
            </div>
            <button class="btn" style="font-size: 13px; padding: 8px 16px" @click="clearCache">清除缓存</button>
          </div>
        </section>

        <button class="btn btn--block logout-btn" @click="logout">退出登录</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.screen-root {
  position: absolute;
  inset: 0;
  overflow-y: auto;
  padding: 76px 40px 32px;
  font-family: var(--font-ui);
  color: var(--ink);
}
.screen-inner {
  margin: 0 auto;
}

.sect {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.item {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 0;
  border-bottom: 1px solid var(--line);
}
.sect .item:last-child {
  border-bottom: none;
}
.item__txt {
  flex: 1;
}
.item__label {
  font-size: 14px;
  color: var(--ink);
}
.item__desc {
  font-size: 12px;
  margin-top: 3px;
}

/* 阵营切换（hat / anchor） */
.tg {
  display: inline-flex;
  gap: 4px;
  padding: 3px;
  border: 1px solid var(--line);
  border-radius: var(--radius-pill);
  background: var(--bg1);
  flex-shrink: 0;
}
.tg__b {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 30px;
  border: none;
  border-radius: var(--radius-pill);
  background: transparent;
  color: var(--ink-dim);
  cursor: pointer;
  transition: all 0.2s;
}
.tg__b:hover {
  color: var(--primary);
}
.tg__b.is-active {
  background: var(--primary);
  color: var(--on-primary);
  box-shadow: 0 0 14px -4px var(--primary-glow);
}

/* 开关 */
.toggle {
  width: 48px;
  height: 28px;
  border-radius: 999px;
  border: none;
  cursor: pointer;
  position: relative;
  flex-shrink: 0;
  background: var(--bg1);
  box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.5);
  transition: background 0.25s;
}
.toggle.is-on {
  background: var(--primary);
  box-shadow: 0 0 14px -4px var(--primary-glow);
}
.toggle__knob {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: #fff;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
  transition: left 0.25s;
}
.toggle.is-on .toggle__knob {
  left: 23px;
}

.vol {
  width: 160px;
  accent-color: var(--primary);
  flex-shrink: 0;
}

.logout-btn {
  font-size: 15px;
  color: var(--accent);
  border-color: color-mix(in srgb, var(--accent) 50%, transparent);
}
.logout-btn:hover {
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  border-color: var(--accent);
}

@media (max-width: 700px) {
  .screen-root { padding: 76px 16px 24px; }
}
</style>
