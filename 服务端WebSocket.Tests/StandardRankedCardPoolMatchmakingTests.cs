using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using GrandUMI.Cards;
using GrandUMI.Game.Ranked;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

[Collection("匹配身份隔离")]
public sealed class StandardRankedCardPoolMatchmakingTests : IDisposable
{
    private static readonly string[] QueueKinds = ["ranked", "rankedWild", "casualStandard", "casual", "hex"];
    private static readonly MethodInfo QueueForMethod = Method("QueueFor");
    private static readonly MethodInfo TakePairMethod = Method("TryTakeMatchPairFromQueue");
    private readonly ConcurrentDictionary<string, string> _accounts = Field("AccountIndex");
    private readonly ConcurrentDictionary<string, string> _reservations = Field("MatchAccountReservations");
    private readonly List<WsSession> _sessions = [];
    private readonly DateTime _nowUtc = DateTime.UtcNow;

    public StandardRankedCardPoolMatchmakingTests()
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
    public void 标准排位_含OP18只与含OP18配对_EB05与无OP18的旧卡组共池(
        string firstLeader, string? firstMain, string secondLeader, string? secondMain)
    {
        var first = Session("甲", leader: firstLeader, previewMain: firstMain);
        var second = Session("乙", leader: secondLeader, previewMain: secondMain);
        Enqueue(first, second);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(first, actualFirst);
        Assert.Same(second, actualSecond);
        Assert.False(first.IsMatching);
        Assert.False(second.IsMatching);
        Assert.Empty(Queue("ranked"));
    }

