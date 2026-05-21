"use client";

import { useEffect } from "react";
import type { AnimationEvent } from "./useGameAnimation";
import { useAudio } from "./useAudio";

/**
 * 音效文件路径映射
 * 对应原 Unity Resources/Audio/ 目录结构
 * 实际文件放置在 public/audio/ 下
 */
const SFX: Record<string, string> = {
  drawCard: "/audio/sfx/draw_card.mp3",
  playCard: "/audio/sfx/play_card.mp3",
  attack: "/audio/sfx/attack.mp3",
  block: "/audio/sfx/block.mp3",
  counter: "/audio/sfx/counter.mp3",
  damage: "/audio/sfx/damage.mp3",
  koUnit: "/audio/sfx/ko_unit.mp3",
  turnStart: "/audio/sfx/turn_start.mp3",
  turnEnd: "/audio/sfx/turn_end.mp3",
  gameWin: "/audio/sfx/game_win.mp3",
  gameLose: "/audio/sfx/game_lose.mp3",
};

const BGM: Record<string, string> = {
  home: "/audio/bgm/home.mp3",
  game: "/audio/bgm/game.mp3",
};

/**
 * useGameAudio — 根据动画事件播放对应音效
 *
 * 用法（在 GamePage 中）：
 *   const anim = useGameAnimation();
 *   useGameAudio(anim);
 *
 * 音效文件不存在时静默处理（catch 忽略），游戏不受影响。
 */
export function useGameAudio(animEvent: AnimationEvent) {
  const { playSfx, playBgm, stopBgm } = useAudio();

  // 游戏开始时播放 BGM
  useEffect(() => {
    playBgm(BGM.game);
    return () => stopBgm();
  }, [playBgm, stopBgm]);

  // 根据动画事件播放音效
  useEffect(() => {
    switch (animEvent.type) {
      case "none":
        break;
      case "drawCard":
        playSfx(SFX.drawCard);
        break;
      case "playCard":
        playSfx(SFX.playCard);
        break;
      case "attack":
        playSfx(SFX.attack);
        break;
      case "block":
        playSfx(SFX.block);
        break;
      case "counter":
        playSfx(SFX.counter);
        break;
      case "damage":
        playSfx(SFX.damage);
        break;
      case "koUnit":
        playSfx(SFX.koUnit);
        break;
      case "turnStart":
        playSfx(SFX.turnStart);
        break;
      case "turnEnd":
        playSfx(SFX.turnEnd);
        break;
      case "gameOver":
        playSfx(animEvent.isWin ? SFX.gameWin : SFX.gameLose);
        break;
    }
  }, [animEvent, playSfx]);
}
