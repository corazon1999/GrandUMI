using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using GrandUMI.Cluster;
using GrandUMI.Diagnostics;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Stats;

namespace GrandUMI.Game;

/// <summary>
/// 房间池：管理活跃的 GameEngine 实例 + 会话↔房间映射 + 断线宽限期
/// </summary>
public static class GameRoomManager
{
    public static IRoomPlacementDirectory RoomDirectory { get; set; } = LocalRoomPlacementDirectory.Instance;
    private const int GracePeriodSeconds = 90;
    /// <summary>仅排障时开启；私有快照平均约 63 KB，不应作为正式服常态日志。</summary>
    private static readonly bool PrivateSnapshotLogEnabled =
        string.Equals(Environment.GetEnvironmentVariable("GRANDUMI_PRIVATE_SNAPSHOT_LOG"), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("GRANDUMI_PRIVATE_SNAPSHOT_LOG"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>对局存活上限：自最后一次操作起超过此时长即弃局（重启不恢复）。</summary>
    private static readonly TimeSpan RestoreTtl = TimeSpan.FromMinutes(30);

    /// <summary>房间池</summary>
    private static readonly ConcurrentDictionary<string, RoomEntry> _rooms = new();
    private static int _roomsBeingCreated;

    public static int RoomCount => _rooms.Count;
    public static int SpectatorCount => _rooms.Values.Sum(room => room.Spectators.Count);
    public static int TotalActionQueueDepth => _rooms.Values.Sum(room => Math.Max(0, Volatile.Read(ref room.ActionQueueDepth)));

    /// <summary>sessionId → roomId</summary>
    private static readonly ConcurrentDictionary<string, string> _sessionRoom = new();

    /// <summary>roomId → 断线计时器</summary>
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _grace = new();

    /// <summary>roomId → 调度手牌超时任务；实际结算仍排入房间串行队列。</summary>
    private static readonly ConcurrentDictionary<string, MulliganTimeout> _mulliganTimeouts = new();
    private sealed record MulliganTimeout(DateTime DeadlineUtc, CancellationTokenSource Cancellation);

    public class RoomEntry
    {
        public required string RoomId { get; init; }
        public required GameEngine Engine { get; init; }
        public required string[] PlayerSessionIds { get; init; }  // [P0, P1]
        public required string[] PlayerAccounts   { get; init; }
        /// <summary>观战会话及其主视角座位（0/1）。</summary>
        public ConcurrentDictionary<string, int> Spectators { get; } = new();
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public string? ReplayPath { get; set; }
        public string? MatchLogPath { get; set; }
        /// <summary>是否为单人测试模式（P1 为机器人）</summary>
        public bool VsBot { get; init; }
        /// <summary>对局来源，用于 Leader 统计与后续分模式分析。</summary>
        public MatchKind MatchKind { get; init; }
        /// <summary>机器人思考是否已排队（去抖）</summary>
        public bool BotScheduled { get; set; }
        /// <summary>关联的友谊战房间 ID(非友谊战为 null);对局结束时回调更新比分并退回房间</summary>
        public string? FriendlyRoomId { get; set; }
        internal Channel<RoomWork> ActionQueue { get; } = Channel.CreateBounded<RoomWork>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        internal Task ActionWorker { get; set; } = Task.CompletedTask;
        internal int ActionQueueDepth;
    }

    internal sealed record RoomWork(string Name, long EnqueuedAt, Func<Task> Execute, long ReceivedAt = 0);

    /// <summary>双方匹配/房间码成功后创建房间</summary>
    public static RoomEntry CreateRoom(string p0Sid, string p0Account, string p0Deck,
                                        string p1Sid, string p1Account, string p1Deck,
                                        bool? p0First = null,
                                         bool p0AlwaysPrompt = false, bool p1AlwaysPrompt = false,
                                          string p0CardBackId = "classic", string p1CardBackId = "classic",
                                          bool vsBot = false,
                                          string? friendlyRoomId = null,
                                          MatchKind matchKind = MatchKind.UnknownHuman,
                                          bool broadcastInitialState = true)
    {
        if (!ServerCapacity.CanCreateRoom(out var overloadReason))
            throw new InvalidOperationException($"服务器暂时无法创建新对局：{overloadReason}");
        using var roomAdmission = ReserveRoomCreation();

        if (vsBot) matchKind = MatchKind.Bot;
        else if (friendlyRoomId is not null && matchKind == MatchKind.UnknownHuman)
            matchKind = MatchKind.Friendly;

        var roomId = Guid.NewGuid().ToString("N")[..12];
        var firstPlayer = p0First.HasValue ? (p0First.Value ? 0 : 1) : -1;
        var openingSetupAfterFirstPlayerChoice = firstPlayer < 0;
        var engine = new GameEngine(roomId,
            (p0Sid, p0Account, p0Deck),
            (p1Sid, p1Account, p1Deck),
            firstPlayer: firstPlayer,
            leaderKeywordWildcard: vsBot,
            deferOpeningSetupUntilFirstPlayerChosen: openingSetupAfterFirstPlayerChoice);
        engine.EnablePrivateSnapshotLog = PrivateSnapshotLogEnabled;
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;
        engine.State.Players[0].CardBackId = p0CardBackId;
        engine.State.Players[1].CardBackId = p1CardBackId;

        var entry = new RoomEntry
        {
            RoomId = roomId,
            Engine = engine,
            PlayerSessionIds = new[] { p0Sid, p1Sid },
            PlayerAccounts   = new[] { p0Account, p1Account },
            VsBot = vsBot,
            MatchKind = matchKind,
            FriendlyRoomId = friendlyRoomId,
        };

        // 配置回调：人类走 WS 下发；单人模式下 P1(机器人) 的消息驱动 BotDriver 思考
        engine.OnSendToPlayer = (idx, payload) =>
        {
            if (idx == 1 && vsBot) { BotDriver.OnBotMessage(entry); return; }
            WebSocketBridge.Send(entry.PlayerSessionIds[idx], payload);
        };

        engine.OnSendToSpectators = (viewPlayerIndex, payload) =>
        {
            foreach (var spectator in entry.Spectators)
            {
                if (spectator.Value == viewPlayerIndex)
                    WebSocketBridge.Send(spectator.Key, payload);
            }
        };
        engine.HasSpectators = () => !entry.Spectators.IsEmpty;
        engine.HasSpectatorsForPerspective = viewPlayerIndex =>
            entry.Spectators.Values.Any(value => value == viewPlayerIndex);
        entry.ReplayPath = ReplayRecorder.Open(roomId);
        entry.MatchLogPath = MatchLogRecorder.Open(roomId);
        engine.OnReplay = (entryObj) => ReplayRecorder.Append(roomId, entryObj);
        engine.OnMatchLog = (kind, actor, payload) => MatchLogRecorder.Append(roomId, engine.State, kind, actor, payload);

        engine.RecordMatchLog("match_start", -1, new
        {
            players = new[]
            {
                new { index = 0, accountName = p0Account, deckRaw = p0Deck },
                new { index = 1, accountName = p1Account, deckRaw = p1Deck },
            },
            firstPlayer,
            startingPlayerChooser = engine.State.StartingPlayerChooser,
            startingDiceRolls = engine.State.StartingDiceRounds,
            rngSeed = engine.State.RngSeed,
            openingSetupAfterFirstPlayerChoice,
            matchKind = matchKind.ToString(),
            rulesVersion = "opcg-grandumi-v1",
            cardDbVersion = "local-card-json",
        });
        engine.FlushPendingMatchLogs();

        // 动作日志持久化（仅 PvP 真人对局；重启恢复用）
        if (!vsBot)
        {
            RoomJournal.Open(roomId, new
            {
                kind = "create",
                roomId,
                seed = engine.State.RngSeed,
                firstPlayer,
                openingSetupAfterFirstPlayerChoice,
                p0 = new { account = p0Account, deckRaw = p0Deck, alwaysPrompt = p0AlwaysPrompt, cardBackId = p0CardBackId },
                p1 = new { account = p1Account, deckRaw = p1Deck, alwaysPrompt = p1AlwaysPrompt, cardBackId = p1CardBackId },
                vsBot,
                matchKind = matchKind.ToString(),
                createdAtUtc = DateTime.UtcNow,
            });
            engine.OnPersistAction = (pi, act, data) => RoomJournal.Append(roomId, pi, act, data);
        }

        _rooms[roomId] = entry;
        RoomDirectory.RegisterLocal(roomId);
        WebSocketBridge.OnGameChatParticipantJoined(p0Sid);
        WebSocketBridge.OnGameChatParticipantJoined(p1Sid);
        _sessionRoom[p0Sid] = roomId;
        _sessionRoom[p1Sid] = roomId;
        WebSocketBridge.BroadcastSpectatorList(entry);
        StartActionWorker(entry);
        EnsureMulliganTimeout(entry);

        // 骰点对局先等待胜者选择先后手；单人测试沿用预设先后手并直接进入 mulligan。
        if (broadcastInitialState)
            engine.BroadcastInitialState();
        return entry;
    }

    private static IDisposable ReserveRoomCreation()
    {
        var creating = Interlocked.Increment(ref _roomsBeingCreated);
        if (RoomCount + creating <= ServerCapacity.MaxRooms) return new RoomAdmissionLease();
        Interlocked.Decrement(ref _roomsBeingCreated);
        throw new InvalidOperationException("服务器暂时无法创建新对局：room_limit");
    }

    private sealed class RoomAdmissionLease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Interlocked.Decrement(ref _roomsBeingCreated);
        }
    }

