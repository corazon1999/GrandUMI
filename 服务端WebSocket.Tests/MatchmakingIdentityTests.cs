using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

[CollectionDefinition("匹配身份隔离", DisableParallelization = true)]
public sealed class MatchmakingIdentityCollectionDefinition;

[Collection("匹配身份隔离")]
public sealed class MatchmakingIdentityTests : IDisposable
{
    private static readonly FieldInfo MatchQueueField = typeof(WebSocketBridge).GetField(
        "MatchQueue", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo AccountIndexField = typeof(WebSocketBridge).GetField(
        "AccountIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo MatchAccountReservationsField = typeof(WebSocketBridge).GetField(
        "MatchAccountReservations", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo TryTakeMatchPairMethod = typeof(WebSocketBridge).GetMethod(
        "TryTakeMatchPair", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo TryReserveMatchAccountMethod = typeof(WebSocketBridge).GetMethod(
        "TryReserveMatchAccount", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ReleaseMatchAccountReservationMethod = typeof(WebSocketBridge).GetMethod(
        "ReleaseMatchAccountReservation", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly ConcurrentQueue<WsSession> _matchQueue =
        (ConcurrentQueue<WsSession>)MatchQueueField.GetValue(null)!;
    private readonly ConcurrentDictionary<string, string> _accountIndex =
        (ConcurrentDictionary<string, string>)AccountIndexField.GetValue(null)!;
    private readonly ConcurrentDictionary<string, string> _matchAccountReservations =
        (ConcurrentDictionary<string, string>)MatchAccountReservationsField.GetValue(null)!;
    private readonly List<WsSession> _sessions = [];

    public MatchmakingIdentityTests()
    {
        Assert.NotNull(MatchQueueField);
        Assert.NotNull(AccountIndexField);
        Assert.NotNull(MatchAccountReservationsField);
        Assert.NotNull(TryTakeMatchPairMethod);
        Assert.NotNull(TryReserveMatchAccountMethod);
        Assert.NotNull(ReleaseMatchAccountReservationMethod);
        DrainQueue();
        _matchAccountReservations.Clear();
    }

    [Fact]
    public void 同一会话重复入队_不会与自己组成对局且只保留一份等待项()
    {
        var player = Session("重复玩家");
        player.IsMatching = true;
        _matchQueue.Enqueue(player);
        _matchQueue.Enqueue(player);

        var paired = TryTakePair(out _, out _);

        Assert.False(paired);
        Assert.True(player.IsMatching);
        Assert.Same(player, Assert.Single(_matchQueue.ToArray()));
    }

    [Fact]
    public void 同账号不同连接_旧连接失效且不能互相组成对局()
    {
        var oldSession = Session("同一账号");
        var currentSession = Session("同一账号");
        oldSession.IsMatching = true;
        currentSession.IsMatching = true;
        _accountIndex["同一账号"] = currentSession.SessionId;
        _matchQueue.Enqueue(oldSession);
        _matchQueue.Enqueue(currentSession);

        var paired = TryTakePair(out _, out _);

        Assert.False(paired);
        Assert.False(oldSession.IsMatching);
        Assert.True(currentSession.IsMatching);
        Assert.Same(currentSession, Assert.Single(_matchQueue.ToArray()));
    }

    [Fact]
    public void 不同玩家_可以被原子取出为一组且不会再次留在匹配中()
    {
        var player0 = Session("玩家甲");
        var player1 = Session("玩家乙");
        player0.IsMatching = true;
        player1.IsMatching = true;
        _matchQueue.Enqueue(player0);
        _matchQueue.Enqueue(player1);

        var paired = TryTakePair(out var actual0, out var actual1);

        Assert.True(paired);
        Assert.Same(player0, actual0);
        Assert.Same(player1, actual1);
        Assert.False(player0.IsMatching);
        Assert.False(player1.IsMatching);
        Assert.Empty(_matchQueue);
    }

    [Fact]
    public void 房间创建_拒绝相同会话或忽略大小写后相同的真人账号()
    {
        var roomCount = GameRoomManager.RoomCount;

        var sameSession = Assert.Throws<InvalidOperationException>(() => GameRoomManager.CreateRoom(
            "same-session", "玩家甲", "",
            "same-session", "玩家乙", "",
            broadcastInitialState: false));
        var sameAccount = Assert.Throws<InvalidOperationException>(() => GameRoomManager.CreateRoom(
            "session-a", "Player", "",
            "session-b", "player", "",
            broadcastInitialState: false));

        Assert.Contains("同一连接", sameSession.Message);
        Assert.Contains("同一账号", sameAccount.Message);
        Assert.Equal(roomCount, GameRoomManager.RoomCount);
    }

    [Fact]
    public void 同账号并发申请匹配占位_只有一个请求成功()
    {
        var session = Session("并发匹配玩家");
        var results = new bool[2];

        Parallel.For(0, 2, index =>
            results[index] = (bool)TryReserveMatchAccountMethod.Invoke(null, [session])!);

        Assert.Single(results.Where(result => result));
        ReleaseMatchAccountReservationMethod.Invoke(null, [session]);
        Assert.Empty(_matchAccountReservations);
    }

    [Fact]
    public void 已在房间中的账号_不能被创建进第二个房间()
    {
        var deck = string.Join('\n', new[] { "OP15-001" }.Concat(Enumerable.Repeat("OP15-003", 50)));
        var room = GameRoomManager.CreateRoom(
            "seat-a", "占座玩家", deck,
            "seat-b", "对手甲", deck,
            broadcastInitialState: false);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => GameRoomManager.CreateRoom(
                "seat-c", "占座玩家", deck,
                "seat-d", "对手乙", deck,
                broadcastInitialState: false));
            Assert.Contains("已在其他对局", error.Message);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.IsMatching = false;
            if (session.Account is not null
                && _accountIndex.TryGetValue(session.Account, out var currentSessionId)
                && currentSessionId == session.SessionId)
                _accountIndex.TryRemove(session.Account, out _);
            session.Socket.Dispose();
        }
        DrainQueue();
        _matchAccountReservations.Clear();
    }

    private WsSession Session(string account)
    {
        var session = new WsSession
        {
            Socket = new OpenTestWebSocket(),
            Account = account,
            PlayerName = account,
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

    private void DrainQueue()
    {
        while (_matchQueue.TryDequeue(out var session))
            session.IsMatching = false;
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
