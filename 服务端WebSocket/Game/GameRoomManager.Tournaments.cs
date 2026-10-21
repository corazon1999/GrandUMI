using System.Diagnostics;
using System.Security.Cryptography;
using GrandUMI.Game.Tournaments;

namespace GrandUMI.Game;

public static partial class GameRoomManager
{
    private static void PersistTournamentDisconnectGrace(RoomEntry room)
    {
        if (room.MatchKind != MatchKind.Tournament || TournamentRuntime.Store is not { } store) return;
        lock (room.ClockGate)
        {
            long Remaining(int player) => Math.Max(0, room.DisconnectGraceRemainingMs[player] -
                (room.DisconnectedPlayers[player] && room.DisconnectStartedAt[player] > 0 ? (long)Math.Ceiling(Stopwatch.GetElapsedTime(room.DisconnectStartedAt[player]).TotalMilliseconds) : 0));
            store.SaveDisconnectGrace(room.RoomId, Remaining(0), Remaining(1));
        }
    }

    /// <summary>所有赛事计时和结束操作进入房间单读者队列，与玩家动作保持确定顺序。</summary>
    internal static async Task ApplyTournamentClockAsync(string roomId, string stage, long startedAt,
        int? extraTurnLimit, bool stop, string reason, int? forcedWinner = null)
    {
        var room = GetRoom(roomId);
        if (room is null || room.MatchKind != MatchKind.Tournament) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = await EnqueueCriticalWorkAsync(room, new RoomWork("TournamentClock", Stopwatch.GetTimestamp(), () =>
        {
            try
            {
                if (room.Engine.State.IsGameOver) { CleanupRoomFromCoordinator(roomId); return Task.CompletedTask; }
                if (room.IsRecoveryPaused && !stop) return Task.CompletedTask;
                PersistTournamentDisconnectGrace(room);
                var state = room.Engine.State;
                int? winner = stop ? forcedWinner : null;
                var terminalKind = stop ? "tournament_stop" : "tournament_timeout";
                if (!stop)
                {
                    if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < startedAt + 1_800_000) return Task.CompletedTask;
                    if (stage == "cut")
                    {
                        if (extraTurnLimit is null)
                        {
                            // 当前回合为第0回合；追加回合全部完成后才比较胜负。
                            var limit = state.TurnCount + (state.CurrentTurnPlayer == state.FirstPlayer ? 3 : 2);
                            TournamentRuntime.SetExtraTurns(roomId, limit);
                            return Task.CompletedTask;
                        }
                        if (state.TurnCount <= extraTurnLimit.Value) return Task.CompletedTask;
                        var life = state.Players[0].LifeCount.CompareTo(state.Players[1].LifeCount);
                        var deck = state.Players[0].DeckCount.CompareTo(state.Players[1].DeckCount);
                        winner = life != 0 ? (life > 0 ? 0 : 1) : deck != 0 ? (deck > 0 ? 0 : 1) : RandomNumberGenerator.GetInt32(2);
                        reason = "赛事淘汰赛追加回合结束，按生命、主卡组张数及系统随机决胜规则结算";
                    }
                    else reason = "赛事瑞士轮本桌30分钟超时，双方判负";
                }
                lock (room.CleanupGate)
                {
                    if (state.IsGameOver) return Task.CompletedTask;
                    var sequence = Volatile.Read(ref room.JournalSequence) + 1;
                    RoomJournal.AppendTerminal(roomId, sequence, winner ?? -1, winner.HasValue ? 1 - winner.Value : -1,
                        terminalKind, reason, DateTime.UtcNow);
                    Volatile.Write(ref room.JournalSequence, sequence);
                    lock (room.ClockGate) { StopOperationClockLocked(room); StopInactivityTrackingLocked(room); }
                    state.WinnerIndex = winner; state.IsDraw = !winner.HasValue; state.GameOverReason = reason;
                    room.TerminalCompletedAtUtc = DateTime.UtcNow;
                    room.Engine.Broadcast("DuelOver", new { winner, reason });
                }
                CleanupRoomFromCoordinator(roomId);
                return Task.CompletedTask;
            }
            catch (Exception ex) { PauseForRecoveryFailure(room, "tournament_terminal_commit_failed", ex); completion.TrySetException(ex); return Task.CompletedTask; }
            finally { completion.TrySetResult(); }
        }), CancellationToken.None);
        if (queued) await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
