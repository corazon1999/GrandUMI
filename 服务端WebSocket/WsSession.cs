using System.Net.WebSockets;
using GrandUMI.Diagnostics;

namespace GrandUMI;

public class WsSession
{
    public string    SessionId  { get; } = Guid.NewGuid().ToString("N");
    public WebSocket Socket     { get; init; } = null!;

    public string?   Account    { get; set; }
    public string?   PlayerName { get; set; }

    public string?   Deck       { get; set; }
    public bool      IsMatching       { get; set; }
    public string?   CurrentRoomCode  { get; set; }

    /// <summary>玩家设置：是否对所有生命牌都弹"是否发动触发"窗口（反信息泄露）</summary>
    public bool      AlwaysPromptOnLifeReveal { get; set; }

    private long _lastSeenUtcTicks = DateTime.UtcNow.Ticks;

    private readonly object _outboundGate = new();
    private readonly LinkedList<OutboundMessage> _outbound = new();
    private readonly SemaphoreSlim _outboundSignal = new(0);
    private LinkedListNode<OutboundMessage>? _replaceableState;
    private Func<OutboundMessage, Task>? _sender;
    private Task _senderLoop = Task.CompletedTask;
    private bool _senderStopping;
    private long _mergedStateCount;

    public bool IsLoggedIn => Account is not null;
    public long MergedStateCount => Interlocked.Read(ref _mergedStateCount);
    public DateTime LastSeenUtc => new(Interlocked.Read(ref _lastSeenUtcTicks), DateTimeKind.Utc);

    /// <summary>收到任意客户端消息时刷新，用于识别 WebSocket 半开连接。</summary>
    public void MarkSeen() => Interlocked.Exchange(ref _lastSeenUtcTicks, DateTime.UtcNow.Ticks);

    public bool IsRecentlyActive(TimeSpan maxIdle)
        => DateTime.UtcNow - LastSeenUtc <= maxIdle;

    public sealed record OutboundMessage(object Data, long EnqueuedAt, int QueueDepth, bool IsStateSnapshot);

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
    {
        lock (_outboundGate)
        {
            if (_senderStopping || _sender is null) return false;
            var enqueuedAt = LatencyDiagnostics.Start();

            if (isStateSnapshot && _replaceableState is not null)
            {
                _replaceableState.Value = new OutboundMessage(data, enqueuedAt, _outbound.Count, true);
                Interlocked.Increment(ref _mergedStateCount);
                return true;
            }

            var message = new OutboundMessage(data, enqueuedAt, _outbound.Count + 1, isStateSnapshot);
            var node = _outbound.AddLast(message);
            _replaceableState = isStateSnapshot ? node : null;
            _outboundSignal.Release();
            return true;
        }
    }

    public async Task StopSenderAsync()
    {
        lock (_outboundGate)
        {
            if (_senderStopping) return;
            _senderStopping = true;
            _replaceableState = null;
            _outboundSignal.Release();
        }
        await _senderLoop;
    }

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
                    if (ReferenceEquals(_replaceableState, first)) _replaceableState = null;
                    _outbound.RemoveFirst();
                }
                else if (_senderStopping)
                {
                    return;
                }
            }

            if (message is null) continue;
            try
            {
                LatencyDiagnostics.Observe("WebSocket 发送队列", message.EnqueuedAt,
                    $"会话={SessionId[..8]}，入队深度={message.QueueDepth}，已合并={MergedStateCount}");
                await _sender!(message);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WebSocket] 会话 {SessionId[..8]} 发送循环异常: {ex.Message}");
            }
        }
    }
}
