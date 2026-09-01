using System.Collections.Concurrent;
using System.Reflection;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

[Collection("匹配身份隔离")]
public sealed class CasualFormatMatchmakingTests
{
    private static readonly MethodInfo QueueForMethod = RequiredMethod("QueueFor");
    private static readonly MethodInfo DeckFormatForQueueMethod = RequiredMethod("DeckFormatForQueue");
    private static readonly MethodInfo DeckFormatForMatchKindMethod = RequiredMethod("DeckFormatForMatchKind");
    private static readonly MethodInfo MatchKindForQueueMethod = RequiredMethod("MatchKindForQueue");
    private static readonly MethodInfo UsesPublicMatchClockMethod = typeof(GameRoomManager).GetMethod(
        "UsesPublicMatchClock", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("未找到公开匹配棋钟判定方法");

    [Fact]
    public void 各公开模式_使用独立队列且海克斯不与普通匹配混排()
    {
        var standardCasual = QueueFor("casualStandard");
        var wildCasual = QueueFor("casual");
        var standardRanked = QueueFor("ranked");
        var wildRanked = QueueFor("rankedWild");
        var hex = QueueFor("hex");

        Assert.NotSame(standardCasual, wildCasual);
        Assert.NotSame(standardCasual, standardRanked);
        Assert.NotSame(standardCasual, wildRanked);
        Assert.NotSame(wildCasual, standardRanked);
        Assert.NotSame(wildCasual, wildRanked);
        Assert.NotSame(hex, standardCasual);
        Assert.NotSame(hex, wildCasual);
        Assert.NotSame(hex, standardRanked);
        Assert.NotSame(hex, wildRanked);
    }

    [Theory]
    [InlineData("casualStandard", DeckValidator.FormatStandard, MatchKind.CasualStandard)]
    [InlineData("casual", DeckValidator.FormatPublicUnrestricted, MatchKind.CasualWild)]
    [InlineData("ranked", DeckValidator.FormatStandardRanked, MatchKind.Ranked)]
    [InlineData("rankedWild", DeckValidator.FormatPublicUnrestricted, MatchKind.RankedWild)]
    [InlineData("hex", DeckValidator.FormatPublicUnrestricted, MatchKind.Hex)]
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
    public void 旧客户端休闲协议_继续进入狂野休闲队列并执行公开禁卡规则()
    {
        Assert.Same(QueueFor("casual"), QueueFor("unknown-client-value"));
        Assert.Equal(DeckValidator.FormatPublicUnrestricted,
            DeckFormatForQueueMethod.Invoke(null, ["unknown-client-value"]));
        Assert.Equal(MatchKind.CasualWild,
            MatchKindForQueueMethod.Invoke(null, ["unknown-client-value"]));
    }

    [Theory]
    [InlineData("casualStandard")]
    [InlineData("casual")]
    [InlineData("ranked")]
    [InlineData("rankedWild")]
    [InlineData("hex")]
    [InlineData("unknown-client-value")]
    public void 所有公开匹配队列_包括旧客户端回退_都拒绝官网禁卡(string queueKind)
    {
        TestScene.New();
        var leader = CardDatabase.Get("OP03-040")!;
        var deck = string.Join('\n', BuildValidDeck(leader));

        var result = ValidateForQueue(deck, queueKind);

        Assert.False(result.Ok);
        Assert.Contains("官方禁卡", result.Reason ?? "");
        Assert.Contains("OP03-040", result.Reason ?? "");
        Assert.Contains("仅好友或房间对战允许", result.Reason ?? "");
    }

    [Theory]
    [InlineData(MatchKind.CasualStandard, DeckValidator.FormatStandard)]
    [InlineData(MatchKind.CasualWild, DeckValidator.FormatPublicUnrestricted)]
    [InlineData(MatchKind.Casual, DeckValidator.FormatPublicUnrestricted)]
    [InlineData(MatchKind.Matchmaking, DeckValidator.FormatPublicUnrestricted)]
    [InlineData(MatchKind.Ranked, DeckValidator.FormatStandardRanked)]
    [InlineData(MatchKind.RankedWild, DeckValidator.FormatPublicUnrestricted)]
    [InlineData(MatchKind.Hex, DeckValidator.FormatPublicUnrestricted)]
    [InlineData(MatchKind.Bot, DeckValidator.FormatPublicUnrestricted)]
    [InlineData(MatchKind.Friendly, DeckValidator.FormatUnrestricted)]
    [InlineData(MatchKind.RoomCode, DeckValidator.FormatUnrestricted)]
    public void 对局来源_统一决定公开禁卡与好友房放行边界(MatchKind matchKind, string expectedDeckFormat)
    {
        Assert.Equal(expectedDeckFormat, DeckFormatForMatchKindMethod.Invoke(null, [matchKind]));
    }

    [Theory]
    [InlineData("casualStandard")]
    [InlineData("casual")]
    [InlineData("ranked")]
    [InlineData("rankedWild")]
    [InlineData("hex")]
    public void 所有公开匹配队列_继续允许合法卡组(string queueKind)
    {
        TestScene.New();
        var leader = CardDatabase.Get("OP15-001")!;
        var deck = string.Join('\n', BuildValidDeck(leader));

        var result = ValidateForQueue(deck, queueKind);

        Assert.True(result.Ok, result.Reason);
    }

    [Theory]
    [InlineData("OP18-031")]
    [InlineData("EB05-016")]
    public void 匹配入口_仅标准排位拒绝OP18与EB05卡组(string cardNumber)
    {
        TestScene.New();
        var leader = CardDatabase.Get("OP15-001")!;
        var lines = BuildValidDeck(leader);
        lines[^1] = cardNumber;
        var deck = string.Join('\n', lines);

        var standardRanked = ValidateForQueue(deck, "ranked");
        var wildRanked = ValidateForQueue(deck, "rankedWild");
        var standardCasual = ValidateForQueue(deck, "casualStandard");
        var wildCasual = ValidateForQueue(deck, "casual");
        var hex = ValidateForQueue(deck, "hex");

        Assert.False(standardRanked.Ok);
        Assert.Contains("OP18/EB05 系列暂不可用于标准排位", standardRanked.Reason ?? "");
        Assert.True(wildRanked.Ok, wildRanked.Reason);
        Assert.True(standardCasual.Ok, standardCasual.Reason);
        Assert.True(wildCasual.Ok, wildCasual.Reason);
        Assert.True(hex.Ok, hex.Reason);
    }

    [Theory]
    [InlineData(MatchKind.CasualStandard)]
    [InlineData(MatchKind.CasualWild)]
    [InlineData(MatchKind.Casual)]
    [InlineData(MatchKind.Hex)]
    public void 新旧休闲房间_都启用公开匹配棋钟(MatchKind matchKind)
    {
        Assert.True((bool)UsesPublicMatchClockMethod.Invoke(null, [matchKind])!);
    }

    private static MethodInfo RequiredMethod(string name)
        => typeof(WebSocketBridge).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException($"未找到匹配方法：{name}");

    private static ConcurrentQueue<WsSession> QueueFor(string queueKind)
        => (ConcurrentQueue<WsSession>)QueueForMethod.Invoke(null, [queueKind])!;

    private static DeckValidator.Result ValidateForQueue(string deck, string queueKind)
        => DeckValidator.Validate(
            deck,
            (string)DeckFormatForQueueMethod.Invoke(null, [queueKind])!);

    private static List<string> BuildValidDeck(CardInfo leader)
    {
        var lines = new List<string> { leader.Number };
        foreach (var card in CardDatabase.GetBySet("OP15")
                     .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader)))
        {
            for (var copy = 0; copy < 4 && lines.Count < 51; copy++) lines.Add(card.Number);
            if (lines.Count == 51) break;
        }
        Assert.Equal(51, lines.Count);
        return lines;
    }
}
