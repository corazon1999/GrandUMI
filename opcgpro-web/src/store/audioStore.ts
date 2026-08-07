import { create } from "zustand";

const STORAGE_KEY = "grandumi_audio_v1";
const DEFAULT_VOLUME = 0.7;

interface PersistedAudioSettings {
  sfxVolume: number;
  isMuted: boolean;
}

interface AudioStore extends PersistedAudioSettings {
  isHydrated: boolean;
  isUnlocked: boolean;
  hydrate: () => void;
  setSfxVolume: (volume: number) => void;
  setMuted: (muted: boolean) => void;
  toggleMute: () => void;
  setUnlocked: (unlocked: boolean) => void;
}

function clampVolume(volume: number): number {
  return Math.max(0, Math.min(1, Number.isFinite(volume) ? volume : DEFAULT_VOLUME));
}

function loadSettings(): PersistedAudioSettings {
  if (typeof window === "undefined") {
    return { sfxVolume: DEFAULT_VOLUME, isMuted: false };
  }

  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { sfxVolume: DEFAULT_VOLUME, isMuted: false };
    const saved = JSON.parse(raw) as Partial<PersistedAudioSettings>;
    return {
      sfxVolume: clampVolume(saved.sfxVolume ?? DEFAULT_VOLUME),
      isMuted: typeof saved.isMuted === "boolean" ? saved.isMuted : false,
    };
  } catch {
    return { sfxVolume: DEFAULT_VOLUME, isMuted: false };
  }
}

function saveSettings(settings: PersistedAudioSettings): void {
  if (typeof window === "undefined") return;
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  } catch {
    // 禁用本地存储时只影响跨刷新记忆，不影响本次使用。
  }
}

export const useAudioStore = create<AudioStore>((set, get) => ({
  sfxVolume: DEFAULT_VOLUME,
  isMuted: false,
  isHydrated: false,
  isUnlocked: false,

  hydrate: () => {
    if (get().isHydrated) return;
    set({ ...loadSettings(), isHydrated: true });
  },

  setSfxVolume: (volume) => {
    const sfxVolume = clampVolume(volume);
    set({ sfxVolume });
    saveSettings({ sfxVolume, isMuted: get().isMuted });
  },

  setMuted: (isMuted) => {
    set({ isMuted });
    saveSettings({ sfxVolume: get().sfxVolume, isMuted });
  },

  toggleMute: () => {
    const isMuted = !get().isMuted;
    set({ isMuted });
    saveSettings({ sfxVolume: get().sfxVolume, isMuted });
  },

  setUnlocked: (isUnlocked) => set({ isUnlocked }),
}));
