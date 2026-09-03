using GrandUMI.Game.Ranked;
using GrandUMI.Game;
using GrandUMI.Game.Stats;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public class RankedStoreTests
{
    [Fact]
    public void 标准与狂野排位使用不同数据库且协议默认标准()
    {
        Assert.NotEqual(
            Path.GetFullPath(RankedStore.ResolveDefaultPath()),
            Path.GetFullPath(RankedStore.ResolveWildDefaultPath()));
        Assert.Equal(RankedMode.Standard, RankedModeWire.Parse(null));
        Assert.Equal(RankedMode.Standard, RankedModeWire.Parse("standard"));
        Assert.Equal(RankedMode.Wild, RankedModeWire.Parse("wild"));
    }

    [Fact]
    public void 匹配资料_同一快照包含隐藏分定级进度悬赏与阵营()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "排位匹配资料测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        var path = Path.Combine(tempRoot, $"grandumi-ranked-matchmaking-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            for (var i = 0; i < RankedStore.PlacementRequired; i++)
            {
                Assert.NotNull(store.RecordMatch($"matchmaking-profile-{i}", now.AddMinutes(i + 1),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0));
            }
            SetRankPoints(path, ("爱丽丝", RankedStore.NewWorldRankPoints));

            var profile = store.GetMatchmakingProfile("alice", "爱丽丝", now.AddMinutes(10));

            Assert.True(profile.Rating > 1500);
            Assert.Equal(RankedStore.PlacementRequired, profile.PlacementGames);
            Assert.Equal(RankedStore.NewWorldRankPoints, profile.RankPoints);
            Assert.Equal(RankedStore.PirateFaction, profile.Faction);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

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

            Assert.True(store.TryRefreshLeaderboardSnapshot(now.AddMinutes(19)));
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

    [Theory]
    [InlineData(1499, 0)]
    [InlineData(1500, 20)]
    [InlineData(2999, 20)]
    [InlineData(3000, 40)]
    [InlineData(5999, 40)]
    [InlineData(6000, 75)]
    [InlineData(9999, 75)]
    [InlineData(10000, 125)]
    [InlineData(19999, 125)]
    [InlineData(20000, 250)]
    public void 排位结算_终结三连胜时按败方赛前悬赏档位发放一次赏金(
        int defeatedRankPoints,
        int expectedBounty)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-streak-bounty-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, $"streak-bounty-{defeatedRankPoints}");

            // 先由爱丽丝获胜清空鲍勃在定级赛末尾形成的连胜，再让鲍勃取得三连胜。
            Assert.NotNull(store.RecordMatch($"streak-bounty-reset-{defeatedRankPoints}", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
            for (var streak = 1; streak <= 3; streak++)
                Assert.NotNull(store.RecordMatch($"streak-bounty-build-{defeatedRankPoints}-{streak}", now.AddMinutes(10 + streak),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1));

            SetRankPoints(path, ("爱丽丝", defeatedRankPoints), ("鲍勃", defeatedRankPoints));
            var result = store.RecordMatch($"streak-bounty-ended-{defeatedRankPoints}", now.AddMinutes(20),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(result);
            Assert.Equal(expectedBounty, result!.Player0.WinStreakEndedBounty);
            Assert.Equal(expectedBounty > 0 ? 3 : 0, result.Player0.EndedWinStreak);
            Assert.Equal(0, result.Player1.WinStreakEndedBounty);
            Assert.Equal(0, result.Player1.EndedWinStreak);
            Assert.Equal(result.Player0.BaseRankPointDelta
                + result.Player0.StreakAdjustment
                + result.Player0.RankDifferenceAdjustment
                + expectedBounty,
                result.Player0.RankPointDelta);

            var wireJson = System.Text.Json.JsonSerializer.Serialize(RankWire.Settlement(result.Player0));
            Assert.Contains($"\"winStreakEndedBounty\":{expectedBounty}", wireJson);
            Assert.Contains($"\"endedWinStreak\":{(expectedBounty > 0 ? 3 : 0)}", wireJson);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_不足三连胜时不发放终结赏金()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-streak-bounty-threshold-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 22, 13, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, "streak-bounty-threshold");
            Assert.NotNull(store.RecordMatch("streak-bounty-threshold-reset", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
            for (var streak = 1; streak <= 2; streak++)
                Assert.NotNull(store.RecordMatch($"streak-bounty-threshold-build-{streak}", now.AddMinutes(10 + streak),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1));

            SetRankPoints(path, ("爱丽丝", 6000), ("鲍勃", 6000));
            var result = store.RecordMatch("streak-bounty-threshold-ended", now.AddMinutes(20),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(result);
            Assert.Equal(0, result!.Player0.WinStreakEndedBounty);
            Assert.Equal(0, result.Player0.EndedWinStreak);
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
    [InlineData(100, 1, 0)]
    [InlineData(299, 2, 0)]
    [InlineData(300, 3, 0)]
    [InlineData(499, 4, 0)]
    [InlineData(500, 5, 0)]
    public void 排位结算_低分方每百分差修正一分且高分方不受分差修正(
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
            Assert.Equal(0, result.Player1.RankDifferenceAdjustment);
            Assert.Equal(-30,
                result.Player1.RankPointDelta - result.Player1.RankProtectionAdjustment);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Theory]
    [InlineData(1499, 20, 10, 5)]
    [InlineData(1500, 40, 20, 10)]
    [InlineData(2999, 40, 20, 10)]
    [InlineData(3000, 80, 40, 20)]
    [InlineData(5999, 80, 40, 20)]
    [InlineData(6000, 150, 75, 38)]
    [InlineData(9999, 150, 75, 38)]
    [InlineData(10000, 250, 125, 63)]
    [InlineData(19999, 250, 125, 63)]
    [InlineData(20000, 500, 250, 126)]
    public void 排位结算_各悬赏档位基础分及连续胜负上限正确(
        int rankPoints,
        int baseDelta,
        int winStreakCap,
        int lossStreakCap)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 14, 30, 0, DateTimeKind.Utc);

            CompletePlacements(store, now, $"bounty-streak-{rankPoints}");
            SetRankPoints(path, ("爱丽丝", rankPoints), ("鲍勃", rankPoints));

            for (var streak = 1; streak <= winStreakCap + 2; streak++)
            {
                SetRankPoints(path, ("爱丽丝", rankPoints), ("鲍勃", rankPoints));
                var result = store.RecordMatch($"bounty-streak-{rankPoints}-{streak}", now.AddMinutes(10 + streak),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);
                var winAdjustment = Math.Min(streak - 1, winStreakCap);
                var lossAdjustment = Math.Min(streak - 1, lossStreakCap);

                Assert.NotNull(result);
                Assert.Equal(baseDelta + winAdjustment, result!.Player0.RankPointDelta);
                Assert.Equal(-baseDelta + lossAdjustment,
                    result.Player1.RankPointDelta - result.Player1.RankProtectionAdjustment);
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

    [Theory]
    [InlineData(900, 1400, 20, 20, 5)]
    [InlineData(1500, 2500, 40, 40, 10)]
    [InlineData(3000, 5000, 80, 80, 20)]
    [InlineData(6000, 9800, 150, 150, 38)]
    [InlineData(10000, 16300, 250, 250, 63)]
    [InlineData(19999, 26299, 250, 500, 63)]
    [InlineData(20000, 32600, 500, 500, 126)]
    public void 排位结算_各悬赏档位低分方修正上限正确且高分方维持基础变化(
        int lowRankPoints,
        int highRankPoints,
        int lowBaseDelta,
        int highBaseDelta,
        int differenceCap)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, $"bounty-gap-{lowRankPoints}");
            SetRankPoints(path, ("爱丽丝", lowRankPoints), ("鲍勃", highRankPoints));

            var lowWins = store.RecordMatch($"bounty-gap-low-win-{lowRankPoints}", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(lowWins);
            Assert.Equal(differenceCap, lowWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(0, lowWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(lowBaseDelta + differenceCap, lowWins.Player0.RankPointDelta);
            Assert.Equal(-highBaseDelta, lowWins.Player1.RankPointDelta);

            SetRankPoints(path, ("爱丽丝", lowRankPoints), ("鲍勃", highRankPoints));
            var highWins = store.RecordMatch($"bounty-gap-high-win-{lowRankPoints}", now.AddMinutes(11),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1);

            Assert.NotNull(highWins);
            Assert.Equal(differenceCap, highWins!.Player0.RankDifferenceAdjustment);
            Assert.Equal(0, highWins.Player1.RankDifferenceAdjustment);
            Assert.Equal(-lowBaseDelta + differenceCap,
                highWins.Player0.RankPointDelta - highWins.Player0.RankProtectionAdjustment);
            Assert.Equal(highBaseDelta, highWins.Player1.RankPointDelta);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Theory]
    [InlineData(RankedStore.NewWorldRankPoints, 40)]
    [InlineData(RankedStore.ThreeHundredMillionBountyRankPoints, 80)]
    [InlineData(RankedStore.SixHundredMillionBountyRankPoints, 150)]
    [InlineData(RankedStore.TenBillionBountyRankPoints, 250)]
    [InlineData(RankedStore.TwoBillionBountyRankPoints, 500)]
    public void 排位结算_达到基础分变化档位后永久保底(int protectionFloor, int baseDelta)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-floor-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
            CompletePlacements(store, now, $"bounty-floor-{protectionFloor}");
            SetRankPoints(path,
                ("爱丽丝", protectionFloor + 1),
                ("鲍勃", protectionFloor + 1));

            // 重新打开存储后仍应从持久化的历史最高悬赏恢复永久保底线。
            var restartedStore = new RankedStore(path);
            var result = restartedStore.RecordMatch($"bounty-floor-loss-{protectionFloor}", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);

            Assert.NotNull(result);
            Assert.Equal(-baseDelta, result!.Player1.BaseRankPointDelta);
            Assert.Equal(protectionFloor + 1, result.Player1.RankPointsBefore);
            Assert.Equal(protectionFloor, result.Player1.RankPointsAfter);
            Assert.Equal(-1, result.Player1.RankPointDelta);
            Assert.Equal(baseDelta - 1, result.Player1.RankProtectionAdjustment);
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
            for (var i = 0; i < LeaderChampionStore.LowVolumeMinimumChampionGames; i++)
            {
                Assert.True(championStore.RecordMatch(new LeaderMatchResult(
                    $"champion-{i}", now.AddDays(-(i % LeaderChampionStore.MinimumActiveDays)), MatchKind.Ranked,
                    "alice", $"opponent-{i}", "OP16-001", "OP01-001", 0, 0, 8, "胜利")));
            }

            var rankedStore = new RankedStore(rankedPath, championStore);
            Assert.NotNull(rankedStore.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            Assert.NotNull(rankedStore.SelectFaction("bob", "鲍勃", RankedStore.MarineFaction, now));
            CompletePlacements(rankedStore, now, "champion-rank");
            Assert.True(rankedStore.TryRefreshLeaderboardSnapshot(now.AddMinutes(19)));
            var snapshot = rankedStore.GetSnapshot("alice", "爱丽丝", now.AddMinutes(20));
            var item = Assert.Single(snapshot.Leaderboard,
                entry => entry.DisplayName == "爱丽丝");

            Assert.Equal(new[] { "OP16-001" }, snapshot.Profile.ChampionLeaderNumbers);
            Assert.Equal(new[] { "OP16-001" }, item.ChampionLeaderNumbers);
            var profileWireJson = System.Text.Json.JsonSerializer.Serialize(RankWire.Profile(snapshot.Profile));
            var wireJson = System.Text.Json.JsonSerializer.Serialize(RankWire.Leaderboard(new[] { item }));
            Assert.Contains("\"championLeaderNumbers\":[\"OP16-001\"]", profileWireJson);
            Assert.Contains("\"championLeaderNumbers\":[\"OP16-001\"]", wireJson);
            Assert.DoesNotContain("championWinRate", wireJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void 排位榜_返回前一百并额外包含当前玩家的真实名次()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-top100-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
            for (var index = 1; index <= 101; index++)
                Assert.NotNull(store.SelectFaction($"player-{index}", $"玩家{index:D3}", RankedStore.PirateFaction, now));

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                for (var index = 1; index <= 101; index++)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = """
                        UPDATE rank_profiles
                        SET placement_games=$placements, games=10, wins=5,
                            rank_points=$points, highest_rank_points=$points
                        WHERE display_name=$name;
                        """;
                    command.Parameters.AddWithValue("$placements", RankedStore.PlacementRequired);
                    command.Parameters.AddWithValue("$points", 10_000 - index);
                    command.Parameters.AddWithValue("$name", $"玩家{index:D3}");
                    Assert.Equal(1, command.ExecuteNonQuery());
                }
            }

            Assert.True(store.TryRefreshLeaderboardSnapshot(now.AddSeconds(30)));
            var snapshot = store.GetSnapshot("player-101", "玩家101", now.AddMinutes(1));

            Assert.Equal(101, snapshot.Leaderboard.Count);
            Assert.Equal(100, snapshot.Leaderboard.Count(item => item.Rank <= 100));
            var current = Assert.Single(snapshot.Leaderboard, item => item.IsCurrentPlayer);
            Assert.Equal(101, current.Rank);
            Assert.Equal("玩家101", current.DisplayName);
            Assert.False(snapshot.Leaderboard.Single(item => item.Rank == 1).IsCurrentPlayer);

            var wireJson = System.Text.Json.JsonSerializer.Serialize(RankWire.Leaderboard(snapshot.Leaderboard));
            Assert.Contains("\"isCurrentPlayer\":true", wireJson);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 聊天装饰目录_新增二十四条语录统一价格且仅接受开场与胜利槽()
    {
        Assert.Equal(36, ChatDecorationCatalog.All.Count);
        Assert.Equal(
            ChatDecorationCatalog.All.Count,
            ChatDecorationCatalog.All.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ChatDecorationSlots.All.Count,
            ChatDecorationSlots.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["opening", "victory"], ChatDecorationSlots.All);
        Assert.Null(ChatDecorationSlots.Normalize("greeting"));
        Assert.Null(ChatDecorationSlots.Normalize("threat"));
        Assert.Null(ChatDecorationSlots.Normalize("slot1"));
        Assert.Equal(ChatDecorationSlots.Opening, ChatDecorationSlots.Normalize(" OPENING "));
        Assert.Equal(ChatDecorationSlots.Victory, ChatDecorationSlots.Normalize(" victory "));

        var newQuotes = ChatDecorationCatalog.All
            .Where(item => item.AvailableForPurchase)
            .ToArray();
        Assert.Equal(24, newQuotes.Length);
        Assert.Equal(new[]
        {
            "我是要成为海贼王的男人!",
            "哟嚯嚯嚯嚯嚯嚯嚯！",
            "我们的相遇是命运的安排！",
            "我是来结束这场战争的。",
            "原来外面的世界里真的存在像你这样强大的男人。",
            "超越我吧。",
            "谢谢大家，直到最后都一直爱着我。",
            "我想活下去！",
            "SUPERRRRRRRRRR~。",
            "你要成为什么王？",
            "四皇的副手也值得一杀。",
            "这是我最小的刀了。",
            "福无双至，祸不单行。",
            "原谅女人谎言的，才是男人。",
            "人的梦想，是不会结束的。",
            "要是向力量屈服，那还算什么男人。",
            "想保护好的东西就好好保护到底。",
            "失去了就是失去了，想想你现在还剩下些什么。",
            "男人的决斗，不需要肤浅的掩护。",
            "唯有胜者才是正义！",
            "背后的伤口，是剑士的耻辱。",
            "存在本身，从来不是罪。",
            "D之一族终将再次掀起风暴",
            "看来你已经看见了比我更加遥远的未来",
        }, newQuotes.Select(item => item.Text));
        Assert.All(newQuotes, item => Assert.Equal(50_000_000, item.PriceBerries));
        var legacy = ChatDecorationCatalog.All.Where(item => !item.AvailableForPurchase).ToArray();
        Assert.Equal(12, legacy.Length);

        Assert.All(ChatDecorationCatalog.All, item =>
        {
            Assert.NotEmpty(item.Name);
            Assert.NotEmpty(item.Text);
            Assert.NotEmpty(item.StyleToken);
            Assert.Equal(ChatDecorationCatalog.PurchasePriceBerries, item.PriceBerries);
        });
    }

    [Fact]
    public void 聊天装饰交易_购买幂等装配持久且跨赛季仅重置钱包()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-persistence");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 500);

            var initial = store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now);
            Assert.Equal(50_000_000, initial.BalanceBerries);
            Assert.Equal(24, initial.Items.Count);
            Assert.All(initial.Items, item => Assert.True(item.AvailableForPurchase));
            Assert.DoesNotContain(initial.Items, item => item.Definition.Id == "greeting-straw-hat");
            var profileBeforePurchase = store.GetProfileSnapshot("alice", "爱丽丝", now);
            var stalePrice = Assert.Throws<ChatDecorationValidationException>(() => store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", "stale-price-0001",
                4_500_000, now.AddMilliseconds(250)));
            Assert.Contains("价格已更新", stalePrice.Message);
            Assert.Equal(50_000_000, store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now).BalanceBerries);
            var delisted = Assert.Throws<ChatDecorationValidationException>(() => store.PurchaseChatDecoration(
                "alice", "爱丽丝", "greeting-straw-hat", "legacy-buy-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddMilliseconds(500)));
            Assert.Contains("已下架", delisted.Message);
            Assert.Equal(50_000_000, store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now).BalanceBerries);

            var purchased = store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", "purchase-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(1));
            Assert.True(purchased.Succeeded);
            Assert.False(purchased.Replayed);
            Assert.Equal("purchased", purchased.Outcome);
            Assert.Equal(0, purchased.Snapshot.BalanceBerries);
            var profileAfterPurchase = store.GetProfileSnapshot("alice", "爱丽丝", now.AddSeconds(1));
            Assert.Equal(profileBeforePurchase.RankPoints, profileAfterPurchase.RankPoints);
            Assert.Equal(profileBeforePurchase.HighestRankPoints, profileAfterPurchase.HighestRankPoints);

            var replayed = store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", "purchase-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(2));
            Assert.True(replayed.Succeeded);
            Assert.True(replayed.Replayed);
            Assert.Equal(0, replayed.Snapshot.BalanceBerries);

            var duplicateItem = store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", "purchase-0002",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(3));
            Assert.True(duplicateItem.Succeeded);
            Assert.Equal("already_owned", duplicateItem.Outcome);
            Assert.Equal(0, duplicateItem.Snapshot.BalanceBerries);

            var insufficient = store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-binks-laugh", "purchase-0003",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(4));
            Assert.False(insufficient.Succeeded);
            Assert.Equal("insufficient_funds", insufficient.Outcome);
            Assert.Equal(0, insufficient.Snapshot.BalanceBerries);

            Assert.Throws<ChatDecorationValidationException>(() => store.EquipChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", ChatDecorationSlots.Opening,
                "purchase-0001", now.AddSeconds(5)));

            var equipped = store.EquipChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", ChatDecorationSlots.Victory,
                "equip-000001", now.AddSeconds(6));
            Assert.True(equipped.Succeeded);
            Assert.Equal("equipped", equipped.Outcome);
            Assert.Equal("quote-pirate-king-man",
                store.ResolveEquippedChatDecoration("alice", ChatDecorationSlots.Victory)?.Id);
            Assert.Null(store.ResolveEquippedChatDecoration("alice", ChatDecorationSlots.Opening));
            Assert.Null(store.ResolveEquippedChatDecoration("alice", "greeting"));

            var equippedAgain = store.EquipChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", ChatDecorationSlots.Opening,
                "equip-000002", now.AddSeconds(7));
            Assert.Equal("equipped", equippedAgain.Outcome);
            Assert.Equal(
                [ChatDecorationSlots.Opening, ChatDecorationSlots.Victory],
                equippedAgain.Snapshot.Items
                    .Single(item => item.Definition.Id == "quote-pirate-king-man")
                    .EquippedSlots);

            var invalidLegacySlot = Assert.Throws<ChatDecorationValidationException>(() =>
                store.EquipChatDecoration(
                    "alice", "爱丽丝", "quote-pirate-king-man", "greeting",
                    "equip-000003", now.AddSeconds(8)));
            Assert.Contains("开场台词或胜利宣言", invalidLegacySlot.Message);

            var restarted = new RankedStore(path);
            var afterRestart = restarted.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(1));
            Assert.Equal(0, afterRestart.BalanceBerries);
            Assert.True(afterRestart.Items.Single(item => item.Definition.Id == "quote-pirate-king-man").Owned);
            Assert.Equal(
                [ChatDecorationSlots.Opening, ChatDecorationSlots.Victory],
                afterRestart.Items.Single(item => item.Definition.Id == "quote-pirate-king-man").EquippedSlots);

            var nextSeason = restarted.GetChatDecorationExchangeSnapshot(
                "alice", "爱丽丝", new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc));
            Assert.NotEqual(afterRestart.SeasonId, nextSeason.SeasonId);
            Assert.Equal(0, nextSeason.BalanceBerries);
            Assert.True(nextSeason.Items.Single(item => item.Definition.Id == "quote-pirate-king-man").Owned);
            Assert.Equal(
                [ChatDecorationSlots.Opening, ChatDecorationSlots.Victory],
                nextSeason.Items.Single(item => item.Definition.Id == "quote-pirate-king-man").EquippedSlots);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包_只按赛季历史新峰值补发且失败后回升不重复铸币()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-season-peak");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            Assert.NotNull(store.SelectFaction("bob", "鲍勃", RankedStore.MarineFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 650, "鲍勃", 650);

            Assert.Equal(65_000_000,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now).BalanceBerries);
            Assert.Equal(65_000_000,
                store.GetChatDecorationExchangeSnapshot("bob", "鲍勃", now).BalanceBerries);

            var loss = Assert.IsType<RankedMatchSettlement>(store.RecordMatch(
                "peak-loss", now.AddMinutes(1),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1));
            Assert.Equal(630, loss.Player0.RankPointsAfter);
            Assert.Equal(65_000_000,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(1)).BalanceBerries);

            var regain = Assert.IsType<RankedMatchSettlement>(store.RecordMatch(
                "peak-regain", now.AddMinutes(2),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
            Assert.Equal(650, regain.Player0.RankPointsAfter);
            Assert.Equal(65_000_000,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(2)).BalanceBerries);

            var newPeak = Assert.IsType<RankedMatchSettlement>(store.RecordMatch(
                "peak-new-record", now.AddMinutes(3),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
            Assert.True(newPeak.Player0.RankPointsAfter > 650);
            var expectedNewPeakBalance = 65_000_000L
                + (long)(newPeak.Player0.RankPointsAfter - 650) * ChatDecorationCatalog.BerriesPerRankPoint;
            Assert.Equal(expectedNewPeakBalance,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(3)).BalanceBerries);
            Assert.Null(store.RecordMatch(
                "peak-new-record", now.AddMinutes(3),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
            Assert.Equal(expectedNewPeakBalance,
                new RankedStore(path)
                    .GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(4))
                    .BalanceBerries);

            var nextSeason = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(0,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", nextSeason).BalanceBerries);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包_首次读取按历史峰值建账且不以当前排位余额为准()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-first-read-peak");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 50);
            SetCurrentAndHighestRankPoints(path, "爱丽丝", current: 50, highest: 100);

            var firstRead = store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now);
            Assert.Equal(10_000_000, firstRead.BalanceBerries);
            Assert.Equal(50, store.GetProfileSnapshot("alice", "爱丽丝", now).RankPoints);

            SetCurrentAndHighestRankPoints(path, "爱丽丝", current: 100, highest: 100);
            Assert.Equal(10_000_000,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(1)).BalanceBerries);
            SetCurrentAndHighestRankPoints(path, "爱丽丝", current: 110, highest: 110);
            Assert.Equal(11_000_000,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(2)).BalanceBerries);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰目录_历史与装备所有权前置且购买响应立即按目录稳定重排()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-owned-first");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 1_500);
            var accountKey = ReadRankedAccountKey(path, "爱丽丝");
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO chat_decoration_ownership(account_key,decoration_id,acquired_at_utc)
                    VALUES($key,'greeting-sea-breeze',$at),($key,'quote-fated-meeting',$at);
                    INSERT INTO chat_decoration_equipment(account_key,slot,decoration_id,equipped_at_utc)
                    VALUES($key,'opening','greeting-sea-breeze',$at);
                    """;
                command.Parameters.AddWithValue("$key", accountKey);
                command.Parameters.AddWithValue("$at", now.ToString("O"));
                command.ExecuteNonQuery();
            }

            var before = store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now);
            Assert.Equal(
                ["greeting-sea-breeze", "quote-fated-meeting"],
                before.Items.TakeWhile(item => item.Owned).Select(item => item.Definition.Id));
            Assert.Equal(
                ["quote-pirate-king-man", "quote-binks-laugh"],
                before.Items.SkipWhile(item => item.Owned).Take(2).Select(item => item.Definition.Id));
            Assert.Contains(
                ChatDecorationSlots.Opening,
                before.Items[0].EquippedSlots);

            var purchased = store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-distant-future", "owned-first-buy-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(1));
            Assert.Equal(
                ["greeting-sea-breeze", "quote-fated-meeting", "quote-distant-future"],
                purchased.Snapshot.Items.TakeWhile(item => item.Owned).Select(item => item.Definition.Id));
            Assert.All(
                purchased.Snapshot.Items.SkipWhile(item => item.Owned),
                item => Assert.False(item.Owned));
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public async Task 聊天装饰钱包迁移_并发初始化原子换算并修复掉分损失与重复额度()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-migration");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var bootstrap = new RankedStore(path);
            Assert.NotNull(bootstrap.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            Assert.NotNull(bootstrap.SelectFaction("bob", "鲍勃", RankedStore.MarineFaction, now));
            Assert.NotNull(bootstrap.SelectFaction("charlie", "查理", RankedStore.GovernmentFaction, now));
            SetCurrentAndHighestRankPoints(path, "爱丽丝", current: 50, highest: 100);
            SetCurrentAndHighestRankPoints(path, "鲍勃", current: 100, highest: 100);
            SetCurrentAndHighestRankPoints(path, "查理", current: 100, highest: 100);
            var aliceKey = ReadRankedAccountKey(path, "爱丽丝");
            var bobKey = ReadRankedAccountKey(path, "鲍勃");
            var charlieKey = ReadRankedAccountKey(path, "查理");

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE rank_exchange_wallets;
                    DROP TABLE chat_decoration_operations;
                    CREATE TABLE rank_exchange_wallets (
                        season_id TEXT NOT NULL,
                        account_key TEXT NOT NULL,
                        balance_points INTEGER NOT NULL,
                        updated_at_utc TEXT NOT NULL,
                        PRIMARY KEY(season_id,account_key));
                    CREATE TABLE chat_decoration_operations (
                        account_key TEXT NOT NULL,
                        request_id TEXT NOT NULL,
                        action TEXT NOT NULL,
                        decoration_id TEXT NOT NULL,
                        slot TEXT NULL,
                        outcome TEXT NOT NULL,
                        price_points INTEGER NOT NULL,
                        balance_after INTEGER NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        PRIMARY KEY(account_key,request_id));
                    INSERT INTO rank_exchange_wallets VALUES('S1',$alice,5,$at);
                    INSERT INTO rank_exchange_wallets VALUES('S1',$bob,80,$at);
                    INSERT INTO rank_exchange_wallets VALUES('S1',$charlie,55,$at);
                    INSERT INTO chat_decoration_operations VALUES(
                        $alice,'legacy-alice-buy','purchase','quote-pirate-king-man',NULL,
                        'purchased',45,55,$at);
                    INSERT INTO chat_decoration_operations VALUES(
                        $bob,'legacy-bob-buy','purchase','quote-binks-laugh',NULL,
                        'purchased',45,55,$at);
                    INSERT INTO chat_decoration_operations VALUES(
                        $charlie,'legacy-charlie-before-reset','purchase','quote-fated-meeting',NULL,
                        'purchased',45,55,$beforeReset);
                    INSERT INTO chat_decoration_operations VALUES(
                        $charlie,'legacy-charlie-after-reset','purchase','quote-end-the-war',NULL,
                        'purchased',45,55,$afterReset);
                    UPDATE rank_factions SET selected_at_utc=$resetAt WHERE account_key=$charlie;
                    """;
                command.Parameters.AddWithValue("$alice", aliceKey);
                command.Parameters.AddWithValue("$bob", bobKey);
                command.Parameters.AddWithValue("$charlie", charlieKey);
                command.Parameters.AddWithValue("$at", now.AddMinutes(1).ToString("O"));
                command.Parameters.AddWithValue("$beforeReset", now.AddSeconds(30).ToString("O"));
                command.Parameters.AddWithValue("$resetAt", now.AddMinutes(1).ToString("O"));
                command.Parameters.AddWithValue("$afterReset", now.AddMinutes(1).AddSeconds(30).ToString("O"));
                command.ExecuteNonQuery();
            }

            const int migratorCount = 16;
            using var simultaneousStart = new Barrier(migratorCount);
            var migrators = Enumerable.Range(0, migratorCount)
                .Select(_ => new RankedStore(path))
                .ToArray();
            var initializationProbeGate = new object();
            var activeInitializers = 0;
            var maximumConcurrentInitializers = 0;
            foreach (var migrator in migrators)
            {
                migrator.DuringDatabaseInitializationForTesting = () =>
                {
                    lock (initializationProbeGate)
                    {
                        activeInitializers++;
                        maximumConcurrentInitializers = Math.Max(
                            maximumConcurrentInitializers,
                            activeInitializers);
                    }

                    Thread.Sleep(25);
                    lock (initializationProbeGate)
                        activeInitializers--;
                };
            }

            var migrationTasks = migrators.Select((store, index) =>
                Task.Factory.StartNew(
                    () =>
                    {
                        Assert.True(simultaneousStart.SignalAndWait(TimeSpan.FromSeconds(10)));
                        var alice = index % 2 == 0;
                        return store.GetChatDecorationExchangeSnapshot(
                            alice ? "alice" : "bob",
                            alice ? "爱丽丝" : "鲍勃",
                            now.AddMinutes(2));
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)).ToArray();
            var migratedSnapshots = await Task.WhenAll(migrationTasks);
            Assert.Equal(1, maximumConcurrentInitializers);
            Assert.All(migratedSnapshots, snapshot => Assert.Equal(5_500_000, snapshot.BalanceBerries));
            Assert.Equal(5_500_000,
                new RankedStore(path)
                    .GetChatDecorationExchangeSnapshot("charlie", "查理", now.AddMinutes(2))
                    .BalanceBerries);

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var columns = connection.CreateCommand();
                columns.CommandText = "SELECT group_concat(name,',') FROM pragma_table_info('rank_exchange_wallets');";
                var walletColumns = Assert.IsType<string>(columns.ExecuteScalar());
                Assert.Contains("balance_berries", walletColumns);
                Assert.Contains("credited_peak_rank_points", walletColumns);
                Assert.DoesNotContain("balance_points", walletColumns);

                using var legacyWallet = connection.CreateCommand();
                legacyWallet.CommandText = """
                    SELECT balance_points FROM rank_exchange_wallets_legacy_current_rp_v1
                    WHERE season_id='S1' AND account_key=$key;
                    """;
                legacyWallet.Parameters.AddWithValue("$key", bobKey);
                Assert.Equal(80L, (long)legacyWallet.ExecuteScalar()!);

                using var operation = connection.CreateCommand();
                operation.CommandText = """
                    SELECT price_berries,balance_after_berries
                    FROM chat_decoration_operations
                    WHERE account_key=$key AND request_id='legacy-alice-buy';
                    """;
                operation.Parameters.AddWithValue("$key", aliceKey);
                using var operationReader = operation.ExecuteReader();
                Assert.True(operationReader.Read());
                Assert.Equal(4_500_000, operationReader.GetInt64(0));
                Assert.Equal(5_500_000, operationReader.GetInt64(1));

                using var legacyOperation = connection.CreateCommand();
                legacyOperation.CommandText = """
                    SELECT price_points,balance_after
                    FROM chat_decoration_operations_legacy_current_rp_v1
                    WHERE account_key=$key AND request_id='legacy-alice-buy';
                    """;
                legacyOperation.Parameters.AddWithValue("$key", aliceKey);
                using var legacyOperationReader = legacyOperation.ExecuteReader();
                Assert.True(legacyOperationReader.Read());
                Assert.Equal(45, legacyOperationReader.GetInt64(0));
                Assert.Equal(55, legacyOperationReader.GetInt64(1));

                using var audit = connection.CreateCommand();
                audit.CommandText = """
                    SELECT legacy_balance_berries,reconstructed_balance_berries,migrated_balance_berries,
                           credited_peak_rank_points,migration_rule,excluded_purchase_berries
                    FROM rank_exchange_wallet_migration_audit
                    WHERE season_id='S1' AND account_key=$key;
                    """;
                audit.Parameters.AddWithValue("$key", aliceKey);
                using var auditReader = audit.ExecuteReader();
                Assert.True(auditReader.Read());
                Assert.Equal(500_000, auditReader.GetInt64(0));
                Assert.Equal(5_500_000, auditReader.GetInt64(1));
                Assert.Equal(5_500_000, auditReader.GetInt64(2));
                Assert.Equal(100, auditReader.GetInt64(3));
                Assert.Equal("profile_peak_minus_post_faction_selection_purchases", auditReader.GetString(4));
                Assert.Equal(0, auditReader.GetInt64(5));

                using var charlieAudit = connection.CreateCommand();
                charlieAudit.CommandText = """
                    SELECT counted_purchase_berries,excluded_purchase_berries,
                           reconstructed_balance_berries,migrated_balance_berries,
                           purchase_window_start_utc
                    FROM rank_exchange_wallet_migration_audit
                    WHERE season_id='S1' AND account_key=$key;
                    """;
                charlieAudit.Parameters.AddWithValue("$key", charlieKey);
                using var charlieAuditReader = charlieAudit.ExecuteReader();
                Assert.True(charlieAuditReader.Read());
                Assert.Equal(4_500_000, charlieAuditReader.GetInt64(0));
                Assert.Equal(4_500_000, charlieAuditReader.GetInt64(1));
                Assert.Equal(5_500_000, charlieAuditReader.GetInt64(2));
                Assert.Equal(5_500_000, charlieAuditReader.GetInt64(3));
                Assert.Equal(now.AddMinutes(1).ToString("O"), charlieAuditReader.GetString(4));
            }

            SetCurrentAndHighestRankPoints(path, "爱丽丝", current: 100, highest: 100);
            Assert.Equal(5_500_000,
                new RankedStore(path)
                    .GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(3))
                    .BalanceBerries);
            SetCurrentAndHighestRankPoints(path, "爱丽丝", current: 110, highest: 110);
            Assert.Equal(6_500_000,
                new RankedStore(path)
                    .GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(4))
                    .BalanceBerries);
            using var verifyAudit = new SqliteConnection($"Data Source={path}");
            verifyAudit.Open();
            using var auditCount = verifyAudit.CreateCommand();
            auditCount.CommandText = "SELECT COUNT(*) FROM rank_exchange_wallet_migration_audit;";
            Assert.Equal(3L, (long)auditCount.ExecuteScalar()!);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包迁移_遇到负数旧余额会完整回滚而不留下半迁移结构()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-invalid-migration");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var bootstrap = new RankedStore(path);
            Assert.NotNull(bootstrap.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            var accountKey = ReadRankedAccountKey(path, "爱丽丝");
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE rank_exchange_wallets;
                    DROP TABLE chat_decoration_operations;
                    CREATE TABLE rank_exchange_wallets (
                        season_id TEXT NOT NULL,account_key TEXT NOT NULL,balance_points INTEGER NOT NULL,
                        updated_at_utc TEXT NOT NULL,PRIMARY KEY(season_id,account_key));
                    CREATE TABLE chat_decoration_operations (
                        account_key TEXT NOT NULL,request_id TEXT NOT NULL,action TEXT NOT NULL,
                        decoration_id TEXT NOT NULL,slot TEXT NULL,outcome TEXT NOT NULL,
                        price_points INTEGER NOT NULL,balance_after INTEGER NOT NULL,created_at_utc TEXT NOT NULL,
                        PRIMARY KEY(account_key,request_id));
                    INSERT INTO rank_exchange_wallets VALUES('S1',$key,-1,$at);
                    """;
                command.Parameters.AddWithValue("$key", accountKey);
                command.Parameters.AddWithValue("$at", now.ToString("O"));
                command.ExecuteNonQuery();
            }

            var error = Assert.Throws<InvalidOperationException>(() => new RankedStore(path).Initialize());
            Assert.Contains("旧版交易所钱包余额", error.Message);
            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            using var columns = verify.CreateCommand();
            columns.CommandText = "SELECT group_concat(name,',') FROM pragma_table_info('rank_exchange_wallets');";
            var names = Assert.IsType<string>(columns.ExecuteScalar());
            Assert.Contains("balance_points", names);
            Assert.DoesNotContain("balance_berries", names);
            using var value = verify.CreateCommand();
            value.CommandText = "SELECT balance_points FROM rank_exchange_wallets;";
            Assert.Equal(-1L, (long)value.ExecuteScalar()!);
            using var audit = verify.CreateCommand();
            audit.CommandText = "SELECT COUNT(*) FROM rank_exchange_wallet_migration_audit;";
            Assert.Equal(0L, (long)audit.ExecuteScalar()!);
            using var backupTable = verify.CreateCommand();
            backupTable.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name='rank_exchange_wallets_legacy_current_rp_v1';
                """;
            Assert.Equal(0L, (long)backupTable.ExecuteScalar()!);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包迁移_孤儿钱包缺少权威峰值时失败关闭并完整回滚()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-orphan-migration");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            new RankedStore(path).Initialize();
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE rank_exchange_wallets;
                    DROP TABLE chat_decoration_operations;
                    CREATE TABLE rank_exchange_wallets (
                        season_id TEXT NOT NULL,account_key TEXT NOT NULL,balance_points INTEGER NOT NULL,
                        updated_at_utc TEXT NOT NULL,PRIMARY KEY(season_id,account_key));
                    CREATE TABLE chat_decoration_operations (
                        account_key TEXT NOT NULL,request_id TEXT NOT NULL,action TEXT NOT NULL,
                        decoration_id TEXT NOT NULL,slot TEXT NULL,outcome TEXT NOT NULL,
                        price_points INTEGER NOT NULL,balance_after INTEGER NOT NULL,created_at_utc TEXT NOT NULL,
                        PRIMARY KEY(account_key,request_id));
                    INSERT INTO rank_exchange_wallets VALUES('S1','orphan-account-key',25,$at);
                    """;
                command.Parameters.AddWithValue("$at", now.ToString("O"));
                command.ExecuteNonQuery();
            }

            var error = Assert.Throws<InvalidOperationException>(() => new RankedStore(path).Initialize());
            Assert.Contains("缺少同赛季排位资料", error.Message);
            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            using var columns = verify.CreateCommand();
            columns.CommandText = "SELECT group_concat(name,',') FROM pragma_table_info('rank_exchange_wallets');";
            var names = Assert.IsType<string>(columns.ExecuteScalar());
            Assert.Contains("balance_points", names);
            Assert.DoesNotContain("balance_berries", names);
            using var audit = verify.CreateCommand();
            audit.CommandText = "SELECT COUNT(*) FROM rank_exchange_wallet_migration_audit;";
            Assert.Equal(0L, (long)audit.ExecuteScalar()!);
            using var backupTable = verify.CreateCommand();
            backupTable.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name='rank_exchange_wallets_legacy_current_rp_v1';
                """;
            Assert.Equal(0L, (long)backupTable.ExecuteScalar()!);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包迁移_核心表新旧版本混合时失败关闭()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-mixed-schema");
        try
        {
            new RankedStore(path).Initialize();
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE chat_decoration_operations;
                    CREATE TABLE chat_decoration_operations (
                        account_key TEXT NOT NULL,request_id TEXT NOT NULL,action TEXT NOT NULL,
                        decoration_id TEXT NOT NULL,slot TEXT NULL,outcome TEXT NOT NULL,
                        price_points INTEGER NOT NULL,balance_after INTEGER NOT NULL,created_at_utc TEXT NOT NULL,
                        PRIMARY KEY(account_key,request_id));
                    """;
                command.ExecuteNonQuery();
            }

            var error = Assert.Throws<InvalidOperationException>(() => new RankedStore(path).Initialize());
            Assert.Contains("版本不一致", error.Message);
            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            using var walletColumns = verify.CreateCommand();
            walletColumns.CommandText = "SELECT group_concat(name,',') FROM pragma_table_info('rank_exchange_wallets');";
            Assert.Contains("balance_berries", Assert.IsType<string>(walletColumns.ExecuteScalar()));
            using var operationColumns = verify.CreateCommand();
            operationColumns.CommandText = "SELECT group_concat(name,',') FROM pragma_table_info('chat_decoration_operations');";
            var operationNames = Assert.IsType<string>(operationColumns.ExecuteScalar());
            Assert.Contains("price_points", operationNames);
            Assert.DoesNotContain("price_berries", operationNames);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包迁移_成功购买流水缺少钱包时拒绝重复补发并完整回滚()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-orphan-purchase");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var bootstrap = new RankedStore(path);
            Assert.NotNull(bootstrap.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            var accountKey = ReadRankedAccountKey(path, "爱丽丝");
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE rank_exchange_wallets;
                    DROP TABLE chat_decoration_operations;
                    CREATE TABLE rank_exchange_wallets (
                        season_id TEXT NOT NULL,account_key TEXT NOT NULL,balance_points INTEGER NOT NULL,
                        updated_at_utc TEXT NOT NULL,PRIMARY KEY(season_id,account_key));
                    CREATE TABLE chat_decoration_operations (
                        account_key TEXT NOT NULL,request_id TEXT NOT NULL,action TEXT NOT NULL,
                        decoration_id TEXT NOT NULL,slot TEXT NULL,outcome TEXT NOT NULL,
                        price_points INTEGER NOT NULL,balance_after INTEGER NOT NULL,created_at_utc TEXT NOT NULL,
                        PRIMARY KEY(account_key,request_id));
                    INSERT INTO chat_decoration_operations VALUES(
                        $key,'orphan-purchase','purchase','quote-pirate-king-man',NULL,
                        'purchased',45,55,$at);
                    """;
                command.Parameters.AddWithValue("$key", accountKey);
                command.Parameters.AddWithValue("$at", now.AddMinutes(1).ToString("O"));
                command.ExecuteNonQuery();
            }

            var error = Assert.Throws<InvalidOperationException>(() => new RankedStore(path).Initialize());
            Assert.Contains("成功购买流水缺少同赛季钱包", error.Message);
            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            using var walletColumns = verify.CreateCommand();
            walletColumns.CommandText = "SELECT group_concat(name,',') FROM pragma_table_info('rank_exchange_wallets');";
            var walletNames = Assert.IsType<string>(walletColumns.ExecuteScalar());
            Assert.Contains("balance_points", walletNames);
            Assert.DoesNotContain("balance_berries", walletNames);
            using var operation = verify.CreateCommand();
            operation.CommandText = "SELECT price_points,balance_after FROM chat_decoration_operations WHERE request_id='orphan-purchase';";
            using var reader = operation.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(45, reader.GetInt64(0));
            Assert.Equal(55, reader.GetInt64(1));
            using var staging = verify.CreateCommand();
            staging.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='rank_exchange_wallets_v2';";
            Assert.Equal(0L, (long)staging.ExecuteScalar()!);
            using var audit = verify.CreateCommand();
            audit.CommandText = "SELECT COUNT(*) FROM rank_exchange_wallet_migration_audit;";
            Assert.Equal(0L, (long)audit.ExecuteScalar()!);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包_负数受数据库约束且峰值补发溢出时事务不改账()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-bounds");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 1);
            Assert.Equal(100_000,
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now).BalanceBerries);
            var accountKey = ReadRankedAccountKey(path, "爱丽丝");
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var negative = connection.CreateCommand();
                negative.CommandText = """
                    UPDATE rank_exchange_wallets SET balance_berries=-1
                    WHERE season_id='S1' AND account_key=$key;
                    """;
                negative.Parameters.AddWithValue("$key", accountKey);
                Assert.Throws<SqliteException>(() => negative.ExecuteNonQuery());

                using var boundary = connection.CreateCommand();
                boundary.CommandText = """
                    UPDATE rank_exchange_wallets
                    SET balance_berries=$max,credited_peak_rank_points=0
                    WHERE season_id='S1' AND account_key=$key;
                    """;
                boundary.Parameters.AddWithValue("$max", RankedStore.MaxChatDecorationWalletBerries);
                boundary.Parameters.AddWithValue("$key", accountKey);
                Assert.Equal(1, boundary.ExecuteNonQuery());
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddSeconds(1)));
            Assert.Contains("将溢出", error.Message);
            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            using var read = verify.CreateCommand();
            read.CommandText = """
                SELECT balance_berries,credited_peak_rank_points
                FROM rank_exchange_wallets WHERE season_id='S1' AND account_key=$key;
                """;
            read.Parameters.AddWithValue("$key", accountKey);
            using var reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(RankedStore.MaxChatDecorationWalletBerries, reader.GetInt64(0));
            Assert.Equal(0, reader.GetInt64(1));
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰钱包_受审计的定额运维事务只改独立额度不改排位资料()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wallet-admin-audit");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("shaka", "释迦", RankedStore.PirateFaction, now));
            SeedRankPointsAndResetWallet(path, "释迦", 750);
            Assert.Equal(75_000_000,
                store.GetChatDecorationExchangeSnapshot("shaka", "释迦", now).BalanceBerries);
            var profileBefore = store.GetProfileSnapshot("shaka", "释迦", now);

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction(deferred: false);
                using var guard = connection.CreateCommand();
                guard.Transaction = transaction;
                guard.CommandText = """
                    SELECT COUNT(*) FROM rank_profiles
                    WHERE season_id='S1' AND display_name='释迦';
                    """;
                Assert.Equal(1L, (long)guard.ExecuteScalar()!);

                using var adjust = connection.CreateCommand();
                adjust.Transaction = transaction;
                adjust.CommandText = """
                    INSERT INTO rank_exchange_wallet_admin_audit(
                        season_id,account_key,display_name,balance_before_berries,balance_after_berries,
                        credited_peak_before,credited_peak_after,rank_points_observed,
                        highest_rank_points_observed,reason,operator,created_at_utc)
                    SELECT p.season_id,p.account_key,p.display_name,w.balance_berries,1000000000,
                           w.credited_peak_rank_points,
                           MAX(COALESCE(w.credited_peak_rank_points,0),p.highest_rank_points),
                           p.rank_points,p.highest_rank_points,$reason,$operator,$at
                    FROM rank_profiles AS p
                    LEFT JOIN rank_exchange_wallets AS w
                      ON w.season_id=p.season_id AND w.account_key=p.account_key
                    WHERE p.season_id='S1' AND p.display_name='释迦';

                    INSERT INTO rank_exchange_wallets(
                        season_id,account_key,balance_berries,credited_peak_rank_points,updated_at_utc)
                    SELECT season_id,account_key,1000000000,highest_rank_points,$at
                    FROM rank_profiles
                    WHERE season_id='S1' AND display_name='释迦'
                    ON CONFLICT(season_id,account_key) DO UPDATE SET
                        balance_berries=excluded.balance_berries,
                        credited_peak_rank_points=MAX(
                            rank_exchange_wallets.credited_peak_rank_points,
                            excluded.credited_peak_rank_points),
                        updated_at_utc=excluded.updated_at_utc;
                    """;
                adjust.Parameters.AddWithValue("$reason", "测试服指定账号语录额度校准为 10 亿贝里");
                adjust.Parameters.AddWithValue("$operator", "unit-test");
                adjust.Parameters.AddWithValue("$at", now.AddMinutes(1).ToString("O"));
                Assert.Equal(2, adjust.ExecuteNonQuery());
                transaction.Commit();
            }

            var profileAfter = store.GetProfileSnapshot("shaka", "释迦", now.AddMinutes(2));
            Assert.Equal(profileBefore.SeasonId, profileAfter.SeasonId);
            Assert.Equal(profileBefore.PlacementGames, profileAfter.PlacementGames);
            Assert.Equal(profileBefore.RankPoints, profileAfter.RankPoints);
            Assert.Equal(profileBefore.HighestRankPoints, profileAfter.HighestRankPoints);
            Assert.Equal(profileBefore.Faction, profileAfter.Faction);
            Assert.Equal(profileBefore.Tier, profileAfter.Tier);
            Assert.Equal(profileBefore.Division, profileAfter.Division);
            Assert.Equal(profileBefore.Games, profileAfter.Games);
            Assert.Equal(profileBefore.Wins, profileAfter.Wins);
            Assert.Equal(profileBefore.Losses, profileAfter.Losses);
            Assert.Equal(1_000_000_000,
                store.GetChatDecorationExchangeSnapshot("shaka", "释迦", now.AddMinutes(2)).BalanceBerries);

            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            using var audit = verify.CreateCommand();
            audit.CommandText = """
                SELECT balance_before_berries,balance_after_berries,credited_peak_after,
                       rank_points_observed,highest_rank_points_observed,reason,operator
                FROM rank_exchange_wallet_admin_audit;
                """;
            using var reader = audit.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(75_000_000, reader.GetInt64(0));
            Assert.Equal(1_000_000_000, reader.GetInt64(1));
            Assert.Equal(750, reader.GetInt64(2));
            Assert.Equal(750, reader.GetInt64(3));
            Assert.Equal(750, reader.GetInt64(4));
            Assert.Contains("10 亿贝里", reader.GetString(5));
            Assert.Equal("unit-test", reader.GetString(6));
            Assert.False(reader.Read());
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public async Task 聊天装饰迁移_旧六槽与四编号槽原子收敛且不损失任何所有权()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-legacy-slots");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var legacyEquipment = new[]
        {
            (Slot: "greeting", DecorationId: "greeting-straw-hat"),
            (Slot: "praise", DecorationId: "praise-fine-play"),
            (Slot: "thanks", DecorationId: "thanks-crewmate"),
            (Slot: "surprise", DecorationId: "surprise-seaquake"),
            (Slot: "mistake", DecorationId: "mistake-compass"),
            (Slot: "threat", DecorationId: "threat-cannon"),
        };
        try
        {
            var oldStore = new RankedStore(path);
            Assert.NotNull(oldStore.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();
                string accountKey;
                using (var profile = connection.CreateCommand())
                {
                    profile.Transaction = transaction;
                    profile.CommandText = "SELECT account_key FROM rank_profiles WHERE display_name='爱丽丝' LIMIT 1;";
                    accountKey = Assert.IsType<string>(profile.ExecuteScalar());
                }

                foreach (var (slot, decorationId) in legacyEquipment)
                {
                    using var ownership = connection.CreateCommand();
                    ownership.Transaction = transaction;
                    ownership.CommandText = """
                        INSERT INTO chat_decoration_ownership(account_key,decoration_id,acquired_at_utc)
                        VALUES($key,$decoration,$at);
                        """;
                    ownership.Parameters.AddWithValue("$key", accountKey);
                    ownership.Parameters.AddWithValue("$decoration", decorationId);
                    ownership.Parameters.AddWithValue("$at", now.ToString("O"));
                    Assert.Equal(1, ownership.ExecuteNonQuery());

                    using var equipment = connection.CreateCommand();
                    equipment.Transaction = transaction;
                    equipment.CommandText = """
                        INSERT INTO chat_decoration_equipment(account_key,slot,decoration_id,equipped_at_utc)
                        VALUES($key,$slot,$decoration,$at);
                        """;
                    equipment.Parameters.AddWithValue("$key", accountKey);
                    equipment.Parameters.AddWithValue("$slot", slot);
                    equipment.Parameters.AddWithValue("$decoration", decorationId);
                    equipment.Parameters.AddWithValue("$at", now.ToString("O"));
                    Assert.Equal(1, equipment.ExecuteNonQuery());
                }

                for (var index = 0; index < 4; index++)
                {
                    using var numbered = connection.CreateCommand();
                    numbered.Transaction = transaction;
                    numbered.CommandText = """
                        INSERT INTO chat_decoration_equipment(account_key,slot,decoration_id,equipped_at_utc)
                        VALUES($key,$slot,$decoration,$at);
                        """;
                    numbered.Parameters.AddWithValue("$key", accountKey);
                    numbered.Parameters.AddWithValue("$slot", $"slot{index + 1}");
                    numbered.Parameters.AddWithValue("$decoration", legacyEquipment[index + 1].DecorationId);
                    numbered.Parameters.AddWithValue("$at", now.AddSeconds(index + 1).ToString("O"));
                    Assert.Equal(1, numbered.ExecuteNonQuery());
                }

                using (var unknownOwnership = connection.CreateCommand())
                {
                    unknownOwnership.Transaction = transaction;
                    unknownOwnership.CommandText = """
                        INSERT INTO chat_decoration_ownership(account_key,decoration_id,acquired_at_utc)
                        VALUES($key,'retired-owned-phrase',$at);
                        """;
                    unknownOwnership.Parameters.AddWithValue("$key", accountKey);
                    unknownOwnership.Parameters.AddWithValue("$at", now.ToString("O"));
                    Assert.Equal(1, unknownOwnership.ExecuteNonQuery());
                }
                using (var unmappableEquipment = connection.CreateCommand())
                {
                    unmappableEquipment.Transaction = transaction;
                    unmappableEquipment.CommandText = """
                        INSERT INTO chat_decoration_equipment(account_key,slot,decoration_id,equipped_at_utc)
                        VALUES($key,'victory','retired-owned-phrase',$at);
                        """;
                    unmappableEquipment.Parameters.AddWithValue("$key", accountKey);
                    unmappableEquipment.Parameters.AddWithValue("$at", now.ToString("O"));
                    Assert.Equal(1, unmappableEquipment.ExecuteNonQuery());
                }
                transaction.Commit();
            }

            // 多进程/多实例同时启动时，SQLite IMMEDIATE 事务必须串行收敛到同一结果。
            var concurrentMigrations = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() => new RankedStore(path).Initialize()))
                .ToArray();
            await Task.WhenAll(concurrentMigrations);
            var migratedStore = new RankedStore(path);
            var migrated = migratedStore.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(1));
            Assert.Equal(30, migrated.Items.Count);
            Assert.Equal(6, migrated.Items.Count(item => item.Owned));
            Assert.All(
                migrated.Items.Where(item => item.Definition.Id is "greeting-straw-hat" or "praise-fine-play"
                    or "thanks-crewmate" or "surprise-seaquake" or "mistake-compass" or "threat-cannon"),
                item =>
                {
                    Assert.True(item.Owned);
                    Assert.False(item.AvailableForPurchase);
                });
            Assert.Equal("greeting-straw-hat",
                migratedStore.ResolveEquippedChatDecoration("alice", ChatDecorationSlots.Opening)?.Id);
            Assert.Equal("threat-cannon",
                migratedStore.ResolveEquippedChatDecoration("alice", ChatDecorationSlots.Victory)?.Id);
            Assert.Empty(migrated.Items.Single(item => item.Definition.Id == "praise-fine-play").EquippedSlots);
            Assert.Empty(migrated.Items.Single(item => item.Definition.Id == "thanks-crewmate").EquippedSlots);
            Assert.Empty(migrated.Items.Single(item => item.Definition.Id == "surprise-seaquake").EquippedSlots);
            Assert.Empty(migrated.Items.Single(item => item.Definition.Id == "mistake-compass").EquippedSlots);

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var legacyCount = connection.CreateCommand();
                legacyCount.CommandText = """
                    SELECT COUNT(*) FROM chat_decoration_equipment
                    WHERE slot IN ('greeting','praise','thanks','surprise','mistake','threat','slot1','slot2','slot3','slot4');
                    """;
                Assert.Equal(0L, (long)legacyCount.ExecuteScalar()!);
                using var ownershipCount = connection.CreateCommand();
                ownershipCount.CommandText = "SELECT COUNT(*) FROM chat_decoration_ownership;";
                Assert.Equal(7L, (long)ownershipCount.ExecuteScalar()!);
                using var equipmentCount = connection.CreateCommand();
                equipmentCount.CommandText = "SELECT COUNT(*) FROM chat_decoration_equipment;";
                Assert.Equal(2L, (long)equipmentCount.ExecuteScalar()!);
            }

            var changed = migratedStore.EquipChatDecoration(
                "alice", "爱丽丝", "threat-cannon", ChatDecorationSlots.Opening,
                "migrated-equip-0001", now.AddMinutes(2));
            Assert.Equal("equipped", changed.Outcome);
            var afterAnotherRestart = new RankedStore(path);
            Assert.Equal("threat-cannon",
                afterAnotherRestart.ResolveEquippedChatDecoration("alice", ChatDecorationSlots.Opening)?.Id);
            Assert.Equal("threat-cannon",
                afterAnotherRestart.ResolveEquippedChatDecoration("alice", ChatDecorationSlots.Victory)?.Id);
            var lockedLoadout = afterAnotherRestart.ResolveEquippedChatDecorationLoadout("alice");
            Assert.Equal("threat-cannon", lockedLoadout.Opening?.Id);
            Assert.Equal("threat-cannon", lockedLoadout.Victory?.Id);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰交易_提交前故障会整体回滚且同请求可安全重试()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-rollback");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var store = new RankedStore(path);
            Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 1_000);
            Assert.Equal(100_000_000, store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now).BalanceBerries);

            store.BeforeChatDecorationMutationCommitForTesting = () =>
                throw new InvalidOperationException("模拟进程在提交前失败");
            Assert.Throws<InvalidOperationException>(() => store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-fated-meeting", "rollback-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(1)));
            store.BeforeChatDecorationMutationCommitForTesting = null;

            var afterFailure = store.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddSeconds(2));
            Assert.Equal(100_000_000, afterFailure.BalanceBerries);
            Assert.False(afterFailure.Items.Single(item => item.Definition.Id == "quote-fated-meeting").Owned);

            var retried = store.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-fated-meeting", "rollback-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(3));
            Assert.True(retried.Succeeded);
            Assert.False(retried.Replayed);
            Assert.Equal(50_000_000, retried.Snapshot.BalanceBerries);
            Assert.True(retried.Snapshot.Items.Single(item => item.Definition.Id == "quote-fated-meeting").Owned);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public async Task 聊天装饰交易_与另一实例的排位结算竞争时不会丢失余额更新()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-settlement-race");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        using var purchaseEntered = new ManualResetEventSlim(false);
        using var allowPurchaseCommit = new ManualResetEventSlim(false);
        using var settlementStarted = new ManualResetEventSlim(false);
        try
        {
            var purchaseStore = new RankedStore(path);
            var settlementStore = new RankedStore(path);
            Assert.NotNull(purchaseStore.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            Assert.NotNull(purchaseStore.SelectFaction("bob", "鲍勃", RankedStore.MarineFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 1_000, "鲍勃", 1_000);
            Assert.Equal(100_000_000,
                purchaseStore.GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now).BalanceBerries);
            settlementStore.Initialize();

            purchaseStore.BeforeChatDecorationMutationCommitForTesting = () =>
            {
                purchaseEntered.Set();
                if (!allowPurchaseCommit.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("等待并发结算进入竞争窗口超时");
            };

            var purchaseTask = Task.Run(() => purchaseStore.PurchaseChatDecoration(
                "alice", "爱丽丝", "quote-pirate-king-man", "race-buy-0001",
                ChatDecorationCatalog.PurchasePriceBerries, now.AddSeconds(1)));
            Assert.True(purchaseEntered.Wait(TimeSpan.FromSeconds(5)));

            var settlementTask = Task.Run(() =>
            {
                settlementStarted.Set();
                return settlementStore.RecordMatch(
                    "chat-decoration-race-match", now.AddSeconds(2),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);
            });
            Assert.True(settlementStarted.Wait(TimeSpan.FromSeconds(5)));
            var completedDuringPurchase = await Task.WhenAny(
                settlementTask,
                Task.Delay(TimeSpan.FromMilliseconds(150)));
            Assert.NotSame(settlementTask, completedDuringPurchase);

            allowPurchaseCommit.Set();
            var purchased = await purchaseTask;
            var settlement = await settlementTask;
            purchaseStore.BeforeChatDecorationMutationCommitForTesting = null;

            Assert.True(purchased.Succeeded);
            Assert.NotNull(settlement);
            var newPeakDelta = Math.Max(0, settlement!.Player0.RankPointsAfter - 1_000);
            var expectedBalance = 50_000_000L
                + (long)newPeakDelta * ChatDecorationCatalog.BerriesPerRankPoint;
            var final = new RankedStore(path)
                .GetChatDecorationExchangeSnapshot("alice", "爱丽丝", now.AddMinutes(1));
            Assert.Equal(expectedBalance, final.BalanceBerries);
            Assert.True(final.Items.Single(item => item.Definition.Id == "quote-pirate-king-man").Owned);
        }
        finally
        {
            allowPurchaseCommit.Set();
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public async Task 聊天装饰交易_跨实例并发重复请求只扣一次且不会超额消费()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-concurrent-purchases");
        var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var firstStore = new RankedStore(path);
            var secondStore = new RankedStore(path);
            Assert.NotNull(firstStore.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
            Assert.NotNull(firstStore.SelectFaction("bob", "鲍勃", RankedStore.MarineFaction, now));
            SeedRankPointsAndResetWallet(path, "爱丽丝", 500, "鲍勃", 500);
            firstStore.Initialize();
            secondStore.Initialize();

            using (var startDuplicate = new ManualResetEventSlim(false))
            {
                var duplicateTasks = new[] { firstStore, secondStore }
                    .Select(store => Task.Run(() =>
                    {
                        startDuplicate.Wait();
                        return store.PurchaseChatDecoration(
                            "alice", "爱丽丝", "quote-pirate-king-man",
                            "concurrent-duplicate-0001",
                            ChatDecorationCatalog.PurchasePriceBerries,
                            now.AddSeconds(1));
                    }))
                    .ToArray();
                startDuplicate.Set();
                var results = await Task.WhenAll(duplicateTasks);

                Assert.All(results, result => Assert.True(result.Succeeded));
                Assert.Single(results, result => result.Replayed);
                Assert.All(results, result => Assert.Equal(0, result.Snapshot.BalanceBerries));
            }

            using (var startOverspend = new ManualResetEventSlim(false))
            {
                var cheap = Task.Run(() =>
                {
                    startOverspend.Wait();
                    return firstStore.PurchaseChatDecoration(
                        "bob", "鲍勃", "quote-pirate-king-man",
                        "concurrent-cheap-0001",
                        ChatDecorationCatalog.PurchasePriceBerries,
                        now.AddSeconds(2));
                });
                var expensive = Task.Run(() =>
                {
                    startOverspend.Wait();
                    return secondStore.PurchaseChatDecoration(
                        "bob", "鲍勃", "quote-binks-laugh",
                        "concurrent-expensive-0001",
                        ChatDecorationCatalog.PurchasePriceBerries,
                        now.AddSeconds(2));
                });
                startOverspend.Set();
                var results = await Task.WhenAll(cheap, expensive);

                Assert.Single(results, result => result.Succeeded);
                Assert.Single(results, result => !result.Succeeded && result.Outcome == "insufficient_funds");
                var final = new RankedStore(path)
                    .GetChatDecorationExchangeSnapshot("bob", "鲍勃", now.AddMinutes(1));
                var owned = final.Items.Where(item => item.Owned).ToArray();
                var purchased = Assert.Single(owned);
                Assert.Equal(0, final.BalanceBerries);
                Assert.True(final.BalanceBerries >= 0);
            }
        }
        finally
        {
            DeleteRankedTestDatabase(path);
        }
    }

    [Fact]
    public void 聊天装饰交易_狂野排位数据库明确拒绝使用()
    {
        var path = CreateRankedTestDatabasePath("chat-decoration-wild-disabled");
        try
        {
            var wildStore = new RankedStore(path, chatDecorationExchangeEnabled: false);
            var error = Assert.Throws<ChatDecorationValidationException>(() =>
                wildStore.GetChatDecorationExchangeSnapshot("alice", "爱丽丝"));
            Assert.Contains("狂野排位不计入", error.Message);
        }
        finally
        {
            DeleteRankedTestDatabase(path);
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

    private static void SetCurrentAndHighestRankPoints(
        string path,
        string displayName,
        int current,
        int highest)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE rank_profiles
            SET placement_games=$placements, games=MAX(games,$placements),
                rank_points=$current, highest_rank_points=$highest
            WHERE display_name=$name;
            """;
        command.Parameters.AddWithValue("$placements", RankedStore.PlacementRequired);
        command.Parameters.AddWithValue("$current", current);
        command.Parameters.AddWithValue("$highest", highest);
        command.Parameters.AddWithValue("$name", displayName);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static string ReadRankedAccountKey(string path, string displayName)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_key FROM rank_profiles WHERE display_name=$name LIMIT 1;";
        command.Parameters.AddWithValue("$name", displayName);
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private static string CreateRankedTestDatabasePath(string prefix)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(configuredRoot))
            throw new InvalidOperationException(
                "聊天装饰交易测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");

        var root = Path.GetFullPath(configuredRoot);
        var requiredRoot = Path.GetFullPath(@"E:\GrandUMI-Temp") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(requiredRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("聊天装饰交易测试临时目录必须位于 E:\\GrandUMI-Temp\\ 下。");
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"{prefix}-{Guid.NewGuid():N}.db");
    }

    private static void SeedRankPointsAndResetWallet(
        string path,
        params object[] displayNameAndRankPoints)
    {
        Assert.True(displayNameAndRankPoints.Length > 0 && displayNameAndRankPoints.Length % 2 == 0);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        for (var index = 0; index < displayNameAndRankPoints.Length; index += 2)
        {
            var displayName = Assert.IsType<string>(displayNameAndRankPoints[index]);
            var rankPoints = Assert.IsType<int>(displayNameAndRankPoints[index + 1]);
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE rank_profiles
                SET placement_games=$placements, games=$placements,
                    rank_points=$points, highest_rank_points=$points
                WHERE display_name=$name;
                """;
            update.Parameters.AddWithValue("$placements", RankedStore.PlacementRequired);
            update.Parameters.AddWithValue("$points", rankPoints);
            update.Parameters.AddWithValue("$name", displayName);
            Assert.Equal(1, update.ExecuteNonQuery());
        }

        using var reset = connection.CreateCommand();
        reset.Transaction = transaction;
        reset.CommandText = "DELETE FROM rank_exchange_wallets;";
        reset.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void DeleteRankedTestDatabase(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
