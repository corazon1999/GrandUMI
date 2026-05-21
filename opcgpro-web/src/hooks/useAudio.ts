"use client";

import { useCallback, useRef } from "react";
import { useAudioStore } from "@/store/audioStore";

export function useAudio() {
  const { bgmVolume, sfxVolume, isMuted } = useAudioStore();
  const bgmRef = useRef<HTMLAudioElement | null>(null);

  const playBgm = useCallback(
    (src: string) => {
      if (bgmRef.current) {
        bgmRef.current.pause();
      }
      const audio = new Audio(src);
      audio.loop = true;
      audio.volume = isMuted ? 0 : bgmVolume;
      audio.play().catch(() => {});
      bgmRef.current = audio;
      useAudioStore.getState().setCurrentBgm(src);
    },
    [bgmVolume, isMuted]
  );

  const playSfx = useCallback(
    (src: string) => {
      if (isMuted) return;
      const audio = new Audio(src);
      audio.volume = sfxVolume;
      audio.play().catch(() => {});
    },
    [sfxVolume, isMuted]
  );

  const stopBgm = useCallback(() => {
    bgmRef.current?.pause();
    bgmRef.current = null;
    useAudioStore.getState().setCurrentBgm(null);
  }, []);

  return { playBgm, playSfx, stopBgm };
}