    public static RoomEntry? GetRoomBySession(string sessionId)
        => _sessionRoom.TryGetValue(sessionId, out var rid) && _rooms.TryGetValue(rid, out var e) ? e : null;

    public static RoomEntry? GetRoom(string roomId)
        => _rooms.TryGetValue(roomId, out var e) ? e : null;

    /// <summary>客户端通过 MsgGameAction 派发的入口</summary>
    public static void HandleAction(
        string sessionId,
        string action,
        System.Text.Json.JsonElement data,
        string? requestId = null,
        long receivedAt = 0)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null)
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "你不在任何对局中", requestId });
            return;
        }
        int idx = Array.IndexOf(room.PlayerSessionIds, sessionId);
        if (idx < 0)
        {
            // 观战者，禁止操作
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "观战者不能操作", requestId });
            return;
        }
        if (!EnqueuePlayerAction(room, idx, action, data.Clone(), requestId, receivedAt))
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "对局正在结束，操作未执行", requestId });
    }

    internal static void EnqueueBotAction(RoomEntry room, int playerIndex, string action, JsonElement data)
        => EnqueuePlayerAction(room, playerIndex, action, data.Clone());

    private static bool EnqueuePlayerAction(
        RoomEntry room,
        int playerIndex,
        string action,
        JsonElement data,
        string? requestId = null,
        long receivedAt = 0)
    {
        var promptIdBefore = action == "PromptResponse" ? room.Engine.State.PendingPrompt?.PromptId : null;
        return EnqueueWork(room, new RoomWork(action, LatencyDiagnostics.Start(), async () =>
        {
            room.Engine.RecordMatchLog("player_action_requested", playerIndex, new { action, data });
            var accepted = room.Engine.HandleAction(playerIndex, action, data, requestId);
            // 被拒绝的 PromptResponse 不会消费旧 Prompt，不应等待效果链稳定；
            // 否则单读者房间队列会被卡到等待超时，后续合法响应也无法进入。
            if (accepted)
                await room.Engine.WaitSettledAsync(resolvingPromptId: promptIdBefore);
            EnsureMulliganTimeout(room);
            if (room.Engine.State.IsGameOver)
                CleanupRoom(room.RoomId);
        }, receivedAt));
    }

    /// <summary>根据服务端权威截止时间创建或清除调度超时任务；不会因客户端断线或后台而停止。</summary>
    private static void EnsureMulliganTimeout(RoomEntry room)
    {
        var deadline = room.Engine.State.MulliganDeadlineUtc;
        if (deadline is null || room.Engine.State.MulliganBothDone)
        {
            CancelMulliganTimeout(room.RoomId);
            return;
        }

        if (_mulliganTimeouts.TryGetValue(room.RoomId, out var current)
            && current.DeadlineUtc == deadline.Value)
            return;

        var next = new MulliganTimeout(deadline.Value, new CancellationTokenSource());
        while (true)
        {
            if (_mulliganTimeouts.TryGetValue(room.RoomId, out current))
            {
                if (current.DeadlineUtc == deadline.Value)
                {
                    next.Cancellation.Dispose();
                    return;
                }
                if (!_mulliganTimeouts.TryUpdate(room.RoomId, next, current)) continue;
                current.Cancellation.Cancel();
                current.Cancellation.Dispose();
            }
            else if (!_mulliganTimeouts.TryAdd(room.RoomId, next))
            {
                continue;
            }

            StartMulliganTimeoutWait(room, next);
            return;
        }
    }

    private static void StartMulliganTimeoutWait(RoomEntry room, MulliganTimeout timer)
    {
        _ = Task.Run(async () =>
        {
            // 系统时钟校准或计时器精度可能让 Task.Delay 提前极短时间返回；
            // 必须重新核对服务端权威截止时间，避免唯一一次超时任务被提前消费。
            while (true)
            {
                var delay = timer.DeadlineUtc - DateTime.UtcNow;
                if (delay <= TimeSpan.Zero) break;
                try { await Task.Delay(delay, timer.Cancellation.Token); }
                catch (OperationCanceledException) { return; }
            }

            if (timer.Cancellation.IsCancellationRequested) return;
            var active = GetRoom(room.RoomId);
            if (active is null || !ReferenceEquals(active, room)) return;

            // 超时结算属于不可丢失的房间控制动作。普通 TryWrite 在队列暂满时会返回 false，
            // 如果忽略该结果，客户端就会永久停在“剩余 0 秒”。这里等待到成功入队或房间关闭。
            await EnqueueCriticalWorkAsync(active, new RoomWork("MulliganTimeout", LatencyDiagnostics.Start(), async () =>
            {
                if (active.Engine.State.MulliganDeadlineUtc == timer.DeadlineUtc)
                    await ResolveExpiredMulliganAsync(active, DateTime.UtcNow);
                EnsureMulliganTimeout(active);
            }), timer.Cancellation.Token);
        });
    }

    /// <summary>补做已过期的调度选择；供计时器、刷新取状态和账号重绑共同复用。</summary>
    private static async Task<IReadOnlyList<int>> ResolveExpiredMulliganAsync(RoomEntry room, DateTime utcNow)
    {
        var autoKept = room.Engine.AutoKeepMulligans(utcNow);
        if (autoKept.Count == 0) return autoKept;

        foreach (var playerIndex in autoKept)
        {
            var data = JsonSerializer.SerializeToElement(new { redraw = false });
            room.Engine.RecordMatchLog("mulligan_timeout_auto_keep", playerIndex, new { redraw = false });
            room.Engine.OnPersistAction?.Invoke(playerIndex, "Mulligan", data);
        }
        await room.Engine.WaitSettledAsync();
        return autoKept;
    }

    private static void CancelMulliganTimeout(string roomId)
    {
        if (_mulliganTimeouts.TryRemove(roomId, out var timer))
        {
            timer.Cancellation.Cancel();
            timer.Cancellation.Dispose();
        }
    }

    private static bool EnqueueWork(RoomEntry room, RoomWork work)
    {
        Interlocked.Increment(ref room.ActionQueueDepth);
        if (room.ActionQueue.Writer.TryWrite(work)) return true;
        Interlocked.Decrement(ref room.ActionQueueDepth);
        return false;
    }

    private static async Task<bool> EnqueueCriticalWorkAsync(
        RoomEntry room,
        RoomWork work,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref room.ActionQueueDepth);
        try
        {
            await room.ActionQueue.Writer.WriteAsync(work, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref room.ActionQueueDepth);
            return false;
        }
        catch (ChannelClosedException)
        {
            Interlocked.Decrement(ref room.ActionQueueDepth);
            return false;
        }
    }

    private static void StartActionWorker(RoomEntry room)
    {
        room.ActionWorker = Task.Run(async () =>
        {
            await foreach (var work in room.ActionQueue.Reader.ReadAllAsync())
            {
                if (!_rooms.TryGetValue(room.RoomId, out var active) || !ReferenceEquals(active, room))
                    break;
                var depth = Interlocked.Decrement(ref room.ActionQueueDepth);
                LatencyDiagnostics.Observe("房间动作排队", work.EnqueuedAt,
                    $"房间={room.RoomId}，动作={work.Name}，剩余深度={Math.Max(0, depth)}");
                LatencyDiagnostics.RecordMetric("房间动作队列深度", Math.Max(0, depth), "条");
                if (work.ReceivedAt != 0)
                    LatencyDiagnostics.Observe("动作接收到房间出队", work.ReceivedAt,
                        $"房间={room.RoomId}，动作={work.Name}");
                try
                {
                    await work.Execute();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[房间队列] {room.RoomId} 动作 {work.Name} 异常: {ex.Message}");
                }
            }
        });
    }

    /// <summary>客户端 MsgRequestState 入口</summary>
    public static void HandleRequestState(string sessionId)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null)
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "对局已结束，无法恢复" });
            return;
        }
        int idx = Array.IndexOf(room.PlayerSessionIds, sessionId);
        EnqueueWork(room, new RoomWork("RequestState", LatencyDiagnostics.Start(), async () =>
        {
            await ResolveExpiredMulliganAsync(room, DateTime.UtcNow);
            EnsureMulliganTimeout(room);
            if (idx < 0)
            {
                var viewPlayerIndex = room.Spectators.TryGetValue(sessionId, out var storedViewPlayerIndex)
                    ? storedViewPlayerIndex
                    : 0;
                WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(
                    room.Engine.State,
                    -1,
                    "Resync",
                    spectatorPlayerIndex: viewPlayerIndex));
                return;
            }
            if (_grace.TryRemove(room.RoomId + ":" + sessionId, out var cts)) cts.Cancel();
            WebSocketBridge.Send(room.PlayerSessionIds[1 - idx], new { proto = "MsgPlayerReconnected" });
            WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(room.Engine.State, idx, "Resync"));
            WebSocketBridge.BroadcastSpectatorList(room);
        }));
    }

    /// <summary>玩家断线 → 启动 90s 宽限期</summary>
    public static void OnPlayerDisconnect(string sessionId)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null) return;
        int idx = Array.IndexOf(room.PlayerSessionIds, sessionId);
        if (idx < 0)
        {
            // 观战者直接移除
            var removed = room.Spectators.TryRemove(sessionId, out _);
            _sessionRoom.TryRemove(sessionId, out _);
            if (removed) WebSocketBridge.BroadcastSpectatorList(room);
            return;
        }

        var oppSid = room.PlayerSessionIds[1 - idx];
        WebSocketBridge.Send(oppSid, new { proto = "MsgPlayerDisconnected", gracePeriodSeconds = GracePeriodSeconds });

        var cts = new CancellationTokenSource();
        _grace[room.RoomId + ":" + sessionId] = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(GracePeriodSeconds), cts.Token); }
            catch (TaskCanceledException) { return; }
            // 超时 → 判负
            var r = GetRoom(room.RoomId);
            if (r is null) return;
            EnqueueWork(r, new RoomWork("DisconnectTimeout", LatencyDiagnostics.Start(), () =>
            {
                if (!r.Engine.State.IsGameOver)
                {
                    r.Engine.State.WinnerIndex = 1 - idx;
                    r.Engine.State.GameOverReason = $"{r.PlayerAccounts[idx]} 断线超时";
                    r.Engine.Broadcast("DisconnectTimeout", new { disconnected = idx });
                }
                CleanupRoom(room.RoomId);
                return Task.CompletedTask;
            }));
        });
    }

    /// <summary>
    /// 在线方在对手断线宽限期内，主动请求即时结束对局（判对手负）。
    /// 仅当对手确实处于断线宽限期中（其计时器存在）时才生效，避免对手在线/已重连时被误判。
    /// 与 90s 超时判负复用同一套结束流程，但由玩家手动触发，规避后端计时器因重启/异常丢失导致的永久卡死。
    /// </summary>
    public static void RequestEndByDisconnect(string sessionId)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null) return;
        int idx = Array.IndexOf(room.PlayerSessionIds, sessionId);
        if (idx < 0) return; // 观战者无权

        int oppIdx = 1 - idx;
        var oppSid = room.PlayerSessionIds[oppIdx];

        // 必须确认对手确实在断线宽限期中（计时器存在），否则拒绝，避免在线方滥用判负
        if (!_grace.TryRemove(room.RoomId + ":" + oppSid, out var cts))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "对手已重连或不在断线状态，无法结束对局" });
            return;
        }
        cts.Cancel(); // 取消对手宽限计时器，避免随后超时逻辑重复判负

        if (!EnqueueWork(room, new RoomWork("EndByDisconnect", LatencyDiagnostics.Start(), () =>
        {
            if (!room.Engine.State.IsGameOver)
            {
                room.Engine.State.WinnerIndex = idx;
                room.Engine.State.GameOverReason = $"{room.PlayerAccounts[oppIdx]} 断线，对手确认结束对局";
                room.Engine.Broadcast("DisconnectTimeout", new { disconnected = oppIdx });
            }
            CleanupRoom(room.RoomId);
            return Task.CompletedTask;
        })))
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "对局正在结束，请稍候" });
    }

    /// <summary>断线玩家在宽限期内重新连接（同账号新 sessionId）</summary>
    public static bool TryReclaim(string newSessionId, string accountName, string cardBackId = "classic")
    {
        // 找到匹配 accountName 的房间
        foreach (var kv in _rooms)
        {
            var r = kv.Value;
            for (int i = 0; i < 2; i++)
            {
                if (string.Equals(r.PlayerAccounts[i], accountName, StringComparison.OrdinalIgnoreCase))
                {
                    var oldSid = r.PlayerSessionIds[i];
                    if (oldSid == newSessionId) return false; // 同 sid 不算重连
                    // 取消宽限期
                    if (_grace.TryRemove(r.RoomId + ":" + oldSid, out var cts)) cts.Cancel();
                    // 替换 session
                    _sessionRoom.TryRemove(oldSid, out _);
                    r.PlayerSessionIds[i] = newSessionId;
                    r.Engine.State.Players[i].CardBackId = cardBackId;
                    _sessionRoom[newSessionId] = r.RoomId;
                    WebSocketBridge.OnGameSessionRebound(oldSid, newSessionId, r.PlayerSessionIds[1 - i]);
                    // 重新绑定引擎回调（PlayerIndex 编号未变，sid 已替换）
                    r.Engine.OnSendToPlayer = (idx, payload) =>
                        WebSocketBridge.Send(r.PlayerSessionIds[idx], payload);
                    var playerIndex = i;
                    EnqueueWork(r, new RoomWork("Reclaim", LatencyDiagnostics.Start(), async () =>
                    {
                        await ResolveExpiredMulliganAsync(r, DateTime.UtcNow);
                        EnsureMulliganTimeout(r);
                        WebSocketBridge.Send(r.PlayerSessionIds[1 - playerIndex], new { proto = "MsgPlayerReconnected" });
                        WebSocketBridge.Send(newSessionId, StateSnapshotBuilder.Build(r.Engine.State, playerIndex, "Resync"));
                        WebSocketBridge.BroadcastSpectatorList(r);
                    }));
                    return true;
                }
            }
        }
        return false;
    }

    public static void AddSpectator(string roomId, string sessionId, int viewPlayerIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "房间 ID 不能为空" });
            return;
        }
        if (!_rooms.TryGetValue(roomId, out var r))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "房间不存在" });
            return;
        }

        if (_sessionRoom.TryGetValue(sessionId, out var currentRoomId))
        {
            if (!_rooms.TryGetValue(currentRoomId, out var currentRoom))
            {
                _sessionRoom.TryRemove(sessionId, out _);
            }
            else
            {
                if (Array.IndexOf(currentRoom.PlayerSessionIds, sessionId) >= 0)
                {
                    WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "对战中的玩家无法观战" });
                    return;
                }
                if (!string.Equals(currentRoomId, roomId, StringComparison.Ordinal))
                {
                    WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "请先退出当前观战" });
                    return;
                }
            }
        }

        var normalizedViewPlayerIndex = viewPlayerIndex == 1 ? 1 : 0;
        r.Spectators[sessionId] = normalizedViewPlayerIndex;
        _sessionRoom[sessionId] = roomId;
        if (!_rooms.TryGetValue(roomId, out var activeRoom) || !ReferenceEquals(activeRoom, r))
        {
            r.Spectators.TryRemove(sessionId, out _);
            _sessionRoom.TryRemove(sessionId, out _);
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "对局刚刚结束" });
            return;
        }
        WebSocketBridge.OnGameChatParticipantJoined(sessionId);
        WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = true, roomId });
        WebSocketBridge.BroadcastSpectatorList(r);
        EnqueueWork(r, new RoomWork("SpectateJoin", LatencyDiagnostics.Start(), () =>
        {
            if (r.Spectators.TryGetValue(sessionId, out var storedViewPlayerIndex))
                WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(
                    r.Engine.State,
                    -1,
                    "SpectateJoin",
                    spectatorPlayerIndex: storedViewPlayerIndex));
            return Task.CompletedTask;
        }));
    }

    /// <summary>主动退出观战。重复退出按成功处理，保证客户端可以安全返回大厅。</summary>
    public static void RemoveSpectator(string sessionId)
    {
        if (!_sessionRoom.TryGetValue(sessionId, out var roomId))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgLeaveSpectate", result = true });
            return;
        }

        if (_rooms.TryGetValue(roomId, out var room))
        {
            if (Array.IndexOf(room.PlayerSessionIds, sessionId) >= 0)
            {
                WebSocketBridge.Send(sessionId, new { proto = "MsgLeaveSpectate", result = false, logStr = "对战玩家不能退出观战" });
                return;
            }
            var removed = room.Spectators.TryRemove(sessionId, out _);
            if (removed) WebSocketBridge.BroadcastSpectatorList(room);
        }

        _sessionRoom.TryRemove(sessionId, out _);
        WebSocketBridge.Send(sessionId, new { proto = "MsgLeaveSpectate", result = true });
    }

    public static void CleanupRoom(string roomId)
    {
        if (_rooms.TryRemove(roomId, out var r))
        {
            WebSocketBridge.OnGameRoomClosed(
                r.PlayerSessionIds,
                r.Spectators.Keys,
                preservePostGameChat: r.Engine.State.IsGameOver);
            RoomDirectory.Unregister(roomId);
            CancelMulliganTimeout(roomId);
            r.ActionQueue.Writer.TryComplete();
            foreach (var sid in r.PlayerSessionIds) _sessionRoom.TryRemove(sid, out _);
            foreach (var sid in r.Spectators.Keys)   _sessionRoom.TryRemove(sid, out _);
            r.Engine.RecordMatchLog("match_end", -1, new
            {
                winnerIndex = r.Engine.State.WinnerIndex,
                reason = r.Engine.State.GameOverReason,
                turnCount = r.Engine.State.TurnCount,
                finalTick = r.Engine.State.Tick,
                matchKind = r.MatchKind.ToString(),
            });

            TryRecordLeaderStats(r.RoomId, r.MatchKind, r.PlayerAccounts, r.Engine.State);
            // 文件命令在各自单写 Channel 内仍然严格保序，但不让数百个房间清理线程
            // 同步占住线程池等待磁盘关闭。正常关服的 Shutdown 仍会排空全部队列。
            var persistenceCleanup = Task.WhenAll(
                ReplayRecorder.CloseDeferred(roomId),
                MatchLogRecorder.CloseDeferred(roomId),
                RoomJournal.DeleteDeferred(roomId));
            _ = persistenceCleanup.ContinueWith(task =>
            {
                if (task.Exception is not null)
                    Console.Error.WriteLine($"[房间清理] {roomId} 持久化收尾失败：{task.Exception.GetBaseException().Message}");
            }, TaskScheduler.Default);

            // 友谊战房间:对局结束 → 回调更新比分并让双方退回房间
            if (r.FriendlyRoomId is not null)
            {
                int? wi = r.Engine.State.WinnerIndex;
                string? winnerAccount = (wi is >= 0 and < 2) ? r.PlayerAccounts[wi.Value] : null;
                WebSocketBridge.OnFriendlyGameEnd(r.FriendlyRoomId, winnerAccount);
            }
        }
    }

    // ── 重启恢复 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 服务器启动时调用：扫描 Persist/*.jsonl，把 TTL 内未结束的 PvP 对局重放重建回 _rooms。
    /// 必须在 WebSocketBridge.Start 之前、CardDatabase/Dsl 加载之后调用。
    /// 串行重放各房间（逐个 await），更稳；并发安全虽已修但无需冒险。
    /// </summary>
    public static async Task RestoreAll()
    {
        var dir = RoomJournal.GetPersistDir();
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "*.jsonl");
        int restored = 0, skipped = 0;
        foreach (var file in files)
        {
            try
            {
                if (await RestoreOne(file)) restored++;
                else skipped++;
            }
            catch (Exception ex)
            {
                skipped++;
                Console.WriteLine($"[Restore] 跳过 {Path.GetFileName(file)}（重建失败）：{ex.Message}");
                try { File.Delete(file); } catch { }
            }
        }
        if (files.Length > 0)
            Console.WriteLine($"[Restore] 恢复完成：成功 {restored}，跳过/弃局 {skipped}。");
    }

    /// <summary>恢复单个房间。成功放回 _rooms 返回 true；弃局（删文件）返回 false。</summary>
    private static async Task<bool> RestoreOne(string file)
    {
        var lines = await File.ReadAllLinesAsync(file);
        if (lines.Length == 0) { TryDelete(file); return false; }

        // 首行 header
        using var headerDoc = JsonDocument.Parse(lines[0]);
        var h = headerDoc.RootElement;
        if (h.GetProperty("kind").GetString() != "create") { TryDelete(file); return false; }

        var roomId      = h.GetProperty("roomId").GetString()!;
        var seed        = h.GetProperty("seed").GetInt32();
        var firstPlayer = h.GetProperty("firstPlayer").GetInt32();
        var vsBot       = h.TryGetProperty("vsBot", out var vb) && vb.GetBoolean();
        var matchKind = MatchKind.UnknownHuman;
        if (h.TryGetProperty("matchKind", out var mk)
            && Enum.TryParse<MatchKind>(mk.GetString(), ignoreCase: true, out var parsedMatchKind))
            matchKind = parsedMatchKind;
        if (vsBot) matchKind = MatchKind.Bot;
        var p0          = h.GetProperty("p0");
        var p1          = h.GetProperty("p1");
        var p0Account   = p0.GetProperty("account").GetString()!;
        var p1Account   = p1.GetProperty("account").GetString()!;
        var p0Deck      = p0.GetProperty("deckRaw").GetString()!;
        var p1Deck      = p1.GetProperty("deckRaw").GetString()!;
        var p0Always    = p0.TryGetProperty("alwaysPrompt", out var a0) && a0.GetBoolean();
        var p1Always    = p1.TryGetProperty("alwaysPrompt", out var a1) && a1.GetBoolean();
        var p0CardBackId = p0.TryGetProperty("cardBackId", out var cb0) ? cb0.GetString() ?? "classic" : "classic";
        var p1CardBackId = p1.TryGetProperty("cardBackId", out var cb1) ? cb1.GetString() ?? "classic" : "classic";
        // 旧日志没有此字段，默认 false，保持升级前“构造时发牌”的随机序列以便正确恢复。
        var openingSetupAfterFirstPlayerChoice =
            h.TryGetProperty("openingSetupAfterFirstPlayerChoice", out var deferredSetup)
            && deferredSetup.GetBoolean();

        if (vsBot) { TryDelete(file); return false; } // 范围=仅 PvP

        // 解析动作磁带 + 记录"最后一次操作时间"
        var actions = new List<MatchReplay.ActionEntry>();
        DateTime lastActivity = h.TryGetProperty("createdAtUtc", out var ca)
            ? ca.GetDateTime() : DateTime.UtcNow;
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            using var doc = JsonDocument.Parse(lines[i]);
            var e = doc.RootElement;
            if (e.GetProperty("kind").GetString() != "action") continue;
            var pi   = e.GetProperty("playerIndex").GetInt32();
            var act  = e.GetProperty("action").GetString()!;
            var data = e.GetProperty("data").Clone();
            actions.Add(new MatchReplay.ActionEntry(pi, act, data));
            if (e.TryGetProperty("tsUtc", out var ts)) lastActivity = ts.GetDateTime();
        }

        // TTL：自最后一次操作起超过 30 分钟 → 弃局
        if (DateTime.UtcNow - lastActivity > RestoreTtl)
        {
            Console.WriteLine($"[Restore] 弃局 {roomId}（超 TTL，最后操作 {lastActivity:u}）。");
            TryDelete(file);
            return false;
        }

        // 重放重建（静默引擎，重放期间不落盘、不广播）
        var engine = await MatchReplay.RebuildAsync(
            roomId, seed, firstPlayer,
            (p0Account, p0Deck), (p1Account, p1Deck),
            actions,
            leaderKeywordWildcard: false,
            p0AlwaysPrompt: p0Always,
            p1AlwaysPrompt: p1Always,
            openingSetupAfterFirstPlayerChoice: openingSetupAfterFirstPlayerChoice);
        engine.EnablePrivateSnapshotLog = PrivateSnapshotLogEnabled;
        engine.State.Players[0].CardBackId = p0CardBackId;
        engine.State.Players[1].CardBackId = p1CardBackId;

        if (engine.State.IsGameOver)
        {
            // 服务进程可能在胜负已产生、正常 CleanupRoom 尚未落盘时退出；恢复时补做幂等结算。
            TryRecordLeaderStats(roomId, matchKind, new[] { p0Account, p1Account }, engine.State, lastActivity);
            TryDelete(file);
            return false;
        }

        // 构造房间放回池：sid 用占位（真实 sid 由玩家重登时 TryReclaim 替换）
        var entry = new RoomEntry
        {
            RoomId = roomId,
            Engine = engine,
            PlayerSessionIds = new[] { "offline-0", "offline-1" },
            PlayerAccounts   = new[] { p0Account, p1Account },
            VsBot = false,
            MatchKind = matchKind,
        };

        // 重新挂回回调（按当前 sid 发；日志/录像/动作日志均"续写"而非覆盖）
        engine.OnSendToPlayer = (idx, payload) =>
            WebSocketBridge.Send(entry.PlayerSessionIds[idx], payload);
        engine.OnSendToSpectators = (viewPlayerIndex, payload) =>
        {
            foreach (var spectator in entry.Spectators)
            {
                if (spectator.Value == viewPlayerIndex)
                    WebSocketBridge.Send(spectator.Key, payload);
            }
        };
        engine.HasSpectators = () => !entry.Spectators.IsEmpty;
        engine.HasSpectatorsForPerspective = viewPlayerIndex =>
            entry.Spectators.Values.Any(value => value == viewPlayerIndex);
        entry.ReplayPath   = ReplayRecorder.OpenAppend(roomId);
        entry.MatchLogPath = MatchLogRecorder.OpenAppend(roomId);
        engine.OnReplay        = (entryObj) => ReplayRecorder.Append(roomId, entryObj);
        engine.OnMatchLog      = (kind, actor, payload) => MatchLogRecorder.Append(roomId, engine.State, kind, actor, payload);
        engine.OnPersistAction = (pi, act, data) => RoomJournal.Append(roomId, pi, act, data);
        RoomJournal.Reopen(roomId); // 续写新动作到同一文件（不重写 header）

        _rooms[roomId] = entry;
        RoomDirectory.RegisterLocal(roomId);
        StartActionWorker(entry);
        EnsureMulliganTimeout(entry);
        // 不加 _sessionRoom（占位 sid 无意义）；不调 BroadcastInitialState（无人在线，静默重建）
        Console.WriteLine($"[Restore] 已恢复对局 {roomId}（{p0Account} vs {p1Account}，回放 {actions.Count} 个动作）。");
        return true;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }

    private static void TryRecordLeaderStats(
        string roomId,
        MatchKind matchKind,
        IReadOnlyList<string> playerAccounts,
        GameState state,
        DateTime? endedAtUtc = null)
    {
        // 未分胜负的手动清理、超 TTL 弃局不属于已完成对局，不进入事实表。
        if (state.WinnerIndex is not (0 or 1)) return;

        try
        {
            LeaderStatsStore.Default.RecordMatch(new LeaderMatchResult(
                roomId,
                endedAtUtc ?? DateTime.UtcNow,
                matchKind,
                playerAccounts[0],
                playerAccounts[1],
                state.Players[0].Leader.Info.Number,
                state.Players[1].Leader.Info.Number,
                state.WinnerIndex,
                state.FirstPlayer,
                state.TurnCount,
                state.GameOverReason ?? ""));
        }
        catch (Exception ex)
        {
            // 排行榜落盘失败不能阻塞正常对局清理；保留明确日志供运维补录。
            Console.Error.WriteLine($"[LeaderStats] 对局 {roomId} 写入失败：{ex.Message}");
        }
    }
}
