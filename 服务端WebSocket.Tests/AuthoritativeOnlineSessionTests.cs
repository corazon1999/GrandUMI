using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using Xunit;

namespace GrandUMI.Tests;

[CollectionDefinition("权威在线会话隔离", DisableParallelization = true)]
public sealed class AuthoritativeOnlineSessionCollectionDefinition;

[Collection("权威在线会话隔离")]
public sealed class AuthoritativeOnlineSessionTests : IDisposable
{
    private static readonly ConcurrentDictionary<string, WsSession> Sessions =
        (ConcurrentDictionary<string, WsSession>)typeof(WebSocketBridge)
            .GetField("Sessions", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static readonly ConcurrentDictionary<string, string> AccountIndex =
        (ConcurrentDictionary<string, string>)typeof(WebSocketBridge)
            .GetField("AccountIndex", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static readonly ConcurrentDictionary<string, DateTime> SupersededClientInstances =
        (ConcurrentDictionary<string, DateTime>)typeof(WebSocketBridge)
            .GetField("SupersededClientInstances", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static readonly MethodInfo TryBindAccountSession = typeof(WebSocketBridge).GetMethod(
        "TryBindAccountSession", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SupersedeSession = typeof(WebSocketBridge).GetMethod(
        "SupersedeSession", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CloseSession = typeof(WebSocketBridge).GetMethod(
        "CloseSession", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo SupersededSessionTotal = typeof(WebSocketBridge).GetField(
        "_supersededSessionTotal", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly List<WsSession> _sessions = [];
    private readonly HashSet<string> _clientInstanceIds = new(StringComparer.Ordinal);
    private readonly long _initialSupersededSessionTotal = WebSocketBridge.SupersededSessionTotal;

    [Fact]
    public void 同账号重复登录_旧会话立即失权且在线人数只统计唯一账号()
    {
        var baseline = WebSocketBridge.GetSessionMetrics();
        var account = Unique("duplicate-account");
        var oldSession = AddSession();
        var newSession = AddSession();

        Assert.True(Bind(oldSession, account, Unique("old-client"), isResume: false, out var firstReplaced));
        Assert.Null(firstReplaced);
        Assert.True(Bind(newSession, account, Unique("new-client"), isResume: false, out var replaced));

        Assert.Same(oldSession, replaced);
        Assert.True(oldSession.IsSuperseded);
        Assert.False(newSession.IsSuperseded);
        Assert.Equal(newSession.SessionId, AccountIndex[account]);

        var current = WebSocketBridge.GetSessionMetrics();
        Assert.Equal(baseline.ConnectionCount + 2, current.ConnectionCount);
        Assert.Equal(baseline.LoggedInSessionCount + 2, current.LoggedInSessionCount);
        Assert.Equal(baseline.UniqueLoggedInAccountCount + 1, current.UniqueLoggedInAccountCount);
        Assert.Equal(baseline.SupersededSessionCount + 1, current.SupersededSessionCount);
        Assert.Equal(baseline.SupersededSessionTotal + 1, current.SupersededSessionTotal);
        Assert.Equal(current.UniqueLoggedInAccountCount, WebSocketBridge.LoggedInCount);
    }

    [Fact]
    public void 并发重连风暴与乱序清理_始终只保留一个权威会话且旧清理不误删新索引()
    {
        var baseline = WebSocketBridge.GetSessionMetrics();
        var account = Unique("storm-account");
        var storm = Enumerable.Range(0, 128).Select(_ => AddSession()).ToArray();

        Parallel.ForEach(storm, session =>
        {
            var bound = Bind(session, account, Unique("storm-client"), isResume: false, out _);
            Assert.True(bound);
        });

        var ownerSessionId = AccountIndex[account];
        var owner = Assert.Single(storm.Where(session => session.SessionId == ownerSessionId));
        Assert.False(owner.IsSuperseded);
        Assert.Equal(127, storm.Count(session => session.IsSuperseded));

        var duringStorm = WebSocketBridge.GetSessionMetrics();
        Assert.Equal(baseline.ConnectionCount + 128, duringStorm.ConnectionCount);
        Assert.Equal(baseline.LoggedInSessionCount + 128, duringStorm.LoggedInSessionCount);
        Assert.Equal(baseline.UniqueLoggedInAccountCount + 1, duringStorm.UniqueLoggedInAccountCount);
        Assert.Equal(baseline.SupersededSessionCount + 127, duringStorm.SupersededSessionCount);

        Parallel.ForEach(
            storm.Where(session => session.SessionId != ownerSessionId).OrderBy(_ => Random.Shared.Next()),
            session => CloseSession.Invoke(null, [session]));

        Assert.Equal(ownerSessionId, AccountIndex[account]);
        var afterCleanup = WebSocketBridge.GetSessionMetrics();
        Assert.Equal(baseline.ConnectionCount + 1, afterCleanup.ConnectionCount);
        Assert.Equal(baseline.LoggedInSessionCount + 1, afterCleanup.LoggedInSessionCount);
        Assert.Equal(baseline.UniqueLoggedInAccountCount + 1, afterCleanup.UniqueLoggedInAccountCount);
        Assert.Equal(baseline.SupersededSessionCount, afterCleanup.SupersededSessionCount);
        Assert.Equal(baseline.SupersededSessionTotal + 127, afterCleanup.SupersededSessionTotal);
    }

    [Fact]
    public void 新客户端接管后_迟到的旧客户端恢复请求不能反夺账号索引()
    {
        var account = Unique("late-resume-account");
        var oldClientId = Unique("old-client");
        var newClientId = Unique("new-client");
        var oldSession = AddSession();
        var newSession = AddSession();
        var delayedResume = AddSession();

        Assert.True(Bind(oldSession, account, oldClientId, isResume: false, out _));
        Assert.True(Bind(newSession, account, newClientId, isResume: false, out var replaced));
        Assert.Same(oldSession, replaced);

        Assert.False(Bind(delayedResume, account, oldClientId, isResume: true, out var unexpectedReplacement));
        Assert.Null(unexpectedReplacement);
        Assert.Null(delayedResume.Account);
        Assert.Equal(newSession.SessionId, AccountIndex[account]);
        Assert.False(newSession.IsSuperseded);
    }

    [Fact]
    public async Task 被替代旧连接不响应关闭_有界超时后强制中止且重复清理只启动一次()
    {
        var baseline = WebSocketBridge.GetSessionMetrics();
        var account = Unique("hanging-close-account");
        var hangingSocket = new TestWebSocket(hangCloseOutput: true);
        var oldSession = AddSession(hangingSocket);
        oldSession.StartSender(_ => Task.CompletedTask);
        var newSession = AddSession();

        Assert.True(Bind(oldSession, account, Unique("hanging-old-client"), isResume: false, out _));
        Assert.True(Bind(newSession, account, Unique("hanging-new-client"), isResume: false, out var replaced));
        Assert.Same(oldSession, replaced);

        SupersedeSession.Invoke(null, [oldSession, "测试替代"]);
        SupersedeSession.Invoke(null, [oldSession, "重复替代"]);

        await hangingSocket.Aborted.WaitAsync(TimeSpan.FromSeconds(5));
        await oldSession.StopSenderAsync();

        Assert.Equal(1, hangingSocket.CloseOutputCallCount);
        Assert.Equal(WebSocketState.Aborted, hangingSocket.State);
        Assert.True(oldSession.IsSuperseded);
        Assert.Equal(newSession.SessionId, AccountIndex[account]);
        Assert.Equal(baseline.SupersededSessionTotal + 1, WebSocketBridge.SupersededSessionTotal);

        CloseSession.Invoke(null, [oldSession]);
        Assert.Equal(newSession.SessionId, AccountIndex[account]);
        Assert.Equal(baseline.UniqueLoggedInAccountCount + 1, WebSocketBridge.UniqueLoggedInAccountCount);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            Sessions.TryRemove(session.SessionId, out _);
            if (session.Account is not null &&
                AccountIndex.TryGetValue(session.Account, out var currentSessionId) &&
                currentSessionId == session.SessionId)
                AccountIndex.TryRemove(new KeyValuePair<string, string>(session.Account, currentSessionId));
            session.Socket.Dispose();
        }

        foreach (var clientInstanceId in _clientInstanceIds)
            SupersededClientInstances.TryRemove(clientInstanceId, out _);

        SupersededSessionTotal.SetValue(null, _initialSupersededSessionTotal);
    }

    private WsSession AddSession(TestWebSocket? socket = null)
    {
        var session = new WsSession { Socket = socket ?? new TestWebSocket() };
        _sessions.Add(session);
        Sessions[session.SessionId] = session;
        return session;
    }

    private bool Bind(
        WsSession session,
        string account,
        string clientInstanceId,
        bool isResume,
        out WsSession? supersededSession)
    {
        // 并发重连风暴测试会从多个工作线程同时登记清理键，普通 HashSet 不能并发写。
        lock (_clientInstanceIds) _clientInstanceIds.Add(clientInstanceId);
        object?[] args = [session, account, clientInstanceId, isResume, null];
        var result = (bool)TryBindAccountSession.Invoke(null, args)!;
        supersededSession = args[4] as WsSession;
        return result;
    }

    private static string Unique(string prefix) => $"test-{prefix}-{Guid.NewGuid():N}";

    private sealed class TestWebSocket(bool hangCloseOutput = false) : WebSocket
    {
        private readonly TaskCompletionSource _aborted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _neverClose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state = (int)WebSocketState.Open;
        private int _closeOutputCallCount;

        public Task Aborted => _aborted.Task;
        public int CloseOutputCallCount => Volatile.Read(ref _closeOutputCallCount);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => (WebSocketState)Volatile.Read(ref _state);
        public override string? SubProtocol => null;

        public override void Abort()
        {
            Volatile.Write(ref _state, (int)WebSocketState.Aborted);
            _aborted.TrySetResult();
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref _state, (int)WebSocketState.Closed);
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _closeOutputCallCount);
            if (hangCloseOutput) return _neverClose.Task;
            Volatile.Write(ref _state, (int)WebSocketState.CloseSent);
            return Task.CompletedTask;
        }

        public override void Dispose()
            => Volatile.Write(ref _state, (int)WebSocketState.Closed);

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
