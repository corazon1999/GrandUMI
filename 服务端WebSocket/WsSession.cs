using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using GrandUMI.Diagnostics;

namespace GrandUMI;

public class WsSession
{
    public const int MaxOutboundMessages = 256;
    public const string GameStateCoalesceKey = "game-state";

    public enum OutboundPriority
    {
        BestEffort,
        Normal,
        Critical,
    }

    public string    SessionId  { get; } = Guid.NewGuid().ToString("N");
    public WebSocket Socket     { get; init; } = null!;

    public string?   Account    { get; set; }
    /// <summary>
    /// 白名单从未导入时，经账号密码认证且在首次迁移快照中的管理员账号。
    /// 此字段不代表完整登录，不计在线，也不能访问白名单状态/导入之外的业务协议。
    /// </summary>
    public string?   QqBootstrapAccount { get; set; }
    public string?   ClientInstanceId { get; set; }
    public string?   PlayerName { get; set; }
    public string    CardBackId { get; set; } = Persistence.PlayerDataStore.DefaultCardBackId;

    public string?   Deck       { get; set; }
    public string?   DeckName   { get; set; }
    public bool      IsMatching       { get; set; }
    /// <summary>公开匹配队列类型；旧客户端未传时默认兼容狂野休闲，但仍执行官网禁卡表。</summary>
    public string    MatchQueueKind   { get; set; } = "casual";
    public DateTime  MatchEnqueuedAtUtc { get; set; }
    public double    MatchRating      { get; set; } = 1500;
    public string?   CurrentRoomCode  { get; set; }

    /// <summary>本连接的默认观战权限；建局时固化到对局。</summary>
    public string    SpectateMode { get; set; } = Game.SpectatingRules.Open;
    public bool      SpectatorHandsPublic { get; set; }
    public string?   SpectateCode { get; set; }

    /// <summary>玩家设置：是否对所有生命牌都弹"是否发动触发"窗口（反信息泄露）</summary>
    public bool      AlwaysPromptOnLifeReveal { get; set; }

    private long _lastSeenUtcTicks = DateTime.UtcNow.Ticks;

    private readonly object _outboundGate = new();
    private readonly LinkedList<OutboundMessage> _outbound = new();
    private readonly Dictionary<string, LinkedListNode<OutboundMessage>> _coalesced = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _outboundSignal = new(0);
    private Func<OutboundMessage, Task>? _sender;
    private Task _senderLoop = Task.CompletedTask;
    private bool _senderStopping;
    private long _mergedStateCount;
    private long _droppedOutboundCount;
    private int _maxOutboundDepth;
    private readonly object _rateLimitGate = new();
    private readonly Dictionary<string, RateBucket> _rateBuckets = new(StringComparer.Ordinal);
    private int _superseded;
    private int _supersededCleanupStarted;
    private int _qqAccessRevoked;
    private int _qqRevokedCloseMonitorStarted;

    /// <summary>由握手能力协商决定；旧客户端保持完整快照。</summary>
    public bool SupportsDeltaSnapshots { get; set; }
    internal JsonElement? SnapshotBaseline { get; private set; }
    internal int SnapshotBaselineTick { get; private set; } = -1;
    internal int SnapshotDeltasSinceFull { get; private set; }

    public bool IsLoggedIn => Account is not null;
    /// <summary>
    /// 当前连接已被同账号的新连接替代。该状态不可逆；一旦失去账号权威，旧连接不得再收发业务消息，
    /// 但仍保留 Account 供乱序断开时完成房间、邀请等会话级清理。
    /// </summary>
    public bool IsSuperseded => Volatile.Read(ref _superseded) != 0;
    /// <summary>管理员解绑或白名单变化后，仅允许当前已注册对局继续；离开对局即终止会话。</summary>
    public bool IsQqAccessRevoked => Volatile.Read(ref _qqAccessRevoked) != 0;
    public long MergedStateCount => Interlocked.Read(ref _mergedStateCount);
    public long DroppedOutboundCount => Interlocked.Read(ref _droppedOutboundCount);
    public int OutboundDepth { get { lock (_outboundGate) return _outbound.Count; } }
    public int MaxOutboundDepth => Volatile.Read(ref _maxOutboundDepth);
    public DateTime LastSeenUtc => new(Interlocked.Read(ref _lastSeenUtcTicks), DateTimeKind.Utc);

