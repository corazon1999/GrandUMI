import type { CardData } from "./card";

export type GameMode = "Player" | "Observer" | "Playback";
export type BattlePhase =
  | "Main"
  | "Attack"
  | "Block"
  | "Counter"
  | "Damage"
  | "End";

// ── 咚!!卡牌 ──────────────────────────────────────────────────────────

export type DonState = "deck" | "active" | "rest" | "attached";

export interface DonCard {
  id: string;             // "don-0" ~ "don-9"
  state: DonState;
  attachedTo?: number;    // fieldIndex，仅 state="attached" 时有效
}

// ── 游戏状态 ──────────────────────────────────────────────────────────

export interface CostState {
  active: number;
  rest: number;
  max: number;
  donCards: DonCard[];    // 全部 10 张咚卡的完整生命周期追踪
}

export interface HandState {
  cards: CardData[];
  canChoose: boolean;
}

export interface FieldCardState {
  card: CardData;
  isTapped: boolean;
  powerBuff: number;
  isSelected: boolean;
}

export interface FieldState {
  characterCards: FieldCardState[];
  stageCard: CardData | null;
  isMyTurn: boolean;
}

export interface PlayerState {
  cost: CostState;
  hand: HandState;
  field: FieldState;
  deckCount: number;
  life: CardData[];
  leader: CardData | null;
}

export interface GameState {
  mode: GameMode;
  isStart: boolean;
  currentTurn: boolean;
  turnCount: number;
  haveExtraTurn: boolean;
  phase: BattlePhase;
  my: PlayerState;
  opponent: PlayerState;
  myName: string;
  opponentName: string;
}
