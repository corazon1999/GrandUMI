using GrandUMI.Game.Stats;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LeaderStatsBackfillTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "grandumi-leader-backfill-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void 可回填新旧日志并保持幂等且公开榜排除私下战和机器人局()
    {
        var logs = Path.Combine(_tempDir, "MatchLogs", "2026-08-07");
        var database = Path.Combine(_tempDir, "leader-stats.db");
        Directory.CreateDirectory(logs);

        WriteLog("old-match",
            """{"schema":"grandumi.matchlog.v1","matchId":"old-match","kind":"match_start","payload":{"players":[{"index":0,"accountName":"Alice","deckRaw":"L-OLD-A\nC-1"},{"index":1,"accountName":"Bob","deckRaw":"L-OLD-B\nC-2"}],"firstPlayer":1}}""",
            """{"schema":"grandumi.matchlog.v1","matchId":"old-match","timeUtc":"2026-08-07T08:00:00Z","kind":"match_end","payload":{"winnerIndex":0,"reason":"正常结束","turnCount":8}}""");

        WriteLog("new-match",
            """{"schema":"grandumi.matchlog.v1","matchId":"new-match","kind":"match_start","payload":{"players":[{"index":0,"accountName":"Carol","deckRaw":"L-NEW-A\nC-1"},{"index":1,"accountName":"Dave","deckRaw":"L-NEW-B\nC-2"}],"firstPlayer":-1,"matchKind":"Friendly"}}""",
            """{"schema":"grandumi.matchlog.v1","matchId":"new-match","kind":"player_action_requested","actor":1,"payload":{"action":"ChooseFirstPlayer","data":{"goFirst":false}}}""",
            """{"schema":"grandumi.matchlog.v1","matchId":"new-match","timeUtc":"2026-08-07T09:00:00Z","kind":"match_end","payload":{"winnerIndex":1,"reason":"正常结束","turnCount":10,"matchKind":"Friendly"}}""");

        WriteLog("bot-match",
            """{"schema":"grandumi.matchlog.v1","matchId":"bot-match","kind":"match_start","payload":{"players":[{"index":0,"accountName":"Alice","deckRaw":"L-BOT-A\nC-1"},{"index":1,"accountName":"测试机器人","deckRaw":"L-BOT-B\nC-2"}],"firstPlayer":0}}""",
            """{"schema":"grandumi.matchlog.v1","matchId":"bot-match","timeUtc":"2026-08-07T10:00:00Z","kind":"match_end","payload":{"winnerIndex":0,"reason":"正常结束","turnCount":20}}""");

        WriteLog("unfinished-match",
            """{"schema":"grandumi.matchlog.v1","matchId":"unfinished-match","kind":"match_start","payload":{"players":[{"index":0,"accountName":"Eve","deckRaw":"L-X\nC-1"},{"index":1,"accountName":"Frank","deckRaw":"L-Y\nC-2"}],"firstPlayer":0}}""");

        var store = new LeaderStatsStore(database);
        var first = LeaderStatsBackfill.ImportDirectory(Path.Combine(_tempDir, "MatchLogs"), store);
        var second = LeaderStatsBackfill.ImportDirectory(Path.Combine(_tempDir, "MatchLogs"), store);
        var leaderboard = store.GetLeaderboard("all", new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(4, first.FilesScanned);
        Assert.Equal(3, first.Imported);
        Assert.Equal(1, first.SkippedIncomplete);
        Assert.Empty(first.Errors);
        Assert.Equal(3, second.AlreadyRecorded);
        Assert.Equal(1, second.SkippedIncomplete);
        Assert.Equal(1, leaderboard.TotalMatches);
        Assert.Contains(leaderboard.Items, x => x.LeaderNumber == "L-OLD-A" && x.SecondGames == 1);
        Assert.DoesNotContain(leaderboard.Items, x => x.LeaderNumber.StartsWith("L-NEW", StringComparison.Ordinal));
        Assert.DoesNotContain(leaderboard.Items, x => x.LeaderNumber.StartsWith("L-BOT", StringComparison.Ordinal));
    }

    private void WriteLog(string matchId, params string[] lines)
        => File.WriteAllLines(Path.Combine(_tempDir, "MatchLogs", "2026-08-07", $"{matchId}.jsonl"), lines);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
