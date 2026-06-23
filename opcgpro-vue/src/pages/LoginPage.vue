<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from "vue";
import { HomeRequest } from "@/net/HomeProtocol";
import { useStore } from "@/composables/useStore";
import { useNetStore } from "@/store/netStore";

type Tab = "login" | "register";

const tab = ref<Tab>("login");
const tabDir = ref(1);

// ── 主题感知文案 ──────────────────────────────────────────
const themeKey = ref<"pirate" | "navy">("pirate");
const THEME_COPY = {
  pirate: { tagline: "准备好了吗，航海王？", cta: "启 航", faction: "海贼" },
  navy: { tagline: "为了绝对的正义，起锚。", cta: "起 锚", faction: "海军" },
} as const;
const copy = computed(() => THEME_COPY[themeKey.value]);

// 读取当前主题（通过 MutationObserver 追踪 DOM attribute 变更）
function readTheme(): "pirate" | "navy" {
  const t = document.documentElement.dataset.theme;
  return t === "navy" || t === "marine" ? "navy" : "pirate";
}
themeKey.value = readTheme();
let themeObs: MutationObserver | null = null;
onMounted(() => {
  themeObs = new MutationObserver(() => {
    themeKey.value = readTheme();
  });
  themeObs.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ["data-theme"],
  });
});
onBeforeUnmount(() => themeObs?.disconnect());

// 切换主题：仅本页面提供入口，写入 dataset + localStorage
const THEME_STORAGE = "grandumi-theme";
function applyTheme(t: "pirate" | "navy") {
  themeKey.value = t;
  document.documentElement.dataset.theme = t;
  try {
    localStorage.setItem(THEME_STORAGE, t);
  } catch {}
}

// Login
const loginAccount = ref("");
const loginPassword = ref("");
const showLoginPwd = ref(false);

// Register
const regAccount = ref("");
const regPassword = ref("");
const regConfirm = ref("");
const regName = ref("");
const showRegPwd = ref(false);
const regSent = ref(false);

const isLoading = ref(false);
const connState = useStore(useNetStore, (s) => s.connState);
const error = useStore(useNetStore, (s) => s.error);

const isConnected = computed(() => connState.value === "connected");
const canSubmit = computed(() => isConnected.value && !isLoading.value);
const pwdMismatch = computed(
  () => !!regConfirm.value && regPassword.value !== regConfirm.value,
);

const connDotClass = computed(() => {
  if (connState.value === "connected") return "is-ok";
  if (connState.value === "connecting" || connState.value === "handshaking")
    return "is-pending";
  return "is-down";
});

const connLabel: Record<string, string> = {
  disconnected: "服务器未连接",
  connecting: "连接中...",
  handshaking: "握手中...",
  connected: "服务器已连接",
  reconnecting: "重连中...",
  failed: "连接失败",
};

watch(error, (e) => {
  if (e) isLoading.value = false;
});

const loggedIn = useStore(useNetStore, (s) => s.loggedIn);
watch(loggedIn, () => {});

function switchTab(t: Tab) {
  tabDir.value = t === "register" ? 1 : -1;
  tab.value = t;
  regSent.value = false;
  useNetStore.getState().setError(null);
  isLoading.value = false;
}

function handleLogin() {
  if (
    !canSubmit.value ||
    !loginAccount.value.trim() ||
    !loginPassword.value.trim()
  )
    return;
  useNetStore.getState().setError(null);
  isLoading.value = true;
  HomeRequest.login(loginAccount.value.trim(), loginPassword.value.trim());
}

function handleRegister() {
  if (!canSubmit.value || !regAccount.value.trim() || !regPassword.value.trim())
    return;
  if (pwdMismatch.value) return;
  useNetStore.getState().setError(null);
  HomeRequest.addAccount(
    regAccount.value.trim(),
    regPassword.value.trim(),
    regName.value.trim() || regAccount.value.trim(),
  );
  regSent.value = true;
  loginAccount.value = regAccount.value;
  setTimeout(() => switchTab("login"), 1800);
}
</script>

