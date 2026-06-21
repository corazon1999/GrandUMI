/**
 * 回放 mock 数据 — 用于开发测试
 * 服务器录制上线后，此文件可删除
 */
import type { PlaybackRecord } from "@/types/playback";

/**
 * 生成一份简单的 mock 回放记录
 * 模拟：双方各出一张角色卡 → 攻击 → 领航受伤 → 游戏继续
 */
export function createMockPlayback(): PlaybackRecord {
  return {
    version: 1,
    matchId: "mock-001",
    myName: "测试玩家",
    opponentName: "电脑对手",
    turnCount: 4,
    turns: [
      {
        turnNumber: 1,
        steps: [
          { timeOffset: 0, action: "TurnStart", payload: {}, side: "my" },
          { timeOffset: 800, action: "DrawCard", payload: { cardNumber: "ST01-001" }, side: "my" },
          { timeOffset: 1500, action: "PlayCard", payload: { cardNumber: "ST01-001", fieldIndex: 0 }, side: "my" },
          { timeOffset: 2500, action: "TurnEnd", payload: {}, side: "my" },
        ],
      },
      {
        turnNumber: 2,
        steps: [
          { timeOffset: 0, action: "TurnStart", payload: {}, side: "opponent" },
          { timeOffset: 600, action: "DrawCard", payload: { cardNumber: "ST01-002" }, side: "opponent" },
          { timeOffset: 1200, action: "PlayCard", payload: { cardNumber: "ST01-002", fieldIndex: 0 }, side: "opponent" },
          { timeOffset: 2000, action: "TurnEnd", payload: {}, side: "opponent" },
        ],
      },
      {
        turnNumber: 3,
        steps: [
          { timeOffset: 0, action: "TurnStart", payload: {}, side: "my" },
          { timeOffset: 600, action: "DrawCard", payload: { cardNumber: "ST01-003" }, side: "my" },
          { timeOffset: 1000, action: "Attack", payload: { attackerIndex: 0, targetIndex: "leader" }, side: "my" },
          { timeOffset: 2000, action: "Damage", payload: { target: "leader", success: true }, side: "opponent" },
          { timeOffset: 2800, action: "TurnEnd", payload: {}, side: "my" },
        ],
      },
      {
        turnNumber: 4,
        steps: [
          { timeOffset: 0, action: "TurnStart", payload: {}, side: "opponent" },
          { timeOffset: 500, action: "DrawCard", payload: { cardNumber: "ST01-004" }, side: "opponent" },
          { timeOffset: 1000, action: "Attack", payload: { attackerIndex: 0, targetIndex: "leader" }, side: "opponent" },
          { timeOffset: 1800, action: "Damage", payload: { target: "leader", success: true }, side: "my" },
          { timeOffset: 2500, action: "TurnEnd", payload: {}, side: "opponent" },
        ],
      },
    ],
  };
}
