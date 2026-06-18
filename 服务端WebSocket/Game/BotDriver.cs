using System.Text.Json;

namespace GrandUMI.Game;

/// <summary>
/// 单人测试模式的机器人对手（作为 P1 的"假会话"）。
/// 不主动出牌/攻击，只做让对局推进所需的最小应答：
///   重抽(不换) → 防守时放弃阻挡/反击 → 轮到它且主要阶段则结束回合 → 对它的 prompt 给默认应答。
/// 由"发给机器人的每条状态广播"反应式驱动（见 GameRoomManager.CreateRoom 的 OnSendToPlayer）。
/// </summary>
public static class BotDriver
{
    private const int BOT = 1;          // 机器人固定为 P1
    private const int ThinkDelayMs = 350;

    /// <summary>机器人收到一条状态广播：调度一次思考（带去抖，避免重复排队）</summary>
    public static void OnBotMessage(GameRoomManager.RoomEntry room)
    {
        if (room.BotScheduled) return;
        room.BotScheduled = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(ThinkDelayMs);
            room.BotScheduled = false;
            try { Act(room); }
            catch (Exception ex) { Console.Error.WriteLine($"[Bot] 异常: {ex.Message}"); }
        });
    }

    private static void Act(GameRoomManager.RoomEntry room)
    {
        var engine = room.Engine;
        var s = engine.State;
        if (s.IsGameOver) return;

        lock (engine) // 与人类动作串行化（引擎线程不安全）
        {
            if (s.IsGameOver) return;

            // 1. 有发给机器人的 prompt → 默认应答
            if (s.PendingPrompt is { } p && p.PlayerIndex == BOT)
            {
                string[] chosen;
                if (p.Kind == "LifeTrigger")
                    chosen = new[] { "hand" };                       // 生命触发：直接加入手牌，不发动
                else if (p.MinChoose > 0)
                    chosen = p.ValidChoices.Take(p.MinChoose).ToArray(); // 必选：取前 N 个合法项
                else
                    chosen = Array.Empty<string>();                  // 可选：跳过
                engine.HandleAction(BOT, "PromptResponse", El(new { promptId = p.PromptId, chosen }));
                return;
            }

            // 2. 重抽决策（不换牌）
            if (!s.Players[BOT].MulliganDone)
            {
                engine.HandleAction(BOT, "Mulligan", El(new { redraw = false }));
                return;
            }

            // 3. 防守：人类攻击机器人时，放弃阻挡 / 放弃反击
            if (s.CurrentBattle is { } b && b.DefenderPlayerIndex == BOT)
            {
                if (s.Phase == Phase.BattleBlock) { engine.HandleAction(BOT, "PassBlock", El(new { })); return; }
                if (s.Phase == Phase.BattleCounter) { engine.HandleAction(BOT, "PassCounter", El(new { })); return; }
            }

            // 4. 轮到机器人 + 主要阶段 + 无战斗 → 直接结束回合，交还给玩家
            if (s.CurrentTurnPlayer == BOT && s.Phase == Phase.Main && s.CurrentBattle is null)
            {
                engine.HandleAction(BOT, "EndTurn", El(new { }));
                return;
            }
        }
    }

    private static JsonElement El(object o) => JsonSerializer.SerializeToElement(o);
}
