import { createStore } from "zustand/vanilla";

interface AudioStore {
  bgmVolume: number;
  sfxVolume: number;
  isMuted: boolean;
  currentBgm: string | null;

  setBgmVolume: (v: number) => void;
  setSfxVolume: (v: number) => void;
  toggleMute: () => void;
  setCurrentBgm: (name: string | null) => void;
}

export const useAudioStore = createStore<AudioStore>()((set) => ({
  bgmVolume: 0.7,
  sfxVolume: 1.0,
  isMuted: false,
  currentBgm: null,

  setBgmVolume: (v) => set({ bgmVolume: Math.max(0, Math.min(1, v)) }),
  setSfxVolume: (v) => set({ sfxVolume: Math.max(0, Math.min(1, v)) }),
  toggleMute: () => set((s) => ({ isMuted: !s.isMuted })),
  setCurrentBgm: (name) => set({ currentBgm: name }),
}));
