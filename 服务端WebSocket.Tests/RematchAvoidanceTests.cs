using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using Xunit;

namespace GrandUMI.Tests;

[Collection("匹配身份隔离")]
public sealed class RematchAvoidanceTests : IDisposable
{
    private static readonly FieldInfo RecentOpponentsField = typeof(WebSocketBridge).GetField(
        "RecentOpponentsByAccount", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo MatchQueueField = typeof(WebSocketBridge).GetField(
        "MatchQueue", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo AccountIndexField = typeof(WebSocketBridge).GetField(
        "AccountIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo MatchAccountReservationsField = typeof(WebSocketBridge).GetField(
        "MatchAccountReservations", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo TryTakeMatchPairMethod = typeof(WebSocketBridge).GetMethod(
        "TryTakeMatchPair", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly DateTime NowUtc = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly System.Collections.IDictionary _recentOpponents;
    private readonly ConcurrentQueue<WsSession> _matchQueue;
    private readonly ConcurrentDictionary<string, string> _accountIndex;
    private readonly List<WsSession> _sessions = [];

    public RematchAvoidanceTests()
    {
        Assert.NotNull(RecentOpponentsField);
        _recentOpponents = (System.Collections.IDictionary)RecentOpponentsField.GetValue(null)!;
        _matchQueue = (ConcurrentQueue<WsSession>)MatchQueueField.GetValue(null)!;
        _accountIndex = (ConcurrentDictionary<string, string>)AccountIndexField.GetValue(null)!;
        ClearSharedState();
    }

    [Fact]
    public void 近期对手_记录后双向生效且忽略大小写()
    {
        WebSocketBridge.RecordRecentOpponents("Alpha", "Beta", NowUtc);

        Assert.True(WebSocketBridge.IsRecentOpponent("Alpha", "Beta", NowUtc));
        Assert.True(WebSocketBridge.IsRecentOpponent("beta", "ALPHA", NowUtc));
        Assert.False(WebSocketBridge.IsRecentOpponent("Alpha", "Gamma", NowUtc));
    }

    [Fact]
    public void 近期对手_超过记忆时长后解除回避()
    {
        // 与服务端 RecentOpponentMemoryLifetime（30 分钟）对齐。
        WebSocketBridge.RecordRecentOpponents("Alpha", "Beta", NowUtc);

        Assert.True(WebSocketBridge.IsRecentOpponent("Alpha", "Beta", NowUtc.AddMinutes(29)));
        Assert.False(WebSocketBridge.IsRecentOpponent("Alpha", "Beta", NowUtc.AddMinutes(31)));
    }

    [Fact]
    public void 近期对手_每个账号只记住最近三个()
    {
        foreach (var opponent in new[] { "O1", "O2", "O3", "O4" })
            WebSocketBridge.RecordRecentOpponents("Alpha", opponent, NowUtc);

        Assert.False(WebSocketBridge.IsRecentOpponent("Alpha", "O1", NowUtc));
        Assert.True(WebSocketBridge.IsRecentOpponent("Alpha", "O4", NowUtc));
    }

    [Fact]
    public void 排位_存在其他合法候选时优先配给非近期对手()
    {
        var first = WaitingSession("A", rating: 1500, waitedSeconds: 0);
        var recentOpponent = WaitingSession("B", rating: 1500, waitedSeconds: 0);
        var freshOpponent = WaitingSession("C", rating: 1560, waitedSeconds: 0);
        WebSocketBridge.RecordRecentOpponents("A", "B", NowUtc);

        var waiting = new List<WsSession> { first, recentOpponent, freshOpponent };

        Assert.Equal(2, WebSocketBridge.FindBestOpponentIndex(waiting, 0, ranked: true, NowUtc));
    }

    [Fact]
    public void 排位_只剩近期对手时共同等待满六十秒才允许重赛()
    {
        WebSocketBridge.RecordRecentOpponents("A", "B", NowUtc);

        var freshPair = new List<WsSession>
        {
            WaitingSession("A", rating: 1500, waitedSeconds: 30),
            WaitingSession("B", rating: 1500, waitedSeconds: 30),
        };
        Assert.Equal(-1, WebSocketBridge.FindBestOpponentIndex(freshPair, 0, ranked: true, NowUtc));

        // 单方久等不算：共同等待取双方较小值。
        var oneSidedWait = new List<WsSession>
        {
            WaitingSession("A", rating: 1500, waitedSeconds: 120),
            WaitingSession("B", rating: 1500, waitedSeconds: 10),
        };
        Assert.Equal(-1, WebSocketBridge.FindBestOpponentIndex(oneSidedWait, 0, ranked: true, NowUtc));

        var bothWaited = new List<WsSession>
        {
            WaitingSession("A", rating: 1500, waitedSeconds: 70),
            WaitingSession("B", rating: 1500, waitedSeconds: 65),
        };
        Assert.Equal(1, WebSocketBridge.FindBestOpponentIndex(bothWaited, 0, ranked: true, NowUtc));
    }

    [Fact]
    public void 非排位_跳过近期对手取下一个候选()
    {
        var first = WaitingSession("A", rating: 1500, waitedSeconds: 0);
        var recentOpponent = WaitingSession("B", rating: 1500, waitedSeconds: 0);
        var freshOpponent = WaitingSession("C", rating: 1800, waitedSeconds: 0);
        WebSocketBridge.RecordRecentOpponents("A", "B", NowUtc);

        var waiting = new List<WsSession> { first, recentOpponent, freshOpponent };

        Assert.Equal(2, WebSocketBridge.FindBestOpponentIndex(waiting, 0, ranked: false, NowUtc));
    }

    [Fact]
    public void 休闲队列_从真实匹配队列取对时优先配给非近期对手()
    {
        var alpha = QueuedSession("Alpha");
        var beta = QueuedSession("Beta");
        var gamma = QueuedSession("Gamma");
        WebSocketBridge.RecordRecentOpponents("Alpha", "Beta", DateTime.UtcNow);
        _matchQueue.Enqueue(alpha);
        _matchQueue.Enqueue(beta);
        _matchQueue.Enqueue(gamma);

        var paired = TryTakePair(out var actual0, out var actual1);

        Assert.True(paired);
        Assert.Same(alpha, actual0);
        Assert.Same(gamma, actual1);
        Assert.True(beta.IsMatching);
        Assert.Same(beta, Assert.Single(_matchQueue.ToArray()));
    }

    [Fact]
    public void 休闲队列_只剩近期对手且共同等待不足时不组成对局()
    {
        var alpha = QueuedSession("Alpha");
        var beta = QueuedSession("Beta");
        WebSocketBridge.RecordRecentOpponents("Alpha", "Beta", DateTime.UtcNow);
        _matchQueue.Enqueue(alpha);
        _matchQueue.Enqueue(beta);

        var paired = TryTakePair(out _, out _);

        Assert.False(paired);
        Assert.True(alpha.IsMatching);
        Assert.True(beta.IsMatching);
        Assert.Equal(2, _matchQueue.Count);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.IsMatching = false;
            session.Socket.Dispose();
        }
        ClearSharedState();
    }

    private void ClearSharedState()
    {
        _recentOpponents.Clear();
        while (_matchQueue.TryDequeue(out var session))
            session.IsMatching = false;
        foreach (var session in _sessions)
        {
            if (session.Account is not null
                && _accountIndex.TryGetValue(session.Account, out var currentSessionId)
                && currentSessionId == session.SessionId)
                _accountIndex.TryRemove(session.Account, out _);
        }
        ((ConcurrentDictionary<string, string>)MatchAccountReservationsField.GetValue(null)!).Clear();
    }

    private static WsSession WaitingSession(string account, double rating, int waitedSeconds)
        => new()
        {
            Account = account,
            MatchQueueKind = "ranked",
            MatchRating = rating,
            MatchEnqueuedAtUtc = NowUtc.AddSeconds(-waitedSeconds),
            MatchPlacementGames = GrandUMI.Game.Ranked.RankedStore.PlacementRequired,
            IsMatching = true,
        };

    private WsSession QueuedSession(string account)
    {
        var session = new WsSession
        {
            Socket = new OpenTestWebSocket(),
            Account = account,
            PlayerName = account,
            IsMatching = true,
            MatchQueueKind = "casual",
            MatchEnqueuedAtUtc = DateTime.UtcNow,
        };
        _sessions.Add(session);
        _accountIndex[account] = session.SessionId;
        return session;
    }

    private bool TryTakePair(out WsSession? player0, out WsSession? player1)
    {
        object?[] args = [null, null];
        var result = (bool)TryTakeMatchPairMethod.Invoke(null, args)!;
        player0 = args[0] as WsSession;
        player1 = args[1] as WsSession;
        return result;
    }

    private sealed class OpenTestWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
