import { create } from "zustand";
import { NetManager } from "@/net/NetManager";

/**
 * 全局玩家设置（持久化到 localStorage）
 *
 * 当前包含：
 *   - alwaysPromptOnLifeReveal: 防触发信息泄露
 *     开启后每张生命牌加入手牌都弹"是否发动触发"窗口（即使没有触发），
 *     对手只能看到"对方正在选择"，无法通过弹窗时机推断生命牌内容
 *   - cardSize: 卡牌显示大小
 *   - animationSpeed: 对局动画速度
 *   - confirmAttachDon: 贴咚前是否需要二次确认
 *   - hideOpponentCardBack: 是否将当前主视角看到的敌方卡背统一显示为经典款
 */

const KEY = "grandumi_settings";

export type CardSizePreference = "auto" | "sm" | "md" | "lg";
export type AnimationSpeed = "off" | "fast" | "standard";

interface Settings {
  alwaysPromptOnLifeReveal: boolean;
  cardSize: CardSizePreference;
  animationSpeed: AnimationSpeed;
  confirmAttachDon: boolean;
  hideOpponentCardBack: boolean;
}

const defaults: Settings = {
  alwaysPromptOnLifeReveal: false,
  cardSize: "auto",
  animationSpeed: "standard",
  confirmAttachDon: false,
  hideOpponentCardBack: false,
};

function loadFromStorage(): Settings {
  if (typeof window === "undefined") return defaults;
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return defaults;
    const parsed = JSON.parse(raw) as Partial<Settings>;
    return {
      alwaysPromptOnLifeReveal: typeof parsed.alwaysPromptOnLifeReveal === "boolean"
        ? parsed.alwaysPromptOnLifeReveal
        : defaults.alwaysPromptOnLifeReveal,
      cardSize: ["auto", "sm", "md", "lg"].includes(parsed.cardSize ?? "")
        ? parsed.cardSize as CardSizePreference
        : defaults.cardSize,
      animationSpeed: ["off", "fast", "standard"].includes(parsed.animationSpeed ?? "")
        ? parsed.animationSpeed as AnimationSpeed
        : defaults.animationSpeed,
      confirmAttachDon: typeof parsed.confirmAttachDon === "boolean"
        ? parsed.confirmAttachDon
        : defaults.confirmAttachDon,
      hideOpponentCardBack: typeof parsed.hideOpponentCardBack === "boolean"
        ? parsed.hideOpponentCardBack
        : defaults.hideOpponentCardBack,
    };
  } catch { return defaults; }
}

function saveToStorage(s: Settings) {
  if (typeof window === "undefined") return;
  localStorage.setItem(KEY, JSON.stringify(s));
}

interface SettingsStore extends Settings {
  toggleAlwaysPromptOnLifeReveal: () => void;
  setAlwaysPromptOnLifeReveal: (v: boolean) => void;
  setCardSize: (v: CardSizePreference) => void;
  setAnimationSpeed: (v: AnimationSpeed) => void;
  setConfirmAttachDon: (v: boolean) => void;
  setHideOpponentCardBack: (v: boolean) => void;
}

function persistCurrent() {
  const {
    alwaysPromptOnLifeReveal,
    cardSize,
    animationSpeed,
    confirmAttachDon,
    hideOpponentCardBack,
  } = useSettingsStore.getState();
  saveToStorage({
    alwaysPromptOnLifeReveal,
    cardSize,
    animationSpeed,
    confirmAttachDon,
    hideOpponentCardBack,
  });
}

export const useSettingsStore = create<SettingsStore>((set, get) => ({
  ...loadFromStorage(),

  toggleAlwaysPromptOnLifeReveal: () => {
    const next = !get().alwaysPromptOnLifeReveal;
    set({ alwaysPromptOnLifeReveal: next });
    persistCurrent();
    syncToServer();
  },

  setAlwaysPromptOnLifeReveal: (v) => {
    set({ alwaysPromptOnLifeReveal: v });
    persistCurrent();
    syncToServer();
  },

  setCardSize: (v) => {
    set({ cardSize: v });
    persistCurrent();
  },

  setAnimationSpeed: (v) => {
    set({ animationSpeed: v });
    persistCurrent();
  },

  setConfirmAttachDon: (v) => {
    set({ confirmAttachDon: v });
    persistCurrent();
  },

  setHideOpponentCardBack: (v) => {
    set({ hideOpponentCardBack: v });
    persistCurrent();
  },
}));

export function animationDuration(standardMs: number, speed: AnimationSpeed) {
  if (speed === "off") return 0;
  return speed === "fast" ? Math.max(1, Math.round(standardMs * 0.45)) : standardMs;
}

/// 把当前设置上报给服务端，影响生命牌触发流程
export function syncToServer() {
  const s = useSettingsStore.getState();
  NetManager.send({
    proto: "MsgUpdateSettings",
    alwaysPromptOnLifeReveal: s.alwaysPromptOnLifeReveal,
  } as never);
}
