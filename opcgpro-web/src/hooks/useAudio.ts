"use client";

import { useCallback } from "react";
import { audioEngine } from "@/audio/AudioEngine";
import type { PlaySoundOptions, SoundId } from "@/audio/types";

export function useAudio() {
  const play = useCallback((id: SoundId, options?: PlaySoundOptions) => {
    audioEngine.play(id, options);
  }, []);

  const unlock = useCallback(async () => audioEngine.unlock(), []);
  const stopAll = useCallback(() => audioEngine.stopAll(), []);

  return { play, unlock, stopAll };
}