    /// <summary>收到任意客户端消息时刷新，用于识别 WebSocket 半开连接。</summary>
    public void MarkSeen() => Interlocked.Exchange(ref _lastSeenUtcTicks, DateTime.UtcNow.Ticks);

    public bool IsRecentlyActive(TimeSpan maxIdle)
        => DateTime.UtcNow - LastSeenUtc <= maxIdle;

    public void MarkQqAccessRevoked() => Volatile.Write(ref _qqAccessRevoked, 1);
    public void ClearQqAccessRevoked() => Volatile.Write(ref _qqAccessRevoked, 0);
    public bool TryStartQqRevokedCloseMonitor()
        => Interlocked.CompareExchange(ref _qqRevokedCloseMonitorStarted, 1, 0) == 0;

    public sealed record OutboundMessage(
        object Data,
        long EnqueuedAt,
        int QueueDepth,
        bool IsStateSnapshot,
        string? CoalesceKey = null,
        OutboundPriority Priority = OutboundPriority.Normal,
        TaskCompletionSource<bool>? Completion = null,
        bool StopsSender = false);

    /// <summary>每个连接只启动一个发送循环，从根源上保证 WebSocket SendAsync 不并发且顺序稳定。</summary>
    public void StartSender(Func<OutboundMessage, Task> sender)
    {
        lock (_outboundGate)
        {
            if (_sender is not null) return;
            _sender = sender;
            _senderLoop = Task.Run(SenderLoopAsync);
        }
    }

    /// <summary>
    /// 入队待发送消息。连续、尚未发送的游戏状态快照可原位替换；
    /// 任何控制消息都会切断合并窗口，确保 Prompt/拒绝/聊天等顺序不被跨越。
    /// </summary>
    public bool Enqueue(object data, bool isStateSnapshot)
        => Enqueue(
            data,
            isStateSnapshot ? GameStateCoalesceKey : null,
            isStateSnapshot ? OutboundPriority.BestEffort : OutboundPriority.Normal,
            isStateSnapshot);

    /// <summary>按优先级和合并键入队；队列满时优先淘汰可丢弃消息，绝不无限增长。</summary>
    public bool Enqueue(object data, string? coalesceKey, OutboundPriority priority, bool isStateSnapshot = false)
    {
        lock (_outboundGate)
        {
            if (_senderStopping || _sender is null || IsSuperseded) return false;
            var enqueuedAt = LatencyDiagnostics.Start();

            if (coalesceKey is not null && _coalesced.TryGetValue(coalesceKey, out var replaceable))
            {
                replaceable.Value = new OutboundMessage(
                    data, enqueuedAt, _outbound.Count, isStateSnapshot, coalesceKey, priority);
                Interlocked.Increment(ref _mergedStateCount);
                return true;
            }

            // 控制消息必须切断连续状态合并，保证 Prompt 等消息不会被后来的状态跨越。
            if (!string.Equals(coalesceKey, GameStateCoalesceKey, StringComparison.Ordinal))
                _coalesced.Remove(GameStateCoalesceKey);

            if (_outbound.Count >= MaxOutboundMessages && !TryEvictBestEffort(priority))
            {
                Interlocked.Increment(ref _droppedOutboundCount);
                LatencyDiagnostics.RecordMetric("WebSocket 丢弃消息", 1, "条");
                return false;
            }

            var message = new OutboundMessage(
                data, enqueuedAt, _outbound.Count + 1, isStateSnapshot, coalesceKey, priority);
            var node = _outbound.AddLast(message);
            if (coalesceKey is not null) _coalesced[coalesceKey] = node;
            UpdateMaxOutboundDepth(_outbound.Count);
            _outboundSignal.Release();
            return true;
        }
    }

    public async Task StopSenderAsync()
    {
        Task senderLoop;
        lock (_outboundGate)
        {
            if (!_senderStopping)
            {
                _senderStopping = true;
                _coalesced.Clear();
                _outboundSignal.Release();
            }
            senderLoop = _senderLoop;
        }
        await senderLoop;
    }

