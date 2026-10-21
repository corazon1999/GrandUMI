using GrandUMI.Effects.Rules;

namespace GrandUMI.Game.Tournaments;

public static class TournamentRuntime
{
    public static TournamentStore? Store { get; private set; }
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static void Initialize(TournamentStore store) => Store = store;

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try { await SynchronizeAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[赛事] 状态同步暂未完成，将重试：{ex.Message}"); }
        }
    }

    public static async Task SynchronizeAsync()
    {
        if (Store is null) return;
        await Gate.WaitAsync();
        try { await SynchronizeCoreAsync(); }
        finally { Gate.Release(); }
    }

    public static async Task<Tournament> ExecuteAsync(Func<Tournament> mutation)
    {
        await Gate.WaitAsync();
        try { var result = mutation(); await SynchronizeCoreAsync(); return result; }
        finally { Gate.Release(); }
    }

    private static async Task SynchronizeCoreAsync()
    {
        if (Store is null) return;
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var tournament in Store.ReadAll().Where(t => t.Status is "approved" or "swiss" or "cut" || t.Rounds.Any(r => r.Tables.Any(m => m.RoomId is not null && GameRoomManager.GetRoom(m.RoomId) is not null))))
            {
                if (tournament.Status == "approved")
                {
                    Store.SystemUpdate(tournament.Id, t => TournamentStore.ReconcileCheckins(t, now));
                    continue;
                }
                foreach (var round in tournament.Rounds)
                foreach (var table in round.Tables)
                {
                    if (table.Status == "waiting" && round.Status == "live" && now >= round.PublishedAt + 300_000)
                    {
                        Store.SystemUpdate(tournament.Id, t =>
                        {
                            var current = t.CurrentRound;
                            var m = current?.Tables.FirstOrDefault(m => m.Id == table.Id);
                            if (current?.Status != "live" || m?.Status != "waiting") return false;
                            var outcome = m.Seated0 ? "p0" : m.Seated1 ? "p1" : "doubleLoss";
                            if (current.Stage == "cut" && outcome == "doubleLoss")
                            {
                                m.Status = "blocked"; m.Reason = "淘汰赛双方缺席，请裁判处理或选手重新落座。"; return true;
                            }
                            TournamentStore.Finish(m, outcome, "超过5分钟落座窗口", "system", now);
                            foreach (var account in new[] { !m.Seated0 ? m.Player0 : null, !m.Seated1 ? m.Player1 : null }.OfType<string>())
                            {
                                var entrant = t.Entrants.First(e => string.Equals(e.Account, account, StringComparison.OrdinalIgnoreCase));
                                if (++entrant.MissedRounds >= 2) entrant.Status = "withdrawn";
                            }
                            TournamentStore.RefreshRound(t); return true;
                        });
                    }
                    if (table.Status == "starting" && round.Status == "live")
                        WebSocketBridge.StartTournamentTable(tournament.Id, table.Id);
                    if (table.Status == "playing" && round.Status == "live" && table.RoomId is { } missingRoom && GameRoomManager.GetRoom(missingRoom) is null)
                    {
                        Store.SystemUpdate(tournament.Id, t =>
                        {
                            var m = t.CurrentRound?.Tables.FirstOrDefault(m => m.Id == table.Id);
                            if (m?.Status != "playing" || m.Confirmed) return false;
                            m.Status = "blocked"; m.Seated0 = m.Seated1 = false;
                            m.Reason = "对局未能恢复，请裁判核查恢复日志后裁定，或确认可重赛后重新落座。";
                            return true;
                        });
                    }
                    if (table.RoomId is not { } roomId || GameRoomManager.GetRoom(roomId) is null) continue;
                    var stopped = round.Status == "manual" || table.Confirmed;
                    await GameRoomManager.ApplyTournamentClockAsync(roomId, round.Stage, table.StartedAt ?? now, table.ExtraTurnLimit,
                        stopped, round.Status == "manual" ? "赛事管理员结束本轮，等待逐桌人工结算" : table.Reason,
                        table.Confirmed ? table.Outcome == "p0" ? 0 : table.Outcome == "p1" ? 1 : null : null);
                    WebSocketBridge.SendTournamentClock(tournament, round, table);
                }
            }
        }
    }

    public static void RecordResult(string roomId, int? winner, string? reason, DateTime completedAt)
    {
        if (Store is null) return;
        RecordResult(Store, roomId, winner, reason, completedAt);
    }

    internal static void RecordResult(TournamentStore store, string roomId, int? winner, string? reason, DateTime completedAt)
    {
        var tournament = store.ReadAll().FirstOrDefault(t => t.Rounds.Any(r => r.Tables.Any(m => m.RoomId == roomId)));
        if (tournament is null) return;
        store.SystemUpdate(tournament.Id, t =>
        {
            var round = t.Rounds.First(r => r.Tables.Any(m => m.RoomId == roomId));
            var table = round.Tables.First(m => m.RoomId == roomId);
            // 主动结束后的迟到终局只保留参考，不得替代逐桌人工确认。
            if (table.Confirmed || round.Status == "manual" || table.Status == "manual") return false;
            TournamentStore.Finish(table, winner == 0 ? "p0" : winner == 1 ? "p1" : "doubleLoss", reason ?? "对局结束", "system", new DateTimeOffset(completedAt).ToUnixTimeMilliseconds());
            TournamentStore.RefreshRound(t); return true;
        });
    }

    public static void SetExtraTurns(string roomId, int limit)
    {
        var tournament = Store?.ReadAll().FirstOrDefault(t => t.CurrentRound?.Tables.Any(m => m.RoomId == roomId) == true);
        if (tournament is null) return;
        Store!.SystemUpdate(tournament.Id, t =>
        {
            var table = t.CurrentRound!.Tables.First(m => m.RoomId == roomId);
            if (table.ExtraTurnLimit.HasValue) return false;
            table.ExtraTurnLimit = limit; return true;
        });
    }
}
