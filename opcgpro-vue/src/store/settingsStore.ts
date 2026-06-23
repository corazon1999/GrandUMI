import { createStore } from "zustand/vanilla";
import { NetManager } from "@/net/NetManager";

/**
 * 全局玩家设置（持久化到 localStorage）
 *
 * 当前包含：
 *   - alwaysPromptOnLifeReveal: 防触发信息泄露
 *     开启后每张生命牌加入手牌都弹"是否发动触发"窗口（即使没有触发），
 *     对手只能看到"对方正在选择"，无法通过弹窗时机推断生命牌内容
 */

const KEY = "grandumi_settings";

interface Settings {
  alwaysPromptOnLifeReveal: boolean;
}

const defaults: Settings = {
  alwaysPromptOnLifeReveal: false,
};

function loadFromStorage(): Settings {
  if (typeof window === "undefined") return defaults;
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? { ...defaults, ...JSON.parse(raw) } : defaults;
  } catch { return defaults; }
}

function saveToStorage(s: Settings) {
  if (typeof window === "undefined") return;
  localStorage.setItem(KEY, JSON.stringify(s));
}

interface SettingsStore extends Settings {
  toggleAlwaysPromptOnLifeReveal: () => void;
  setAlwaysPromptOnLifeReveal: (v: boolean) => void;
}

export const useSettingsStore = createStore<SettingsStore>()((set, get) => ({
  ...loadFromStorage(),

  toggleAlwaysPromptOnLifeReveal: () => {
    const next = !get().alwaysPromptOnLifeReveal;
    set({ alwaysPromptOnLifeReveal: next });
    saveToStorage({ alwaysPromptOnLifeReveal: next });
    syncToServer();
  },

  setAlwaysPromptOnLifeReveal: (v) => {
    set({ alwaysPromptOnLifeReveal: v });
    saveToStorage({ alwaysPromptOnLifeReveal: v });
    syncToServer();
  },
}));

/// 把当前设置上报给服务端，影响生命牌触发流程
export function syncToServer() {
  const s = useSettingsStore.getState();
  NetManager.send({
    proto: "MsgUpdateSettings",
    alwaysPromptOnLifeReveal: s.alwaysPromptOnLifeReveal,
  } as never);
}
