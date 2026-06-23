/**
 * 回放相关类型定义
 * 服务器录制 action 序列 → 客户端按时间轴回放
 */

/** 单条回放记录 */
export interface PlaybackStep {
  /** 自回合开始的时间偏移（ms） */
  timeOffset: number;
  /** 行动类型 */
  action: string;
  /** 行动负载 */
  payload: Record<string, unknown>;
  /** 执行该行动的玩家侧 */
  side: "my" | "opponent";
}

/** 回放文件的顶层结构 */
export interface PlaybackRecord {
  /** 格式版本 */
  version: number;
  /** 对局 ID */
  matchId: string;
  /** 双方名称 */
  myName: string;
  opponentName: string;
  /** 回合数 */
  turnCount: number;
  /** 按回合分组的行动序列 */
  turns: PlaybackTurn[];
}

export interface PlaybackTurn {
  turnNumber: number;
  steps: PlaybackStep[];
}

/** 回放播放状态 */
export type PlaybackState = "idle" | "playing" | "paused" | "ended";

/** 回放速度选项 */
export type PlaybackSpeed = 0.5 | 1 | 2 | 4;
