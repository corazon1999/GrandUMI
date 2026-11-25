using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using GrandUMI.Cards;
using GrandUMI.Game.Ranked;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

[Collection("匹配身份隔离")]
public sealed class StandardRankedMatchmakingTests : IDisposable
{
    private static readonly string[] QueueKinds = ["ranked", "rankedWild", "casualStandard", "casual", "hex"];
    private static readonly MethodInfo QueueForMethod = Method("QueueFor");
    private static readonly MethodInfo TakePairMethod = Method("TryTakeMatchPairFromQueue");
    private readonly ConcurrentDictionary<string, string> _accounts = Field("AccountIndex");
    private readonly ConcurrentDictionary<string, string> _reservations = Field("MatchAccountReservations");
    private readonly List<WsSession> _sessions = [];
    private readonly DateTime _nowUtc = DateTime.UtcNow;

    public StandardRankedMatchmakingTests()
    {
        TestScene.New();
        DrainQueues();
    }

    [Theory]
    [InlineData("OP15-001", null, "OP15-001", null)]
    [InlineData("OP15-001", "EB05-002", "OP15-001", null)]
    [InlineData("EB05-010", null, "OP15-001", "EB05-002")]
    [InlineData("OP18-001", null, "OP15-001", "OP18-003")]
    [InlineData("OP18-001", "EB05-002", "OP15-001", "OP18-003")]
    [InlineData("OP15-001", "OP18-003", "OP15-001", "OP18-003")]
    [InlineData("ST37-001", null, "ST38-001", null)]
    [InlineData("ST37-001", null, "OP18-001", null)]
    [InlineData("ST38-001", null, "OP15-001", "OP18-003")]
    [InlineData("OP15-001", null, "OP18-001", null)]
    [InlineData("OP15-001", "EB05-002", "OP15-001", "OP18-003")]
    [InlineData("EB05-010", null, "ST37-001", null)]
    [InlineData("OP15-001", "EB05-002", "ST38-001", null)]
    public void 标准排位_新旧卡组统一匹配(
        string firstLeader, string? firstMain, string secondLeader, string? secondMain)
    {
        var first = Session("甲", leader: firstLeader, extraCard: firstMain);
        var second = Session("乙", leader: secondLeader, extraCard: secondMain);
        Enqueue(first, second);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(first, actualFirst);
        Assert.Same(second, actualSecond);
        Assert.False(first.IsMatching);
        Assert.False(second.IsMatching);
        Assert.Empty(Queue("ranked"));
    }

    [Fact]
    public void 标准排位_隐藏分更接近的旧卡对手优先()
    {
        var first = Session("新卡甲", extraCard: "OP18-003", waitedSeconds: 120);
        var closer = Session("实卡", waitedSeconds: 110);
        var farther = Session("新卡乙", extraCard: "OP18-003", rating: 1700, waitedSeconds: 100);
        Enqueue(first, closer, farther);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(first, actualFirst);
        Assert.Same(closer, actualSecond);
        Assert.Same(farther, Assert.Single(Queue("ranked")));
    }

