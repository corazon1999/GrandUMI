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
    public void 公开统计来源使用显式白名单且未知来源默认关闭()
    {
        var expectedPublicKinds = new HashSet<MatchKind>
        {
            MatchKind.Matchmaking,
            MatchKind.Casual,
            MatchKind.CasualStandard,
            MatchKind.CasualWild,
            MatchKind.Ranked,
            MatchKind.RankedWild,
        };

        Assert.All(Enum.GetValues<MatchKind>(), matchKind =>
            Assert.Equal(expectedPublicKinds.Contains(matchKind), LeaderStatsEligibilityPolicy.IsPublicMatch(matchKind)));
    }

    [Fact]
    public void 只有显式公开模式计入且好友房间禁卡对局不污染榜单()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();

        var publicKinds = new[]
        {
            MatchKind.Matchmaking,
            MatchKind.Casual,
            MatchKind.CasualStandard,
            MatchKind.CasualWild,
            MatchKind.Ranked,
            MatchKind.RankedWild,
        };
        foreach (var matchKind in publicKinds)
            store.RecordMatch(Match($"public-{matchKind}", now, matchKind, $"L-{matchKind}", "L-PUBLIC-OPPONENT", 0, 0, 8));

        store.RecordMatch(Match("room-code", now, MatchKind.RoomCode, "OP03-040", "L-ROOM", 0, 0, 20));
        store.RecordMatch(Match("friendly", now, MatchKind.Friendly, "OP03-040", "L-FRIEND", 0, 0, 20));
        store.RecordMatch(Match("unknown", now, MatchKind.UnknownHuman, "OP03-040", "L-UNKNOWN", 0, 0, 20));
        store.RecordMatch(Match("too-short", now, MatchKind.CasualWild, "L-SHORT", "L-OTHER", 0, 0, 7));
        store.RecordMatch(Match("bot", now, MatchKind.Bot, "OP03-040", "L-BOT", 0, 0, 20));

        var result = store.GetLeaderboard("all", now);

        Assert.Equal(publicKinds.Length, result.TotalMatches);
        Assert.All(publicKinds, matchKind =>
            Assert.Contains(result.Items, x => x.LeaderNumber == $"L-{matchKind}"));
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber is "OP03-040" or "L-ROOM" or "L-FRIEND" or "L-UNKNOWN" or "L-SHORT" or "L-BOT");
    }

    [Fact]
    public void 重复对局幂等且相同账号对局不参与榜单()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        var match = Match("same-id", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 1, 8);

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
        Assert.Empty(sevenDays.Items);
        Assert.Equal(21, all.TotalMatches);
        Assert.Equal(1, Assert.Single(all.Items, x => x.LeaderNumber == "L-A").Rank);
        Assert.Equal(2, Assert.Single(all.Items, x => x.LeaderNumber == "L-B").Rank);
        Assert.True(Assert.Single(all.Items, x => x.LeaderNumber == "L-C").InsufficientSample);
    }

    [Theory]
    [InlineData(" 7D ", "7d", LeaderStatsStore.MinimumSevenDayLeaderboardGames)]
    [InlineData(" 30D ", "30d", LeaderStatsStore.MinimumThirtyDayLeaderboardGames)]
    [InlineData(null, "7d", LeaderStatsStore.MinimumSevenDayLeaderboardGames)]
    [InlineData("unexpected", "7d", LeaderStatsStore.MinimumSevenDayLeaderboardGames)]
    public void 周期榜隐藏低于最低场次的领航且保留恰好达到门槛的领航(
        string? requestedPeriod,
        string expectedPeriod,
        int minimumGames)
    {
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        store.Initialize();
        SeedCountedMatches(store, "exact", "L-EXACT", "L-EXACT-OPPONENT", minimumGames, now);
        SeedCountedMatches(store, "below", "L-BELOW", "L-BELOW-OPPONENT", minimumGames - 1, now);

        var result = store.GetLeaderboard(requestedPeriod, now);

        Assert.Equal(expectedPeriod, result.Period);
        Assert.Equal(minimumGames * 2 - 1, result.TotalMatches);
        Assert.Equal(minimumGames, Assert.Single(result.Items, x => x.LeaderNumber == "L-EXACT").Games);
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber is "L-BELOW" or "L-BELOW-OPPONENT");
        Assert.All(result.Items, x => Assert.True(x.Games >= minimumGames));
    }

    [Fact]
    public void 测试服独立写入且读取未迁移正式库时仍只展示公开匹配()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempDir);
        var productionPath = Path.Combine(_tempDir, "production.db");
        var testPath = Path.Combine(_tempDir, "test.db");
        var productionStore = new LeaderStatsStore(productionPath);
        var testStore = new LeaderStatsStore(testPath, productionPath);

        for (var index = 0; index < LeaderStatsStore.MinimumRankedGames; index++)
        {
            productionStore.RecordMatch(Match(
                $"production-{index}",
                now,
                MatchKind.Matchmaking,
                "L-PROD-A",
                "L-PROD-B",
                0,
                index % 2,
                8,
                index == 0 ? ["PUBLIC-CARD"] : null));
        }
        productionStore.RecordMatch(Match(
            "legacy-private", now, MatchKind.Friendly, "OP03-040", "L-PROD-A", 0, 0, 20));
        testStore.RecordMatch(Match(
            "test-match", now, MatchKind.Matchmaking, "L-TEST-A", "L-TEST-B", 0, 0, 8));
        using (var connection = new SqliteConnection($"Data Source={productionPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // 模拟测试服先升级、正式只读数据源仍保留 v1 误标资格的部署窗口。
            command.CommandText = """
                UPDATE match_results
                SET counted = 1, exclude_reason = NULL, stats_version = 1
                WHERE match_id = 'legacy-private';
                INSERT INTO match_starting_hand_cards (match_id, player_index, card_number)
                VALUES ('legacy-private', 1, 'PRIVATE-CARD');
                """;
            command.ExecuteNonQuery();
        }

        var result = testStore.GetLeaderboard("all", now);
        var profile = testStore.GetPlayerProfile("Alice", "all", now);
        var favorites = testStore.GetFavoriteLeaders([HashAccount("Alice")]);
        var matchups = testStore.GetMatchups("L-PROD-A", "all", now);
        var matrix = testStore.GetMatchupMatrix("all", now);

        Assert.Equal(productionPath, testStore.LeaderboardDatabasePath);
        Assert.Equal(LeaderStatsStore.MinimumRankedGames, result.TotalMatches);
        Assert.Contains(result.Items, x => x.LeaderNumber == "L-PROD-A");
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber == "L-TEST-A");
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber == "OP03-040");
        Assert.Equal(LeaderStatsStore.MinimumRankedGames, profile.Games);
        Assert.DoesNotContain(profile.TopLeaders, x => x.LeaderNumber == "OP03-040");
        Assert.Equal("L-PROD-A", favorites[HashAccount("Alice")].LeaderNumber);
        Assert.Equal(1, matchups.StartingHandSampleGames);
        Assert.Contains(matchups.StartingHandItems, x => x.CardNumber == "PUBLIC-CARD");
        Assert.DoesNotContain(matchups.StartingHandItems, x => x.CardNumber == "PRIVATE-CARD");
        Assert.DoesNotContain(matrix.Rows, x => x.LeaderNumber == "OP03-040");
        Assert.True(testStore.ContainsMatch("test-match"));
        Assert.False(testStore.ContainsMatch("production-0"));
    }

    [Fact]
    public void 正式WAL锚点保持侧车供外部只读源访问并可幂等释放()
    {
        Directory.CreateDirectory(_tempDir);
        var productionPath = Path.Combine(_tempDir, "wal-anchor-production.db");
        var testPath = Path.Combine(_tempDir, "wal-anchor-test.db");
        var productionStore = new LeaderStatsStore(productionPath);
        using var testStore = new LeaderStatsStore(testPath, productionPath);

        productionStore.Initialize();
        Assert.False(productionStore.WalAnchorActive);

        // 已完成初始化的 Store 仍可在服务启动阶段显式升级为进程寿命锚点。
        productionStore.Initialize(keepWalAnchor: true);
        productionStore.RecordMatch(Match(
            "anchor-visible", new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc),
            MatchKind.Matchmaking, "L-ANCHOR-A", "L-ANCHOR-B", 0, 0, 8));

        Assert.True(productionStore.WalAnchorActive);
        Assert.True(File.Exists(productionPath + "-wal"));
        Assert.True(File.Exists(productionPath + "-shm"));
        Assert.Equal(1, testStore.GetLeaderboard("all").TotalMatches);

        Parallel.For(0, 16, _ => productionStore.Dispose());
        Assert.False(productionStore.WalAnchorActive);
        Assert.Throws<ObjectDisposedException>(() => productionStore.Initialize(keepWalAnchor: true));
        Assert.Throws<ObjectDisposedException>(() => productionStore.RecordMatch(Match(
            "after-dispose", DateTime.UtcNow, MatchKind.Matchmaking,
            "L-A", "L-B", 0, 0, 8)));
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
                $"mirror-{gameIndex}", now, MatchKind.RankedWild, target, target, winner, firstPlayer, 10));
        }

        for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames - 1; gameIndex++)
        {
            store.RecordMatch(Match(
                $"unranked-{gameIndex}", now, MatchKind.CasualStandard, target, "L-20", 1, gameIndex % 2, 12));
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
    public void 对阵矩阵取胜率榜前二十且双方胜率互补()
    {
        var now = new DateTime(2026, 8, 9, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();

        const int candidateLeaderCount = LeaderStatsStore.MatchupMatrixLeaderLimit + 1;
        for (var leaderIndex = 0; leaderIndex < candidateLeaderCount; leaderIndex++)
        {
            var leader = $"L-{leaderIndex:D2}";
            var opponent = $"L-{(leaderIndex + 1) % candidateLeaderCount:D2}";
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

        Assert.Equal(1, result.Games);
        Assert.Equal(1, result.Wins);
        Assert.Equal(0, result.Losses);
        Assert.Equal(1, result.WinRate, precision: 8);
        var favorite = Assert.Single(result.TopLeaders, item => item.LeaderNumber == "L-A");
        Assert.Equal(1, favorite.Games);
        Assert.Equal(1, favorite.Wins);
        Assert.Equal(10, result.Trend.Count);
        Assert.Equal(1, result.Trend.Sum(point => point.Games));

        var bob = store.GetPlayerProfile("Bob", "30d", now);
        Assert.Equal(1, bob.Games);
        Assert.Single(bob.TopLeaders);
    }

    [Fact]
    public void 个人详情返回本人所用全部领航并合并公开匹配模式()
    {
        var now = new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        var publicKinds = new[]
        {
            MatchKind.CasualStandard,
            MatchKind.CasualWild,
            MatchKind.Ranked,
            MatchKind.RankedWild,
        };
        for (var index = 0; index < publicKinds.Length; index++)
        {
            store.RecordMatch(Match(
                $"profile-all-leaders-{index}",
                now.AddMinutes(index),
                publicKinds[index],
                $"L-{index}",
                "L-OPPONENT",
                winner: index % 2,
                firstPlayer: index % 2,
                turnCount: 8));
        }
        store.RecordMatch(Match(
            "profile-private-excluded", now, MatchKind.Friendly,
            "L-PRIVATE", "L-OPPONENT", winner: 0, firstPlayer: 0, turnCount: 20));

        var profile = store.GetPlayerProfile("Alice", "all", now.AddMinutes(10));

        Assert.Equal(4, profile.Games);
        Assert.Equal(["L-0", "L-1", "L-2", "L-3"],
            profile.TopLeaders.Select(item => item.LeaderNumber).OrderBy(number => number));
        Assert.DoesNotContain(profile.TopLeaders, item => item.LeaderNumber == "L-PRIVATE");
    }

    [Fact]
    public void 公开排位榜可按账号哈希读取最常使用的Leader()
    {
        var now = new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        store.RecordMatch(Match("favorite-1", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8));
        store.RecordMatch(Match("favorite-2", now.AddMinutes(1), MatchKind.Matchmaking, "L-A", "L-C", 1, 0, 8));
        store.RecordMatch(Match("favorite-3", now.AddMinutes(2), MatchKind.Matchmaking, "L-D", "L-B", 0, 0, 8));
        for (var index = 0; index < 5; index++)
            store.RecordMatch(Match($"private-favorite-{index}", now, MatchKind.Friendly, "OP03-040", "L-X", 0, 0, 20));

        var favorites = store.GetFavoriteLeaders(new[] { HashAccount("Alice"), HashAccount("Bob"), "missing" });

        var alice = favorites[HashAccount("Alice")];
        Assert.Equal("L-A", alice.LeaderNumber);
        Assert.Equal(2, alice.Games);
        Assert.Equal(1, alice.Wins);
        Assert.NotEqual("OP03-040", alice.LeaderNumber);
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

    [Fact]
    public void 每日场次按UTC加八统计真人完成局并补齐空日期()
    {
        var store = CreateStore();
        store.RecordMatch(Match("day-25", new DateTime(2026, 8, 25, 15, 59, 0, DateTimeKind.Utc), MatchKind.Friendly, "L-A", "L-B", 0, 0, 2));
        store.RecordMatch(Match("day-26", new DateTime(2026, 8, 25, 16, 1, 0, DateTimeKind.Utc), MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8));
        store.RecordMatch(Match("bot", new DateTime(2026, 8, 25, 17, 0, 0, DateTimeKind.Utc), MatchKind.Bot, "L-A", "L-B", 0, 0, 8));

        var points = store.GetRecentDailyMatchCounts(3, new DateTime(2026, 8, 26, 1, 0, 0, DateTimeKind.Utc));

        Assert.Equal(["2026-08-24", "2026-08-25", "2026-08-26"], points.Select(point => point.Date));
        Assert.Equal([0, 1, 1], points.Select(point => point.Count));
    }

    [Fact]
    public void V1历史资格会原子重算且保留逐局事实和合法公开战绩()
    {
        var now = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
        var databasePath = SeedLegacyV1Database(now);

        var store = new LeaderStatsStore(databasePath);
        store.Initialize();

        var rows = ReadEligibilityRows(databasePath);
        Assert.Equal((1L, (string?)null, 2L), rows["public"]);
        Assert.Equal((0L, "private_match", 2L), rows["friendly"]);
        Assert.Equal((0L, "private_match", 2L), rows["room-code"]);
        Assert.Equal((0L, "unsupported_match_kind", 2L), rows["unknown"]);
        Assert.Equal((0L, "bot", 2L), rows["bot"]);
        Assert.Equal((0L, "no_winner", 2L), rows["no-winner"]);
        Assert.Equal((0L, "disconnect", 2L), rows["disconnect"]);
        Assert.Equal((0L, "too_short", 2L), rows["too-short"]);
        Assert.Equal((0L, "same_account", 2L), rows["same-account"]);

        var leaderboard = store.GetLeaderboard("all", now);
        var namiMatchups = store.GetMatchups("OP03-040", "all", now);
        Assert.Equal(1, leaderboard.TotalMatches);
        Assert.Contains(leaderboard.Items, item => item.LeaderNumber == "L-PUBLIC");
        Assert.DoesNotContain(leaderboard.Items, item => item.LeaderNumber == "OP03-040");
        Assert.Equal(0, namiMatchups.StartingHandSampleGames);

        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            Assert.Equal(LeaderStatsStore.StatsVersion, ReadIntScalar(connection, "PRAGMA user_version;"));
            Assert.Equal(1, ReadIntScalar(connection,
                "SELECT COUNT(*) FROM leader_stats_migrations WHERE version = 2 AND description = 'public_match_only';"));
            Assert.Equal(rows.Count, ReadIntScalar(connection, "SELECT COUNT(*) FROM match_results;"));
            Assert.Equal(2, ReadIntScalar(connection, "SELECT COUNT(*) FROM match_starting_hand_cards;"));
            Assert.Equal("ok", ReadStringScalar(connection, "PRAGMA integrity_check;"));
        }

        // 重启重复执行不改变事实、不重复登记迁移。
        new LeaderStatsStore(databasePath).Initialize();
        Assert.Equal(
            rows.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            ReadEligibilityRows(databasePath).OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void V2触发器会约束回滚后的旧程序写入且重复对局仍幂等()
    {
        var now = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
        var databasePath = SeedLegacyV1Database(now);
        var store = new LeaderStatsStore(databasePath);
        store.Initialize();

        InsertLegacyV1Match(databasePath, "rollback-private", MatchKind.Friendly, "OP03-040", "L-FRIEND");
        InsertLegacyV1Match(databasePath, "rollback-public", MatchKind.RankedWild, "L-ROLLBACK", "L-OPPONENT");

        var rows = ReadEligibilityRows(databasePath);
        Assert.Equal((0L, "private_match", 2L), rows["rollback-private"]);
        Assert.Equal((1L, (string?)null, 2L), rows["rollback-public"]);
        Assert.False(store.RecordMatch(Match(
            "rollback-public", now, MatchKind.RankedWild, "L-CHANGED", "L-CHANGED-OPPONENT", 1, 1, 20)));

        var leaderboard = store.GetLeaderboard("all", now);
        Assert.Equal(2, leaderboard.TotalMatches);
        Assert.Contains(leaderboard.Items, item => item.LeaderNumber == "L-ROLLBACK");
        Assert.DoesNotContain(leaderboard.Items, item => item.LeaderNumber is "OP03-040" or "L-CHANGED");
    }

    [Fact]
    public async Task 多实例并发初始化会串行完成同一份可重入迁移()
    {
        var databasePath = SeedLegacyV1Database(new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc));
        var emptyDatabasePath = Path.Combine(_tempDir, "concurrent-empty.db");

        await Task.WhenAll(new[] { databasePath, emptyDatabasePath }.SelectMany(path =>
            Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                var store = new LeaderStatsStore(path);
                store.Initialize();
            }))));

        var rows = ReadEligibilityRows(databasePath);
        Assert.Equal((0L, "private_match", 2L), rows["friendly"]);
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        Assert.Equal(1, ReadIntScalar(connection,
            "SELECT COUNT(*) FROM leader_stats_migrations WHERE version = 2;"));
        Assert.Equal(1, ReadIntScalar(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'trg_match_results_upgrade_legacy_insert';"));
        Assert.Equal("ok", ReadStringScalar(connection, "PRAGMA integrity_check;"));

        using var emptyConnection = new SqliteConnection($"Data Source={emptyDatabasePath}");
        emptyConnection.Open();
        Assert.Equal(LeaderStatsStore.StatsVersion, ReadIntScalar(emptyConnection, "PRAGMA user_version;"));
        Assert.Equal("wal", ReadStringScalar(emptyConnection, "PRAGMA journal_mode;"));
        Assert.Equal("ok", ReadStringScalar(emptyConnection, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void 较新数据库版本会拒绝旧程序写入而不是降级覆盖()
    {
        var databasePath = Path.Combine(_tempDir, "future-version.db");
        Directory.CreateDirectory(_tempDir);
        new LeaderStatsStore(databasePath).Initialize();
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {LeaderStatsStore.StatsVersion + 1};";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<InvalidOperationException>(() => new LeaderStatsStore(databasePath).Initialize());
        Assert.Contains("高于当前程序支持", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 迁移中途失败会整体回滚且修复结构后可安全重试()
    {
        var databasePath = Path.Combine(_tempDir, "interrupted-migration.db");
        Directory.CreateDirectory(_tempDir);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // 故意模拟旧库缺列：建迁移审计表之后，资格重算会失败。
            command.CommandText = """
                CREATE TABLE match_results (
                    match_id TEXT PRIMARY KEY,
                    ended_at_utc TEXT NOT NULL,
                    match_kind TEXT NOT NULL,
                    player0_key TEXT NOT NULL,
                    player1_key TEXT NOT NULL,
                    player0_leader TEXT NOT NULL,
                    player1_leader TEXT NOT NULL,
                    winner_index INTEGER NULL,
                    first_player_index INTEGER NOT NULL,
                    turn_count INTEGER NOT NULL,
                    finish_reason TEXT NOT NULL,
                    counted INTEGER NOT NULL,
                    exclude_reason TEXT NULL
                );
                INSERT INTO match_results VALUES (
                    'private-before-retry', '2026-08-26T08:00:00.0000000Z', 'Friendly',
                    'alice-key', 'bob-key', 'OP03-040', 'L-B', 0, 0, 20, '测试结束', 1, NULL
                );
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        var store = new LeaderStatsStore(databasePath);
        var exception = Assert.Throws<SqliteException>(() => store.Initialize());
        Assert.Contains("stats_version", exception.Message, StringComparison.OrdinalIgnoreCase);

        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            Assert.Equal(1, ReadIntScalar(connection, "PRAGMA user_version;"));
            Assert.Equal(0, ReadIntScalar(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'leader_stats_migrations';"));

            using var repair = connection.CreateCommand();
            repair.CommandText = "ALTER TABLE match_results ADD COLUMN stats_version INTEGER NOT NULL DEFAULT 1;";
            repair.ExecuteNonQuery();
        }

        // 同一个 Store 在失败后仍可重试；成功时审计、触发器和版本号一次提交。
        store.Initialize();
        Assert.Equal((0L, "private_match", 2L), ReadEligibilityRows(databasePath)["private-before-retry"]);
        using var verified = new SqliteConnection($"Data Source={databasePath}");
        verified.Open();
        Assert.Equal(LeaderStatsStore.StatsVersion, ReadIntScalar(verified, "PRAGMA user_version;"));
        Assert.Equal(1, ReadIntScalar(verified,
            "SELECT COUNT(*) FROM leader_stats_migrations WHERE version = 2;"));
        Assert.Equal("ok", ReadStringScalar(verified, "PRAGMA integrity_check;"));
    }

    private string SeedLegacyV1Database(DateTime now)
    {
        Directory.CreateDirectory(_tempDir);
        var databasePath = Path.Combine(_tempDir, "legacy-v1.db");
        var seedStore = new LeaderStatsStore(databasePath);
        seedStore.RecordMatch(Match(
            "public", now, MatchKind.Matchmaking, "L-PUBLIC", "L-PUBLIC-OPPONENT", 0, 0, 20,
            ["PUBLIC-CARD"]));
        seedStore.RecordMatch(Match(
            "friendly", now, MatchKind.Friendly, "OP03-040", "L-FRIEND", 0, 0, 20));
        seedStore.RecordMatch(Match(
            "room-code", now, MatchKind.RoomCode, "OP03-040", "L-ROOM", 0, 0, 20));
        seedStore.RecordMatch(Match(
            "unknown", now, MatchKind.UnknownHuman, "OP03-040", "L-UNKNOWN", 0, 0, 20));
        seedStore.RecordMatch(Match(
            "bot", now, MatchKind.Bot, "OP03-040", "L-BOT", 0, 0, 20));
        seedStore.RecordMatch(new LeaderMatchResult(
            "no-winner", now, MatchKind.CasualWild,
            "Alice", "Bob", "L-NO-WINNER", "L-OTHER", null, 0, 20, "未分胜负"));
        seedStore.RecordMatch(new LeaderMatchResult(
            "disconnect", now, MatchKind.Ranked,
            "Alice", "Bob", "L-DISCONNECT", "L-OTHER", 0, 0, 20, "Bob 断线超时"));
        seedStore.RecordMatch(Match(
            "too-short", now, MatchKind.CasualStandard, "L-SHORT", "L-OTHER", 0, 0, 7));
        seedStore.RecordMatch(new LeaderMatchResult(
            "same-account", now, MatchKind.RankedWild,
            "Alice", " alice ", "L-SAME", "L-OTHER", 0, 0, 20, "测试结束"));

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TRIGGER IF EXISTS trg_match_results_upgrade_legacy_insert;
            DELETE FROM leader_stats_migrations;
            PRAGMA user_version = 0;
            UPDATE match_results
            SET counted = 1,
                exclude_reason = NULL,
                stats_version = 1;
            INSERT INTO match_starting_hand_cards (match_id, player_index, card_number)
            VALUES ('friendly', 0, 'PRIVATE-CARD');
            """;
        command.ExecuteNonQuery();
        return databasePath;
    }

    private static void InsertLegacyV1Match(
        string databasePath,
        string matchId,
        MatchKind matchKind,
        string player0Leader,
        string player1Leader)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO match_results (
                match_id, ended_at_utc, match_kind,
                player0_key, player1_key, player0_leader, player1_leader,
                winner_index, first_player_index, turn_count, finish_reason,
                counted, exclude_reason, stats_version
            ) VALUES (
                $matchId, '2026-08-26T08:00:00.0000000Z', $matchKind,
                $player0Key, $player1Key, $player0Leader, $player1Leader,
                0, 0, 20, '旧程序写入',
                1, NULL, 1
            );
            """;
        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$matchKind", matchKind.ToString());
        command.Parameters.AddWithValue("$player0Key", HashAccount("Alice"));
        command.Parameters.AddWithValue("$player1Key", HashAccount("Bob"));
        command.Parameters.AddWithValue("$player0Leader", player0Leader);
        command.Parameters.AddWithValue("$player1Leader", player1Leader);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, (long Counted, string? ExcludeReason, long StatsVersion)> ReadEligibilityRows(
        string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_id, counted, exclude_reason, stats_version
            FROM match_results
            ORDER BY match_id;
            """;
        var result = new Dictionary<string, (long, string?, long)>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = (
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt64(3));
        }
        return result;
    }

    private static int ReadIntScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string ReadStringScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private LeaderStatsStore CreateStore()
    {
        Directory.CreateDirectory(_tempDir);
        return new LeaderStatsStore(Path.Combine(_tempDir, "leader-stats.db"));
    }

    private static void SeedCountedMatches(
        LeaderStatsStore store,
        string idPrefix,
        string leader0,
        string leader1,
        int count,
        DateTime endedAtUtc)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO match_results (
                match_id, ended_at_utc, match_kind,
                player0_key, player1_key, player0_leader, player1_leader,
                winner_index, first_player_index, turn_count, finish_reason,
                counted, exclude_reason, stats_version
            ) VALUES (
                $matchId, $endedAtUtc, 'Matchmaking',
                $player0Key, $player1Key, $player0Leader, $player1Leader,
                0, 0, 8, '测试结束',
                1, NULL, $statsVersion
            );
            """;
        var matchIdParameter = command.Parameters.Add("$matchId", SqliteType.Text);
        command.Parameters.AddWithValue("$endedAtUtc", endedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$player0Key", HashAccount("Alice"));
        command.Parameters.AddWithValue("$player1Key", HashAccount("Bob"));
        command.Parameters.AddWithValue("$player0Leader", leader0);
        command.Parameters.AddWithValue("$player1Leader", leader1);
        command.Parameters.AddWithValue("$statsVersion", LeaderStatsStore.StatsVersion);
        command.Prepare();

        for (var index = 0; index < count; index++)
        {
            matchIdParameter.Value = $"{idPrefix}-{index}";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
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
