"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { audioEngine } from "@/audio/AudioEngine";
import { useAudio } from "@/hooks/useAudio";
import { useGameAudio } from "@/hooks/useGameAudio";
import { eventBus } from "@/net/eventBus";
import { useAudioStore } from "@/store/audioStore";
import { useNetStore } from "@/store/netStore";

export default function AudioProvider({ children }: { children: ReactNode }) {
  const { play } = useAudio();
  const sfxVolume = useAudioStore((state) => state.sfxVolume);
  const isMuted = useAudioStore((state) => state.isMuted);
  const hydrate = useAudioStore((state) => state.hydrate);
  const setUnlocked = useAudioStore((state) => state.setUnlocked);
  const incomingInvite = useNetStore((state) => state.incomingInvite);
  const previousInviteRef = useRef<string | null>(null);

  useGameAudio();

  useEffect(() => hydrate(), [hydrate]);

  useEffect(() => audioEngine.setVolume(sfxVolume), [sfxVolume]);
  useEffect(() => audioEngine.setMuted(isMuted), [isMuted]);

  useEffect(() => {
    let disposed = false;
    const tryUnlock = () => {
      void audioEngine.unlock().then((unlocked) => {
        if (disposed) return;
        setUnlocked(unlocked);
        if (unlocked) {
          window.removeEventListener("pointerdown", tryUnlock, true);
          window.removeEventListener("keydown", tryUnlock, true);
        }
      });
    };

    window.addEventListener("pointerdown", tryUnlock, true);
    window.addEventListener("keydown", tryUnlock, true);
    return () => {
      disposed = true;
      window.removeEventListener("pointerdown", tryUnlock, true);
      window.removeEventListener("keydown", tryUnlock, true);
    };
  }, [setUnlocked]);

  useEffect(() => {
    const onRejected = () => play("error");
    const onDisconnected = () => play("disconnect");
    const onReconnected = () => play("reconnect");
    const onGameChat = (message: { fromAccount?: string }) => {
      const account = useNetStore.getState().account;
      if (message.fromAccount && message.fromAccount !== account) play("message");
    };

    eventBus.on("actionRejected", onRejected);
    eventBus.on("opponentDisconnected", onDisconnected);
    eventBus.on("opponentReconnected", onReconnected);
    eventBus.on("gameChat", onGameChat);
    return () => {
      eventBus.off("actionRejected", onRejected);
      eventBus.off("opponentDisconnected", onDisconnected);
      eventBus.off("opponentReconnected", onReconnected);
      eventBus.off("gameChat", onGameChat);
    };
  }, [play]);

  useEffect(() => {
    const inviteId = incomingInvite?.inviteId ?? null;
    if (inviteId && inviteId !== previousInviteRef.current) play("message");
    previousInviteRef.current = inviteId;
  }, [incomingInvite, play]);

  useEffect(() => {
    const onVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        audioEngine.stopAll();
      } else if (audioEngine.isUnlocked()) {
        void audioEngine.unlock();
      }
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    return () => document.removeEventListener("visibilitychange", onVisibilityChange);
  }, []);

  return children;
}