    [Fact]
    public void 标准排位_继续遵守双方共同等待的隐藏分范围()
    {
        var first = Session("新卡甲", extraCard: "OP18-003", rating: 1500, waitedSeconds: 120);
        var second = Session("新卡乙", extraCard: "OP18-003", rating: 1601, waitedSeconds: 0);
        Enqueue(first, second);

        Assert.False(TakePair("ranked", out _, out _));
        Assert.Equal(2, Queue("ranked").Count);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    public void 标准排位_继续遵守定级保护(int commonWaitSeconds, bool expected)
    {
        var placement = Session("定级新卡", extraCard: "OP18-003", waitedSeconds: commonWaitSeconds);
        placement.MatchPlacementGames = 4;
        var mature = Session("成熟新卡", extraCard: "OP18-003", waitedSeconds: 120);
        mature.MatchRankPoints = RankedStore.NewWorldRankPoints;
        Enqueue(placement, mature);

        Assert.Equal(expected, TakePair("ranked", out _, out _));
    }

    [Theory]
    [InlineData("rankedWild")]
    [InlineData("casualStandard")]
    [InlineData("casual")]
    [InlineData("hex")]
    public void 狂野排位及其他公开模式_可以混合匹配新旧卡组(string queueKind)
    {
        var first = Session("实卡", queueKind);
        var second = Session("新卡", queueKind, extraCard: "OP18-003");
        Enqueue(first, second);

        Assert.True(TakePair(queueKind, out _, out _));
    }

    [Theory]
    [InlineData("rankedWild", "ST37-001")]
    [InlineData("rankedWild", "ST38-001")]
    [InlineData("casualStandard", "ST37-001")]
    [InlineData("casualStandard", "ST38-001")]
    [InlineData("casual", "ST37-001")]
    [InlineData("casual", "ST38-001")]
    [InlineData("hex", "ST37-001")]
    [InlineData("hex", "ST38-001")]
    public void 狂野排位及其他公开模式_预组领航仍可匹配旧卡组(string queueKind, string leaderNumber)
    {
        var first = Session("旧卡组", queueKind);
        var second = Session("预组领航", queueKind, leader: leaderNumber);
        Enqueue(first, second);

        Assert.True(TakePair(queueKind, out _, out _));
    }

    [Fact]
    public void 取消标准后改排狂野_旧队列项不能配对或取消新队列状态()
    {
        var rejoined = Session("换模式玩家", extraCard: "OP18-003");
        Queue("ranked").Enqueue(rejoined);
        rejoined.IsMatching = false;
        rejoined.MatchQueueKind = "rankedWild";
        rejoined.IsMatching = true;
        Queue("rankedWild").Enqueue(rejoined);
        var standard = Session("标准实卡");
        Enqueue(standard);

        Assert.False(TakePair("ranked", out _, out _));
        Assert.True(rejoined.IsMatching);
        Assert.Same(rejoined, Assert.Single(Queue("rankedWild")));
        Assert.Same(standard, Assert.Single(Queue("ranked")));

        var wild = Session("狂野实卡", "rankedWild");
        Enqueue(wild);
        Assert.True(TakePair("rankedWild", out var actualFirst, out var actualSecond));
        Assert.Same(rejoined, actualFirst);
        Assert.Same(wild, actualSecond);
    }

    [Fact]
    public void 禁用组合生效前已入队的卡组_零点后拒绝配对并释放账号占位()
    {
        var effectiveAt = DeckValidator.NovemberBannedPairsEffectiveAtUtc;
        var before = effectiveAt.AddSeconds(-1);
        var banned = Session("禁用组合", leader: "OP13-001", extraCard: "EB05-001", waitedSeconds: 120, validationTimeUtc: before);
        var first = Session("合法甲", extraCard: "OP18-003", waitedSeconds: 100, validationTimeUtc: before);
        var second = Session("合法乙", waitedSeconds: 90, validationTimeUtc: before);
        Enqueue(banned, first, second);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond, effectiveAt));
        Assert.Same(first, actualFirst);
        Assert.Same(second, actualSecond);
        Assert.False(banned.IsMatching);
        Assert.False(_reservations.ContainsKey(banned.Account!));
        Assert.Empty(Queue("ranked"));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "ST37-001")]
    [InlineData(true, "ST37-001")]
    [InlineData(false, "ST38-001")]
    [InlineData(true, "ST38-001")]
    public void 取消后换卡组重新排标准_重复旧项只按本次卡组配对一次(bool originallyUsesNewCards, string? starterLeader)
    {
        var newLeader = starterLeader ?? "OP15-001";
        var newMain = starterLeader is null ? "OP18-003" : null;
        var rejoined = Session("换卡组玩家", leader: originallyUsesNewCards ? newLeader : "OP15-001",
            extraCard: originallyUsesNewCards ? newMain : "EB05-002");
        Enqueue(rejoined);
        rejoined.IsMatching = false;
        rejoined.Deck = Session("卡组供体", leader: originallyUsesNewCards ? "OP15-001" : newLeader,
            extraCard: originallyUsesNewCards ? "EB05-002" : newMain).Deck;
        rejoined.IsMatching = true;
        var released = Session("EB05环境对手", extraCard: "EB05-002");
        var preview = Session("新卡对手", extraCard: "OP18-003");
        Enqueue(rejoined, released, preview);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(rejoined, actualFirst);
        Assert.Same(released, actualSecond);
        Assert.Same(preview, Assert.Single(Queue("ranked")));
    }

    private WsSession Session(string account, string queueKind = "ranked", string leader = "OP15-001",
        string? extraCard = null, double rating = 1500, int waitedSeconds = 100, DateTime? validationTimeUtc = null)
    {
        var leaderCard = CardDatabase.Get(leader)!;
        var lines = new List<string> { leader };
        // 路飞只允许阿拉巴斯坦卡组；使用不含三个新系列的合法主卡，验证预组领航能进入统一队列。
        var pool = leader == "ST37-001"
            ? new[] { "EB01", "EB02", "EB03", "EB04", "OP13", "OP15" }
                .SelectMany(CardDatabase.GetBySet)
                .Where(card => card.Subscript != 1 && card.HasKeyword("阿拉巴斯坦王国"))
            : CardDatabase.GetBySet("OP15");
        foreach (var card in pool
                     .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leaderCard)))
        {
            for (var copy = 0; copy < 4 && lines.Count < 51; copy++) lines.Add(card.Number);
            if (lines.Count == 51) break;
        }
        if (extraCard is not null) lines[^1] = extraCard;
        var deck = string.Join('\n', lines);
        var nowUtc = validationTimeUtc ?? _nowUtc;
        var validation = DeckValidator.Validate(deck, queueKind switch
        {
            "ranked" => DeckValidator.FormatStandardRanked,
            "casualStandard" => DeckValidator.FormatStandard,
            _ => DeckValidator.FormatPublicUnrestricted,
        }, nowUtc);
        Assert.True(validation.Ok, validation.Reason);
        var session = new WsSession
        {
            Account = account, PlayerName = account, Socket = new OpenSocket(), Deck = deck,
            MatchQueueKind = queueKind,
            MatchEnqueuedAtUtc = nowUtc.AddSeconds(-waitedSeconds), MatchRating = rating,
            MatchPlacementGames = RankedStore.PlacementRequired, IsMatching = true,
        };
        _sessions.Add(session);
        _accounts[account] = session.SessionId;
        _reservations[account] = session.SessionId;
        return session;
    }

    private static void Enqueue(params WsSession[] sessions)
    {
        foreach (var session in sessions) Queue(session.MatchQueueKind).Enqueue(session);
    }

    private static bool TakePair(string queueKind, out WsSession? first, out WsSession? second, DateTime? nowUtc = null)
    {
        object?[] args = [Queue(queueKind), queueKind is "ranked" or "rankedWild", nowUtc ?? DateTime.UtcNow, null, null];
        var matched = (bool)TakePairMethod.Invoke(null, args)!;
        first = args[3] as WsSession;
        second = args[4] as WsSession;
        return matched;
    }

    private static ConcurrentQueue<WsSession> Queue(string kind)
        => (ConcurrentQueue<WsSession>)QueueForMethod.Invoke(null, [kind])!;

    private static MethodInfo Method(string name)
        => typeof(WebSocketBridge).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static ConcurrentDictionary<string, string> Field(string name)
        => (ConcurrentDictionary<string, string>)typeof(WebSocketBridge)
            .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static void DrainQueues()
    {
        foreach (var kind in QueueKinds)
            while (Queue(kind).TryDequeue(out var session)) session.IsMatching = false;
    }

    public void Dispose()
    {
        DrainQueues();
        foreach (var session in _sessions)
        {
            _accounts.TryRemove(session.Account!, out _);
            _reservations.TryRemove(session.Account!, out _);
            session.Socket.Dispose();
        }
    }

    private sealed class OpenSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
