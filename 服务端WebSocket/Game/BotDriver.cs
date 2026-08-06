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

        // 机器人也走房间动作队列，与真人操作保持同一有序入口。
        if (s.PendingPrompt is { } p && p.PlayerIndex == BOT)
        {
            string[] chosen;
            if (p.Kind == "LifeTrigger")
                chosen = new[] { "hand" };
            else if (p.MinChoose > 0)
                chosen = p.ValidChoices.Take(p.MinChoose).ToArray();
            else
                chosen = Array.Empty<string>();
            GameRoomManager.EnqueueBotAction(room, BOT, "PromptResponse", El(new { promptId = p.PromptId, chosen }));
            return;
        }

        // 真人对局目前不会启用机器人；保留此分支以保证未来骰点机器人房间可自行推进。
        if (!s.StartingPlayerChosen)
        {
            if (s.StartingPlayerChooser == BOT)
                GameRoomManager.EnqueueBotAction(room, BOT, "ChooseFirstPlayer", El(new { goFirst = true }));
            return;
        }

        if (!s.Players[BOT].MulliganDone)
        {
            GameRoomManager.EnqueueBotAction(room, BOT, "Mulligan", El(new { redraw = false }));
            return;
        }

        if (s.CurrentBattle is { } b && b.DefenderPlayerIndex == BOT)
        {
            if (s.Phase == Phase.BattleBlock) { GameRoomManager.EnqueueBotAction(room, BOT, "PassBlock", El(new { })); return; }
            if (s.Phase == Phase.BattleCounter) { GameRoomManager.EnqueueBotAction(room, BOT, "PassCounter", El(new { })); return; }
        }

        if (s.CurrentTurnPlayer == BOT && s.Phase == Phase.Main && s.CurrentBattle is null)
            GameRoomManager.EnqueueBotAction(room, BOT, "EndTurn", El(new { }));
    }

    private static JsonElement El(object o) => JsonSerializer.SerializeToElement(o);
}