<template>
  <div class="login-root">
    <!-- ── 主题切换：仅登录页展示 ─────────────────── -->
    <div class="theme-switch-wrap">
      <span class="mono faint theme-switch-wrap__label">选择阵营</span>
      <div class="theme-toggle" title="切换主题">
      <button
        :class="['theme-toggle__btn', { 'is-active': themeKey === 'pirate' }]"
        title="海贼"
        aria-label="切换到海贼主题"
        @click="applyTheme('pirate')">
        <svg
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.7"
          stroke-linecap="round"
          stroke-linejoin="round">
          <path d="M4 16c2 1.6 5 2.4 8 2.4s6-.8 8-2.4" />
          <path d="M7.5 16C7.5 10 9 6 12 6s4.5 4 4.5 10" />
          <path d="M6.5 15.4h11" />
        </svg>
      </button>
      <button
        :class="['theme-toggle__btn', { 'is-active': themeKey === 'navy' }]"
        title="海军"
        aria-label="切换到海军主题"
        @click="applyTheme('navy')">
        <svg
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.7"
          stroke-linecap="round"
          stroke-linejoin="round">
          <circle cx="12" cy="5" r="2" />
          <line x1="12" y1="7" x2="12" y2="20" />
          <line x1="8" y1="11" x2="16" y2="11" />
          <path d="M5 14c0 4 3.5 6 7 6s7-2 7-6" />
        </svg>
      </button>
      </div>
    </div>

    <!-- ── 内容层 ──────────────────────────────────── -->
    <div class="content-layer">
      <!-- 两栏主布局 -->
      <div class="login-layout">
        <!-- ── 左栏品牌 ────────────────────────────── -->
        <div class="brand-col enter">
          <div class="kicker" style="font-size: 13px">海贼王卡牌对战</div>
          <h1 class="glow-title brand-title">GRANDUMI</h1>
          <div class="rule brand-rule">在线 · 对战 · 集结</div>
          <p class="brand-tagline">{{ copy.tagline }}</p>
          <div class="mono dim brand-status">
            <span
              class="dot"
              :class="{
                'dot--live': connDotClass === 'is-ok',
                'dot--wait': connDotClass === 'is-pending',
                'dot--down': connDotClass === 'is-down',
              }" />
            {{ connLabel[connState] ?? connState }} · {{ copy.faction }}阵营
          </div>
        </div>

        <!-- ── 右栏表单 ────────────────────────────── -->
        <div class="form-col">
          <div class="panel panel-pad enter-scale form-panel">
            <!-- 角落 L 型装饰 -->
            <div class="ticks"><i /><i /><i /><i /></div>

            <!-- 标题分隔线 -->
            <div class="rule" style="margin-bottom: 22px">登录</div>

            <!-- 错误 / 成功提示 -->
            <Transition name="form-notice">
              <div v-if="error" class="form-notice form-notice--error">
                <span class="form-notice__icon">!</span>
                {{ error }}
              </div>
              <div v-else-if="regSent" class="form-notice form-notice--success">
                <span class="form-notice__icon">✓</span>
                注册成功，正在跳转到登录...
              </div>
            </Transition>

            <!-- 分段标签切换 -->
            <div class="seg form-seg">
              <button
                class="seg__opt"
                :class="{ 'is-active': tab === 'login' }"
                @click="switchTab('login')">
                登 录
              </button>
              <button
                class="seg__opt"
                :class="{ 'is-active': tab === 'register' }"
                @click="switchTab('register')">
                注 册
              </button>
            </div>

            <!-- 表单切换动画 -->
            <div :style="{ '--dir': tabDir }" class="form-body">
              <Transition name="form-slide" mode="out-in">
                <!-- 登录表单 -->
                <form
                  v-if="tab === 'login'"
                  key="login"
                  class="form-fields"
                  @submit.prevent="handleLogin">
                  <div class="field">
                    <span class="ic mono">@</span>
                    <input
                      v-model="loginAccount"
                      placeholder="账号"
                      autocomplete="username"
                      :disabled="isLoading"
                      :class="{ 'is-error': !!error }" />
                  </div>
                  <div class="field">
                    <span class="ic mono">·</span>
                    <input
                      v-model="loginPassword"
                      :type="showLoginPwd ? 'text' : 'password'"
                      placeholder="密码"
                      autocomplete="current-password"
                      :disabled="isLoading"
                      :class="{ 'is-error': !!error }" />
                    <button
                      type="button"
                      class="ic"
                      tabindex="-1"
                      @click="showLoginPwd = !showLoginPwd">
                      <svg
                        v-if="showLoginPwd"
                        class="eye-icon"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24">
                        <path
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="1.6"
                          d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 4.411m0 0L21 21" />
                      </svg>
                      <svg
                        v-else
                        class="eye-icon"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24">
                        <path
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="1.6"
                          d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        <path
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="1.6"
                          d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                      </svg>
                    </button>
                  </div>
                  <button
                    type="submit"
                    class="btn btn--primary btn--lg btn--block"
                    :disabled="!canSubmit"
                    style="margin-top: 6px">
                    <span v-if="isLoading" class="login-spinner" />
                    {{
                      isLoading
                        ? "登录中..."
                        : isConnected
                          ? copy.cta
                          : (connLabel[connState] ?? "未连接")
                    }}
                  </button>
                </form>

                <!-- 注册表单 -->
                <form
                  v-else
                  key="register"
                  class="form-fields"
                  @submit.prevent="handleRegister">
                  <div class="field">
                    <span class="ic mono">@</span>
                    <input
                      v-model="regAccount"
                      placeholder="账号 ID"
                      autocomplete="username" />
                  </div>
                  <div class="field">
                    <span class="ic mono">@</span>
                    <input
                      v-model="regName"
                      placeholder="昵称（选填）"
                      autocomplete="nickname" />
                  </div>
                  <div class="field">
                    <span class="ic mono">·</span>
                    <input
                      v-model="regPassword"
                      :type="showRegPwd ? 'text' : 'password'"
                      placeholder="密码"
                      autocomplete="new-password" />
                    <button
                      type="button"
                      class="ic"
                      tabindex="-1"
                      @click="showRegPwd = !showRegPwd">
                      <svg
                        v-if="showRegPwd"
                        class="eye-icon"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24">
                        <path
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="1.6"
                          d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 4.411m0 0L21 21" />
                      </svg>
                      <svg
                        v-else
                        class="eye-icon"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24">
                        <path
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="1.6"
                          d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        <path
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="1.6"
                          d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                      </svg>
                    </button>
                  </div>
                  <div class="field" :class="{ 'field--error': pwdMismatch }">
                    <span class="ic mono">·</span>
                    <input
                      v-model="regConfirm"
                      type="password"
                      placeholder="确认密码"
                      autocomplete="new-password" />
                  </div>
                  <Transition name="notice">
                    <p v-if="pwdMismatch" class="mismatch-msg">
                      两次输入的密码不一致
                    </p>
                  </Transition>
                  <button
                    type="submit"
                    class="btn btn--secondary btn--lg btn--block"
                    :disabled="!canSubmit || pwdMismatch"
                    style="margin-top: 6px">
                    {{
                      isConnected ? "入 伙" : (connLabel[connState] ?? "未连接")
                    }}
                  </button>
                </form>
              </Transition>
            </div>

            <!-- 底部连接状态 -->
            <div class="form-status">
              <span
                class="dot"
                :class="{
                  'dot--live': connDotClass === 'is-ok',
                  'dot--wait': connDotClass === 'is-pending',
                  'dot--down': connDotClass === 'is-down',
                }" />
              <span class="mono dim">{{
                connLabel[connState] ?? connState
              }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── 根容器 ── */
.login-root {
  position: fixed;
  inset: 0;
  overflow: hidden;
  background: transparent; /* 全局 AnimatedBackground 在 App.vue 根布局（z:0） */
  color: var(--ink);
  font-family: var(--font-ui);
}

/* ── 内容层 ── */
.content-layer {
  position: absolute;
  inset: 0;
  z-index: 5;
}

/* ── 顶部状态栏 ── */
.top-bar {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 56px;
  z-index: 30;
  display: flex;
  align-items: center;
  padding: 0 20px;
  pointer-events: none;
}

/* ── 主题切换按钮组（仅登录页显示） ── */
.theme-switch-wrap {
  position: absolute;
  top: 20px;
  right: 20px;
  z-index: 40;
  display: flex;
  align-items: center;
  gap: 10px;
}
.theme-switch-wrap__label {
  font-size: 11px;
  letter-spacing: 0.16em;
}
.theme-toggle {
  display: inline-flex;
  align-items: center;
  padding: 4px;
  gap: 2px;
  background: color-mix(in srgb, var(--bg1) 86%, transparent);
  border: 1px solid var(--line);
  border-radius: var(--radius-pill);
  backdrop-filter: blur(var(--panel-blur));
}
.theme-toggle__btn {
  width: 38px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: transparent;
  color: var(--ink-faint);
  cursor: pointer;
  border-radius: var(--radius-pill);
  transition: all 0.25s;
}
.theme-toggle__btn:hover {
  color: var(--ink-dim);
}
.theme-toggle__btn.is-active {
  color: var(--on-primary);
  background: var(--primary);
}

/* ── 两栏主布局 ── */
.login-layout {
  position: absolute;
  inset: 0;
  display: flex;
  z-index: 10;
}

/* ── 左栏 ── */
.brand-col {
  flex: 1.15;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  gap: 22px;
  padding: 40px;
  text-align: center;
}
.brand-title {
  font-size: clamp(56px, 8vw, 132px);
  letter-spacing: 0.06em;
  line-height: 1;
}
.brand-rule {
  width: 320px;
}
.brand-tagline {
  color: var(--accent);
  font-family: var(--font-ui);
  font-size: 16px;
  letter-spacing: 0.04em;
  margin: 0;
}
.brand-status {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  margin-top: 8px;
}

/* ── 右栏 ── */
.form-col {
  width: 520px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px;
}
.form-panel {
  width: 100%;
  max-width: 420px;
}

/* ── 分段控件全宽 ── */
.form-seg {
  width: 100%;
  margin-bottom: 20px;
}
.form-seg .seg__opt {
  flex: 1;
}

/* ── 表单字段列 ── */
.form-fields {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* ── 错误 / 成功通知 ── */
.form-notice {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border-radius: var(--radius);
  font-size: 13px;
  margin-bottom: 14px;
}
.form-notice--error {
  color: var(--accent);
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  border: 1px solid color-mix(in srgb, var(--accent) 40%, transparent);
}
.form-notice--success {
  color: var(--good);
  background: color-mix(in srgb, var(--good) 12%, transparent);
  border: 1px solid color-mix(in srgb, var(--good) 40%, transparent);
}
.form-notice__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  font-weight: 900;
  font-size: 11px;
  border: 1px solid currentColor;
  flex-shrink: 0;
}

/* ── 连接状态行 ── */
.form-status {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--line);
  font-size: 12px;
}

