using GrandUMI.Game.Ranked;
using GrandUMI.Game.Stats;
using Xunit;

namespace GrandUMI.Tests;

public sealed class RankedLeaderboardSnapshotTests
{
    [Fact]
    public void 请求组合实时个人资料与上一版公共榜单()
    {
        using var fixture = new SnapshotFixture();
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        var store = fixture.CreateStore();
        Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
        CompletePlacements(store, now, "cached");

        var beforeRefresh = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(10));
        Assert.Equal(RankedStore.PlacementRequired, beforeRefresh.Profile.Games);
        Assert.Empty(beforeRefresh.Leaderboard);

        Assert.True(store.TryRefreshLeaderboardSnapshot(now.AddMinutes(11)));
        var refreshed = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(11));
        var cachedPlayer = Assert.Single(refreshed.Leaderboard, item => item.IsCurrentPlayer);
        Assert.Equal(RankedStore.PlacementRequired, cachedPlayer.Games);
        Assert.True(refreshed.SnapshotVersion > beforeRefresh.SnapshotVersion);

        Assert.NotNull(store.RecordMatch("cached-after-refresh", now.AddMinutes(12),
            "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
        var combined = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(13));

        Assert.Equal(RankedStore.PlacementRequired + 1, combined.Profile.Games);
        Assert.Equal(RankedStore.PlacementRequired, Assert.Single(combined.Leaderboard, item => item.IsCurrentPlayer).Games);
        Assert.Equal(refreshed.SnapshotVersion, combined.SnapshotVersion);
        Assert.Equal(refreshed.GeneratedAtUtc, combined.GeneratedAtUtc);
    }

    [Fact]
    public async Task 快照刷新是单飞且只发布一个完整版本()
    {
        using var fixture = new SnapshotFixture();
        var now = new DateTime(2026, 8, 12, 13, 0, 0, DateTimeKind.Utc);
        var store = fixture.CreateStore();
        Assert.True(store.TryRefreshLeaderboardSnapshot(now));
        var version = store.GetSnapshot("alice", "爱丽丝", now).SnapshotVersion;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        store.BeforeLeaderboardSnapshotBuildForTesting = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };

        var first = Task.Run(() => store.TryRefreshLeaderboardSnapshot(now.AddSeconds(15)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        bool overlapping;
        try
        {
            overlapping = store.TryRefreshLeaderboardSnapshot(now.AddSeconds(16));
            var settlement = Task.Run(() => store.RecordMatch("refresh-does-not-block-settlement", now.AddSeconds(16),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
            Assert.Same(settlement, await Task.WhenAny(settlement, Task.Delay(TimeSpan.FromSeconds(2))));
            Assert.NotNull(await settlement);
        }
        finally
        {
            release.Set();
        }

        Assert.False(overlapping);
        Assert.True(await first);
        Assert.Equal(version + 1, store.GetSnapshot("alice", "爱丽丝", now.AddSeconds(17)).SnapshotVersion);
    }

    [Fact]
    public async Task 冷启动补热期间完成结算不会返回早于榜单的个人资料()
    {
        using var fixture = new SnapshotFixture();
        var now = new DateTime(2026, 8, 12, 13, 30, 0, DateTimeKind.Utc);
        var setupStore = fixture.CreateStore();
        Assert.NotNull(setupStore.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
        CompletePlacements(setupStore, now, "cold-start");

        var coldStore = fixture.CreateStore();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        coldStore.BeforeLeaderboardSnapshotBuildForTesting = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };

        var request = Task.Run(() => coldStore.GetSnapshot("alice", "爱丽丝", now.AddMinutes(10)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.NotNull(coldStore.RecordMatch("cold-start-race", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0));
        }
        finally
        {
            release.Set();
        }

        var snapshot = await request;
        var publicPlayer = Assert.Single(snapshot.Leaderboard, item => item.IsCurrentPlayer);
        Assert.Equal(RankedStore.PlacementRequired + 1, snapshot.Profile.Games);
        Assert.Equal(snapshot.Profile.Games, publicPlayer.Games);
    }

    [Fact]
    public void 候选生成失败保留上一版且恢复后版本继续增长()
    {
        using var fixture = new SnapshotFixture();
        var now = new DateTime(2026, 8, 12, 14, 0, 0, DateTimeKind.Utc);
        var store = fixture.CreateStore();
        Assert.True(store.TryRefreshLeaderboardSnapshot(now));
        var successful = store.GetSnapshot("alice", "爱丽丝", now);
        store.BeforeLeaderboardSnapshotBuildForTesting = () => throw new InvalidOperationException("模拟装饰数据源失败");

        Assert.False(store.TryRefreshLeaderboardSnapshot(now.AddSeconds(15)));
        var degraded = store.GetSnapshot("alice", "爱丽丝", now.AddSeconds(16));
        Assert.Equal(successful.SnapshotVersion, degraded.SnapshotVersion);
        Assert.Equal(successful.GeneratedAtUtc, degraded.GeneratedAtUtc);
        Assert.Contains("模拟装饰数据源失败", store.LastLeaderboardRefreshError);

        store.BeforeLeaderboardSnapshotBuildForTesting = null;
        Assert.True(store.TryRefreshLeaderboardSnapshot(now.AddSeconds(30)));
        Assert.True(store.GetSnapshot("alice", "爱丽丝", now.AddSeconds(31)).SnapshotVersion > successful.SnapshotVersion);
        Assert.Null(store.LastLeaderboardRefreshError);
    }

    [Fact]
    public void 版本号跨进程重启仍单调且新赛季不回放旧快照()
    {
        using var fixture = new SnapshotFixture();
        var seasonOne = new DateTime(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc);
        var firstStore = fixture.CreateStore();
        Assert.True(firstStore.TryRefreshLeaderboardSnapshot(seasonOne));
        var firstVersion = firstStore.GetSnapshot("alice", "爱丽丝", seasonOne).SnapshotVersion;

        var restartedStore = fixture.CreateStore();
        Assert.True(restartedStore.TryRefreshLeaderboardSnapshot(seasonOne.AddSeconds(15)));
        var restartedVersion = restartedStore.GetSnapshot("alice", "爱丽丝", seasonOne.AddSeconds(16)).SnapshotVersion;
        Assert.True(restartedVersion > firstVersion);

        restartedStore.BeforeLeaderboardSnapshotBuildForTesting = () => throw new InvalidOperationException("新赛季预热失败");
        var seasonTwo = seasonOne.AddDays(60);
        var exception = Assert.Throws<RankLeaderboardUnavailableException>(
            () => restartedStore.GetSnapshot("alice", "爱丽丝", seasonTwo));
        Assert.Contains("新赛季预热失败", exception.Message);
    }

    [Fact]
    public void 返回值变更不会污染已发布的不可变快照()
    {
        using var fixture = new SnapshotFixture();
        var now = new DateTime(2026, 8, 12, 16, 0, 0, DateTimeKind.Utc);
        var store = fixture.CreateStore();
        Assert.NotNull(store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now));
        CompletePlacements(store, now, "immutable");
        Assert.True(store.TryRefreshLeaderboardSnapshot(now.AddMinutes(10)));

        var first = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(11));
        var firstArray = Assert.IsType<RankLeaderboardItem[]>(first.Leaderboard);
        var originalName = firstArray[0].DisplayName;
        firstArray[0] = firstArray[0] with { DisplayName = "篡改名称" };

        var second = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(12));
        Assert.Equal(originalName, second.Leaderboard[0].DisplayName);
    }

    private static void CompletePlacements(RankedStore store, DateTime now, string prefix)
    {
        for (var index = 0; index < RankedStore.PlacementRequired; index++)
        {
            Assert.NotNull(store.RecordMatch($"{prefix}-{index}", now.AddMinutes(index),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: index % 2));
        }
    }

    private sealed class SnapshotFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"grandumi-ranked-snapshot-{Guid.NewGuid():N}");
        private readonly string _rankedPath;
        private readonly string _statsPath;

        public SnapshotFixture()
        {
            Directory.CreateDirectory(_directory);
            _rankedPath = Path.Combine(_directory, "ranked.db");
            _statsPath = Path.Combine(_directory, "leader-stats.db");
        }

        public RankedStore CreateStore()
            => new(
                _rankedPath,
                new LeaderChampionStore(_statsPath),
                new LeaderStatsStore(_statsPath));

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch { }
        }
    }
}