    [Theory]
    [InlineData("OP15-001", null, "OP18-001", null)]
    [InlineData("OP15-001", "EB05-002", "OP15-001", "OP18-003")]
    [InlineData("EB05-010", null, "OP18-001", null)]
    [InlineData("EB05-010", null, "OP15-001", "OP18-003")]
    public void 标准排位_不同池等待十分钟也不会合并_玩家继续等待(
        string environmentLeader, string? environmentMain, string op18Leader, string? op18Main)
    {
        var released = Session("EB05环境玩家", leader: environmentLeader, previewMain: environmentMain, waitedSeconds: 600);
        var preview = Session("OP18玩家", leader: op18Leader, previewMain: op18Main, waitedSeconds: 600);
        Enqueue(released, preview);

        Assert.False(TakePair("ranked", out _, out _));
        Assert.True(released.IsMatching);
        Assert.True(preview.IsMatching);
        Assert.Equal(2, Queue("ranked").Count);
        Assert.Equal(released.SessionId, _reservations[released.Account!]);
        Assert.Equal(preview.SessionId, _reservations[preview.Account!]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 某池只有一人_不会阻塞另一池的两人配对(bool firstUsesPreview)
    {
        var waiting = Session("最早等待", previewMain: firstUsesPreview ? "OP18-003" : "EB05-002", waitedSeconds: 200);
        var first = Session("可配对甲", previewMain: firstUsesPreview ? "EB05-002" : "OP18-003", waitedSeconds: 100);
        var second = Session("可配对乙", previewMain: firstUsesPreview ? null : "OP18-003", waitedSeconds: 90);
        Enqueue(waiting, first, second);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(first, actualFirst);
        Assert.Same(second, actualSecond);
        Assert.Same(waiting, Assert.Single(Queue("ranked")));
        Assert.True(waiting.IsMatching);
    }

    [Fact]
    public void 隐藏分更接近的异池对手_不能抢先于同池对手()
    {
        var first = Session("新卡甲", previewMain: "OP18-003", waitedSeconds: 120);
        var wrongPool = Session("实卡", waitedSeconds: 110);
        var compatible = Session("新卡乙", previewMain: "OP18-003", rating: 1700, waitedSeconds: 100);
        Enqueue(first, wrongPool, compatible);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(first, actualFirst);
        Assert.Same(compatible, actualSecond);
        Assert.Same(wrongPool, Assert.Single(Queue("ranked")));
    }

    [Fact]
    public void 新卡池_继续遵守双方共同等待的隐藏分范围()
    {
        var first = Session("新卡甲", previewMain: "OP18-003", rating: 1500, waitedSeconds: 120);
        var second = Session("新卡乙", previewMain: "OP18-003", rating: 1601, waitedSeconds: 0);
        Enqueue(first, second);

        Assert.False(TakePair("ranked", out _, out _));
        Assert.Equal(2, Queue("ranked").Count);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    public void 新卡池_继续遵守定级保护(int commonWaitSeconds, bool expected)
    {
        var placement = Session("定级新卡", previewMain: "OP18-003", waitedSeconds: commonWaitSeconds);
        placement.MatchPlacementGames = 4;
        var mature = Session("成熟新卡", previewMain: "OP18-003", waitedSeconds: 120);
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
        var second = Session("新卡", queueKind, previewMain: "OP18-003");
        Enqueue(first, second);

        Assert.True(TakePair(queueKind, out _, out _));
    }

    [Fact]
    public void 取消标准后改排狂野_旧队列项不能配对或取消新队列状态()
    {
        var rejoined = Session("换模式玩家", previewMain: "OP18-003");
        Queue("ranked").Enqueue(rejoined);
        rejoined.IsMatching = false;
        rejoined.MatchQueueKind = "rankedWild";
        rejoined.MatchUsesPreviewCards = false;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 取消后换卡组重新排标准_重复旧项只按本次卡组配对一次(bool originallyUsesOp18)
    {
        var rejoined = Session("换卡组玩家", previewMain: originallyUsesOp18 ? "OP18-003" : "EB05-002");
        Enqueue(rejoined);
        rejoined.IsMatching = false;
        rejoined.Deck = Session("卡组供体", previewMain: originallyUsesOp18 ? "EB05-002" : "OP18-003").Deck;
        rejoined.MatchUsesPreviewCards = WebSocketBridge.ContainsStandardRankedPreviewCards(rejoined.Deck!);
        rejoined.IsMatching = true;
        var released = Session("EB05环境对手", previewMain: "EB05-002");
        var preview = Session("新卡对手", previewMain: "OP18-003");
        Enqueue(rejoined, released, preview);

        Assert.True(TakePair("ranked", out var actualFirst, out var actualSecond));
        Assert.Same(rejoined, actualFirst);
        Assert.Same(originallyUsesOp18 ? released : preview, actualSecond);
        Assert.Same(originallyUsesOp18 ? preview : released, Assert.Single(Queue("ranked")));
    }

    private WsSession Session(string account, string queueKind = "ranked", string leader = "OP15-001",
        string? previewMain = null, double rating = 1500, int waitedSeconds = 100)
    {
        var leaderCard = CardDatabase.Get(leader)!;
        var lines = new List<string> { leader };
        foreach (var card in CardDatabase.GetBySet("OP15")
                     .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leaderCard)))
        {
            for (var copy = 0; copy < 4 && lines.Count < 51; copy++) lines.Add(card.Number);
            if (lines.Count == 51) break;
        }
        if (previewMain is not null) lines[^1] = previewMain;
        var deck = string.Join('\n', lines);
        var validation = DeckValidator.Validate(deck, queueKind switch
        {
            "ranked" => DeckValidator.FormatStandardRanked,
            "casualStandard" => DeckValidator.FormatStandard,
            _ => DeckValidator.FormatPublicUnrestricted,
        });
        Assert.True(validation.Ok, validation.Reason);
        var session = new WsSession
        {
            Account = account, PlayerName = account, Socket = new OpenSocket(), Deck = deck,
            MatchQueueKind = queueKind,
            MatchUsesPreviewCards = queueKind == "ranked" && WebSocketBridge.ContainsStandardRankedPreviewCards(deck),
            MatchEnqueuedAtUtc = _nowUtc.AddSeconds(-waitedSeconds), MatchRating = rating,
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

    private static bool TakePair(string queueKind, out WsSession? first, out WsSession? second)
    {
        object?[] args = [Queue(queueKind), queueKind is "ranked" or "rankedWild", null, null];
        var matched = (bool)TakePairMethod.Invoke(null, args)!;
        first = args[2] as WsSession;
        second = args[3] as WsSession;
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
