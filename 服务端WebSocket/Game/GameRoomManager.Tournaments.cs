using System.Diagnostics;
using GrandUMI.Game.Tournaments;

namespace GrandUMI.Game;

public static partial class GameRoomManager
{
    internal static void PersistTournamentDisconnectGrace(RoomEntry room)
    {
        if (room.MatchKind != MatchKind.Tournament || TournamentRuntime.Store is not { } store) return;
        lock (room.ClockGate)
        {
            long Remaining(int player) => Math.Max(0, room.DisconnectGraceRemainingMs[player] -
                (room.DisconnectedPlayers[player] && room.DisconnectStartedAt[player] > 0 ? (long)Math.Ceiling(Stopwatch.GetElapsedTime(room.DisconnectStartedAt[player]).TotalMilliseconds) : 0));
            store.SaveDisconnectGrace(room.RoomId, Remaining(0), Remaining(1));
        }
    }

    /// <summary>只有已确认的人工赛事裁定可以终止房间；时限届满仅由赛事层发出提醒。</summary>
    internal static async Task ApplyTournamentDecisionAsync(string roomId, string reason, int? forcedWinner = null)
    {
        var room = GetRoom(roomId);
        if (room is null || room.MatchKind != MatchKind.Tournament) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = await EnqueueCriticalWorkAsync(room, new RoomWork("TournamentDecision", Stopwatch.GetTimestamp(), () =>
        {
            try
            {
                if (room.Engine.State.IsGameOver) { CleanupRoomFromCoordinator(roomId); return Task.CompletedTask; }
                PersistTournamentDisconnectGrace(room);
                var state = room.Engine.State;
                int? winner = forcedWinner;
                lock (room.CleanupGate)
                {
                    if (state.IsGameOver) return Task.CompletedTask;
                    var sequence = Volatile.Read(ref room.JournalSequence) + 1;
                    RoomJournal.AppendTerminal(roomId, sequence, winner ?? -1, winner.HasValue ? 1 - winner.Value : -1,
                        "tournament_stop", reason, DateTime.UtcNow);
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
