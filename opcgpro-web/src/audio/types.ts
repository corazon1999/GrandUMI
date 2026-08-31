export type SoundId =
  | "matchStart"
  | "turnSelf"
  | "turnOpponent"
  | "cardPlayCharacter"
  | "cardPlayEvent"
  | "cardPlayStage"
  | "attachDon"
  | "attack"
  | "block"
  | "counter"
  | "effect"
  | "reveal"
  | "damage"
  | "ko"
  | "win"
  | "lose"
  | "prompt"
  | "error"
  | "disconnect"
  | "reconnect"
  | "message";

export interface SoundDefinition {
  src: string;
  volume: number;
  cooldownMs: number;
  maxVoices: number;
  priority: number;
  pitchVariance?: number;
}

export interface PlaySoundOptions {
  volume?: number;
  /** 已由用户手势解锁音频后，是否允许在后台标签页尝试播放。 */
  allowWhenHidden?: boolean;
}
