using GrandUMI.Game.AI;

namespace GrandUMI.Game;

/// <summary>
/// AI 对战模式的 synthetic 基线对手（作为 P1 的"假会话"）。
/// 策略会出牌、分配咚、攻击并处理防守／Prompt；所有选择都必须从当前 LegalActionSet 产生。
/// 由"发给机器人的每条状态广播"反应式驱动（见 GameRoomManager.CreateRoom 的 OnSendToPlayer）。
/// </summary>
public static class BotDriver
{
    private const int BOT = 1;          // 机器人固定为 P1
    private const int ThinkDelayMs = 350;
    private static readonly SyntheticBaselinePolicy PrimaryPolicy = SyntheticBaselinePolicy.LoadConfiguredOrBuiltIn();
    private static readonly IAiPolicy FallbackPolicy = new DeterministicSafePolicy();

    /// <summary>机器人收到一条状态广播：调度一次思考（带去抖，避免重复排队）</summary>
    public static void OnBotMessage(GameRoomManager.RoomEntry room)
    {
        if (Interlocked.CompareExchange(ref room.BotScheduleState, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(ThinkDelayMs);
            if (!GameRoomManager.EnqueueBotDecision(room))
                Volatile.Write(ref room.BotScheduleState, 0);
        });
    }

    /// <summary>只在房间单读者动作队列内调用，避免枚举期间与真人动作并发读取可变状态。</summary>
    internal static async Task DecideAndQueueAsync(GameRoomManager.RoomEntry room)
    {
        var retryAfterQueuePressure = false;
        try
        {
            if (room.Engine.State.IsGameOver) return;
            var decision = await AiDecisionCoordinator.DecideAsync(
                room.Engine.State,
                BOT,
                PrimaryPolicy,
                FallbackPolicy,
                TimeSpan.FromMilliseconds(200));
            if (decision is null) return;
            room.Engine.RecordMatchLog("ai_decision", BOT, new
            {
                policyId = decision.PolicyId,
                modelHash = decision.ModelHash,
                modelSource = PrimaryPolicy.ModelSource,
                actionId = decision.ActionId,
                usedFallback = decision.UsedFallback,
                fallbackReason = decision.FallbackReason,
            });
            // 决策与实际动作是两个队列项；HandleAction 会在执行时再次校验当前状态，过期候选只会被拒绝。
            retryAfterQueuePressure = !GameRoomManager.EnqueueBotAction(
                room,
                BOT,
                decision.Action,
                decision.Data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Bot] 决策异常，未发送猜测动作: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref room.BotScheduleState, 0);
        }
        if (retryAfterQueuePressure && !room.Engine.State.IsGameOver)
            OnBotMessage(room);
    }
}
