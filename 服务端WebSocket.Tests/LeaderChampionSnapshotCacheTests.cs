using System.Reflection;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Stats;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LeaderChampionSnapshotCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("称号缓存测试必须配置隔离临时目录。"),
        "champion-snapshot-cache", Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new();

    [Fact]
    public async Task 首次展示不等待统计锁且并发刷新合并为一个任务()
    {
        var store = CreateStore();
        AddCandidate(store, "alice", "OP16-001");
        Task? refresh = null;
        await WhileStatisticsLocked(store, async () =>
        {
            Assert.Null(await Task.Run(() => store.ResolveCachedEquippedChampionLeaderNumber("alice"))
                .WaitAsync(TimeSpan.FromSeconds(2)));
            refresh = store.RefreshDisplayCacheAsync();
            Assert.False(refresh.IsCompleted);
            for (var i = 0; i < 50; i++) Assert.Same(refresh, store.RefreshDisplayCacheAsync());
        });
        await refresh!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("OP16-001", store.ResolveCachedEquippedChampionLeaderNumber("alice"));
    }

    [Fact]
    public async Task 过期缓存在后台刷新期间仍可无等待读取并遵循装备偏好()
    {
        var store = CreateStore();
        AddCandidate(store, "alice", "OP16-001");
        AddCandidate(store, "alice", "OP17-020");
        await store.RefreshDisplayCacheAsync();
        store.RememberEquippedChampionLeaderNumber("alice", "OP17-020");
        _clock.Advance(TimeSpan.FromSeconds(16));
        Task? refresh = null;
        await WhileStatisticsLocked(store, async () =>
        {
            Assert.Equal("OP17-020", await Task.Run(() => store.ResolveCachedEquippedChampionLeaderNumber("alice"))
                .WaitAsync(TimeSpan.FromSeconds(2)));
            refresh = store.RefreshDisplayCacheAsync();
            Assert.False(refresh.IsCompleted);
        });
        await refresh!.WaitAsync(TimeSpan.FromSeconds(5));
        store.RememberEquippedChampionLeaderNumber("alice", "OP99-999");
        Assert.Equal(store.GetChampionLeaderNumbers("alice")[0], store.ResolveCachedEquippedChampionLeaderNumber("alice"));
    }

    [Fact]
    public async Task 刷新故障保留短期展示且过旧称号隐藏并保留权威资格校验()
    {
        var store = CreateStore();
        AddCandidate(store, "alice", "OP16-001");
        await store.RefreshDisplayCacheAsync();
        File.Move(store.DatabasePath, store.DatabasePath + ".offline");
        _clock.Advance(TimeSpan.FromSeconds(16));
        await store.RefreshDisplayCacheAsync();
        Assert.Equal("OP16-001", store.ResolveCachedEquippedChampionLeaderNumber("alice"));
        Assert.Throws<FileNotFoundException>(() => store.IsChampion("alice", "OP16-001"));
        _clock.Advance(TimeSpan.FromSeconds(46));
        await store.RefreshDisplayCacheAsync();
        Assert.Null(store.ResolveCachedEquippedChampionLeaderNumber("alice"));
    }

    [Fact]
    public async Task 完整对局快照不等待最强称号统计锁()
    {
        var state = TestScene.MaxScenario();
        state.SuppressExternalProfileLookups = false;
        await WhileStatisticsLocked(LeaderChampionStore.Default, async () =>
        {
            var snapshots = await Task.Run(() => StateSnapshotBuilder.BuildAll(state, "Attack"))
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(snapshots.Player0);
            Assert.NotNull(snapshots.Player1);
        });
        await LeaderChampionStore.Default.RefreshDisplayCacheAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private LeaderChampionStore CreateStore()
    {
        Directory.CreateDirectory(_root);
        return new LeaderChampionStore(Path.Combine(_root, "champions.db"), timeProvider: _clock);
    }

    private void AddCandidate(LeaderChampionStore store, string account, string leader)
    {
        for (var i = 0; i < 30; i++)
            Assert.True(store.RecordMatch(new LeaderMatchResult(
                $"{account}-{leader}-{i}", _clock.GetUtcNow().UtcDateTime.AddDays(-(i % 5)),
                MatchKind.Ranked, account, $"opponent-{i}", leader, "OP01-001", 0, 0, 8, "胜利")));
    }

    private static async Task WhileStatisticsLocked(LeaderChampionStore store, Func<Task> check)
    {
        var gate = typeof(LeaderChampionStore).GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (gate)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("统计锁测试超时。");
            }
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await check();
        }
        finally
        {
            release.Set();
            await holder;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
