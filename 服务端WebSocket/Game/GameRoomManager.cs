using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using GrandUMI.Cluster;
using GrandUMI.Diagnostics;
using GrandUMI.Effects.Rules;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Stats;
using GrandUMI.Game.Ranked;

namespace GrandUMI.Game;

/// <summary>
/// 房间池：管理活跃的 GameEngine 实例 + 会话↔房间映射 + 断线宽限期
/// </summary>
public static class GameRoomManager
{
    public static IRoomPlacementDirectory RoomDirectory { get; set; } = LocalRoomPlacementDirectory.Instance;
    private const int GracePeriodSeconds = 90;
    private const long OperationTimeLimitMs = 20 * 60 * 1000;
    private const long OperationTurnTimeLimitMs = 8 * 60 * 1000;
    /// <summary>仅排障时开启；私有快照平均约 63 KB，不应作为正式服常态日志。</summary>
    private static readonly bool PrivateSnapshotLogEnabled =
        string.Equals(Environment.GetEnvironmentVariable("GRANDUMI_PRIVATE_SNAPSHOT_LOG"), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("GRANDUMI_PRIVATE_SNAPSHOT_LOG"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>对局无有效操作的存活上限；启动恢复与运行期清理使用同一口径。</summary>
    internal static readonly TimeSpan RoomInactivityTimeout = TimeSpan.FromMinutes(30);

    /// <summary>运行期死房间扫描间隔。</summary>
    internal static readonly TimeSpan RoomExpirationSweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>房间池</summary>
    private static readonly ConcurrentDictionary<string, RoomEntry> _rooms = new();
    private static GameMaintenanceState Maintenance = new();

    public static int RoomCount => _rooms.Count;
    public static int SpectatorCount => _rooms.Values.Sum(room => room.Spectators.Count);
    public static int TotalActionQueueDepth => _rooms.Values.Sum(room => Math.Max(0, Volatile.Read(ref room.ActionQueueDepth)));
    public static IReadOnlyDictionary<string, int> RoomCountsByRuleset
        => _rooms.Values
            .GroupBy(room => room.Engine.State.RulesetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    public static MaintenanceSnapshot GetMaintenanceSnapshot()
        => Maintenance.GetSnapshot(RoomCount);

    public static MaintenanceSnapshot SetMaintenanceMode(bool enabled)
        => Maintenance.SetEnabled(enabled, RoomCount);

    public static void InitializeMaintenance(string persistencePath)
        => Maintenance = new GameMaintenanceState(persistencePath);

    /// <summary>sessionId → roomId</summary>
    private static readonly ConcurrentDictionary<string, string> _sessionRoom = new();

    /// <summary>roomId → 断线计时器</summary>
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _grace = new();

    /// <summary>roomId → 先后手选择超时任务；实际结算仍排入房间串行队列。</summary>
    private static readonly ConcurrentDictionary<string, StartingPlayerChoiceTimeout> _startingPlayerChoiceTimeouts = new();
    private sealed record StartingPlayerChoiceTimeout(
        DateTime DeadlineUtc,
        CancellationTokenSource Cancellation,
        CancellationToken Token,
        int RetryAttempt);

    /// <summary>roomId → 调度手牌超时任务；实际结算仍排入房间串行队列。</summary>
    private static readonly ConcurrentDictionary<string, MulliganTimeout> _mulliganTimeouts = new();
    private sealed record MulliganTimeout(
        DateTime DeadlineUtc,
        CancellationTokenSource Cancellation,
        CancellationToken Token,
        int RetryAttempt);

    public class RoomEntry
    {
        public required string RoomId { get; init; }
        public required GameEngine Engine { get; init; }
        public required string[] PlayerSessionIds { get; init; }  // [P0, P1]
        public required string[] PlayerAccounts   { get; init; }
        public required string[] PlayerDisplayNames { get; init; }
        /// <summary>观战会话、主视角与个人手牌授权。</summary>
        public ConcurrentDictionary<string, SpectatorConnection> Spectators { get; } = new();
        public string[] SpectateModes { get; init; } = [SpectatingRules.Open, SpectatingRules.Open];
        public bool[] SpectatorHandsPublic { get; init; } = [false, false];
        public string?[] SpectateCodes { get; init; } = [null, null];
        public ConcurrentDictionary<string, byte> KickedSpectatorAccounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal object SpectatorGate { get; } = new();
        internal Dictionary<string, SpectatorHandRequest> PendingHandRequests { get; } = new(StringComparer.Ordinal);
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        private long _lastActivityUtcTicks = DateTime.UtcNow.Ticks;
        public DateTime LastActivityUtc
            => new(Interlocked.Read(ref _lastActivityUtcTicks), DateTimeKind.Utc);

        internal void MarkActivity(DateTime? activityUtc = null)
        {
            var normalized = activityUtc?.ToUniversalTime() ?? DateTime.UtcNow;
            Interlocked.Exchange(ref _lastActivityUtcTicks, normalized.Ticks);
        }
        public string? MatchLogPath { get; set; }
        /// <summary>是否为单人测试模式（P1 为机器人）</summary>
        public bool VsBot { get; init; }
        /// <summary>对局来源，用于 Leader 统计与后续分模式分析。</summary>
        public MatchKind MatchKind { get; init; }
        internal object ClockGate { get; } = new();
        internal long OperationClockActiveSince;
        internal CancellationTokenSource? OperationClockTimer;
        internal int OperationClockTimerVersion;
        internal bool[] DisconnectedPlayers { get; } = new bool[2];
        internal long[] DisconnectGraceRemainingMs { get; } = [GracePeriodSeconds * 1000L, GracePeriodSeconds * 1000L];
        internal long[] DisconnectStartedAt { get; } = new long[2];
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
        /// <summary>同一玩家的 requestId 只执行一次；房间销毁时随 RoomEntry 一并释放。</summary>
        internal RequestDedupeWindow ProcessedPlayerRequests { get; } = new(512, TimeSpan.FromMinutes(10));
        /// <summary>服务端为已接受动作分配的持久化序号。</summary>
        internal long JournalSequence;
        internal long[] LastOperationSequences { get; } = [-1, -1];
        internal int AcceptedActionsSinceSnapshot;
    }

    internal sealed record RoomWork(string Name, long EnqueuedAt, Func<Task> Execute, long ReceivedAt = 0);

    /// <summary>双方匹配/房间码成功后创建房间</summary>
    public static RoomEntry CreateRoom(string p0Sid, string p0Account, string p0Deck,
                                        string p1Sid, string p1Account, string p1Deck,
                                        bool? p0First = null,
                                         bool p0AlwaysPrompt = false, bool p1AlwaysPrompt = false,
                                          string p0CardBackId = "classic", string p1CardBackId = "classic",
                                          IReadOnlyDictionary<string, string>? p0SpriteMap = null,
                                          IReadOnlyDictionary<string, string>? p1SpriteMap = null,
                                          bool vsBot = false,
                                          string? friendlyRoomId = null,
                                          MatchKind matchKind = MatchKind.UnknownHuman,
                                          bool broadcastInitialState = true,
                                          string? p0DisplayName = null,
                                          string? p1DisplayName = null,
                                          string? p0SpectateMode = null,
                                          string? p1SpectateMode = null,
                                          bool p0SpectatorHandsPublic = false,
                                          bool p1SpectatorHandsPublic = false,
                                          string? p0SpectateCode = null,
                                          string? p1SpectateCode = null)
    {
        if (string.Equals(p0Sid, p1Sid, StringComparison.Ordinal))
            throw new InvalidOperationException("同一连接不能同时作为对局双方");
        if (!vsBot && string.Equals(p0Account, p1Account, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("同一账号不能同时作为真人对局双方");

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
            deferOpeningSetupUntilFirstPlayerChosen: openingSetupAfterFirstPlayerChoice,
            deferInitialSetupUntilStart: true);
        engine.State.Players[0].DisplayName = p0DisplayName ?? p0Account;
        engine.State.Players[1].DisplayName = p1DisplayName ?? p1Account;
        engine.EnablePrivateSnapshotLog = PrivateSnapshotLogEnabled;
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;
        engine.State.Players[0].CardBackId = p0CardBackId;
        engine.State.Players[1].CardBackId = p1CardBackId;
        CopySpriteMap(p0SpriteMap, engine.State.Players[0].SpriteMap);
        CopySpriteMap(p1SpriteMap, engine.State.Players[1].SpriteMap);

        var entry = new RoomEntry
        {
            RoomId = roomId,
            Engine = engine,
            PlayerSessionIds = new[] { p0Sid, p1Sid },
            PlayerAccounts   = new[] { p0Account, p1Account },
            PlayerDisplayNames = new[] { p0DisplayName ?? p0Account, p1DisplayName ?? p1Account },
            SpectateModes = [SpectatingRules.NormalizeMode(p0SpectateMode), SpectatingRules.NormalizeMode(p1SpectateMode)],
            SpectatorHandsPublic = [p0SpectatorHandsPublic, p1SpectatorHandsPublic],
            SpectateCodes = [p0SpectateCode, p1SpectateCode],
            VsBot = vsBot,
            MatchKind = matchKind,
            FriendlyRoomId = friendlyRoomId,
        };
        engine.State.MatchKind = matchKind;
        AttachRankIdentities(engine.State, matchKind, entry.PlayerAccounts, entry.PlayerDisplayNames);
        engine.State.OperationClockEnabled = matchKind is MatchKind.Ranked or MatchKind.RankedWild or MatchKind.Casual or MatchKind.Matchmaking;
        engine.State.OperationClockRemainingMs[0] = OperationTimeLimitMs;
        engine.State.OperationClockRemainingMs[1] = OperationTimeLimitMs;
        ResetOperationTurnClock(engine.State);
        engine.BeforeSnapshot = () => SyncOperationClock(entry);
        engine.OnOpeningSequenceReady = () =>
        {
            EnsureStartingPlayerChoiceTimeout(entry);
            EnsureMulliganTimeout(entry);
        };

        // 配置回调：人类走 WS 下发；单人模式下 P1(机器人) 的消息驱动 BotDriver 思考
        engine.OnSendToPlayer = (idx, payload) =>
        {
            if (idx == 1 && vsBot) { BotDriver.OnBotMessage(entry); return; }
            WebSocketBridge.Send(entry.PlayerSessionIds[idx], payload);
        };

        engine.OnSendToSpectators = (viewPlayerIndex, payload, handPayload) =>
        {
            foreach (var spectator in entry.Spectators)
            {
                if (spectator.Value.ViewPlayerIndex == viewPlayerIndex)
                    WebSocketBridge.Send(spectator.Key,
                        spectator.Value.HandVisible && handPayload is not null ? handPayload : payload);
            }
        };
        engine.HasSpectators = () => !entry.Spectators.IsEmpty;
        engine.HasSpectatorsForPerspective = viewPlayerIndex =>
            entry.Spectators.Values.Any(value => value.ViewPlayerIndex == viewPlayerIndex);
        engine.HasSpectatorsWithHandForPerspective = viewPlayerIndex =>
            entry.Spectators.Values.Any(value => value.ViewPlayerIndex == viewPlayerIndex && value.HandVisible);
        entry.MatchLogPath = MatchLogRecorder.Open(roomId);
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
            rulesVersion = engine.State.RulesetId,
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
                rulesetId = engine.State.RulesetId,
                openingSetupAfterFirstPlayerChoice,
                p0 = new { account = p0Account, displayName = entry.PlayerDisplayNames[0], deckRaw = p0Deck, alwaysPrompt = p0AlwaysPrompt, cardBackId = p0CardBackId, spriteMap = engine.State.Players[0].SpriteMap, spectateMode = entry.SpectateModes[0], spectatorHandsPublic = entry.SpectatorHandsPublic[0], spectateCode = entry.SpectateCodes[0] },
                p1 = new { account = p1Account, displayName = entry.PlayerDisplayNames[1], deckRaw = p1Deck, alwaysPrompt = p1AlwaysPrompt, cardBackId = p1CardBackId, spriteMap = engine.State.Players[1].SpriteMap, spectateMode = entry.SpectateModes[1], spectatorHandsPublic = entry.SpectatorHandsPublic[1], spectateCode = entry.SpectateCodes[1] },
                vsBot,
                matchKind = matchKind.ToString(),
                createdAtUtc = DateTime.UtcNow,
            });
            engine.OnPersistAction = (pi, act, data, requestId) =>
                PersistAcceptedAction(entry, pi, act, data, requestId);
        }

        _rooms[roomId] = entry;
        RoomDirectory.RegisterLocal(roomId);
        CaptureRecoverySnapshot(entry);
        WebSocketBridge.OnGameChatParticipantJoined(p0Sid);
        WebSocketBridge.OnGameChatParticipantJoined(p1Sid);
        _sessionRoom[p0Sid] = roomId;
        _sessionRoom[p1Sid] = roomId;
        WebSocketBridge.BroadcastSpectatorList(entry);
        StartActionWorker(entry);
        EnsureStartingPlayerChoiceTimeout(entry);
        EnsureMulliganTimeout(entry);

        // 骰点对局先等待胜者选择先后手；单人测试沿用预设先后手并直接进入 mulligan。
        if (broadcastInitialState)
            engine.BroadcastInitialState();
        return entry;
    }

    private static void CopySpriteMap(
        IReadOnlyDictionary<string, string>? source,
        IDictionary<string, string> target)
    {
        if (source is null) return;
        foreach (var (number, sprite) in source)
            target[number] = sprite;
    }

    private static IDisposable ReserveRoomCreation()
    {
        if (Maintenance.TryReserveRoomCreation(RoomCount, ServerCapacity.MaxRooms, out var rejectionReason))
            return new RoomAdmissionLease();
        if (string.Equals(rejectionReason, GameMaintenanceState.PlayerMessage, StringComparison.Ordinal))
            throw new GameMaintenanceException(GameMaintenanceState.PlayerMessage);
        throw new InvalidOperationException(rejectionReason ?? "服务器暂时无法创建新对局");
    }

    private sealed class RoomAdmissionLease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                var snapshot = Maintenance.CompleteRoomCreation(RoomCount);
                if (snapshot.Enabled) WebSocketBridge.BroadcastMaintenanceState();
            }
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
        var tracksRequest = room.ProcessedPlayerRequests.IsTrackable(requestId);
        if (tracksRequest && !room.ProcessedPlayerRequests.TryRegister(idx, requestId))
        {
            // 重复包可能是客户端没有收到第一次回包后的补发。按房间队列顺序回一份带原 requestId
            // 的权威快照，让客户端安全结束 pending，绝不再次执行动作。
            EnqueueRecoveryWork(room, new RoomWork("DuplicateRequest", LatencyDiagnostics.Start(), () =>
            {
                WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(
                    room.Engine.State, idx, "DuplicateRequest", requestId: requestId));
                return Task.CompletedTask;
            }));
            LatencyDiagnostics.RecordMetric("对局动作去重", 1, "次");
            return;
        }
        if (!EnqueuePlayerAction(room, idx, action, data.Clone(), requestId, receivedAt))
        {
            if (tracksRequest) room.ProcessedPlayerRequests.Remove(idx, requestId);
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "对局正在结束，操作未执行", requestId });
        }
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
            var expiredPlayer = PauseOperationClockForAction(room, playerIndex, action, receivedAt);
            if (expiredPlayer is 0 or 1)
            {
                FinishByOperationTimeout(room, expiredPlayer.Value);
                CleanupRoom(room.RoomId);
                return;
            }
            room.Engine.RecordMatchLog("player_action_requested", playerIndex, new { action, data });
            var accepted = room.Engine.HandleAction(playerIndex, action, data, requestId);
            // 被拒绝的 PromptResponse 不会消费旧 Prompt，不应等待效果链稳定；
            // 否则单读者房间队列会被卡到等待超时，后续合法响应也无法进入。
            if (accepted)
            {
                room.MarkActivity();
                await room.Engine.WaitSettledAsync(resolvingPromptId: promptIdBefore);
                RoomJournal.AppendClock(
                    room.RoomId,
                    room.Engine.State.OperationClockRemainingMs,
                    room.Engine.State.OperationTurnClockRemainingMs,
                    room.Engine.State.OperationTurnClockTurnCount);
                MaybeCaptureRecoverySnapshot(room);
            }
            EnsureOperationClockRunning(room);
            EnsureStartingPlayerChoiceTimeout(room);
            EnsureMulliganTimeout(room);
            if (room.Engine.State.IsGameOver)
                CleanupRoom(room.RoomId);
        }, receivedAt));
    }

    private static int? PauseOperationClockForAction(
        RoomEntry room,
        int playerIndex,
        string action,
        long receivedAt)
    {
        if (!room.Engine.State.OperationClockEnabled) return null;
        lock (room.ClockGate)
        {
            var cutoff = receivedAt > 0 ? receivedAt : Stopwatch.GetTimestamp();
            var activePlayer = room.Engine.State.OperationClockActivePlayer;
            ChargeOperationClockLocked(room, cutoff);
            if (activePlayer is 0 or 1 && IsOperationClockExpired(room.Engine.State, activePlayer))
                return activePlayer;

            // 非当前决策者发来的非法动作不能暂停对手棋钟；投降和平局协商是例外。
            if (action is not "Surrender" and not "RequestDraw" and not "RespondDraw"
                && activePlayer != playerIndex) return null;
            StopOperationClockLocked(room);
            return null;
        }
    }

    private static void EnsureOperationClockRunning(RoomEntry room)
    {
        if (!room.Engine.State.OperationClockEnabled || room.Engine.State.IsGameOver) return;
        lock (room.ClockGate)
        {
            if (room.Engine.State.OperationClockActivePlayer >= 0 && room.OperationClockActiveSince > 0) return;
            StartOperationClockLocked(room, DetermineOperationClockPlayer(room));
        }
    }

    private static void SyncOperationClock(RoomEntry room)
    {
        var state = room.Engine.State;
        if (!state.OperationClockEnabled) return;
        lock (room.ClockGate)
        {
            ChargeOperationClockLocked(room, Stopwatch.GetTimestamp());
            StartOperationClockLocked(room, DetermineOperationClockPlayer(room));
        }
    }

    private static int DetermineOperationClockPlayer(RoomEntry room)
    {
        var state = room.Engine.State;
        if (!state.OperationClockEnabled || state.IsGameOver || !state.MulliganBothDone) return -1;
        if (room.DisconnectedPlayers[0] || room.DisconnectedPlayers[1]) return -1;
        if (state.PendingDrawRequester is not null) return -1;
        if (state.PendingPrompt is { } prompt) return prompt.PlayerIndex;
        if (state.Phase is Phase.BattleBlock or Phase.BattleCounter)
            return state.CurrentBattle?.DefenderPlayerIndex ?? -1;
        return state.Phase == Phase.Main ? state.CurrentTurnPlayer : -1;
    }

    private static void ChargeOperationClockLocked(RoomEntry room, long cutoff)
    {
        var state = room.Engine.State;
        var active = state.OperationClockActivePlayer;
        if (active is not (0 or 1) || room.OperationClockActiveSince <= 0) return;
        if (cutoff < room.OperationClockActiveSince) cutoff = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(room.OperationClockActiveSince, cutoff).TotalMilliseconds;
        if (elapsed > 0)
        {
            var elapsedMs = (long)Math.Ceiling(elapsed);
            state.OperationClockRemainingMs[active] = Math.Max(0,
                state.OperationClockRemainingMs[active] - elapsedMs);
            state.OperationTurnClockRemainingMs[active] = Math.Max(0,
                state.OperationTurnClockRemainingMs[active] - elapsedMs);
            state.OperationTurnClockRemainingMs[active] = Math.Min(
                state.OperationTurnClockRemainingMs[active],
                state.OperationClockRemainingMs[active]);
        }
        room.OperationClockActiveSince = cutoff;
        state.OperationClockSyncUtc = DateTime.UtcNow;
    }

    private static void StartOperationClockLocked(RoomEntry room, int playerIndex)
    {
        var state = room.Engine.State;
        if (state.OperationTurnClockTurnCount != state.TurnCount)
            ResetOperationTurnClock(state);
        CancelOperationClockTimerLocked(room);
        state.OperationClockActivePlayer = playerIndex;
        state.OperationClockPaused = state.MulliganBothDone
            && (room.DisconnectedPlayers[0] || room.DisconnectedPlayers[1]);
        state.OperationClockSyncUtc = DateTime.UtcNow;
        room.OperationClockActiveSince = playerIndex is 0 or 1 ? Stopwatch.GetTimestamp() : 0;
        if (playerIndex is not (0 or 1)) return;

        var remaining = Math.Min(
            state.OperationClockRemainingMs[playerIndex],
            state.OperationTurnClockRemainingMs[playerIndex]);
        var version = ++room.OperationClockTimerVersion;
        var cancellation = new CancellationTokenSource();
        room.OperationClockTimer = cancellation;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remaining)), cancellation.Token); }
            catch (OperationCanceledException) { return; }
            var activeRoom = GetRoom(room.RoomId);
            if (activeRoom is null || !ReferenceEquals(activeRoom, room)) return;
            await EnqueueCriticalWorkAsync(activeRoom,
                new RoomWork("OperationTimeout", LatencyDiagnostics.Start(), () =>
                {
                    int? expired = null;
                    lock (activeRoom.ClockGate)
                    {
                        if (version != activeRoom.OperationClockTimerVersion) return Task.CompletedTask;
                        var current = activeRoom.Engine.State.OperationClockActivePlayer;
                        ChargeOperationClockLocked(activeRoom, Stopwatch.GetTimestamp());
                        if (current is 0 or 1 && IsOperationClockExpired(activeRoom.Engine.State, current))
                            expired = current;
                        else
                            // Task.Delay 允许因系统调度精度而比权威扣时点早醒极短时间。
                            // 此时必须按尚余时间重新挂载任务，否则棋钟会继续在客户端归零，
                            // 服务端却再也没有超时任务来完成判负。
                            StartOperationClockLocked(activeRoom, DetermineOperationClockPlayer(activeRoom));
                    }
                    if (expired is 0 or 1)
                    {
                        FinishByOperationTimeout(activeRoom, expired.Value);
                        CleanupRoom(activeRoom.RoomId);
                    }
                    return Task.CompletedTask;
                }), CancellationToken.None);
        });
    }

    private static void StopOperationClockLocked(RoomEntry room)
    {
        CancelOperationClockTimerLocked(room);
        room.Engine.State.OperationClockActivePlayer = -1;
        room.Engine.State.OperationClockSyncUtc = DateTime.UtcNow;
        room.OperationClockActiveSince = 0;
    }

    private static void CancelOperationClockTimerLocked(RoomEntry room)
    {
        room.OperationClockTimerVersion++;
        if (room.OperationClockTimer is null) return;
        room.OperationClockTimer.Cancel();
        room.OperationClockTimer.Dispose();
        room.OperationClockTimer = null;
    }

    private static void FinishByOperationTimeout(RoomEntry room, int expiredPlayer)
    {
        if (room.Engine.State.IsGameOver) return;
        lock (room.ClockGate)
        {
            var turnExpired = room.Engine.State.OperationTurnClockRemainingMs[expiredPlayer] <= 0;
            if (!turnExpired) room.Engine.State.OperationClockRemainingMs[expiredPlayer] = 0;
            room.Engine.State.OperationTurnClockRemainingMs[expiredPlayer] = 0;
            StopOperationClockLocked(room);
        }
        room.Engine.State.WinnerIndex = 1 - expiredPlayer;
        var reason = room.Engine.State.OperationClockRemainingMs[expiredPlayer] <= 0
            ? "总操作时间耗尽"
            : "本回合操作时间耗尽";
        room.Engine.State.GameOverReason = $"{room.PlayerDisplayNames[expiredPlayer]} {reason}";
        room.Engine.Broadcast("OperationTimeout", new { expiredPlayer, reason });
    }

    private static bool IsOperationClockExpired(GameState state, int playerIndex)
        => state.OperationClockRemainingMs[playerIndex] <= 0
           || state.OperationTurnClockRemainingMs[playerIndex] <= 0;

    private static void ResetOperationTurnClock(GameState state)
    {
        state.OperationTurnClockTurnCount = state.TurnCount;
        for (var player = 0; player < 2; player++)
            state.OperationTurnClockRemainingMs[player] = Math.Min(
                OperationTurnTimeLimitMs,
                state.OperationClockRemainingMs[player]);
    }

    /// <summary>根据服务端权威截止时间创建或清除先后手选择超时任务。</summary>
    private static void EnsureStartingPlayerChoiceTimeout(RoomEntry room, int retryAttempt = 0)
    {
        var deadline = room.Engine.State.StartingPlayerChoiceDeadlineUtc;
        if (deadline is null || room.Engine.State.StartingPlayerChosen)
        {
            CancelStartingPlayerChoiceTimeout(room.RoomId);
            return;
        }

        if (_startingPlayerChoiceTimeouts.TryGetValue(room.RoomId, out var current)
            && current.DeadlineUtc == deadline.Value)
            return;

        var cancellation = new CancellationTokenSource();
        var next = new StartingPlayerChoiceTimeout(deadline.Value, cancellation, cancellation.Token, retryAttempt);
        while (true)
        {
            if (_startingPlayerChoiceTimeouts.TryGetValue(room.RoomId, out current))
            {
                if (current.DeadlineUtc == deadline.Value)
                {
                    next.Cancellation.Dispose();
                    return;
                }
                if (!_startingPlayerChoiceTimeouts.TryUpdate(room.RoomId, next, current)) continue;
                current.Cancellation.Cancel();
                current.Cancellation.Dispose();
            }
            else if (!_startingPlayerChoiceTimeouts.TryAdd(room.RoomId, next))
            {
                continue;
            }

            StartStartingPlayerChoiceTimeoutWait(room, next);
            return;
        }
    }

    private static void StartStartingPlayerChoiceTimeoutWait(RoomEntry room, StartingPlayerChoiceTimeout timer)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var delay = timer.DeadlineUtc - DateTime.UtcNow;
                    if (delay <= TimeSpan.Zero) break;
                    await Task.Delay(delay, timer.Token);
                }

                if (timer.Token.IsCancellationRequested) return;
                var active = GetRoom(room.RoomId);
                if (active is null || !ReferenceEquals(active, room)) return;

                await EnqueueCriticalWorkAsync(active, new RoomWork("StartingPlayerChoiceTimeout", LatencyDiagnostics.Start(), async () =>
                {
                    if (active.Engine.State.StartingPlayerChoiceDeadlineUtc == timer.DeadlineUtc)
                        await ResolveExpiredStartingPlayerChoiceAsync(active, DateTime.UtcNow);
                    EnsureStartingPlayerChoiceTimeout(active);
                    EnsureMulliganTimeout(active);
                }), timer.Token);
            }
            catch (OperationCanceledException) when (timer.Token.IsCancellationRequested)
            {
                // 玩家已完成选择、房间已结束或权威截止时间已经更换。
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[先后手超时] 房间 {room.RoomId} 第 {timer.RetryAttempt + 1} 次计时任务异常: {ex.Message}");
                if (!TryRemoveStartingPlayerChoiceTimeout(room.RoomId, timer)) return;

                timer.Cancellation.Dispose();
                var active = GetRoom(room.RoomId);
                if (active is null
                    || !ReferenceEquals(active, room)
                    || active.Engine.State.StartingPlayerChoiceDeadlineUtc != timer.DeadlineUtc
                    || active.Engine.State.StartingPlayerChosen)
                    return;

                if (timer.RetryAttempt >= 2) return;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (timer.RetryAttempt + 1)));
                var retryRoom = GetRoom(room.RoomId);
                if (retryRoom is not null
                    && ReferenceEquals(retryRoom, room)
                    && retryRoom.Engine.State.StartingPlayerChoiceDeadlineUtc == timer.DeadlineUtc
                    && !retryRoom.Engine.State.StartingPlayerChosen)
                    EnsureStartingPlayerChoiceTimeout(retryRoom, timer.RetryAttempt + 1);
            }
        });
    }

    private static bool TryRemoveStartingPlayerChoiceTimeout(string roomId, StartingPlayerChoiceTimeout expected)
        => ((ICollection<KeyValuePair<string, StartingPlayerChoiceTimeout>>)_startingPlayerChoiceTimeouts)
            .Remove(new KeyValuePair<string, StartingPlayerChoiceTimeout>(roomId, expected));

    /// <summary>补做已过期的先后手选择：骰点胜者超时后默认选择自己先手。</summary>
    private static async Task<bool> ResolveExpiredStartingPlayerChoiceAsync(RoomEntry room, DateTime utcNow)
    {
        var state = room.Engine.State;
        if (state.StartingPlayerChoiceDeadlineUtc is not { } deadline
            || utcNow < deadline
            || state.StartingPlayerChosen
            || state.StartingPlayerChooser is not (0 or 1))
            return false;

        var chooser = state.StartingPlayerChooser;
        var data = JsonSerializer.SerializeToElement(new { goFirst = true });
        room.Engine.RecordMatchLog("starting_player_choice_timeout_auto_select", chooser, new { goFirst = true });
        var accepted = room.Engine.HandleAction(chooser, "ChooseFirstPlayer", data);
        if (!accepted) return false;

        await room.Engine.WaitSettledAsync();
        return true;
    }

    private static void CancelStartingPlayerChoiceTimeout(string roomId)
    {
        if (_startingPlayerChoiceTimeouts.TryRemove(roomId, out var timer))
        {
            timer.Cancellation.Cancel();
            timer.Cancellation.Dispose();
        }
    }

    /// <summary>根据服务端权威截止时间创建或清除调度超时任务；不会因客户端断线或后台而停止。</summary>
    private static void EnsureMulliganTimeout(RoomEntry room, int retryAttempt = 0)
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

        var cancellation = new CancellationTokenSource();
        var next = new MulliganTimeout(deadline.Value, cancellation, cancellation.Token, retryAttempt);
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
            try
            {
                // 系统时钟校准或计时器精度可能让 Task.Delay 提前极短时间返回；
                // 必须重新核对服务端权威截止时间，避免唯一一次超时任务被提前消费。
                while (true)
                {
                    var delay = timer.DeadlineUtc - DateTime.UtcNow;
                    if (delay <= TimeSpan.Zero) break;
                    await Task.Delay(delay, timer.Token);
                }

                if (timer.Token.IsCancellationRequested) return;
                var active = GetRoom(room.RoomId);
                if (active is null || !ReferenceEquals(active, room)) return;

                // 超时结算属于不可丢失的房间控制动作。普通 TryWrite 在队列暂满时会返回 false，
                // 如果忽略该结果，客户端就会永久停在“剩余 0 秒”。这里等待到成功入队或房间关闭。
                await EnqueueCriticalWorkAsync(active, new RoomWork("MulliganTimeout", LatencyDiagnostics.Start(), async () =>
                {
                    if (active.Engine.State.MulliganDeadlineUtc == timer.DeadlineUtc)
                        await ResolveExpiredMulliganAsync(active, DateTime.UtcNow);
                    EnsureMulliganTimeout(active);
                }), timer.Token);
            }
            catch (OperationCanceledException) when (timer.Token.IsCancellationRequested)
            {
                // 房间已结束、截止时间已更换或调度已正常完成。
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[调度超时] 房间 {room.RoomId} 第 {timer.RetryAttempt + 1} 次计时任务异常: {ex.Message}");
                if (!TryRemoveMulliganTimeout(room.RoomId, timer)) return;

                timer.Cancellation.Dispose();
                var active = GetRoom(room.RoomId);
                if (active is null
                    || !ReferenceEquals(active, room)
                    || active.Engine.State.MulliganDeadlineUtc != timer.DeadlineUtc
                    || active.Engine.State.MulliganBothDone)
                    return;

                // 避免瞬时运行时异常把唯一一次超时结算永久吞掉；有限重试防止持续故障形成忙循环。
                if (timer.RetryAttempt >= 2) return;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (timer.RetryAttempt + 1)));
                var retryRoom = GetRoom(room.RoomId);
                if (retryRoom is not null
                    && ReferenceEquals(retryRoom, room)
                    && retryRoom.Engine.State.MulliganDeadlineUtc == timer.DeadlineUtc
                    && !retryRoom.Engine.State.MulliganBothDone)
                    EnsureMulliganTimeout(retryRoom, timer.RetryAttempt + 1);
            }
        });
    }

    private static bool TryRemoveMulliganTimeout(string roomId, MulliganTimeout expected)
        => ((ICollection<KeyValuePair<string, MulliganTimeout>>)_mulliganTimeouts)
            .Remove(new KeyValuePair<string, MulliganTimeout>(roomId, expected));

    /// <summary>补做已过期的调度选择；供计时器、刷新取状态和账号重绑共同复用。</summary>
    private static async Task<IReadOnlyList<int>> ResolveExpiredMulliganAsync(RoomEntry room, DateTime utcNow)
    {
        var autoKept = room.Engine.AutoKeepMulligans(utcNow);
        if (autoKept.Count == 0) return autoKept;

        foreach (var playerIndex in autoKept)
        {
            var data = JsonSerializer.SerializeToElement(new { redraw = false });
            room.Engine.RecordMatchLog("mulligan_timeout_auto_keep", playerIndex, new { redraw = false });
            room.Engine.OnPersistAction?.Invoke(playerIndex, "Mulligan", data, null);
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

    /// <summary>状态恢复属于客户端脱困入口；队列暂满时等待写入，不能像普通操作一样静默丢弃。</summary>
    private static void EnqueueRecoveryWork(RoomEntry room, RoomWork work)
    {
        if (EnqueueWork(room, work)) return;
        _ = EnqueueCriticalWorkAsync(room, work, CancellationToken.None);
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
        EnqueueRecoveryWork(room, new RoomWork("RequestState", LatencyDiagnostics.Start(), async () =>
        {
            await ResolveExpiredStartingPlayerChoiceAsync(room, DateTime.UtcNow);
            EnsureStartingPlayerChoiceTimeout(room);
            await ResolveExpiredMulliganAsync(room, DateTime.UtcNow);
            EnsureMulliganTimeout(room);
            if (idx < 0)
            {
                var spectator = room.Spectators.TryGetValue(sessionId, out var storedSpectator)
                    ? storedSpectator
                    : null;
                WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(
                    room.Engine.State,
                    -1,
                    "Resync",
                    spectatorPlayerIndex: spectator?.ViewPlayerIndex ?? 0,
                    revealSpectatorMainHand: spectator?.HandVisible == true));
                return;
            }
            var wasDisconnected = CompleteDisconnectGrace(room, idx, sessionId);
            if (wasDisconnected)
                WebSocketBridge.Send(room.PlayerSessionIds[1 - idx], new { proto = "MsgPlayerReconnected" });
            WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(room.Engine.State, idx, "Resync"));
            WebSocketBridge.BroadcastSpectatorList(room);
        }));
    }

    /// <summary>玩家断线 → 暂停操作棋钟并启动每局累计 90s 宽限期。</summary>
    public static void OnPlayerDisconnect(string sessionId)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null) return;
        int idx = Array.IndexOf(room.PlayerSessionIds, sessionId);
        if (idx < 0)
        {
            // 观战者直接移除
            var removed = room.Spectators.TryRemove(sessionId, out var spectator);
            if (spectator?.PendingRequestId is { } pendingId)
            {
                lock (room.SpectatorGate) room.PendingHandRequests.Remove(pendingId);
            }
            _sessionRoom.TryRemove(sessionId, out _);
            if (removed) WebSocketBridge.BroadcastSpectatorList(room);
            return;
        }

        long graceRemaining;
        lock (room.ClockGate)
        {
            if (room.DisconnectedPlayers[idx]) return;
            ChargeOperationClockLocked(room, Stopwatch.GetTimestamp());
            room.DisconnectedPlayers[idx] = true;
            room.DisconnectStartedAt[idx] = Stopwatch.GetTimestamp();
            StopOperationClockLocked(room);
            room.Engine.State.OperationClockPaused = room.Engine.State.OperationClockEnabled;
            graceRemaining = room.DisconnectGraceRemainingMs[idx];
        }

        var oppSid = room.PlayerSessionIds[1 - idx];
        var graceSeconds = Math.Max(0, (int)Math.Ceiling(graceRemaining / 1000d));
        WebSocketBridge.Send(oppSid, new { proto = "MsgPlayerDisconnected", gracePeriodSeconds = graceSeconds });
        EnqueueRecoveryWork(room, new RoomWork("PlayerDisconnected", LatencyDiagnostics.Start(), () =>
        {
            room.Engine.Broadcast("PlayerDisconnected", new { disconnected = idx });
            return Task.CompletedTask;
        }));

        StartDisconnectGraceTimer(room, idx, sessionId, graceRemaining);
    }

    private static void StartDisconnectGraceTimer(
        RoomEntry room,
        int playerIndex,
        string sessionId,
        long graceRemaining)
    {
        var cts = new CancellationTokenSource();
        _grace[room.RoomId + ":" + sessionId] = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(graceRemaining), cts.Token); }
            catch (TaskCanceledException) { return; }
            // 超时 → 判负
            var r = GetRoom(room.RoomId);
            if (r is null) return;
            // 终局任务不能像普通玩家操作一样在队列满时静默丢弃；否则房间最终会被清理，
            // 在线一方却收不到权威终局快照，只会一直停留在“正在结算”。
            EnqueueRecoveryWork(r, new RoomWork("DisconnectTimeout", LatencyDiagnostics.Start(), () =>
            {
                lock (r.ClockGate)
                {
                    r.DisconnectGraceRemainingMs[playerIndex] = 0;
                    r.DisconnectStartedAt[playerIndex] = 0;
                }
                if (!r.Engine.State.IsGameOver)
                {
                    r.Engine.State.WinnerIndex = 1 - playerIndex;
                    r.Engine.State.GameOverReason = $"{r.PlayerDisplayNames[playerIndex]} 断线超时";
                    r.Engine.Broadcast("DisconnectTimeout", new { disconnected = playerIndex });
                }
                CleanupRoom(room.RoomId);
                return Task.CompletedTask;
            }));
        });
    }

    /// <summary>
    /// 在线方在对手断线宽限期内，主动请求即时结束对局（判对手负）。
    /// 仅当对手确实处于断线宽限期中（其计时器存在）时才生效，避免对手在线/已重连时被误判。
    /// 与 90s 超时判负复用同一套结束流程；宽限尚未用完时拒绝提前判负。
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
        if (!_grace.TryGetValue(room.RoomId + ":" + oppSid, out var cts))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "对手已重连或不在断线状态，无法结束对局" });
            return;
        }
        lock (room.ClockGate)
        {
            var elapsed = room.DisconnectStartedAt[oppIdx] <= 0
                ? 0
                : Stopwatch.GetElapsedTime(room.DisconnectStartedAt[oppIdx]).TotalMilliseconds;
            if (elapsed < room.DisconnectGraceRemainingMs[oppIdx])
            {
                WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "对手仍在 90 秒断线宽限期内" });
                return;
            }
        }
        _grace.TryRemove(room.RoomId + ":" + oppSid, out _);
        cts.Cancel(); // 取消对手宽限计时器，避免随后超时逻辑重复判负

        // 与自动断线判负共用保证入队语义，避免压力下“结束对局”请求被丢弃。
        EnqueueRecoveryWork(room, new RoomWork("EndByDisconnect", LatencyDiagnostics.Start(), () =>
        {
            if (!room.Engine.State.IsGameOver)
            {
                room.Engine.State.WinnerIndex = idx;
                room.Engine.State.GameOverReason = $"{room.PlayerDisplayNames[oppIdx]} 断线，对手确认结束对局";
                room.Engine.Broadcast("DisconnectTimeout", new { disconnected = oppIdx });
            }
            CleanupRoom(room.RoomId);
            return Task.CompletedTask;
        }));
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
                    var wasDisconnected = CompleteDisconnectGrace(r, i, oldSid);
                    r.MarkActivity();
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
                    EnqueueRecoveryWork(r, new RoomWork("Reclaim", LatencyDiagnostics.Start(), async () =>
                    {
                        await ResolveExpiredStartingPlayerChoiceAsync(r, DateTime.UtcNow);
                        EnsureStartingPlayerChoiceTimeout(r);
                        await ResolveExpiredMulliganAsync(r, DateTime.UtcNow);
                        EnsureMulliganTimeout(r);
                        if (wasDisconnected)
                        {
                            WebSocketBridge.Send(r.PlayerSessionIds[1 - playerIndex], new { proto = "MsgPlayerReconnected" });
                            r.Engine.Broadcast("PlayerReconnected", new { player = playerIndex });
                        }
                        WebSocketBridge.Send(newSessionId, StateSnapshotBuilder.Build(r.Engine.State, playerIndex, "Resync"));
                        SendCurrentOpponentDisconnectState(r, playerIndex, newSessionId);
                        WebSocketBridge.BroadcastSpectatorList(r);
                    }));
                    return true;
                }
            }
        }
        return false;
    }

    private static bool CompleteDisconnectGrace(RoomEntry room, int playerIndex, string sessionId)
    {
        if (_grace.TryRemove(room.RoomId + ":" + sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        lock (room.ClockGate)
        {
            var wasDisconnected = room.DisconnectedPlayers[playerIndex];
            if (wasDisconnected && room.DisconnectStartedAt[playerIndex] > 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(room.DisconnectStartedAt[playerIndex]).TotalMilliseconds;
                room.DisconnectGraceRemainingMs[playerIndex] = Math.Max(0,
                    room.DisconnectGraceRemainingMs[playerIndex] - (long)Math.Ceiling(elapsed));
            }
            room.DisconnectedPlayers[playerIndex] = false;
            room.DisconnectStartedAt[playerIndex] = 0;
            room.Engine.State.OperationClockPaused = room.DisconnectedPlayers[0] || room.DisconnectedPlayers[1];
            if (wasDisconnected && !room.Engine.State.OperationClockPaused)
                StartOperationClockLocked(room, DetermineOperationClockPlayer(room));
            return wasDisconnected;
        }
    }

    private static void SendCurrentOpponentDisconnectState(RoomEntry room, int playerIndex, string sessionId)
    {
        var opponentIndex = 1 - playerIndex;
        long graceRemaining;
        lock (room.ClockGate)
        {
            if (!room.DisconnectedPlayers[opponentIndex]) return;
            var elapsed = room.DisconnectStartedAt[opponentIndex] <= 0
                ? 0
                : Stopwatch.GetElapsedTime(room.DisconnectStartedAt[opponentIndex]).TotalMilliseconds;
            graceRemaining = Math.Max(0,
                room.DisconnectGraceRemainingMs[opponentIndex] - (long)Math.Ceiling(elapsed));
        }
        WebSocketBridge.Send(sessionId, new
        {
            proto = "MsgPlayerDisconnected",
            gracePeriodSeconds = Math.Max(0, (int)Math.Ceiling(graceRemaining / 1000d)),
        });
    }

    public static void AddSpectator(
        string roomId,
        string sessionId,
        string account,
        string displayName,
        int viewPlayerIndex = 0,
        string? spectateCode = null,
        bool isFriend = false)
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

        var normalizedViewPlayerIndex = viewPlayerIndex == 1 ? 1 : 0;
        var mode = r.SpectateModes[normalizedViewPlayerIndex];
        var access = SpectatingRules.CheckAccess(
            mode,
            isFriend,
            r.SpectateCodes[normalizedViewPlayerIndex],
            spectateCode,
            r.KickedSpectatorAccounts.ContainsKey(account));
        if (!access.Allowed)
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = access.Error });
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

        var spectator = new SpectatorConnection
        {
            SessionId = sessionId,
            Account = account,
            DisplayName = displayName,
            ViewPlayerIndex = normalizedViewPlayerIndex,
            HandVisible = r.SpectatorHandsPublic[normalizedViewPlayerIndex],
        };
        r.Spectators[sessionId] = spectator;
        _sessionRoom[sessionId] = roomId;
        if (!_rooms.TryGetValue(roomId, out var activeRoom) || !ReferenceEquals(activeRoom, r))
        {
            r.Spectators.TryRemove(sessionId, out _);
            _sessionRoom.TryRemove(sessionId, out _);
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "对局刚刚结束" });
            return;
        }
        WebSocketBridge.OnGameChatParticipantJoined(sessionId);
        WebSocketBridge.Send(sessionId, new
        {
            proto = "MsgSpectateRoom",
            result = true,
            roomId,
            spectatorHandVisible = spectator.HandVisible,
        });
        WebSocketBridge.BroadcastSpectatorList(r);
        EnqueueWork(r, new RoomWork("SpectateJoin", LatencyDiagnostics.Start(), () =>
        {
            if (r.Spectators.TryGetValue(sessionId, out var storedSpectator))
                WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(
                    r.Engine.State,
                    -1,
                    "SpectateJoin",
                    spectatorPlayerIndex: storedSpectator.ViewPlayerIndex,
                    revealSpectatorMainHand: storedSpectator.HandVisible));
            return Task.CompletedTask;
        }));
    }

    public static void RequestSpectatorHand(string sessionId)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null || !room.Spectators.TryGetValue(sessionId, out var spectator))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectatorHandStatus", status = "denied", logStr = "你当前不在观战" });
            return;
        }

        SpectatorHandRequest? request = null;
        string? error = null;
        var retryAfterMs = 0;
        lock (room.SpectatorGate)
        {
            if (spectator.HandVisible)
            {
                WebSocketBridge.Send(sessionId, new { proto = "MsgSpectatorHandStatus", status = "approved" });
                return;
            }
            if (spectator.PendingRequestId is not null)
            {
                error = "已有申请正在等待玩家处理";
            }
            else
            {
                var elapsed = DateTime.UtcNow - spectator.LastHandRequestUtc;
                if (elapsed < SpectatingRules.HandRequestCooldown)
                {
                    retryAfterMs = Math.Max(1, (int)Math.Ceiling((SpectatingRules.HandRequestCooldown - elapsed).TotalMilliseconds));
                    error = "申请过于频繁，请稍后再试";
                }
                else
                {
                    var requestId = Guid.NewGuid().ToString("N");
                    request = new SpectatorHandRequest(
                        requestId,
                        sessionId,
                        spectator.Account,
                        spectator.DisplayName,
                        spectator.ViewPlayerIndex);
                    spectator.LastHandRequestUtc = DateTime.UtcNow;
                    spectator.PendingRequestId = requestId;
                    room.PendingHandRequests[requestId] = request;
                }
            }
        }

        if (request is null)
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectatorHandStatus", status = "denied", logStr = error, retryAfterMs });
            return;
        }

        WebSocketBridge.Send(room.PlayerSessionIds[request.PlayerIndex], new
        {
            proto = "MsgSpectatorHandRequest",
            requestId = request.RequestId,
            spectatorAccount = request.SpectatorAccount,
            spectatorName = request.SpectatorName,
        });
        WebSocketBridge.Send(sessionId, new { proto = "MsgSpectatorHandStatus", status = "pending" });
    }

    public static void RespondSpectatorHand(string playerSessionId, string requestId, bool accept)
    {
        var room = GetRoomBySession(playerSessionId);
        var playerIndex = room is null ? -1 : Array.IndexOf(room.PlayerSessionIds, playerSessionId);
        if (room is null || playerIndex < 0)
        {
            WebSocketBridge.Send(playerSessionId, new { proto = "MsgSpectatorHandResponse", result = false, logStr = "你当前不在对局中" });
            return;
        }

        SpectatorHandRequest? request;
        SpectatorConnection? spectator = null;
        lock (room.SpectatorGate)
        {
            if (!room.PendingHandRequests.TryGetValue(requestId, out request) || request.PlayerIndex != playerIndex)
            {
                WebSocketBridge.Send(playerSessionId, new { proto = "MsgSpectatorHandResponse", result = false, logStr = "该申请已失效" });
                return;
            }
            room.PendingHandRequests.Remove(requestId);
            if (room.Spectators.TryGetValue(request.SpectatorSessionId, out spectator)
                && spectator.PendingRequestId == requestId)
            {
                spectator.PendingRequestId = null;
                if (accept) spectator.HandVisible = true;
            }
        }

        WebSocketBridge.Send(playerSessionId, new { proto = "MsgSpectatorHandResponse", result = true, requestId, accepted = accept });
        if (spectator is null) return;

        WebSocketBridge.Send(spectator.SessionId, new
        {
            proto = "MsgSpectatorHandStatus",
            status = accept ? "approved" : "denied",
            logStr = accept ? "玩家已同意公开主视角手牌" : "玩家拒绝了手牌查看申请",
            retryAfterMs = accept ? 0 : (int)SpectatingRules.HandRequestCooldown.TotalMilliseconds,
        });
        if (accept)
        {
            EnqueueWork(room, new RoomWork("SpectatorHandApproved", LatencyDiagnostics.Start(), () =>
            {
                if (room.Spectators.TryGetValue(spectator.SessionId, out var activeSpectator) && activeSpectator.HandVisible)
                    WebSocketBridge.Send(spectator.SessionId, StateSnapshotBuilder.Build(
                        room.Engine.State,
                        -1,
                        "SpectatorHandApproved",
                        spectatorPlayerIndex: activeSpectator.ViewPlayerIndex,
                        revealSpectatorMainHand: true));
                return Task.CompletedTask;
            }));
        }
        WebSocketBridge.BroadcastSpectatorList(room);
    }

    public static void KickSpectator(string playerSessionId, string spectatorAccount)
    {
        var room = GetRoomBySession(playerSessionId);
        if (room is null || Array.IndexOf(room.PlayerSessionIds, playerSessionId) < 0)
        {
            WebSocketBridge.Send(playerSessionId, new { proto = "MsgKickSpectator", result = false, logStr = "你当前不在对局中" });
            return;
        }

        var normalizedAccount = spectatorAccount.Trim();
        var targets = room.Spectators
            .Where(pair => string.Equals(pair.Value.Account, normalizedAccount, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targets.Length == 0)
        {
            WebSocketBridge.Send(playerSessionId, new { proto = "MsgKickSpectator", result = false, logStr = "该观战者已经离开" });
            return;
        }

        room.KickedSpectatorAccounts[targets[0].Value.Account] = 0;
        foreach (var (sid, target) in targets)
        {
            room.Spectators.TryRemove(sid, out _);
            _sessionRoom.TryRemove(sid, out _);
            lock (room.SpectatorGate)
            {
                if (target.PendingRequestId is { } pendingId)
                    room.PendingHandRequests.Remove(pendingId);
            }
            WebSocketBridge.Send(sid, new { proto = "MsgSpectatorKicked", logStr = "你已被玩家移出观战，本局无法再次进入" });
        }
        WebSocketBridge.Send(playerSessionId, new { proto = "MsgKickSpectator", result = true, spectatorAccount = targets[0].Value.Account });
        WebSocketBridge.BroadcastSpectatorList(room);
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
            var removed = room.Spectators.TryRemove(sessionId, out var spectator);
            if (spectator?.PendingRequestId is { } pendingId)
            {
                lock (room.SpectatorGate) room.PendingHandRequests.Remove(pendingId);
            }
            if (removed) WebSocketBridge.BroadcastSpectatorList(room);
        }

        _sessionRoom.TryRemove(sessionId, out _);
        WebSocketBridge.Send(sessionId, new { proto = "MsgLeaveSpectate", result = true });
    }

    public static void CleanupRoom(string roomId)
        => TryCleanupRoom(roomId);

    private static bool TryCleanupRoom(string roomId)
    {
        if (_rooms.TryRemove(roomId, out var r))
        {
            lock (r.ClockGate) CancelOperationClockTimerLocked(r);
            TrySettleRankedMatch(r);
            WebSocketBridge.OnGameRoomClosed(
                r.RoomId,
                r.PlayerSessionIds,
                r.PlayerAccounts,
                r.Spectators.Keys,
                preservePostGameChat: r.Engine.State.IsGameOver,
                matchKind: r.MatchKind,
                turnCount: r.Engine.State.TurnCount,
                gameOverReason: r.Engine.State.GameOverReason);
            RoomDirectory.Unregister(roomId);
            CancelStartingPlayerChoiceTimeout(roomId);
            CancelMulliganTimeout(roomId);
            foreach (var sid in r.PlayerSessionIds)
                if (_grace.TryRemove(roomId + ":" + sid, out var grace)) { grace.Cancel(); grace.Dispose(); }
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
                rulesetId = r.Engine.State.RulesetId,
            });

            NotifyRulesetUpdateAfterMatch(r);

            TryRecordLeaderStats(r.RoomId, r.MatchKind, r.PlayerAccounts, r.Engine.State);
            // 文件命令在各自单写 Channel 内仍然严格保序，但不让数百个房间清理线程
            // 同步占住线程池等待磁盘关闭。正常关服的 Shutdown 仍会排空全部队列。
            var persistenceCleanup = Task.WhenAll(
                MatchLogRecorder.CloseDeferred(roomId),
                RoomJournal.DeleteDeferred(roomId),
                RoomRecoverySnapshotStore.DeleteDeferred(roomId));
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

            if (GetMaintenanceSnapshot().Enabled)
                WebSocketBridge.BroadcastMaintenanceState();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 周期扫描运行中的房间，兜底回收已经终局但未完成清理，或连续 30 分钟没有有效操作的死房间。
    /// 返回本轮实际清理数量，便于测试和运维日志观察。
    /// </summary>
    public static int SweepExpiredRooms(DateTime utcNow)
    {
        var normalizedNow = utcNow.ToUniversalTime();
        var cleaned = 0;
        foreach (var room in _rooms.Values)
        {
            if (TryCleanupExpiredRoom(room.RoomId, normalizedNow)) cleaned++;
        }
        return cleaned;
    }

    internal static bool TryCleanupExpiredRoom(string roomId, DateTime utcNow)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return false;

        var terminalRoom = room.Engine.State.IsGameOver;
        var inactiveFor = utcNow.ToUniversalTime() - room.LastActivityUtc;
        if (!terminalRoom && inactiveFor <= RoomInactivityTimeout) return false;

        if (!terminalRoom)
        {
            const string description = "对局长时间无操作，房间已自动关闭";
            foreach (var sessionId in room.PlayerSessionIds.Concat(room.Spectators.Keys))
                WebSocketBridge.Send(sessionId, new { proto = "MsgDuelOver", IsWin = false, Description = description });
        }

        if (!TryCleanupRoom(roomId)) return false;

        var reason = terminalRoom
            ? "终局后残留"
            : $"连续无操作 {Math.Max(0, (int)inactiveFor.TotalMinutes)} 分钟";
        Console.WriteLine($"[房间超时清理] 已回收 {roomId}（{reason}）。");
        return true;
    }

    public static async Task RunExpirationMonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RoomExpirationSweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                SweepExpiredRooms(DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常关服：由调用方取消周期扫描。
        }
    }

    private static void TrySettleRankedMatch(RoomEntry room)
    {
        if (!IsRankedSettlementEligible(room.MatchKind, room.Engine.State)) return;
        try
        {
            var mode = RankedModeForMatch(room.MatchKind);
            var store = RankedStore.ForMode(mode);
            var settlement = store.RecordMatch(
                room.RoomId,
                DateTime.UtcNow,
                room.PlayerAccounts[0],
                room.PlayerDisplayNames[0],
                room.PlayerAccounts[1],
                room.PlayerDisplayNames[1],
                room.Engine.State.WinnerIndex.GetValueOrDefault());
            if (settlement is null) return;
            var players = new[] { settlement.Player0, settlement.Player1 };
            for (var i = 0; i < 2; i++)
            {
                var snapshot = store.GetSnapshot(room.PlayerAccounts[i], room.PlayerDisplayNames[i]);
                WebSocketBridge.Send(room.PlayerSessionIds[i], new
                {
                    proto = "MsgRankResult",
                    mode = RankedModeWire.Value(mode),
                    result = RankWire.Settlement(players[i]),
                    profile = RankWire.Profile(snapshot.Profile),
                    leaderboard = RankWire.Leaderboard(snapshot.Leaderboard),
                    factionStandings = RankWire.FactionStandings(snapshot.FactionStandings),
                });
            }

            var winnerIndex = room.Engine.State.WinnerIndex.GetValueOrDefault();
            var loserIndex = 1 - winnerIndex;
            WebSocketBridge.BroadcastRankedWinStreakEnded(
                room.PlayerDisplayNames[loserIndex],
                players[loserIndex].WinStreakBefore,
                players[winnerIndex].Faction,
                room.PlayerDisplayNames[winnerIndex]);
            WebSocketBridge.BroadcastRankedWinStreak(
                room.PlayerDisplayNames[winnerIndex],
                players[loserIndex].Faction,
                players[loserIndex].Tier,
                players[winnerIndex].WinStreak);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[排位] 对局 {room.RoomId} 结算失败：{ex.Message}");
            foreach (var sessionId in room.PlayerSessionIds)
                WebSocketBridge.Send(sessionId, new { proto = "MsgRankResult", error = "排位结算暂时失败，服务端将保留对局记录" });
        }
    }

    internal static bool IsRankedSettlementEligible(MatchKind matchKind, GameState state)
        => matchKind is MatchKind.Ranked or MatchKind.RankedWild
           && state.WinnerIndex is 0 or 1
           && state.MulliganBothDone;

    private static void NotifyRulesetUpdateAfterMatch(RoomEntry room)
    {
        var notice = CardRulesetManager.BuildUpdateNotice(room.Engine.State.RulesetId);
        if (notice is null) return;
        foreach (var sessionId in room.PlayerSessionIds)
        {
            WebSocketBridge.Send(sessionId, new
            {
                proto = "MsgRulesetUpdated",
                previousRulesetId = notice.PreviousRulesetId,
                currentRulesetId = notice.CurrentRulesetId,
                description = notice.Description,
                changedCards = notice.ChangedCards,
                logStr = "卡牌效果已更新，将从下一局开始生效",
            });
        }
    }

    // ── 重启恢复 ──────────────────────────────────────────────────────────

    private static void PersistAcceptedAction(
        RoomEntry room,
        int playerIndex,
        string action,
        JsonElement data,
        string? requestId)
    {
        var journalSequence = Interlocked.Increment(ref room.JournalSequence);
        Volatile.Write(ref room.LastOperationSequences[playerIndex], journalSequence);
        Interlocked.Increment(ref room.AcceptedActionsSinceSnapshot);
        RoomJournal.Append(room.RoomId, journalSequence, playerIndex, action, data, requestId, journalSequence);
    }

    private static void MaybeCaptureRecoverySnapshot(RoomEntry room)
    {
        if (Volatile.Read(ref room.AcceptedActionsSinceSnapshot)
            < RoomRecoverySnapshotStore.CaptureEveryAcceptedActions)
            return;
        CaptureRecoverySnapshot(room);
    }

    private static void CaptureRecoverySnapshot(RoomEntry room)
    {
        if (room.VsBot) return;
        Interlocked.Exchange(ref room.AcceptedActionsSinceSnapshot, 0);
        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(room.Engine.State));
        RoomRecoverySnapshotStore.Capture(new RoomRecoverySnapshot(
            RoomRecoverySnapshotStore.SchemaVersion,
            room.RoomId,
            Volatile.Read(ref room.JournalSequence),
            DateTime.UtcNow,
            room.LastOperationSequences.ToArray(),
            room.Engine.State.OperationClockRemainingMs.ToArray(),
            room.ProcessedPlayerRequests.Snapshot().ToArray(),
            RoomRecoverySnapshotStore.ComputeStateSha256(privateState),
            privateState));
    }

    public static void CaptureAllRecoverySnapshots()
    {
        foreach (var room in _rooms.Values)
        {
            try { CaptureRecoverySnapshot(room); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[恢复快照] 捕获 {room.RoomId} 失败：{ex.Message}");
            }
        }
    }

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
                Quarantine(file, ex.Message);
            }
        }
        if (files.Length > 0)
            Console.WriteLine($"[Restore] 恢复完成：成功 {restored}，跳过/弃局 {skipped}。");
    }

    /// <summary>恢复单个房间。成功放回 _rooms 返回 true；弃局（删文件）返回 false。</summary>
    private static async Task<bool> RestoreOne(string file)
    {
        var lines = await File.ReadAllLinesAsync(file);
        if (lines.Length == 0) { Quarantine(file, "日志为空"); return false; }

        // 首行 header
        using var headerDoc = JsonDocument.Parse(lines[0]);
        var h = headerDoc.RootElement;
        if (h.GetProperty("kind").GetString() != "create")
        {
            Quarantine(file, "首行不是建房记录");
            return false;
        }

        var roomId      = h.GetProperty("roomId").GetString()!;
        var seed        = h.GetProperty("seed").GetInt32();
        var firstPlayer = h.GetProperty("firstPlayer").GetInt32();
        var rulesetId = h.TryGetProperty("rulesetId", out var storedRulesetId)
            ? storedRulesetId.GetString() ?? CardRulesetManager.BuiltIn.Id
            : CardRulesetManager.BuiltIn.Id;
        var ruleset = CardRulesetManager.GetRequired(rulesetId);
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
        var p0DisplayName = p0.TryGetProperty("displayName", out var dn0)
            ? dn0.GetString() ?? p0Account
            : p0Account;
        var p1DisplayName = p1.TryGetProperty("displayName", out var dn1)
            ? dn1.GetString() ?? p1Account
            : p1Account;
        var p0Deck      = p0.GetProperty("deckRaw").GetString()!;
        var p1Deck      = p1.GetProperty("deckRaw").GetString()!;
        var p0Always    = p0.TryGetProperty("alwaysPrompt", out var a0) && a0.GetBoolean();
        var p1Always    = p1.TryGetProperty("alwaysPrompt", out var a1) && a1.GetBoolean();
        var p0CardBackId = p0.TryGetProperty("cardBackId", out var cb0) ? cb0.GetString() ?? "classic" : "classic";
        var p1CardBackId = p1.TryGetProperty("cardBackId", out var cb1) ? cb1.GetString() ?? "classic" : "classic";
        var p0SpectateMode = p0.TryGetProperty("spectateMode", out var sm0) ? SpectatingRules.NormalizeMode(sm0.GetString()) : SpectatingRules.Open;
        var p1SpectateMode = p1.TryGetProperty("spectateMode", out var sm1) ? SpectatingRules.NormalizeMode(sm1.GetString()) : SpectatingRules.Open;
        var p0SpectatorHandsPublic = p0.TryGetProperty("spectatorHandsPublic", out var shp0) && shp0.GetBoolean();
        var p1SpectatorHandsPublic = p1.TryGetProperty("spectatorHandsPublic", out var shp1) && shp1.GetBoolean();
        var p0SpectateCode = p0.TryGetProperty("spectateCode", out var sc0) ? sc0.GetString() : null;
        var p1SpectateCode = p1.TryGetProperty("spectateCode", out var sc1) ? sc1.GetString() : null;
        var p0SpriteMap = ReadSpriteMap(p0);
        var p1SpriteMap = ReadSpriteMap(p1);
        // 旧日志没有此字段，默认 false，保持升级前“构造时发牌”的随机序列以便正确恢复。
        var openingSetupAfterFirstPlayerChoice =
            h.TryGetProperty("openingSetupAfterFirstPlayerChoice", out var deferredSetup)
            && deferredSetup.GetBoolean();

        if (vsBot) { TryDelete(file); return false; } // 范围=仅 PvP

        // 解析动作磁带 + 记录"最后一次操作时间"
        var actions = new List<MatchReplay.ActionEntry>();
        var processedRequests = new List<RequestDedupeEntry>();
        var lastOperationSequences = new long[] { -1, -1 };
        long journalSequence = 0;
        var restoredClockMs = new long[] { OperationTimeLimitMs, OperationTimeLimitMs };
        var restoredTurnClockMs = new long[] { OperationTurnTimeLimitMs, OperationTurnTimeLimitMs };
        var restoredTurnClockTurnCount = 0;
        DateTime lastActivity = h.TryGetProperty("createdAtUtc", out var ca)
            ? ca.GetDateTime() : DateTime.UtcNow;
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            using var doc = JsonDocument.Parse(lines[i]);
            var e = doc.RootElement;
            var kind = e.GetProperty("kind").GetString();
            if (kind == "clock")
            {
                if (e.TryGetProperty("player0RemainingMs", out var c0)) restoredClockMs[0] = Math.Max(0, c0.GetInt64());
                if (e.TryGetProperty("player1RemainingMs", out var c1)) restoredClockMs[1] = Math.Max(0, c1.GetInt64());
                if (e.TryGetProperty("player0TurnRemainingMs", out var tc0)) restoredTurnClockMs[0] = Math.Max(0, tc0.GetInt64());
                if (e.TryGetProperty("player1TurnRemainingMs", out var tc1)) restoredTurnClockMs[1] = Math.Max(0, tc1.GetInt64());
                if (e.TryGetProperty("turnCount", out var turnCount)) restoredTurnClockTurnCount = Math.Max(0, turnCount.GetInt32());
                if (e.TryGetProperty("tsUtc", out var clockTs)) lastActivity = clockTs.GetDateTime();
                continue;
            }
            if (kind != "action") continue;
            var pi   = e.GetProperty("playerIndex").GetInt32();
            var act  = e.GetProperty("action").GetString()!;
            var data = e.GetProperty("data").Clone();
            actions.Add(new MatchReplay.ActionEntry(pi, act, data));
            journalSequence = e.TryGetProperty("journalSequence", out var sequenceElement)
                ? Math.Max(journalSequence, sequenceElement.GetInt64())
                : journalSequence + 1;
            lastOperationSequences[pi] = e.TryGetProperty("operationSequence", out var operationElement)
                && operationElement.ValueKind == JsonValueKind.Number
                    ? Math.Max(lastOperationSequences[pi], operationElement.GetInt64())
                    : journalSequence;
            if (e.TryGetProperty("tsUtc", out var ts)) lastActivity = ts.GetDateTime();
            if (e.TryGetProperty("requestId", out var requestElement)
                && requestElement.ValueKind == JsonValueKind.String
                && requestElement.GetString() is { Length: > 0 } restoredRequestId)
                processedRequests.Add(new RequestDedupeEntry(pi, restoredRequestId, lastActivity));
        }

        // TTL：自最后一次操作起超过 30 分钟 → 弃局
        if (DateTime.UtcNow - lastActivity > RoomInactivityTimeout)
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
            openingSetupAfterFirstPlayerChoice: openingSetupAfterFirstPlayerChoice,
            ruleset: ruleset);
        if (!engine.State.StartingPlayerChosen)
            engine.State.StartingPlayerChoiceDeadlineUtc = lastActivity.AddSeconds(GameEngine.StartingPlayerChoiceTimeoutSeconds);
        engine.EnablePrivateSnapshotLog = PrivateSnapshotLogEnabled;
        engine.State.Players[0].DisplayName = p0DisplayName;
        engine.State.Players[1].DisplayName = p1DisplayName;
        engine.State.Players[0].CardBackId = p0CardBackId;
        engine.State.Players[1].CardBackId = p1CardBackId;
        CopySpriteMap(p0SpriteMap, engine.State.Players[0].SpriteMap);
        CopySpriteMap(p1SpriteMap, engine.State.Players[1].SpriteMap);

        if (engine.State.IsGameOver)
        {
            // 服务进程可能在胜负已产生、正常 CleanupRoom 尚未落盘时退出；恢复时补做幂等结算。
            TryRecordLeaderStats(roomId, matchKind, new[] { p0Account, p1Account }, engine.State, lastActivity);
            if (IsRankedSettlementEligible(matchKind, engine.State))
                RankedStore.ForMode(RankedModeForMatch(matchKind)).RecordMatch(roomId, lastActivity, p0Account, p0Account,
                    p1Account, p1Account, engine.State.WinnerIndex.GetValueOrDefault());
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
            PlayerDisplayNames = new[] { p0DisplayName, p1DisplayName },
            SpectateModes = [p0SpectateMode, p1SpectateMode],
            SpectatorHandsPublic = [p0SpectatorHandsPublic, p1SpectatorHandsPublic],
            SpectateCodes = [p0SpectateCode, p1SpectateCode],
            VsBot = false,
            MatchKind = matchKind,
            CreatedAt = h.TryGetProperty("createdAtUtc", out var createdAt)
                ? createdAt.GetDateTime().ToUniversalTime()
                : lastActivity,
        };
        entry.MarkActivity(lastActivity > DateTime.UtcNow ? DateTime.UtcNow : lastActivity);
        entry.JournalSequence = journalSequence;
        entry.LastOperationSequences[0] = lastOperationSequences[0];
        entry.LastOperationSequences[1] = lastOperationSequences[1];
        entry.ProcessedPlayerRequests.Restore(processedRequests);
        engine.State.MatchKind = matchKind;
        AttachRankIdentities(engine.State, matchKind, entry.PlayerAccounts, entry.PlayerDisplayNames);
        engine.State.OperationClockEnabled = matchKind is MatchKind.Ranked or MatchKind.RankedWild or MatchKind.Casual or MatchKind.Matchmaking;
        engine.State.OperationClockRemainingMs[0] = restoredClockMs[0];
        engine.State.OperationClockRemainingMs[1] = restoredClockMs[1];
        engine.State.OperationTurnClockRemainingMs[0] = Math.Min(restoredTurnClockMs[0], restoredClockMs[0]);
        engine.State.OperationTurnClockRemainingMs[1] = Math.Min(restoredTurnClockMs[1], restoredClockMs[1]);
        engine.State.OperationTurnClockTurnCount = restoredTurnClockTurnCount;
        entry.DisconnectedPlayers[0] = true;
        entry.DisconnectedPlayers[1] = true;
        var restoredDisconnectStartedAt = Stopwatch.GetTimestamp();
        entry.DisconnectStartedAt[0] = restoredDisconnectStartedAt;
        entry.DisconnectStartedAt[1] = restoredDisconnectStartedAt;
        engine.State.OperationClockPaused = engine.State.OperationClockEnabled;
        engine.BeforeSnapshot = () => SyncOperationClock(entry);

        // 重新挂回回调（按当前 sid 发；日志/动作日志均"续写"而非覆盖）
        engine.OnSendToPlayer = (idx, payload) =>
            WebSocketBridge.Send(entry.PlayerSessionIds[idx], payload);
        engine.OnSendToSpectators = (viewPlayerIndex, payload, handPayload) =>
        {
            foreach (var spectator in entry.Spectators)
            {
                if (spectator.Value.ViewPlayerIndex == viewPlayerIndex)
                    WebSocketBridge.Send(spectator.Key,
                        spectator.Value.HandVisible && handPayload is not null ? handPayload : payload);
            }
        };
        engine.HasSpectators = () => !entry.Spectators.IsEmpty;
        engine.HasSpectatorsForPerspective = viewPlayerIndex =>
            entry.Spectators.Values.Any(value => value.ViewPlayerIndex == viewPlayerIndex);
        engine.HasSpectatorsWithHandForPerspective = viewPlayerIndex =>
            entry.Spectators.Values.Any(value => value.ViewPlayerIndex == viewPlayerIndex && value.HandVisible);
        entry.MatchLogPath = MatchLogRecorder.OpenAppend(roomId);
        engine.OnMatchLog      = (kind, actor, payload) => MatchLogRecorder.Append(roomId, engine.State, kind, actor, payload);
        engine.OnPersistAction = (pi, act, data, requestId) =>
            PersistAcceptedAction(entry, pi, act, data, requestId);
        RoomJournal.Reopen(roomId); // 续写新动作到同一文件（不重写 header）

        _rooms[roomId] = entry;
        RoomDirectory.RegisterLocal(roomId);
        StartActionWorker(entry);
        StartDisconnectGraceTimer(entry, 0, entry.PlayerSessionIds[0], entry.DisconnectGraceRemainingMs[0]);
        StartDisconnectGraceTimer(entry, 1, entry.PlayerSessionIds[1], entry.DisconnectGraceRemainingMs[1]);
        EnsureStartingPlayerChoiceTimeout(entry);
        EnsureMulliganTimeout(entry);
        ValidateAndRefreshRecoverySnapshot(entry);
        // 不加 _sessionRoom（占位 sid 无意义）；不调 BroadcastInitialState（无人在线，静默重建）
        Console.WriteLine($"[Restore] 已恢复对局 {roomId}（{p0Account} vs {p1Account}，回放 {actions.Count} 个动作）。");
        return true;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
        _ = RoomRecoverySnapshotStore.DeleteDeferred(Path.GetFileNameWithoutExtension(file));
    }

    private static void ValidateAndRefreshRecoverySnapshot(RoomEntry room)
    {
        var snapshot = RoomRecoverySnapshotStore.TryRead(room.RoomId);
        if (snapshot is not null)
        {
            room.ProcessedPlayerRequests.Restore(snapshot.ProcessedRequests);
            if (snapshot.JournalSequence == room.JournalSequence)
            {
                var current = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(room.Engine.State));
                var currentHash = RoomRecoverySnapshotStore.ComputeStateSha256(current);
                if (!string.Equals(currentHash, snapshot.StateSha256, StringComparison.Ordinal))
                    Console.Error.WriteLine($"[恢复快照] {room.RoomId} 重放校验不一致，已以动作日志重建结果为准。");
            }
        }
        CaptureRecoverySnapshot(room);
    }

    private static void Quarantine(string file, string reason)
    {
        try
        {
            var quarantineDirectory = Path.Combine(Path.GetDirectoryName(file)!, "quarantine");
            Directory.CreateDirectory(quarantineDirectory);
            var target = Path.Combine(
                quarantineDirectory,
                $"{Path.GetFileNameWithoutExtension(file)}-{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl");
            File.Move(file, target, overwrite: true);
            var snapshot = Path.Combine(
                Path.GetDirectoryName(file)!,
                $"{Path.GetFileNameWithoutExtension(file)}.snapshot.json");
            if (File.Exists(snapshot))
            {
                var snapshotTarget = Path.Combine(
                    quarantineDirectory,
                    $"{Path.GetFileNameWithoutExtension(file)}-{DateTime.UtcNow:yyyyMMddHHmmss}.snapshot.json");
                File.Move(snapshot, snapshotTarget, overwrite: true);
            }
            Console.Error.WriteLine($"[Restore] 已隔离损坏日志 {target}：{reason}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Restore] 隔离日志失败 {file}：{ex.Message}");
        }
    }

    private static IReadOnlyDictionary<string, string> ReadSpriteMap(JsonElement player)
    {
        if (!player.TryGetProperty("spriteMap", out var element) || element.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(element.GetRawText())
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void AttachRankIdentities(
        GameState state,
        MatchKind matchKind,
        IReadOnlyList<string> playerAccounts,
        IReadOnlyList<string> playerDisplayNames)
    {
        if (matchKind is not (MatchKind.Ranked or MatchKind.RankedWild)) return;

        var store = RankedStore.ForMode(RankedModeForMatch(matchKind));

        for (var i = 0; i < state.Players.Length; i++)
        {
            try
            {
                var profile = store.GetSnapshot(playerAccounts[i], playerDisplayNames[i]).Profile;
                if (profile.Faction is null) continue;
                state.Players[i].RankIdentity = new PlayerRankIdentity(
                    profile.Faction,
                    profile.Tier,
                    profile.Division,
                    profile.PlacementGames,
                    profile.PlacementRequired);
            }
            catch (Exception ex)
            {
                // 排位身份展示失败不能阻止对局创建；保留日志供排查，客户端安全回退为只显示昵称。
                Console.Error.WriteLine($"[排位] 玩家 {playerAccounts[i]} 的对局身份读取失败：{ex.Message}");
            }
        }
    }

    private static RankedMode RankedModeForMatch(MatchKind matchKind)
        => matchKind == MatchKind.RankedWild ? RankedMode.Wild : RankedMode.Standard;

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
            var result = new LeaderMatchResult(
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
                state.GameOverReason ?? "",
                state.StartingHandCardNumbers[0],
                state.StartingHandCardNumbers[1]);
            LeaderStatsStore.Default.RecordMatch(result);
            LeaderChampionStore.Default.RecordMatch(result);
        }
        catch (Exception ex)
        {
            // 排行榜落盘失败不能阻塞正常对局清理；保留明确日志供运维补录。
            Console.Error.WriteLine($"[LeaderStats] 对局 {roomId} 写入失败：{ex.Message}");
        }
    }
}
