using GrandUMI.Diagnostics;
using GrandUMI.Game.Stats;
using GrandUMI.Persistence;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class AdminOperationsMetricsCacheTests
{
    [Fact]
    public void 玩家流量缓存一分钟场次缓存十分钟而磁盘缓存三小时且网页轮询不会重复读取()
    {
        var matchLoads = 0;
        var trafficLoads = 0;
        var storageLoads = 0;
        var cache = new AdminOperationsMetricsCache(
            (days, now) =>
            {
                matchLoads++;
                return [new DailyMatchCountPoint("2026-08-25", days)];
            },
            (days, now) =>
            {
                trafficLoads++;
                return new PlayerTrafficSnapshot(
                    3,
                    [new OnlinePlayerPeakPoint("2026-08-25", days)],
                    [new DailyActivePlayerPoint("2026-08-25", 2)]);
            },
            () =>
            {
                storageLoads++;
                return new StorageHealthSnapshot(true, "", 1000, 400);
            });
        var now = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        cache.GetDailyMatchCounts(now);
        cache.GetPlayerTraffic(now);
        cache.GetStorageHealth(now);
        cache.GetDailyMatchCounts(now.AddMinutes(9));
        cache.GetPlayerTraffic(now.AddSeconds(59));
        cache.GetStorageHealth(now.AddHours(2));
        Assert.Equal(1, matchLoads);
        Assert.Equal(1, trafficLoads);
        Assert.Equal(1, storageLoads);

        cache.GetDailyMatchCounts(now.AddMinutes(10));
        cache.GetPlayerTraffic(now.AddMinutes(1));
        cache.GetStorageHealth(now.AddHours(3));
        Assert.Equal(2, matchLoads);
        Assert.Equal(2, trafficLoads);
        Assert.Equal(2, storageLoads);
    }
}