/* ── 密码不匹配字段 ── */
.field--error {
  border-color: var(--accent) !important;
  box-shadow: 0 0 8px color-mix(in srgb, var(--accent) 30%, transparent) !important;
}
.mismatch-msg {
  font-size: 12px;
  color: var(--accent);
  margin: -6px 0 0;
}

/* ── eye 图标尺寸 ── */
.eye-icon {
  width: 18px;
  height: 18px;
}

/* ── 加载 spinner ── */
.login-spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid rgba(26, 18, 6, 0.4);
  border-top-color: transparent;
  border-radius: 50%;
  animation: login-spin 0.8s linear infinite;
}
@keyframes login-spin {
  to {
    transform: rotate(360deg);
  }
}

/* ── 表单切换动画 ── */
.form-body {
  overflow: hidden;
}
.form-slide-enter-active {
  transition: all 0.32s cubic-bezier(0.2, 0.7, 0.2, 1);
}
.form-slide-leave-active {
  transition: all 0.18s ease;
}
.form-slide-enter-from {
  opacity: 0;
  transform: translateX(calc(var(--dir, 1) * 32px));
}
.form-slide-leave-to {
  opacity: 0;
  transform: translateX(calc(var(--dir, 1) * -32px));
}

/* ── 通知动画 ── */
.form-notice-enter-active,
.form-notice-leave-active {
  transition:
    opacity 0.2s,
    transform 0.2s;
}
.form-notice-enter-from,
.form-notice-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
.notice-enter-active,
.notice-leave-active {
  transition:
    opacity 0.2s,
    transform 0.2s;
}
.notice-enter-from,
.notice-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}

/* ── 移动端适配 ── */
@media (max-width: 767px) {
  .brand-col {
    display: none;
  }
  .form-col {
    width: 100%;
    padding: 1.5rem;
  }
  .top-bar {
    padding: 0 1rem;
  }
}
</style>
