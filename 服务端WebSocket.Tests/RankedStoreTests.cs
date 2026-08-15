using GrandUMI.Game.Ranked;
using GrandUMI.Game;
using GrandUMI.Game.Stats;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public class RankedStoreTests
{
    [Fact]
    public void 排位结算_五局完成定级且同一对局只结算一次()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            var initial = store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now)!;

            Assert.Equal(0, initial.Profile.PlacementGames);
            Assert.Equal(RankedStore.PlacementRequired, initial.Profile.PlacementRequired);
            Assert.Equal(RankedStore.PirateFaction, initial.Profile.Faction);
            Assert.Empty(initial.Leaderboard);

            for (var i = 0; i < RankedStore.PlacementRequired; i++)
            {
                var result = store.RecordMatch($"ranked-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0);
                Assert.NotNull(result);
            }

            Assert.Null(store.RecordMatch("ranked-4", now.AddMinutes(10),
                "alice", "爱丽丝", "bob-4", "对手4", winnerIndex: 0));

            var settled = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(20));
            Assert.Equal(5, settled.Profile.PlacementGames);
            Assert.Equal(5, settled.Profile.Games);
            Assert.Equal(5, settled.Profile.Wins);
            Assert.Equal(0, settled.Profile.Losses);
            Assert.InRange(settled.Profile.RankPoints, 0, 899);
            Assert.Contains(settled.Leaderboard, item => item.DisplayName == "爱丽丝" && item.Games == 5);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Theory]
    [InlineData(0, RankedStore.PirateFaction, "见习海贼", 3, null)]
    [InlineData(599, RankedStore.PirateFaction, "海贼战斗员", 1, null)]
    [InlineData(899, RankedStore.MarineFaction, "海军少校", 1, null)]
    [InlineData(1499, RankedStore.GovernmentFaction, "浅海契约", 1, null)]
    [InlineData(1500, RankedStore.PirateFaction, "超新星", null, 6)]
    [InlineData(1500, RankedStore.MarineFaction, "大将候补", null, 5)]
    [InlineData(1500, RankedStore.GovernmentFaction, "神之骑士团", null, 7)]
    [InlineData(1500, RankedStore.PirateFaction, "海贼王", null, 1)]
    [InlineData(1500, RankedStore.PirateFaction, "四皇", null, 5)]
    [InlineData(1500, RankedStore.MarineFaction, "海军大将", null, 4)]
    [InlineData(1500, RankedStore.GovernmentFaction, "五老星", null, 6)]
    public void 排位分段_阵营称号映射正确(int points, string faction, string tier, int? division, int? factionRank)
    {
        var actual = RankedStore.RankLabel(points, faction, factionRank);
        Assert.Equal(tier, actual.Tier);
        Assert.Equal(division, actual.Division);
    }

    [Fact]
    public void 阵营选择_确认更换后清空本赛季排位进度()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

            var selected = store.SelectFaction("alice", "爱丽丝", RankedStore.MarineFaction, now)!;
            for (var i = 0; i < RankedStore.PlacementRequired; i++)
            {
                Assert.NotNull(store.RecordMatch($"ranked-reset-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0));
            }

            var beforeReset = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(10));
            var unconfirmed = store.SelectFaction("alice", "爱丽丝", RankedStore.GovernmentFaction, now.AddMinutes(11));
            var changed = store.SelectFaction("alice", "爱丽丝", RankedStore.GovernmentFaction, now.AddMinutes(12),
                resetRankProgress: true);

            Assert.Equal(RankedStore.MarineFaction, selected.Profile.Faction);
            Assert.Equal(RankedStore.PlacementRequired, beforeReset.Profile.Games);
            Assert.NotNull(unconfirmed);
            Assert.Equal(RankedStore.MarineFaction, unconfirmed!.Profile.Faction);
            Assert.Equal(beforeReset.Profile.RankPoints, unconfirmed.Profile.RankPoints);
            Assert.NotNull(changed);
            Assert.Equal(RankedStore.GovernmentFaction, changed!.Profile.Faction);
            Assert.Equal(0, changed.Profile.RankPoints);
            Assert.Equal(0, changed.Profile.HighestRankPoints);
            Assert.Equal(0, changed.Profile.PlacementGames);
            Assert.Equal(0, changed.Profile.Games);
            Assert.Equal(0, changed.Profile.Wins);
            Assert.Equal(0, changed.Profile.Losses);
            Assert.Equal(1500, store.GetMatchRating("alice", "爱丽丝", now.AddMinutes(13)));
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_连续获胜三场后记录连胜且失败会中断()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

            for (var i = 1; i <= 3; i++)
            {
                var result = store.RecordMatch($"streak-win-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0);
                Assert.NotNull(result);
                Assert.Equal(i - 1, result!.Player0.WinStreakBefore);
                Assert.Equal(i, result!.Player0.WinStreak);
                Assert.Equal(0, result.Player1.WinStreak);
            }

            var loss = store.RecordMatch("streak-loss", now.AddMinutes(4),
                "alice", "爱丽丝", "bob-loss", "对手", winnerIndex: 1);
            Assert.NotNull(loss);
            Assert.Equal(3, loss!.Player0.WinStreakBefore);
            Assert.Equal(0, loss!.Player0.WinStreak);
            Assert.Equal(0, loss.Player1.WinStreakBefore);
            Assert.Equal(1, loss.Player1.WinStreak);

            var winAfterLoss = store.RecordMatch("streak-restart", now.AddMinutes(5),
                "alice", "爱丽丝", "bob-restart", "对手", winnerIndex: 0);
            Assert.NotNull(winAfterLoss);
            Assert.Equal(1, winAfterLoss!.Player0.WinStreak);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_连胜奖励十一连胜封顶十分且连败保护六连败封顶五分()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

            CompletePlacements(store, now, "streak");
            SetRankPoints(path, ("爱丽丝", 1000), ("鲍勃", 1000));

            for (var streak = 1; streak <= 12; streak++)
            {
                // 此用例只验证连续场次修正；每局前拉回同分，隔离分差修正。
                SetRankPoints(path, ("爱丽丝", 1000), ("鲍勃", 1000));
                var result = store.RecordMatch($"streak-{streak}", now.AddMinutes(10 + streak),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);
                var winAdjustment = Math.Min(streak - 1, 10);
                var lossAdjustment = Math.Min(streak - 1, 5);

                Assert.NotNull(result);
                Assert.Equal(20 + winAdjustment, result!.Player0.RankPointDelta);
                Assert.Equal(-20 + lossAdjustment, result.Player1.RankPointDelta);
                Assert.Equal(winAdjustment, result.Player0.StreakAdjustment);
                Assert.Equal(lossAdjustment, result.Player1.StreakAdjustment);
                Assert.Equal(streak, result.Player0.ResultStreak);
                Assert.Equal(streak, result.Player1.ResultStreak);
                Assert.Equal(0, result.Player0.RankDifferenceAdjustment);
                Assert.Equal(0, result.Player1.RankDifferenceAdjustment);
            }
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Theory]
    [InlineData(99, 0, 0)]
    [InlineData(100, 1, -1)]
    [InlineData(299, 2, -2)]
    [InlineData(300, 3, -3)]
    [InlineData(499, 4, -4)]
    [InlineData(500, 5, -5)]
    public void 排位结算_低分方与高分方每百分差修正一分且均封顶五分(
        int rankDifference,
        int expectedLowAdjustment,
        int expectedHighAdjustment)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 13, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, $"gap-{rankDifference}");
            SetRankPoints(path, ("爱丽丝", 900), ("鲍勃", 900 + rankDifference));

            var lowWins = store.RecordMatch($"gap-low-win-{rankDifference}", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(lowWins);
            Assert.Equal(-rankDifference, lowWins!.Player0.RankDifference);
            Assert.Equal(rankDifference, lowWins.Player1.RankDifference);
            Assert.Equal(expectedLowAdjustment, lowWins.Player0.RankDifferenceAdjustment);
            Assert.Equal(expectedHighAdjustment, lowWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(20 + expectedLowAdjustment, lowWins.Player0.RankPointDelta);
            Assert.Equal(-20 + expectedHighAdjustment, lowWins.Player1.RankPointDelta);

            // 重置 RP 并交换胜负，覆盖低分方失败与高分方获胜的另外两个象限。
            SetRankPoints(path, ("爱丽丝", 900), ("鲍勃", 900 + rankDifference));
            var highWins = store.RecordMatch($"gap-high-win-{rankDifference}", now.AddMinutes(11),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1);

            Assert.NotNull(highWins);
            Assert.Equal(expectedLowAdjustment, highWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(expectedHighAdjustment, highWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(-20 + expectedLowAdjustment, highWins.Player0.RankPointDelta);
            Assert.Equal(20 + expectedHighAdjustment, highWins.Player1.RankPointDelta);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_连续场次与五百分差修正可以叠加()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 14, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, "combined");
            SetRankPoints(path, ("爱丽丝", 1000), ("鲍勃", 1000));

            for (var i = 1; i <= 10; i++)
                Assert.NotNull(store.RecordMatch($"combined-streak-{i}", now.AddMinutes(10 + i),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));

            SetRankPoints(path, ("爱丽丝", 1000), ("鲍勃", 1500));
            var result = store.RecordMatch("combined-final", now.AddMinutes(30),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(result);
            Assert.Equal(10, result!.Player0.StreakAdjustment);
            Assert.Equal(5, result.Player0.RankDifferenceAdjustment);
            Assert.Equal(35, result.Player0.RankPointDelta);
            Assert.Equal(10, result.Player1.StreakAdjustment);
            Assert.Equal(-5, result.Player1.RankDifferenceAdjustment);
            Assert.Equal(-35, result.Player1.RankPointDelta);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_新世界基础分与连续胜负上限全部翻倍()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 14, 30, 0, DateTimeKind.Utc);

            CompletePlacements(store, now, "new-world-streak");
            SetRankPoints(path, ("爱丽丝", RankedStore.NewWorldRankPoints), ("鲍勃", RankedStore.NewWorldRankPoints));

            for (var streak = 1; streak <= 22; streak++)
            {
                SetRankPoints(path, ("爱丽丝", RankedStore.NewWorldRankPoints), ("鲍勃", RankedStore.NewWorldRankPoints));
                var result = store.RecordMatch($"new-world-streak-{streak}", now.AddMinutes(10 + streak),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);
                var winAdjustment = Math.Min(streak - 1, 20);
                var lossAdjustment = Math.Min(streak - 1, 10);

                Assert.NotNull(result);
                Assert.Equal(40 + winAdjustment, result!.Player0.RankPointDelta);
                Assert.Equal(-40 + lossAdjustment, result.Player1.RankPointDelta);
                Assert.Equal(winAdjustment, result.Player0.StreakAdjustment);
                Assert.Equal(lossAdjustment, result.Player1.StreakAdjustment);
                Assert.Equal(0, result.Player0.RankDifferenceAdjustment);
                Assert.Equal(0, result.Player1.RankDifferenceAdjustment);
            }
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_新世界高低分双方分差修正上限翻倍至十分()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, "new-world-gap");
            SetRankPoints(path, ("爱丽丝", 1500), ("鲍勃", 2500));

            var lowWins = store.RecordMatch("new-world-gap-low-win", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(lowWins);
            Assert.Equal(10, lowWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(-10, lowWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(50, lowWins.Player0.RankPointDelta);
            Assert.Equal(-50, lowWins.Player1.RankPointDelta);

            SetRankPoints(path, ("爱丽丝", 1500), ("鲍勃", 2500));
            var highWins = store.RecordMatch("new-world-gap-high-win", now.AddMinutes(11),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1);

            Assert.NotNull(highWins);
            Assert.Equal(10, highWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(-10, highWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(-30, highWins.Player0.RankPointDelta);
            Assert.Equal(30, highWins.Player1.RankPointDelta);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_十亿悬赏后基础分与连续胜负上限再次翻倍()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 15, 30, 0, DateTimeKind.Utc);

            CompletePlacements(store, now, "ten-billion-streak");
            SetRankPoints(path, ("爱丽丝", RankedStore.TenBillionBountyRankPoints),
                ("鲍勃", RankedStore.TenBillionBountyRankPoints));

            for (var streak = 1; streak <= 42; streak++)
            {
                SetRankPoints(path, ("爱丽丝", RankedStore.TenBillionBountyRankPoints),
                    ("鲍勃", RankedStore.TenBillionBountyRankPoints));
                var result = store.RecordMatch($"ten-billion-streak-{streak}", now.AddMinutes(10 + streak),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);
                var winAdjustment = Math.Min(streak - 1, 40);
                var lossAdjustment = Math.Min(streak - 1, 20);

                Assert.NotNull(result);
                Assert.Equal(80 + winAdjustment, result!.Player0.RankPointDelta);
                Assert.Equal(-80 + lossAdjustment, result.Player1.RankPointDelta);
                Assert.Equal(winAdjustment, result.Player0.StreakAdjustment);
                Assert.Equal(lossAdjustment, result.Player1.StreakAdjustment);
                Assert.Equal(0, result.Player0.RankDifferenceAdjustment);
                Assert.Equal(0, result.Player1.RankDifferenceAdjustment);
            }
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_十亿悬赏后高低分双方分差修正上限再次翻倍至二十分()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 16, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, "ten-billion-gap");
            SetRankPoints(path, ("爱丽丝", 10000), ("鲍勃", 12000));

            var lowWins = store.RecordMatch("ten-billion-gap-low-win", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(lowWins);
            Assert.Equal(20, lowWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(-20, lowWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(100, lowWins.Player0.RankPointDelta);
            Assert.Equal(-100, lowWins.Player1.RankPointDelta);

            SetRankPoints(path, ("爱丽丝", 10000), ("鲍勃", 12000));
            var highWins = store.RecordMatch("ten-billion-gap-high-win", now.AddMinutes(11),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1);

            Assert.NotNull(highWins);
            Assert.Equal(20, highWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(-20, highWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(-60, highWins.Player0.RankPointDelta);
            Assert.Equal(60, highWins.Player1.RankPointDelta);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位榜_返回玩家持有的最强称号且不包含称号胜率()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-champion-{Guid.NewGuid():N}");
        var rankedPath = Path.Combine(tempDir, "ranked.db");
        var championPath = Path.Combine(tempDir, "champions.db");
        try
        {
            Directory.CreateDirectory(tempDir);
            var now = new DateTime(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc);
            var championStore = new LeaderChampionStore(championPath);
            for (var i = 0; i < LeaderChampionStore.MinimumChampionGames; i++)
            {
                Assert.True(championStore.RecordMatch(new LeaderMatchResult(
                    $"champion-{i}", now, MatchKind.Ranked,
                    "alice", $"opponent-{i}", "OP16-001", "OP01-001", 0, 0, 8, "胜利")));
            }

            var rankedStore = new RankedStore(rankedPath, championStore);
            Assert.NotNull(rankedStore.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            Assert.NotNull(rankedStore.SelectFaction("bob", "鲍勃", RankedStore.MarineFaction, now));
            CompletePlacements(rankedStore, now, "champion-rank");
            var item = Assert.Single(rankedStore.GetSnapshot("alice", "爱丽丝", now.AddMinutes(20)).Leaderboard,
                entry => entry.DisplayName == "爱丽丝");

            Assert.Equal(new[] { "OP16-001" }, item.ChampionLeaderNumbers);
            var wireJson = System.Text.Json.JsonSerializer.Serialize(RankWire.Leaderboard(new[] { item }));
            Assert.Contains("\"championLeaderNumbers\":[\"OP16-001\"]", wireJson);
            Assert.DoesNotContain("championWinRate", wireJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void CompletePlacements(RankedStore store, DateTime now, string prefix)
    {
        // 最后一场由鲍勃获胜，保证随后爱丽丝胜、鲍勃负时都从一连开始。
        for (var i = 0; i < RankedStore.PlacementRequired; i++)
            Assert.NotNull(store.RecordMatch($"{prefix}-placement-{i}", now.AddMinutes(i),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: (i + 1) % 2));
    }

    private static void SetRankPoints(string path, params (string DisplayName, int RankPoints)[] values)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        foreach (var (displayName, rankPoints) in values)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rank_profiles SET rank_points=$points, highest_rank_points=$points WHERE display_name=$name;";
            command.Parameters.AddWithValue("$points", rankPoints);
            command.Parameters.AddWithValue("$name", displayName);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
