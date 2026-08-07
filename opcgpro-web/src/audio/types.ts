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
}
