import { useAudioStore } from "@/store/audioStore";

export function useAudio() {
  let bgmEl: HTMLAudioElement | null = null;

  function playBgm(src: string) {
    const { bgmVolume, isMuted } = useAudioStore.getState();
    if (bgmEl) bgmEl.pause();
    const audio = new Audio(src);
    audio.loop = true;
    audio.volume = isMuted ? 0 : bgmVolume;
    audio.play().catch(() => {});
    bgmEl = audio;
    useAudioStore.getState().setCurrentBgm(src);
  }

  function playSfx(src: string) {
    const { sfxVolume, isMuted } = useAudioStore.getState();
    if (isMuted) return;
    const audio = new Audio(src);
    audio.volume = sfxVolume;
    audio.play().catch(() => {});
  }

  function stopBgm() {
    bgmEl?.pause();
    bgmEl = null;
    useAudioStore.getState().setCurrentBgm(null);
  }

  return { playBgm, playSfx, stopBgm };
}
