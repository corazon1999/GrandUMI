using GrandUMI.Game.Ranked;
using Xunit;

namespace GrandUMI.Tests;

public sealed class RankedMatchmakingPolicyTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void 单方等待很久_不会替刚入队玩家扩圈()
    {
        var longWaiting = Session(rating: 1500, waitedSeconds: 120);
        var justJoined = Session(rating: 1601, waitedSeconds: 1);

        Assert.False(WebSocketBridge.CanRankedPlayersMatch(longWaiting, justJoined, NowUtc));

        justJoined.MatchRating = 1600;
        Assert.True(WebSocketBridge.CanRankedPlayersMatch(longWaiting, justJoined, NowUtc));
    }

    [Theory]
    [InlineData(14, 100)]
    [InlineData(15, 175)]
    [InlineData(30, 275)]
    [InlineData(60, 400)]
    [InlineData(90, 500)]
    [InlineData(600, 500)]
    public void 双方共同等待_按阶梯扩圈且永不超过五百分(int commonWaitSeconds, double expectedGap)
    {
        var first = Session(rating: 1500, waitedSeconds: commonWaitSeconds + 30);
        var second = Session(rating: 1500 + expectedGap, waitedSeconds: commonWaitSeconds);

        Assert.Equal(expectedGap, WebSocketBridge.AllowedRankGap(commonWaitSeconds));
        Assert.True(WebSocketBridge.CanRankedPlayersMatch(first, second, NowUtc));

        second.MatchRating += 1;
        Assert.False(WebSocketBridge.CanRankedPlayersMatch(first, second, NowUtc));
    }

    [Fact]
    public void 定级玩家与新世界成熟账号_共同等待不足九十秒时禁止匹配()
    {
        var placement = Session(rating: 1500, waitedSeconds: 89, placementGames: 4);
        var matureHighRanked = Session(
            rating: 1500,
            waitedSeconds: 120,
            placementGames: RankedStore.PlacementRequired,
            rankPoints: RankedStore.NewWorldRankPoints);

        Assert.False(WebSocketBridge.CanRankedPlayersMatch(placement, matureHighRanked, NowUtc));

        matureHighRanked.MatchRankPoints = RankedStore.NewWorldRankPoints - 1;
        Assert.True(WebSocketBridge.CanRankedPlayersMatch(placement, matureHighRanked, NowUtc));
    }

    [Fact]
    public void 定级保护_双方共同等待九十秒后仅在隐藏分差五百分内兜底()
    {
        var placement = Session(rating: 1500, waitedSeconds: 90, placementGames: 4);
        var matureHighRanked = Session(
            rating: 2000,
            waitedSeconds: 120,
            placementGames: RankedStore.PlacementRequired,
            rankPoints: RankedStore.NewWorldRankPoints);

        Assert.True(WebSocketBridge.CanRankedPlayersMatch(placement, matureHighRanked, NowUtc));

        matureHighRanked.MatchRating = 2001;
        Assert.False(WebSocketBridge.CanRankedPlayersMatch(placement, matureHighRanked, NowUtc));
    }

    private static WsSession Session(
        double rating,
        int waitedSeconds,
        int placementGames = RankedStore.PlacementRequired,
        int rankPoints = 0)
        => new()
        {
            MatchRating = rating,
            MatchEnqueuedAtUtc = NowUtc.AddSeconds(-waitedSeconds),
            MatchPlacementGames = placementGames,
            MatchRankPoints = rankPoints,
        };
}
