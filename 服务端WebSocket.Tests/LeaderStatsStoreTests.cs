using GrandUMI.Game;
using GrandUMI.Game.Stats;
using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LeaderStatsStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "grandumi-leader-stats-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void 全部真人模式计入且七回合以内和机器人对局排除()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();

        store.RecordMatch(Match("matchmaking", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8));
        store.RecordMatch(Match("room-code", now, MatchKind.RoomCode, "L-A", "L-C", 1, 1, 10));
        store.RecordMatch(Match("friendly", now, MatchKind.Friendly, "L-D", "L-A", 1, 0, 12));
        store.RecordMatch(Match("too-short", now, MatchKind.Friendly, "L-A", "L-E", 0, 0, 7));
        store.RecordMatch(Match("bot", now, MatchKind.Bot, "L-A", "L-F", 0, 0, 20));

        var result = store.GetLeaderboard("all", now);

        Assert.Equal(3, result.TotalMatches);
        var leaderA = Assert.Single(result.Items, x => x.LeaderNumber == "L-A");
        Assert.Equal(3, leaderA.Games);
        Assert.Equal(2, leaderA.Wins);
        Assert.Equal(1, leaderA.Losses);
        Assert.Equal(2d / 3d, leaderA.WinRate, precision: 8);
        Assert.Equal(0.5, leaderA.UsageRate, precision: 8);
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber is "L-E" or "L-F");
    }

    [Fact]
    public void 重复对局幂等且相同账号对局不参与榜单()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        var match = Match("same-id", now, MatchKind.RoomCode, "L-A", "L-B", 0, 1, 8);

        Assert.True(store.RecordMatch(match));
        Assert.False(store.RecordMatch(match));
        Assert.True(store.RecordMatch(new LeaderMatchResult(
            "same-account",
            now,
            MatchKind.Friendly,
            "Alice",
            "alice",
            "L-A",
            "L-C",
            0,
            0,
            20,
            "测试")));

        var result = store.GetLeaderboard("all", now);
        Assert.Equal(1, result.TotalMatches);
        Assert.All(result.Items, x => Assert.Equal(1, x.Games));
    }

    [Fact]
    public void 时间窗口和二十场排名门槛正确生效()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        for (var i = 0; i < LeaderStatsStore.MinimumRankedGames; i++)
        {
            store.RecordMatch(Match(
                $"recent-{i}",
                now.AddHours(-i),
                MatchKind.Matchmaking,
                "L-A",
                "L-B",
                0,
                i % 2,
                8));
        }
        store.RecordMatch(Match("old", now.AddDays(-8), MatchKind.Matchmaking, "L-C", "L-D", 0, 0, 8));

        var sevenDays = store.GetLeaderboard("7d", now);
        var all = store.GetLeaderboard("all", now);

        Assert.Equal(20, sevenDays.TotalMatches);
        Assert.Equal(1, Assert.Single(sevenDays.Items, x => x.LeaderNumber == "L-A").Rank);
        Assert.Equal(2, Assert.Single(sevenDays.Items, x => x.LeaderNumber == "L-B").Rank);
        Assert.Equal(21, all.TotalMatches);
        Assert.True(Assert.Single(all.Items, x => x.LeaderNumber == "L-C").InsufficientSample);
    }

    [Fact]
    public void 测试服可独立写入但榜单只读取正式服数据库()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempDir);
        var productionPath = Path.Combine(_tempDir, "production.db");
        var testPath = Path.Combine(_tempDir, "test.db");
        var productionStore = new LeaderStatsStore(productionPath);
        var testStore = new LeaderStatsStore(testPath, productionPath);

        productionStore.RecordMatch(Match(
            "production-match", now, MatchKind.Matchmaking, "L-PROD-A", "L-PROD-B", 0, 0, 8));
        testStore.RecordMatch(Match(
            "test-match", now, MatchKind.Matchmaking, "L-TEST-A", "L-TEST-B", 0, 0, 8));

        var result = testStore.GetLeaderboard("all", now);

        Assert.Equal(productionPath, testStore.LeaderboardDatabasePath);
        Assert.Equal(1, result.TotalMatches);
        Assert.Contains(result.Items, x => x.LeaderNumber == "L-PROD-A");
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber == "L-TEST-A");
        Assert.True(testStore.ContainsMatch("test-match"));
        Assert.False(testStore.ContainsMatch("production-match"));
    }

    [Fact]
    public void 对战前十统计覆盖双方位置先后手镜像和排名边界()
    {
        var now = new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        const string target = "L-TARGET";

        for (var opponentIndex = 1; opponentIndex <= 19; opponentIndex++)
        {
            var opponent = $"L-{opponentIndex}";
            for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames; gameIndex++)
            {
                var targetIndex = gameIndex % 2;
                var targetWon = gameIndex < 12;
                var winner = targetWon ? targetIndex : 1 - targetIndex;
                var targetWentFirst = gameIndex % 4 < 2;
                var firstPlayer = targetWentFirst ? targetIndex : 1 - targetIndex;
                store.RecordMatch(Match(
                    $"top-{opponentIndex}-{gameIndex}",
                    now,
                    MatchKind.Matchmaking,
                    targetIndex == 0 ? target : opponent,
                    targetIndex == 1 ? target : opponent,
                    winner,
                    firstPlayer,
                    8));
            }
        }

        for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames; gameIndex++)
        {
            var firstPlayer = gameIndex % 2;
            var winner = gameIndex < 12 ? firstPlayer : 1 - firstPlayer;
            store.RecordMatch(Match(
                $"mirror-{gameIndex}", now, MatchKind.Friendly, target, target, winner, firstPlayer, 10));
        }

        for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames - 1; gameIndex++)
        {
            store.RecordMatch(Match(
                $"unranked-{gameIndex}", now, MatchKind.RoomCode, target, "L-20", 1, gameIndex % 2, 12));
        }

        var result = store.GetMatchups(target, "all", now);

        Assert.Equal(target, result.LeaderNumber);
        Assert.Equal(LeaderStatsStore.MatchupLeaderboardLimit, result.Items.Count);
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber == "L-20");

        var opponentRow = Assert.Single(result.Items, x => x.LeaderNumber == "L-1");
        Assert.False(opponentRow.IsMirror);
        Assert.Equal(20, opponentRow.Games);
        Assert.Equal(12, opponentRow.Wins);
        Assert.Equal(8, opponentRow.Losses);
        Assert.Equal(0.6, opponentRow.WinRate!.Value, precision: 8);
        Assert.Equal(10, opponentRow.FirstGames);
        Assert.Equal(0.6, opponentRow.FirstWinRate!.Value, precision: 8);
        Assert.Equal(10, opponentRow.SecondGames);
        Assert.Equal(0.6, opponentRow.SecondWinRate!.Value, precision: 8);

        var mirrorRow = Assert.Single(result.Items, x => x.LeaderNumber == target);
        Assert.True(mirrorRow.IsMirror);
        Assert.Equal(20, mirrorRow.Games);
        Assert.Null(mirrorRow.WinRate);
        Assert.Null(mirrorRow.Wins);
        Assert.Null(mirrorRow.Losses);
        Assert.Equal(0.6, mirrorRow.FirstWinRate!.Value, precision: 8);
        Assert.Equal(0.4, mirrorRow.SecondWinRate!.Value, precision: 8);
    }

    [Fact]
    public void 起手留牌按换牌完成后的每局出现率统计并去重()
    {
        var now = new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();

        store.RecordMatch(Match(
            "hand-1", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8,
            ["OP01-001", "OP01-001", "OP01-002"], ["OP01-003"]));
        store.RecordMatch(Match(
            "hand-2", now.AddMinutes(1), MatchKind.Matchmaking, "L-A", "L-C", 1, 1, 8,
            ["OP01-001", "OP01-004"], ["OP01-005"]));

        var result = store.GetMatchups("L-A", "all", now.AddMinutes(2));

        Assert.Equal(2, result.StartingHandSampleGames);
        var topCard = Assert.Single(result.StartingHandItems, item => item.CardNumber == "OP01-001");
        Assert.Equal(2, topCard.Games);
        Assert.Equal(1, topCard.Percentage, precision: 8);
        Assert.Equal(1, Assert.Single(result.StartingHandItems, item => item.CardNumber == "OP01-002").Games);
    }

    [Fact]
    public void 对阵矩阵取胜率榜前十五且双方胜率互补()
    {
        var now = new DateTime(2026, 8, 9, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();

        for (var leaderIndex = 0; leaderIndex < 16; leaderIndex++)
        {
            var leader = $"L-{leaderIndex:D2}";
            var opponent = $"L-{(leaderIndex + 1) % 16:D2}";
            for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames; gameIndex++)
            {
                store.RecordMatch(Match(
                    $"matrix-{leaderIndex}-{gameIndex}",
                    now,
                    MatchKind.Matchmaking,
                    leader,
                    opponent,
                    0,
                    gameIndex % 2,
                    8));
            }
        }

        var leaderboard = store.GetLeaderboard("all", now);
        var result = store.GetMatchupMatrix("all", now);
        var expectedLeaders = leaderboard.Items
            .Where(item => item.Rank is not null)
            .Take(LeaderStatsStore.MatchupMatrixLeaderLimit)
            .Select(item => item.LeaderNumber)
            .ToArray();

        Assert.Equal(LeaderStatsStore.MatchupMatrixLeaderLimit, result.Rows.Count);
        Assert.Equal(expectedLeaders, result.Rows.Select(row => row.LeaderNumber));
        Assert.All(result.Rows, row => Assert.Equal(LeaderStatsStore.MatchupMatrixLeaderLimit, row.Items.Count));

        var leader0 = Assert.Single(result.Rows, row => row.LeaderNumber == "L-00");
        var leader1 = Assert.Single(result.Rows, row => row.LeaderNumber == "L-01");
        Assert.Equal(1, Assert.Single(leader0.Items, item => item.LeaderNumber == "L-01").WinRate);
        Assert.Equal(0, Assert.Single(leader1.Items, item => item.LeaderNumber == "L-00").WinRate);
        Assert.True(Assert.Single(leader0.Items, item => item.LeaderNumber == "L-00").IsMirror);
        var excludedLeader = leaderboard.Items
            .Where(item => item.Rank is not null)
            .Skip(LeaderStatsStore.MatchupMatrixLeaderLimit)
            .First()
            .LeaderNumber;
        Assert.DoesNotContain(result.Rows, row => row.LeaderNumber == excludedLeader);
    }

    [Fact]
    public void 个人详情按账号聚合胜负常用领航和趋势()
    {
        var now = new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        store.RecordMatch(new LeaderMatchResult(
            "profile-1", now.AddDays(-1), MatchKind.Matchmaking,
            "Alice", "Bob", "L-A", "L-B", 0, 0, 10, "胜利"));
        store.RecordMatch(new LeaderMatchResult(
            "profile-2", now.AddDays(-2), MatchKind.RoomCode,
            "Bob", "Alice", "L-C", "L-A", 0, 0, 12, "失败"));
        store.RecordMatch(new LeaderMatchResult(
            "profile-3", now.AddDays(-3), MatchKind.Friendly,
            "Alice", "Carol", "L-D", "L-C", 0, 1, 15, "胜利"));
        store.RecordMatch(new LeaderMatchResult(
            "profile-old", now.AddDays(-40), MatchKind.Matchmaking,
            "Alice", "Bob", "L-A", "L-B", 0, 0, 10, "旧对局"));

        var result = store.GetPlayerProfile("alice", "30d", now);

        Assert.Equal(3, result.Games);
        Assert.Equal(2, result.Wins);
        Assert.Equal(1, result.Losses);
        Assert.Equal(2d / 3d, result.WinRate, precision: 8);
        var favorite = Assert.Single(result.TopLeaders, item => item.LeaderNumber == "L-A");
        Assert.Equal(2, favorite.Games);
        Assert.Equal(1, favorite.Wins);
        Assert.Equal(10, result.Trend.Count);
        Assert.Equal(3, result.Trend.Sum(point => point.Games));

        var bob = store.GetPlayerProfile("Bob", "30d", now);
        Assert.Equal(2, bob.Games);
        Assert.Equal(2, bob.TopLeaders.Count);
    }

    [Fact]
    public void 公开排位榜可按账号哈希读取最常使用的Leader()
    {
        var now = new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        store.RecordMatch(Match("favorite-1", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8));
        store.RecordMatch(Match("favorite-2", now.AddMinutes(1), MatchKind.Matchmaking, "L-A", "L-C", 1, 0, 8));
        store.RecordMatch(Match("favorite-3", now.AddMinutes(2), MatchKind.Matchmaking, "L-D", "L-B", 0, 0, 8));

        var favorites = store.GetFavoriteLeaders(new[] { HashAccount("Alice"), HashAccount("Bob"), "missing" });

        var alice = favorites[HashAccount("Alice")];
        Assert.Equal("L-A", alice.LeaderNumber);
        Assert.Equal(2, alice.Games);
        Assert.Equal(1, alice.Wins);
        Assert.Equal("L-B", favorites[HashAccount("Bob")].LeaderNumber);
        Assert.DoesNotContain("missing", favorites.Keys);
    }

    [Fact]
    public void 个人详情响应使用前端约定的驼峰字段()
    {
        var method = typeof(WebSocketBridge).GetMethod(
            "BuildPlayerProfileStatsResponse",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var snapshot = new PlayerProfileStatsSnapshot(
            "30d",
            new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Utc),
            3,
            2,
            1,
            2d / 3d,
            2,
            0.5,
            1,
            1,
            new[]
            {
                new PlayerLeaderStatsItem("OP01-001", 3, 2, 1, 2d / 3d, 1, 2, 0.5, 1, 1),
            },
            new[]
            {
                new PlayerStatsTrendPoint("08/08", 3, 2, 2d / 3d),
            });

        var payload = method.Invoke(null, new object[] { snapshot });
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = document.RootElement;
        var leader = root.GetProperty("topLeaders").EnumerateArray().Single();
        var trend = root.GetProperty("trend").EnumerateArray().Single();

        Assert.Equal("OP01-001", leader.GetProperty("leaderNumber").GetString());
        Assert.False(leader.TryGetProperty("LeaderNumber", out _));
        Assert.Equal("08/08", trend.GetProperty("label").GetString());
        Assert.False(trend.TryGetProperty("Label", out _));
    }

    [Fact]
    public void DisconnectFinishedMatchesAreExcludedFromLeaderboardAndPlayerStats()
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempDir);
        var databasePath = Path.Combine(_tempDir, "disconnect-filter.db");
        var store = new LeaderStatsStore(databasePath);

        store.RecordMatch(Match("normal", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8));
        store.RecordMatch(new LeaderMatchResult(
            "disconnect", now, MatchKind.Matchmaking,
            "Alice", "Carol", "L-A", "L-C", 0, 0, 12, "Carol 断线超时"));
        store.RecordMatch(new LeaderMatchResult(
            "legacy-disconnect", now, MatchKind.Matchmaking,
            "Alice", "Dave", "L-A", "L-D", 0, 0, 12, "DisconnectTimeout"));
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE match_results SET counted = 1 WHERE match_id = 'legacy-disconnect';";
            command.ExecuteNonQuery();
        }

        var leaderboard = store.GetLeaderboard("all", now);
        var profile = store.GetPlayerProfile("Alice", "all", now);

        Assert.Equal(1, leaderboard.TotalMatches);
        Assert.Equal(1, Assert.Single(leaderboard.Items, x => x.LeaderNumber == "L-A").Games);
        Assert.DoesNotContain(leaderboard.Items, x => x.LeaderNumber is "L-C" or "L-D");
        Assert.Equal(1, profile.Games);
        Assert.Equal(1, profile.Wins);
    }

    private LeaderStatsStore CreateStore()
    {
        Directory.CreateDirectory(_tempDir);
        return new LeaderStatsStore(Path.Combine(_tempDir, "leader-stats.db"));
    }

    private static LeaderMatchResult Match(
        string id,
        DateTime endedAtUtc,
        MatchKind kind,
        string leader0,
        string leader1,
        int winner,
        int firstPlayer,
        int turnCount,
        IReadOnlyList<string>? player0StartingHand = null,
        IReadOnlyList<string>? player1StartingHand = null)
        => new(
            id,
            endedAtUtc,
            kind,
            "Alice",
            "Bob",
            leader0,
            leader1,
            winner,
            firstPlayer,
            turnCount,
            "测试结束",
            player0StartingHand,
            player1StartingHand);

    private static string HashAccount(string account)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant()))).ToLowerInvariant();

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
