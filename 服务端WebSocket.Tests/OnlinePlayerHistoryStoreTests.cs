using GrandUMI.Persistence;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class OnlinePlayerHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        OperatingSystem.IsWindows() ? @"E:\GrandUMI-Temp\Tests" : "/tmp/grandumi-tests",
        $"grandumi-online-history-{Guid.NewGuid():N}");

    [Fact]
    public void 同一天只保留最高在线人数并补齐无数据日期()
    {
        Directory.CreateDirectory(_directory);
        var store = new OnlinePlayerHistoryStore(Path.Combine(_directory, "history.db"));
        store.Initialize();
        var day = new DateTimeOffset(2026, 8, 25, 3, 0, 0, TimeSpan.Zero);

        store.Record(3, day);
        store.Record(8, day.AddHours(1));
        store.Record(5, day.AddHours(2));

        var points = store.GetRecentDailyPeaks(7, day.AddHours(3));
        Assert.Equal(7, points.Count);
        Assert.Equal("2026-08-25", points[^1].Date);
        Assert.Equal(8, points[^1].Peak);
        Assert.All(points.Take(points.Count - 1), point => Assert.Equal(0, point.Peak));
    }

    [Fact]
    public void UTC下午四点后归入UTC加八的下一天()
    {
        Directory.CreateDirectory(_directory);
        var store = new OnlinePlayerHistoryStore(Path.Combine(_directory, "history.db"));
        store.Initialize();
        var observedAt = new DateTimeOffset(2026, 8, 25, 16, 30, 0, TimeSpan.Zero);

        store.Record(11, observedAt);

        var point = Assert.Single(store.GetRecentDailyPeaks(1, observedAt));
        Assert.Equal("2026-08-26", point.Date);
        Assert.Equal(11, point.Peak);
    }

    [Fact]
    public void 只读数据源可读取正式峰值但不能混入测试服采样()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "history.db");
        var productionStore = new OnlinePlayerHistoryStore(databasePath);
        productionStore.Initialize();
        var observedAt = new DateTimeOffset(2026, 8, 25, 3, 0, 0, TimeSpan.Zero);
        productionStore.Record(23, observedAt);

        var testReadStore = new OnlinePlayerHistoryStore(databasePath, readOnly: true);

        Assert.Equal(23, Assert.Single(testReadStore.GetRecentDailyPeaks(1, observedAt)).Peak);
        Assert.Throws<InvalidOperationException>(() => testReadStore.Record(99, observedAt));
        Assert.Throws<InvalidOperationException>(() => testReadStore.RecordActivePlayer("test-player", observedAt));
        Assert.Throws<InvalidOperationException>(() => testReadStore.Initialize());
        Assert.Equal(23, testReadStore.GetCurrentOnlineCount(observedAt.AddMinutes(1)));
        Assert.Null(testReadStore.GetCurrentOnlineCount(observedAt.AddMinutes(3)));
        Assert.Equal(23, Assert.Single(productionStore.GetRecentDailyPeaks(1, observedAt)).Peak);
    }

    [Fact]
    public void 成功登录按UTC加八自然日去重并持久化聚合()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "history.db");
        var store = new OnlinePlayerHistoryStore(databasePath);
        store.Initialize();
        var day = new DateTimeOffset(2026, 8, 25, 15, 30, 0, TimeSpan.Zero);

        Assert.True(store.RecordActivePlayer("Alice", day));
        Assert.False(store.RecordActivePlayer(" alice ", day.AddMinutes(1)));
        Assert.Equal(2, store.RecordActivePlayers(["Bob", "BOB", "Carol"], day.AddMinutes(2)));
        Assert.Equal(3, Assert.Single(store.GetRecentDailyActivePlayers(1, day)).Count);

        var restarted = new OnlinePlayerHistoryStore(databasePath);
        Assert.False(restarted.RecordActivePlayer("ALICE", day.AddMinutes(3)));
        Assert.Equal(3, Assert.Single(restarted.GetRecentDailyActivePlayers(1, day)).Count);

        Assert.True(restarted.RecordActivePlayer("Alice", day.AddHours(1)));
        var nextDay = restarted.GetRecentDailyActivePlayers(2, day.AddHours(1));
        Assert.Equal("2026-08-25", nextDay[0].Date);
        Assert.Equal(3, nextDay[0].Count);
        Assert.Equal("2026-08-26", nextDay[1].Date);
        Assert.Equal(1, nextDay[1].Count);
    }

    [Fact]
    public void 快照同时返回当前在线峰值和日活且补齐日期()
    {
        Directory.CreateDirectory(_directory);
        var store = new OnlinePlayerHistoryStore(Path.Combine(_directory, "history.db"));
        store.Initialize();
        var now = new DateTimeOffset(2026, 8, 25, 3, 0, 0, TimeSpan.Zero);
        store.Record(9, now);
        store.RecordActivePlayers(["Alice", "Bob"], now);

        var snapshot = store.GetSnapshot(7, now.AddMinutes(1));

        Assert.Equal(9, snapshot.CurrentOnlineCount);
        Assert.Equal(7, snapshot.Peaks.Count);
        Assert.Equal(9, snapshot.Peaks[^1].Peak);
        Assert.Equal(7, snapshot.DailyActivePlayers.Count);
        Assert.Equal(2, snapshot.DailyActivePlayers[^1].Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
