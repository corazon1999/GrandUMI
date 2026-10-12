using GrandUMI.Diagnostics;

namespace GrandUMI.Game;

public static partial class GameRoomManager
{
    internal const int OpeningPreparationTimeoutSeconds = 30;

    /// <summary>先给新客户端发送未发牌的准备快照；旧客户端沿用原开局路径。</summary>
    public static void BroadcastInitialState(RoomEntry room)
    {
        var preparation = room.OpeningPreparation;
        if (room.Engine.State.OpeningPreparationDeadlineUtc is null || preparation is null)
        {
            room.Engine.BroadcastInitialState();
            return;
        }
        room.Engine.BroadcastInitialState(beginOpeningSequence: false);
        if (preparation.TryStartTimer()) _ = WaitForOpeningPreparationAsync(room, preparation);
    }

    public static void HandleClientPrepared(string sessionId, string? roomId)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null || room.RoomId != roomId || room.IsRecoveryPaused) return;
        var preparation = room.OpeningPreparation;
        if (preparation is null || room.Engine.State.OpeningPreparationDeadlineUtc is null) return;
        _ = ConfirmOpeningPreparationAsync(room, sessionId, preparation);
    }

    private static async Task ConfirmOpeningPreparationAsync(
        RoomEntry room, string sessionId, OpeningPreparationGate preparation)
    {
        try
        {
            await EnqueueCriticalWorkAsync(room, new RoomWork("ClientPrepared", LatencyDiagnostics.Start(), () =>
            {
                var index = Array.IndexOf(room.PlayerSessionIds, sessionId);
                if (index >= 0 && !room.IsRecoveryPaused && !room.Engine.State.IsGameOver
                    && room.Engine.State.OpeningPreparationDeadlineUtc is not null
                    && preparation.TryReady(index)) CompleteOpeningPreparation(room, preparation);
                return Task.CompletedTask;
            }), preparation.CancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        catch (Exception ex) { Console.Error.WriteLine($"[开局准备] 确认失败：{ex.Message}"); }
    }

    private static async Task WaitForOpeningPreparationAsync(RoomEntry room, OpeningPreparationGate preparation)
    {
        var token = preparation.CancellationToken;
        try
        {
            while (true)
            {
                var delay = preparation.DeadlineUtc - DateTime.UtcNow;
                if (delay <= TimeSpan.Zero) break;
                await Task.Delay(delay, token);
            }
            await EnqueueCriticalWorkAsync(room, new RoomWork("OpeningPreparationTimeout", LatencyDiagnostics.Start(), () =>
            {
                if (ReferenceEquals(GetRoom(room.RoomId), room) && !room.IsRecoveryPaused
                    && !room.Engine.State.IsGameOver && preparation.TryExpire(DateTime.UtcNow))
                    CompleteOpeningPreparation(room, preparation);
                return Task.CompletedTask;
            }), token);
        }
        catch (OperationCanceledException) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        catch (Exception ex) { Console.Error.WriteLine($"[开局准备] 等待失败：{ex.Message}"); }
    }

    private static void CompleteOpeningPreparation(RoomEntry room, OpeningPreparationGate preparation)
    {
        room.Engine.State.OpeningPreparationDeadlineUtc = null;
        preparation.Cancel();
        // 开局效果、骰点、发牌和调度计时从这里开始，准备阶段没有可窥看的起手牌。
        // 当前仍在房间队列内，先安排回放检查点，再允许客户端根据发牌快照提交动作。
        ScheduleOpeningReplayCheckpoint(room);
        room.Engine.BroadcastInitialState();
        CaptureRecoverySnapshot(room);
    }

    private static void ScheduleOpeningReplayCheckpoint(RoomEntry room)
    {
        var checkpoints = room.ReplayCheckpoints;
        if (checkpoints is null) return;
        if (!EnqueueWork(room, new RoomWork("ReplayOpeningCheckpoint", LatencyDiagnostics.Start(), async () =>
        {
            try
            {
                await room.Engine.WaitSettledAsync();
                checkpoints.WriteOpening(room.Engine);
            }
            catch (Exception ex)
            {
                checkpoints.Disable(room.Engine.State, "opening_stable_wait_failed");
                Console.Error.WriteLine($"[回放检查点] {room.RoomId} 开局稳定等待失败：{ex.Message}");
            }
        }))) checkpoints.Disable(room.Engine.State, "opening_coordinator_enqueue_failed");
    }
}
