using System.Collections.Concurrent;
using System.Reflection;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

[Collection("匹配身份隔离")]
public sealed class CasualFormatMatchmakingTests
{
    private static readonly MethodInfo QueueForMethod = RequiredMethod("QueueFor");
    private static readonly MethodInfo DeckFormatForQueueMethod = RequiredMethod("DeckFormatForQueue");
    private static readonly MethodInfo MatchKindForQueueMethod = RequiredMethod("MatchKindForQueue");
    private static readonly MethodInfo UsesPublicMatchClockMethod = typeof(GameRoomManager).GetMethod(
        "UsesPublicMatchClock", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("未找到公开匹配棋钟判定方法");

    [Fact]
    public void 标准休闲与狂野休闲_使用独立队列且不与排位混排()
    {
        var standardCasual = QueueFor("casualStandard");
        var wildCasual = QueueFor("casual");
        var standardRanked = QueueFor("ranked");
        var wildRanked = QueueFor("rankedWild");

        Assert.NotSame(standardCasual, wildCasual);
        Assert.NotSame(standardCasual, standardRanked);
        Assert.NotSame(standardCasual, wildRanked);
        Assert.NotSame(wildCasual, standardRanked);
        Assert.NotSame(wildCasual, wildRanked);
    }

    [Theory]
    [InlineData("casualStandard", DeckValidator.FormatStandard, MatchKind.CasualStandard)]
    [InlineData("casual", DeckValidator.FormatUnrestricted, MatchKind.CasualWild)]
    [InlineData("ranked", DeckValidator.FormatStandard, MatchKind.Ranked)]
    [InlineData("rankedWild", DeckValidator.FormatUnrestricted, MatchKind.RankedWild)]
    public void 队列协议_同时决定牌组规则与房间来源(
        string queueKind,
        string expectedDeckFormat,
        MatchKind expectedMatchKind)
    {
        var deckFormat = (string)DeckFormatForQueueMethod.Invoke(null, [queueKind])!;
        var matchKind = (MatchKind)MatchKindForQueueMethod.Invoke(null, [queueKind])!;

        Assert.Equal(expectedDeckFormat, deckFormat);
        Assert.Equal(expectedMatchKind, matchKind);
    }

    [Fact]
    public void 旧客户端休闲协议_继续进入原有无限制休闲队列()
    {
        Assert.Same(QueueFor("casual"), QueueFor("unknown-client-value"));
        Assert.Equal(DeckValidator.FormatUnrestricted,
            DeckFormatForQueueMethod.Invoke(null, ["unknown-client-value"]));
        Assert.Equal(MatchKind.CasualWild,
            MatchKindForQueueMethod.Invoke(null, ["unknown-client-value"]));
    }

    [Theory]
    [InlineData(MatchKind.CasualStandard)]
    [InlineData(MatchKind.CasualWild)]
    [InlineData(MatchKind.Casual)]
    public void 新旧休闲房间_都启用公开匹配棋钟(MatchKind matchKind)
    {
        Assert.True((bool)UsesPublicMatchClockMethod.Invoke(null, [matchKind])!);
    }

    private static MethodInfo RequiredMethod(string name)
        => typeof(WebSocketBridge).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException($"未找到匹配方法：{name}");

    private static ConcurrentQueue<WsSession> QueueFor(string queueKind)
        => (ConcurrentQueue<WsSession>)QueueForMethod.Invoke(null, [queueKind])!;
}