    /// <summary>清空待发数据，只发送最后一条终止通知，并在通知写出后停止发送循环。</summary>
    public Task<bool> EnqueueTerminalAsync(object data)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_outboundGate)
        {
            if (_senderStopping || _sender is null) return Task.FromResult(false);
            _senderStopping = true;
            _outbound.Clear();
            _coalesced.Clear();
            var enqueuedAt = LatencyDiagnostics.Start();
            _outbound.AddLast(new OutboundMessage(
                data,
                enqueuedAt,
                1,
                IsStateSnapshot: false,
                Priority: OutboundPriority.Critical,
                Completion: completion,
                StopsSender: true));
            _outboundSignal.Release();
        }
        return completion.Task;
    }

    /// <summary>原子标记为已被替代，并丢弃尚未发送的普通消息；终止通知由专用入口随后入队。</summary>
    internal bool TryMarkSuperseded()
    {
        if (Interlocked.CompareExchange(ref _superseded, 1, 0) != 0) return false;
        lock (_outboundGate)
        {
            _outbound.Clear();
            _coalesced.Clear();
        }
        return true;
    }

    /// <summary>保证同一旧会话只启动一个终止与强制回收流程。</summary>
    internal bool TryBeginSupersededCleanup()
        => Interlocked.CompareExchange(ref _supersededCleanupStarted, 1, 0) == 0;

    private async Task SenderLoopAsync()
    {
        while (true)
        {
            await _outboundSignal.WaitAsync();

            OutboundMessage? message = null;
            lock (_outboundGate)
            {
                if (_outbound.First is { } first)
                {
                    message = first.Value;
                    if (message.CoalesceKey is not null
                        && _coalesced.TryGetValue(message.CoalesceKey, out var indexed)
                        && ReferenceEquals(indexed, first))
                        _coalesced.Remove(message.CoalesceKey);
                    _outbound.RemoveFirst();
                }
                else if (_senderStopping)
                {
                    return;
                }
            }

            if (message is null) continue;
            var sent = true;
            try
            {
                LatencyDiagnostics.Observe("WebSocket 发送队列", message.EnqueuedAt,
                    $"会话={SessionId[..8]}，入队深度={message.QueueDepth}，已合并={MergedStateCount}");
                LatencyDiagnostics.RecordMetric("WebSocket 发送队列深度", message.QueueDepth, "条");
                await _sender!(message);
            }
            catch (Exception ex)
            {
                sent = false;
                Console.Error.WriteLine($"[WebSocket] 会话 {SessionId[..8]} 发送循环异常: {ex.Message}");
            }
            finally
            {
                message.Completion?.TrySetResult(sent);
            }
            if (message.StopsSender) return;
        }
    }

    internal void CommitSnapshotBaseline(SnapshotWireCodec.EncodedPayload encoded)
    {
        if (!encoded.IsStateSnapshot || encoded.NewBaseline is null) return;
        SnapshotBaseline = encoded.NewBaseline.Value;
        SnapshotBaselineTick = encoded.Tick;
        SnapshotDeltasSinceFull = encoded.DeltasSinceFull;
    }

    /// <summary>连接级令牌桶。用于限制聊天、列表、匹配等高放大入口。</summary>
    public bool TryConsumeRateLimit(string bucket, double capacity, double refillPerSecond, double cost = 1)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_rateLimitGate)
        {
            if (!_rateBuckets.TryGetValue(bucket, out var state))
                state = new RateBucket(capacity, now);

            var elapsed = Stopwatch.GetElapsedTime(state.UpdatedAt, now).TotalSeconds;
            var available = Math.Min(capacity, state.Tokens + elapsed * refillPerSecond);
            if (available < cost)
            {
                _rateBuckets[bucket] = new RateBucket(available, now);
                return false;
            }

            _rateBuckets[bucket] = new RateBucket(available - cost, now);
            return true;
        }
    }

    private bool TryEvictBestEffort(OutboundPriority incomingPriority)
    {
        if (incomingPriority == OutboundPriority.BestEffort) return false;
        var candidate = _outbound.First;
        while (candidate is not null && candidate.Value.Priority != OutboundPriority.BestEffort)
            candidate = candidate.Next;
        if (candidate is null) return false;

        var message = candidate.Value;
        if (message.CoalesceKey is not null
            && _coalesced.TryGetValue(message.CoalesceKey, out var indexed)
            && ReferenceEquals(indexed, candidate))
            _coalesced.Remove(message.CoalesceKey);
        _outbound.Remove(candidate);
        Interlocked.Increment(ref _droppedOutboundCount);
        return true;
    }

    private void UpdateMaxOutboundDepth(int depth)
    {
        var current = Volatile.Read(ref _maxOutboundDepth);
        while (depth > current)
        {
            var observed = Interlocked.CompareExchange(ref _maxOutboundDepth, depth, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private readonly record struct RateBucket(double Tokens, long UpdatedAt);
}
