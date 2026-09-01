using System.Collections.Concurrent;
using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Diagnostics;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Ranked;
using GrandUMI.Game.Stats;
using GrandUMI.Game.Validation;
using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;

namespace GrandUMI;

/// <summary>
/// GrandUMI WebSocket 网关
/// 协议：JSON over WebSocket，字段名与 C# LobbyMsg / GameMsg 完全一致
/// 不依赖任何第三方库，纯 .NET 内置 API
/// </summary>
public static class WebSocketBridge
{
    private const int MaxInboundMessageBytes = 524_288;
    private const int SessionReplacedCloseCode = 4009;
    private const string SessionReplacedMessage = "账号已在其他地方登录，请重新登录。";
    private static readonly HashSet<string> NonReplaceableStateActions = new(StringComparer.Ordinal)
    {
        "GameStart", "Resync", "DuplicateRequest", "SpectateJoin", "FirstPlayerChosen",
        "Prompt", "PromptTimeout", "RevealCards",
        "Attack", "AwaitBlock", "AwaitCounter", "DeclareBlocker", "CounterIcon", "PlayCard", "UndoAttachDon",
        "MulliganComplete", "MulliganUpdate", "DuelOver", "Surrender", "DisconnectTimeout",
        "OperationTimeout", "PlayerDisconnected", "PlayerReconnected",
    };
    private static readonly HashSet<string> CriticalOutboundProtocols = new(StringComparer.Ordinal)
    {
        "MsgLogin", "MsgSecret", "MsgSessionReplaced", "MsgPlayerData", "MsgRankSnapshot", "MsgRankResult", "MsgActionRejected", "MsgDuelOver",
        "MsgPrompt", "MsgPromptResponse", "MsgReconnect", "MsgPlayerReconnected", "MsgMaintenanceState",
        "MsgRulesetState", "MsgRulesetUpdated", "MsgAdminOperations", "MsgAdminPlayerSearch", "MsgAdminPlayerUpdate",
        "MsgOperationsCases", "MsgOperationsCaseDetail", "MsgOperationsCaseUpdate", "MsgOperationsCaseAppeal", "MsgOperationsPenalty",
        "MsgPrivilegedAudit", "MsgConsistencyDoctor", "MsgAdminApproval",
        "MsgQqWhitelistStatus", "MsgQqWhitelistImport", "MsgQqAccessDenied", "MsgQqBindingChanged",
    };
    private static readonly HashSet<string> BestEffortOutboundProtocols = new(StringComparer.Ordinal)
    {
        "MsgOnlineCount", "MsgPlayerList", "MsgChatMsg", "MsgFriendChat", "MsgRateLimited",
    };
    private static readonly HashSet<string> RevokedQqGameProtocols = new(StringComparer.Ordinal)
    {
        "MsgPing", "MsgNetworkDiagnostics", "MsgGameAction", "MsgPromptResponse", "MsgRequestState",
        "MsgSurrender", "MsgEndByDisconnect", "MsgGameChat", "MsgBugReport", "MsgUpdateSettings",
    };
    // ── 会话注册表 ────────────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, WsSession> Sessions    = new();
    private static readonly ConcurrentDictionary<string, string>    AccountIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime>  SupersededClientInstances = new(StringComparer.Ordinal);
    private static readonly object                                  AccountIndexGate = new();
    // 兼容旧客户端：历史 casual 协议值继续表示狂野休闲匹配，但公开对战仍执行官网禁卡表。
    private static readonly ConcurrentQueue<WsSession>              MatchQueue   = new();
    private static readonly ConcurrentQueue<WsSession>              StandardCasualMatchQueue = new();
    private static readonly ConcurrentQueue<WsSession>              RankedMatchQueue = new();
    private static readonly ConcurrentQueue<WsSession>              WildRankedMatchQueue = new();
    private static readonly ConcurrentQueue<WsSession>              HexMatchQueue = new();
    private static readonly object                                  MatchQueueGate = new();
    private static readonly ConcurrentDictionary<string, string>    MatchAccountReservations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string>    GameOpponent = new();
    private static readonly ConcurrentDictionary<string, string>    PendingRooms = new(); // roomCode → 赛前房间ID
    private static readonly ConcurrentDictionary<string, InviteInfo> PendingInvites = new(); // inviteId → 邀请对战
    private static readonly ConcurrentDictionary<string, DuelLobby> FriendlyRooms = new(); // roomId → 共用赛前房间
    private static readonly ConcurrentDictionary<string, string> FriendlyByAccount = new(StringComparer.OrdinalIgnoreCase); // account → roomId
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> FriendlyDisconnectGrace = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> GameChatAt = new(); // sessionId → 上次局内聊天时间(限频防刷屏)
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<GameChatEvidence>> GameChatEvidenceByRoom = new(StringComparer.Ordinal);
    private static readonly PostGameChatRegistry PostGameChats = new(TimeSpan.FromMinutes(30));
    private static readonly ConcurrentDictionary<string, RecentOpponentContext> RecentOpponentContexts = new(StringComparer.Ordinal);
    private static readonly TimeSpan LobbyReconnectGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveSessionMaxIdle = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan SupersededClientLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan SupersededSessionCleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly bool ProtocolLogEnabled = ReadBooleanEnvironment("GRANDUMI_PROTOCOL_LOG");

    private sealed record InviteInfo(string Id, string FromSid, string FromAccount, string FromName, string ToSid);
    private sealed record GameChatEvidence(
        DateTime SentAtUtc,
        string? FromAccount,
        string FromName,
        string FromRole,
        string Text,
        string? Code);
    private sealed record RecentOpponentContext(
        string OpponentAccount,
        string RoomId,
        string MatchKind,
        int TurnCount,
        string? GameOverReason,
        GameChatEvidence[] RecentGameChat,
        DateTime ExpiresAtUtc);

    private static CancellationTokenSource _cts = new();
    private static PlayerDataStore _playerDataStore = null!;
    private static AccountAuthenticationStore _accountAuthenticationStore = null!;
    private static QqAccessStore _qqAccessStore = null!;
    private static OnlinePlayerHistoryStore? _onlinePlayerHistoryStore;
    private static OnlinePlayerHistoryStore? _onlinePlayerHistoryReadStore;
    private static AdminOperationsMetricsCache? _adminOperationsMetricsCache;
    private static AdminDeploymentCoordinator? _adminDeploymentCoordinator;
    private static CloudReplayStore? _cloudReplayStore;
    private static OperationsCenterStore? _operationsCenterStore;
    private static ConsistencyDoctor? _consistencyDoctor;
    private static bool _recordDailyActivePlayers;
    private static int _accepting;
    private static int _onlineBroadcastScheduled;
    private static int _onlineBroadcastVersion;
    private static long _supersededSessionTotal;
    private static long _qqSecurityEventCursor;

    // ── JSON 工具 ─────────────────────────────────────────────────────────
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string? Str(IReadOnlyDictionary<string, JsonElement> d, string key)
        => d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(IReadOnlyDictionary<string, JsonElement> d, string key, bool def = false)
    {
        if (!d.TryGetValue(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.True)  return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        return def;
    }

    private static int Int(IReadOnlyDictionary<string, JsonElement> d, string key, int def = 0)
        => d.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : def;

    private static long Long(IReadOnlyDictionary<string, JsonElement> d, string key, long def = 0)
        => d.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : def;

    // ── 生命周期 ──────────────────────────────────────────────────────────
    public static void Initialize(
        PlayerDataStore playerDataStore,
        AccountAuthenticationStore accountAuthenticationStore,
        QqAccessStore qqAccessStore,
        OnlinePlayerHistoryStore? onlinePlayerHistoryStore = null,
        OnlinePlayerHistoryStore? onlinePlayerHistoryReadStore = null,
        AdminOperationsMetricsCache? adminOperationsMetricsCache = null,
        AdminDeploymentCoordinator? adminDeploymentCoordinator = null,
        bool recordDailyActivePlayers = false,
        CloudReplayStore? cloudReplayStore = null,
        OperationsCenterStore? operationsCenterStore = null,
        ConsistencyDoctor? consistencyDoctor = null)
    {
        _playerDataStore = playerDataStore ?? throw new ArgumentNullException(nameof(playerDataStore));
        _accountAuthenticationStore = accountAuthenticationStore
            ?? throw new ArgumentNullException(nameof(accountAuthenticationStore));
        _qqAccessStore = qqAccessStore ?? throw new ArgumentNullException(nameof(qqAccessStore));
        _onlinePlayerHistoryStore = onlinePlayerHistoryStore;
        _onlinePlayerHistoryReadStore = onlinePlayerHistoryReadStore ?? onlinePlayerHistoryStore;
        _adminOperationsMetricsCache = adminOperationsMetricsCache;
        _adminDeploymentCoordinator = adminDeploymentCoordinator;
        _cloudReplayStore = cloudReplayStore;
        _operationsCenterStore = operationsCenterStore;
        _consistencyDoctor = consistencyDoctor;
        _recordDailyActivePlayers = recordDailyActivePlayers;
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _qqSecurityEventCursor = _qqAccessStore.GetLatestSecurityEventId();
        Volatile.Write(ref _accepting, 1);
        _ = RunOnlinePlayerSamplingAsync(_cts.Token);
        _ = RunQqSecurityEventPollingAsync(_cts.Token);
    }

    public static void Stop()
    {
        Volatile.Write(ref _accepting, 0);
        _cts.Cancel();
    }

    // ── 连接接受 ──────────────────────────────────────────────────────────
    public static bool IsReady => Volatile.Read(ref _accepting) != 0;
    public static int ConnectionCount => Sessions.Count;
    /// <summary>兼容既有调用：在线玩家数始终表示账号索引中的唯一权威会话数。</summary>
    public static int LoggedInCount => GetSessionMetrics().UniqueLoggedInAccountCount;
    public static int LoggedInSessionCount => Sessions.Count(item => item.Value.IsLoggedIn);
    public static int UniqueLoggedInAccountCount => GetSessionMetrics().UniqueLoggedInAccountCount;
    public static int SupersededSessionCount => Sessions.Count(item => item.Value.IsSuperseded);
    public static long SupersededSessionTotal => Interlocked.Read(ref _supersededSessionTotal);
    public static long DroppedOutboundCount => Sessions.Values.Sum(item => item.DroppedOutboundCount);
    public static int MaxCurrentOutboundDepth => Sessions.IsEmpty ? 0 : Sessions.Values.Max(item => item.OutboundDepth);

    internal readonly record struct SessionMetricSnapshot(
        int ConnectionCount,
        int LoggedInSessionCount,
        int UniqueLoggedInAccountCount,
        int SupersededSessionCount,
        long SupersededSessionTotal);

    internal static SessionMetricSnapshot GetSessionMetrics()
    {
        lock (AccountIndexGate)
        {
            var sessions = Sessions.Values.ToArray();
            var uniqueLoggedInAccounts = SnapshotCurrentAccountSessionsLocked().Length;
            return new SessionMetricSnapshot(
                sessions.Length,
                sessions.Count(session => session.IsLoggedIn),
                uniqueLoggedInAccounts,
                sessions.Count(session => session.IsSuperseded),
                Interlocked.Read(ref _supersededSessionTotal));
        }
    }

    public static async Task AcceptClientAsync(WebSocket socket, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (!IsReady)
        {
            await socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "服务器尚未就绪", CancellationToken.None);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, requestAborted);
        var session = new WsSession { Socket = socket };
        session.StartSender(message => SendDirectAsync(session, message));
        Sessions[session.SessionId] = session;
        Log($"连接 {session.SessionId}");

        await ReceiveLoop(session, linked.Token);
        await session.StopSenderAsync();
        CloseSession(session);
    }

    private static async Task ReceiveLoop(WsSession session, CancellationToken ct)
    {
        var buffer = new byte[16384];
        var ws     = session.Socket;
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var payload = new ArrayBufferWriter<byte>(16_384);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        return;
                    }
                    buffer.AsSpan(0, result.Count).CopyTo(payload.GetSpan(result.Count));
                    payload.Advance(result.Count);
                    if (payload.WrittenCount > MaxInboundMessageBytes)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "消息体过大", CancellationToken.None);
                        return;
                    }
                } while (!result.EndOfMessage);

                // 单连接按接收顺序路由；游戏动作进入房间队列后会立即返回，
                // 无需再为每条消息创建可能乱序的独立线程池任务。
                Route(session.SessionId, Encoding.UTF8.GetString(payload.WrittenSpan));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogErr($"接收 {session.SessionId}: {ex.Message}"); }
    }

    private static void CloseSession(WsSession session)
    {
        var wasCurrentAccountSession = false;
        lock (AccountIndexGate)
        {
            Sessions.TryRemove(session.SessionId, out _);
            if (session.Account is not null &&
                AccountIndex.TryGetValue(session.Account, out var indexedSession) &&
                indexedSession == session.SessionId)
            {
                AccountIndex.TryRemove(session.Account, out _);
                wasCurrentAccountSession = true;
            }
        }
        if (session.IsMatching) RebuildMatchQueue(session);
        // 对局中的玩家断开 → 进入 90s 宽限期（M1）
        GameOpponent.TryRemove(session.SessionId, out _);
        RecentOpponentContexts.TryRemove(session.SessionId, out _);
        GameChatAt.TryRemove(session.SessionId, out _);
        PostGameChats.Leave(session.SessionId);
        CleanupInvites(session.SessionId);
        if (session.Account is not null && wasCurrentAccountSession)
            HandleFriendlyDisconnect(session.Account);
        GameRoomManager.OnPlayerDisconnect(session.SessionId);
        Log($"断开 {session.SessionId} ({session.Account ?? "未登录"})");
        // 被替代旧会话的迟到清理不会改变权威在线人数，也不应放大广播风暴。
        if (wasCurrentAccountSession)
            BroadcastOnlineCount();
        if (session.Account is not null && wasCurrentAccountSession)
            PushFriendPresenceToFriends(session.Account);
    }

    // ── 消息路由 ──────────────────────────────────────────────────────────
    private static void Route(string sessionId, string json)
    {
        if (!Sessions.TryGetValue(sessionId, out var session) || session.IsSuperseded) return;
        session.MarkSeen();

        Dictionary<string, JsonElement>? msg;
        try { msg = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts); }
        catch { LogErr($"JSON 解析失败: {json[..Math.Min(80, json.Length)]}"); return; }

        if (msg is null) return;
        var proto = Str(msg, "proto");
        if (proto is null) return;

        if (session.QqBootstrapAccount is not null
            && proto is not ("MsgSecret" or "MsgPing" or "MsgNetworkDiagnostics" or "MsgLogin"
                or "MsgQqWhitelistStatus" or "MsgQqWhitelistImport"))
        {
            Send(session.SessionId, new
            {
                proto = "MsgQqAccessDenied",
                result = false,
                logStr = "当前仅可初始化 QQ 群白名单；导入后请先完成 QQ 绑定。",
            });
            return;
        }

        if (session.IsQqAccessRevoked && !RevokedQqGameProtocols.Contains(proto))
        {
            Send(session.SessionId, new
            {
                proto = "MsgQqAccessDenied",
                result = false,
                currentGameContinues = true,
                logStr = "QQ 绑定资格已变化；当前已注册对局可继续，其他操作需要重新登录并完成绑定。",
            });
            return;
        }

        var allowed = proto == "MsgPing"
            ? session.TryConsumeRateLimit("ping", capacity: 6, refillPerSecond: 0.5)
            : session.TryConsumeRateLimit("messages", capacity: 120, refillPerSecond: 40);
        if (!allowed)
        {
            LatencyDiagnostics.RecordMetric("WebSocket 入口限流", 1, "条");
            return;
        }

        if (ProtocolLogEnabled && proto != "MsgPing")
            Log($"← {proto,-20} ({session.Account ?? session.SessionId[..8]})");

        switch (proto)
        {
            case "MsgSecret":      OnSecret(session, msg);      break;
            case "MsgPing":        OnPing(session, msg);         break;
            case "MsgNetworkDiagnostics": OnNetworkDiagnostics(session, msg); break;
            case "MsgLogin":       OnLogin(session, msg);        break;
            case "MsgQqWhitelistStatus": OnQqWhitelistStatus(session); break;
            case "MsgQqWhitelistImport": OnQqWhitelistImport(session, msg); break;
            case "MsgAddAccount":  OnAddAccount(session, msg);   break;
            case "MsgUpdatePs":    OnUpdatePs(session, msg);     break;
            case "MsgSaveDeck":    OnSaveDeck(session, msg);     break;
            case "MsgDeleteDeck":  OnDeleteDeck(session, msg);   break;
            case "MsgSelectDeck":  OnSelectDeck(session, msg);   break;
            case "MsgUpdateProfile": OnUpdateProfile(session, msg); break;
            case "MsgUpdateChampionTitle": OnUpdateChampionTitle(session, msg); break;
            case "MsgUpdateCardBack": OnUpdateCardBack(session, msg); break;
            case "MsgCardBackGallery": OnCardBackGallery(session, msg); break;
            case "MsgUploadCardBack": OnUploadCardBack(session, msg); break;
            case "MsgLikeCardBack": OnLikeCardBack(session, msg); break;
            case "MsgDeleteCardBack": OnDeleteCardBack(session, msg); break;
            case "MsgCardBackReviewQueue": OnCardBackReviewQueue(session); break;
            case "MsgReviewCardBack": OnReviewCardBack(session, msg); break;
            case "MsgImportDecks": OnImportDecks(session, msg);  break;
            case "MsgDeckPlazaList": OnDeckPlazaList(session, msg); break;
            case "MsgPublishDeckPlaza": OnPublishDeckPlaza(session, msg); break;
            case "MsgLikeDeckPlaza": OnLikeDeckPlaza(session, msg); break;
            case "MsgCopyDeckPlaza": OnCopyDeckPlaza(session, msg); break;
            case "MsgDeleteDeckPlaza": OnDeleteDeckPlaza(session, msg); break;
            case "MsgEnterMatch":  OnEnterMatch(session, msg);   break;
            case "MsgRankSnapshot": SendRankSnapshot(session, RankedModeWire.Parse(Str(msg, "mode")), Str(msg, "requestId")); break;
            case "MsgSelectRankFaction": OnSelectRankFaction(session, msg); break;
            case "MsgEnterBotMatch": OnEnterBotMatch(session, msg); break;
            case "MsgCancelMatch": OnCancelMatch(session, msg);  break;
            case "MsgCreateRoom":  OnCreateRoom(session, msg);   break;
            case "MsgJoinRoom":    OnJoinRoom(session, msg);     break;
            case "MsgCancelRoom":  OnCancelRoom(session, msg);   break;
            case "MsgPlayerList":  OnPlayerList(session, msg);   break;
            case "MsgFriendList": OnFriendList(session); break;
            case "MsgFriendSearch": OnFriendSearch(session, msg); break;
            case "MsgFriendRequest": OnFriendRequest(session, msg); break;
            case "MsgFriendRespond": OnFriendRespond(session, msg); break;
            case "MsgFriendCancel": OnFriendCancel(session, msg); break;
            case "MsgFriendRemove": OnFriendRemove(session, msg); break;
            case "MsgPlayerSafety": OnPlayerSafety(session, msg); break;
            case "MsgLeaderLeaderboard": OnLeaderLeaderboard(session, msg); break;
            case "MsgLeaderChampionQuery": OnLeaderChampionQuery(session, msg); break;
            case "MsgLeaderMatchups": OnLeaderMatchups(session, msg); break;
            case "MsgLeaderMatchupMatrix": OnLeaderMatchupMatrix(session, msg); break;
            case "MsgPlayerProfileStats": OnPlayerProfileStats(session, msg); break;
            case "MsgInvitePlayer": OnInvitePlayer(session, msg); break;
            case "MsgInviteResponse": OnInviteResponse(session, msg); break;
            case "MsgFriendlySelectDeck": OnFriendlySelectDeck(session, msg); break;
            case "MsgFriendlyReady": OnFriendlyReady(session, msg); break;
            case "MsgFriendlyLeave": OnFriendlyLeave(session); break;
            case "MsgTransmit":    OnTransmit(session, msg);     break;
            case "MsgSurrender":   OnSurrender(session);         break;
            case "MsgChatMsg":     OnChatMsg(session, msg);      break;
            case "MsgGameChat":    OnGameChat(session, msg);     break;
            case "MsgFriendChat":  OnFriendChat(session, msg);   break;
            case "MsgLeaveGameChat": OnLeaveGameChat(session);   break;
            case "MsgGlobalAnnouncement": OnGlobalAnnouncement(session, msg); break;
            case "MsgMaintenanceState": OnMaintenanceState(session); break;
            case "MsgSetMaintenance": OnSetMaintenance(session, msg); break;
            case "MsgRulesetState": OnRulesetState(session); break;
            case "MsgActivateRuleset": OnActivateRuleset(session, msg); break;
            case "MsgAdminOperations": OnAdminOperations(session); break;
            case "MsgAdminDeploy": OnAdminDeploy(session, msg); break;
            case "MsgAdminPlayerSearch": OnAdminPlayerSearch(session, msg); break;
            case "MsgAdminPlayerUpdate": OnAdminPlayerUpdate(session, msg); break;
            case "MsgOperationsCases": OnOperationsCases(session, msg); break;
            case "MsgOperationsCaseDetail": OnOperationsCaseDetail(session, msg); break;
            case "MsgOperationsCaseUpdate": OnOperationsCaseUpdate(session, msg); break;
            case "MsgOperationsCaseAppeal": OnOperationsCaseAppeal(session, msg); break;
            case "MsgOperationsPenalty": OnOperationsPenalty(session, msg); break;
            case "MsgPrivilegedAudit": OnPrivilegedAudit(session, msg); break;
            case "MsgConsistencyDoctor": OnConsistencyDoctor(session, msg); break;
            case "MsgAdminApproval": OnAdminApproval(session, msg); break;
            // Sprint 3: 服务端结算协议
            case "MsgGameAction":  OnGameAction(session, msg);   break;
            case "MsgPromptResponse": OnPromptResponse(session, msg); break;
            case "MsgUpdateSettings": OnUpdateSettings(session, msg); break;
            case "MsgUpdateSpectateSettings": OnUpdateSpectateSettings(session, msg); break;
            case "MsgRequestState": OnRequestState(session);     break;
            case "MsgEndByDisconnect": OnEndByDisconnect(session); break;
            case "MsgSpectateRoom": OnSpectateRoom(session, msg); break;
            case "MsgLeaveSpectate": OnLeaveSpectate(session); break;
            case "MsgRequestSpectatorHand": OnRequestSpectatorHand(session); break;
            case "MsgRespondSpectatorHand": OnRespondSpectatorHand(session, msg); break;
            case "MsgKickSpectator": OnKickSpectator(session, msg); break;
            case "MsgBugReport":   OnBugReport(session, msg);     break;
            case "MsgCloudReplayList": OnCloudReplayList(session, msg); break;
            case "MsgCloudReplayLoad": OnCloudReplayLoad(session, msg); break;
            case "MsgCloudReplayBookmark": OnCloudReplayBookmark(session, msg); break;
            case "MsgCloudReplayShare": OnCloudReplayShare(session, msg); break;
            case "MsgCloudReplayDelete": OnCloudReplayDelete(session, msg); break;
            default: LogWarn($"未知协议: {proto}"); break;
        }
    }

    // ── 协议处理器 ────────────────────────────────────────────────────────

    private static void OnSecret(WsSession s, Dictionary<string, JsonElement> msg)
    {
        // 版本校验（目前全部放行，可在此处比对版本号）
        s.SupportsDeltaSnapshots = Bool(msg, "supportsStateDelta");
        Send(s.SessionId, new
        {
            proto = "MsgSecret",
            Secret = "",
            result = true,
            vesion = "0.999",
            stateDeltaEnabled = s.SupportsDeltaSnapshots,
        });
    }

    private static void OnPing(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
        => Send(s.SessionId, new { proto = "MsgPing", id = Str(msg, "id") });

    /// <summary>接收不含账号隐私的线路质量摘要，用于区分入口波动、握手慢和服务端排队。</summary>
    private static void OnNetworkDiagnostics(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.TryConsumeRateLimit("network-diagnostics", capacity: 3, refillPerSecond: 1d / 60d)) return;

        static double Number(IReadOnlyDictionary<string, JsonElement> values, string key)
            => values.TryGetValue(key, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetDouble(out var parsed)
                ? Math.Clamp(parsed, 0, 60_000)
                : 0;

        var endpointHost = (Str(msg, "endpointHost") ?? "unknown").Trim();
        if (endpointHost.Length > 100) endpointHost = endpointHost[..100];
        endpointHost = endpointHost.ToLowerInvariant() switch
        {
            "ygo.grand-umi.com" => "ygo.grand-umi.com",
            "grand-umi.com" => "grand-umi.com",
            "direct.grand-umi.com" => "direct.grand-umi.com",
            "test.grand-umi.com" => "test.grand-umi.com",
            "103.146.230.37" => "103.146.230.37",
            "localhost:8080" => "localhost",
            _ => "other",
        };
        LatencyDiagnostics.RecordMetric($"客户端握手:{endpointHost}", Number(msg, "handshakeMs"), "ms");
        var rtt = Number(msg, "rttMs");
        if (rtt > 0) LatencyDiagnostics.RecordMetric($"客户端RTT:{endpointHost}", rtt, "ms");
        LatencyDiagnostics.RecordMetric($"客户端线路失败:{endpointHost}", Number(msg, "endpointFailureCount"), "次");
        LatencyDiagnostics.RecordMetric("客户端重连累计", Number(msg, "reconnectCount"), "次");
    }

    private static void OnLogin(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedAccount = Str(msg, "account") ?? "";
        var clientInstanceId = Str(msg, "clientInstanceId")?.Trim();
        var isResume = Bool(msg, "resume");
        if (!s.TryConsumeRateLimit("account-login", capacity: 6, refillPerSecond: 0.1))
        {
            Send(s.SessionId, new
            {
                proto = "MsgLogin",
                account = requestedAccount,
                result = false,
                needsPassword = true,
                needsPasswordSetup = false,
                needsQqBinding = false,
                authChallenge = false,
                logStr = "登录尝试过于频繁，请稍后再试。",
            });
            return;
        }
        try
        {
            var authentication = _accountAuthenticationStore.Authenticate(
                requestedAccount,
                Str(msg, "password"),
                Str(msg, "authToken"));
            if (!authentication.Success)
            {
                Send(s.SessionId, new
                {
                    proto = "MsgLogin",
                    account = authentication.Account,
                    result = false,
                    needsPassword = authentication.NeedsPassword,
                    needsPasswordSetup = authentication.NeedsPasswordSetup,
                    needsQqBinding = false,
                    authChallenge = authentication.IsChallenge,
                    logStr = authentication.Message,
                });
                return;
            }

            string? submittedQq = null;
            if (msg.TryGetValue("qq", out var qqElement))
            {
                if (qqElement.ValueKind != JsonValueKind.String)
                    throw new QqAccessValidationException("QQ 必须以字符串提交，不能使用 JSON number。");
                submittedQq = qqElement.GetString();
            }

            // 密码或会话令牌认证只是第一道门。每一次登录/恢复都必须重新读取服务端 QQ 权威状态；
            // 只有 Allowed 才能进入账号索引、恢复赛前房间或重领进行中的对局。
            var qqAccess = _qqAccessStore.EvaluateLogin(authentication.Account, submittedQq);
            var restrictedGameRecovery = false;
            if (qqAccess.Kind == QqLoginAccessKind.WhitelistUninitialized)
            {
                if (AdministratorPolicy.IsAuthorized(authentication.Account)
                    && _qqAccessStore.IsBootstrapAdministrator(authentication.Account))
                {
                    s.QqBootstrapAccount = authentication.Account;
                    Send(s.SessionId, new
                    {
                        proto = "MsgLogin",
                        account = authentication.Account,
                        result = false,
                        needsPassword = false,
                        needsPasswordSetup = false,
                        needsQqBinding = false,
                        needsQqWhitelistInitialization = true,
                        canInitializeQqWhitelist = true,
                        authChallenge = true,
                        authToken = authentication.AuthToken,
                        logStr = "QQ 群白名单尚未初始化。当前会话仅可导入首份名单；导入后仍需绑定名单内 QQ。",
                    });
                }
                else
                {
                    s.QqBootstrapAccount = null;
                    Send(s.SessionId, new
                    {
                        proto = "MsgLogin",
                        account = authentication.Account,
                        result = false,
                        needsPassword = false,
                        needsPasswordSetup = false,
                        needsQqBinding = false,
                        needsQqWhitelistInitialization = true,
                        canInitializeQqWhitelist = false,
                        authChallenge = false,
                        logStr = qqAccess.Message,
                    });
                }
                return;
            }

            if (!qqAccess.Allowed)
            {
                // 管理员解绑或白名单变化不打断已经注册的对局。网络中断后允许玩家凭密码
                // 只恢复该局；恢复会话仍被标记为受限，不能进入大厅或发起下一局。
                restrictedGameRecovery = GameRoomManager.HasActivePlayerAccount(authentication.Account);
                if (!restrictedGameRecovery)
                {
                    s.QqBootstrapAccount = null;
                    Send(s.SessionId, new
                    {
                        proto = "MsgLogin",
                        account = authentication.Account,
                        result = false,
                        needsPassword = false,
                        needsPasswordSetup = false,
                        needsQqBinding = qqAccess.NeedsBinding,
                        needsQqWhitelistInitialization = false,
                        canInitializeQqWhitelist = false,
                        authChallenge = qqAccess.NeedsBinding,
                        authToken = qqAccess.NeedsBinding ? authentication.AuthToken : null,
                        qqWhitelistVersion = qqAccess.WhitelistVersion,
                        qqMasked = qqAccess.MaskedQq,
                        logStr = qqAccess.Message,
                    });
                    return;
                }
            }

            s.QqBootstrapAccount = null;

            if (isResume && IsSupersededClientInstance(clientInstanceId))
            {
                SupersedeSession(s);
                return;
            }

            var playerData = _playerDataStore.Login(authentication.Account);
            if (_recordDailyActivePlayers)
            {
                try { _onlinePlayerHistoryStore?.RecordActivePlayer(playerData.Account); }
                catch (Exception ex) { LogErr($"记录日活玩家失败 {playerData.Account}: {ex.Message}"); }
            }

            s.PlayerName = playerData.DisplayName;
            s.CardBackId = playerData.CardBackId;
            if (restrictedGameRecovery) s.MarkQqAccessRevoked();
            else s.ClearQqAccessRevoked();
            if (!TryBindAccountSession(
                    s,
                    playerData.Account,
                    clientInstanceId,
                    rejectSupersededClientInstance: isResume,
                    out var supersededSession))
            {
                // 认证可能与另一处主动登录并发完成；已失权的迟到恢复不得重新夺回账号索引。
                SupersedeSession(s);
                return;
            }

            // 旧会话在索引切换锁内已经立即失权；此处只启动一次有界终止与回收流程。
            if (supersededSession is not null)
                SupersedeSession(supersededSession);

            // 后续登录回包和恢复只能由此刻仍为权威的连接执行。
            if (!IsCurrentAccountSession(s)) return;

            var (championLeaderNumbers, equippedChampionLeaderNumber) = ResolveChampionTitleSnapshot(playerData);

            Send(s.SessionId, new
            {
                proto = "MsgLogin",
                account = playerData.Account,
                name = playerData.DisplayName,
                avatar = playerData.Avatar,
                cardBackId = playerData.CardBackId,
                canChangeDisplayName = playerData.CanChangeDisplayName,
                selectedDeckName = playerData.SelectedDeckName,
                championLeaderNumbers,
                equippedChampionLeaderNumber,
                decks = playerData.Decks,
                authToken = authentication.AuthToken,
                qqMasked = qqAccess.MaskedQq,
                qqWhitelistVersion = qqAccess.WhitelistVersion,
                qqAccessRestricted = restrictedGameRecovery,
                result = true,
                logStr = restrictedGameRecovery
                    ? "QQ 资格已变化，本次只恢复正在进行的对局；对局结束后需重新绑定。"
                    : submittedQq is null ? authentication.Message : qqAccess.Message,
            });
            if (!restrictedGameRecovery)
            {
                SendFriendData(s, _playerDataStore.GetFriendData(playerData.Account));
                PushQueuedFriendMessages(s);
                SendRankSnapshot(s);
                SendMaintenanceState(s);
            }
            Log($"登录 ✅ {playerData.Account}");

            // 两个登录请求并发时，只有当前账号索引指向的最新连接有权恢复房间。
            if (!IsCurrentAccountSession(s)) return;

            // 登录后尝试断线重连：如该账号还有未结束的对局，自动恢复。
            if (GameRoomManager.TryReclaim(s.SessionId, playerData.Account, playerData.CardBackId))
            {
                Log($"断线重连成功 {playerData.Account}");
                if (restrictedGameRecovery) ScheduleQqRevokedSessionClose(s);
            }
            else if (restrictedGameRecovery)
            {
                SupersedeSession(s, "原对局已经结束，请重新登录并完成 QQ 绑定。");
                return;
            }
            else
                TryRestoreFriendlyRoom(s, playerData.Account);

            BroadcastOnlineCount();
            PushFriendPresenceToFriends(playerData.Account);
        }
        catch (PlayerDataValidationException ex)
        {
            Send(s.SessionId, new { proto = "MsgLogin", account = requestedAccount, name = "", result = false, logStr = ex.Message });
            Log($"登录 ❌ {requestedAccount}: {ex.Message}");
        }
        catch (QqAccessValidationException ex)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLogin",
                account = requestedAccount,
                name = "",
                result = false,
                needsPassword = false,
                needsPasswordSetup = false,
                needsQqBinding = true,
                authChallenge = false,
                logStr = ex.Message,
            });
            Log($"QQ 准入拒绝 {requestedAccount}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Send(s.SessionId, new { proto = "MsgLogin", account = requestedAccount, name = "", result = false, logStr = "玩家数据库暂时不可用" });
            LogErr($"登录数据库异常 {requestedAccount}: {ex.Message}");
        }
    }

    /// <summary>
    /// 在账号索引锁内原子切换权威会话。旧会话先被标记为失权，再发布新索引，
    /// 因此统计、路由和迟到清理都不会把两个连接同时视为在线账号。
    /// </summary>
    private static bool TryBindAccountSession(
        WsSession session,
        string account,
        string? clientInstanceId,
        bool rejectSupersededClientInstance,
        out WsSession? supersededSession)
    {
        supersededSession = null;
        lock (AccountIndexGate)
        {
            if (session.IsSuperseded ||
                (rejectSupersededClientInstance && IsSupersededClientInstance(clientInstanceId)))
                return false;

            if (!rejectSupersededClientInstance && !string.IsNullOrEmpty(clientInstanceId))
                SupersededClientInstances.TryRemove(clientInstanceId, out _);

            if (session.Account is not null &&
                !string.Equals(session.Account, account, StringComparison.OrdinalIgnoreCase) &&
                AccountIndex.TryGetValue(session.Account, out var previousForOldAccount) &&
                previousForOldAccount == session.SessionId)
                AccountIndex.TryRemove(session.Account, out _);

            if (AccountIndex.TryGetValue(account, out var previousForAccount) &&
                previousForAccount != session.SessionId &&
                Sessions.TryGetValue(previousForAccount, out var previousSession))
            {
                supersededSession = previousSession;
                MarkSessionSuperseded(previousSession);
                if (!string.Equals(previousSession.ClientInstanceId, clientInstanceId, StringComparison.Ordinal))
                    MarkClientInstanceSuperseded(previousSession.ClientInstanceId);
            }

            session.Account = account;
            session.ClientInstanceId = clientInstanceId;
            AccountIndex[account] = session.SessionId;
            return true;
        }
    }

    private static bool MarkSessionSuperseded(WsSession session)
    {
        if (!session.TryMarkSuperseded()) return false;
        Interlocked.Increment(ref _supersededSessionTotal);
        return true;
    }

    private static void SupersedeSession(WsSession session, string reason = SessionReplacedMessage)
    {
        MarkSessionSuperseded(session);
        if (session.IsMatching) RebuildMatchQueue(session);
        if (!session.TryBeginSupersededCleanup()) return;

        _ = Task.Run(async () =>
        {
            using var cleanupTimeout = new CancellationTokenSource(SupersededSessionCleanupTimeout);
            try
            {
                await session.EnqueueTerminalAsync(new
                    {
                        proto = "MsgSessionReplaced",
                        reason,
                        logStr = reason,
                    })
                    .WaitAsync(cleanupTimeout.Token);
                if (session.Socket.State == WebSocketState.Open)
                {
                    await session.Socket.CloseOutputAsync(
                            (WebSocketCloseStatus)SessionReplacedCloseCode,
                            SessionReplacedMessage,
                            cleanupTimeout.Token)
                        .WaitAsync(cleanupTimeout.Token);
                }

                // 给遵守关闭握手的客户端一个主动退出窗口；不响应的旧连接在总超时后强制回收。
                // 使用单个取消计时器，避免重连风暴下为每个旧会话反复轮询注册表。
                await Task.Delay(Timeout.InfiniteTimeSpan, cleanupTimeout.Token);
            }
            catch (OperationCanceledException) { }
            catch
            {
                // 统一在 finally 中按注册表状态决定是否仍需强制回收。
            }
            finally
            {
                if (Sessions.ContainsKey(session.SessionId))
                {
                    try { session.Socket.Abort(); } catch { }
                }
            }
        });
    }

    private static bool IsSupersededClientInstance(string? clientInstanceId)
    {
        if (string.IsNullOrEmpty(clientInstanceId)) return false;
        if (!SupersededClientInstances.TryGetValue(clientInstanceId, out var expiresAt)) return false;
        if (expiresAt > DateTime.UtcNow) return true;
        SupersededClientInstances.TryRemove(clientInstanceId, out _);
        return false;
    }

    private static void MarkClientInstanceSuperseded(string? clientInstanceId)
    {
        if (string.IsNullOrEmpty(clientInstanceId)) return;
        SupersededClientInstances[clientInstanceId] = DateTime.UtcNow + SupersededClientLifetime;
        if (SupersededClientInstances.Count <= 10_000) return;
        var now = DateTime.UtcNow;
        foreach (var entry in SupersededClientInstances)
        {
            if (entry.Value <= now) SupersededClientInstances.TryRemove(entry.Key, out _);
        }
    }

    private static void OnLegacyLogin(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var account  = Str(msg, "account")  ?? "";

        // ── TODO: 替换为真实的账户验证逻辑 ──────────────────────────────
        // 现仅凭账号登录，不再校验密码
        bool ok   = account.Length > 0;
        var  name = ok ? account : "";
        // ────────────────────────────────────────────────────────────────

        if (ok)
        {
            s.PlayerName = name;
            ok = TryBindAccountSession(
                s,
                account,
                clientInstanceId: null,
                rejectSupersededClientInstance: false,
                out var supersededSession);
            if (supersededSession is not null)
                SupersedeSession(supersededSession);
        }

        Send(s.SessionId, new { proto = "MsgLogin", account, name, result = ok,
                                logStr = ok ? "登录成功" : "账号不能为空" });
        Log($"登录 {(ok ? "✅" : "❌")} {account}");

        // 登录后尝试断线重连：如该账号还有未结束的对局，自动恢复
        if (ok && IsCurrentAccountSession(s) && GameRoomManager.TryReclaim(s.SessionId, account))
            Log($"断线重连成功 {account}");

        // 在线人数变化，广播给所有客户端（含刚登录的本会话）
        if (ok) BroadcastOnlineCount();
    }

    private static void OnAddAccount(WsSession s, Dictionary<string, JsonElement> msg)
    {
        // TODO: 注册账号逻辑
        Log($"注册 {Str(msg, "id")}");
    }

    private static void OnUpdatePs(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        if (!s.TryConsumeRateLimit("password-change", capacity: 4, refillPerSecond: 0.05))
        {
            Send(s.SessionId, new { proto = "MsgUpdatePs", result = false, logStr = "尝试过于频繁，请稍后再试。" });
            return;
        }

        try
        {
            var result = _accountAuthenticationStore.ChangePassword(
                s.Account!,
                Str(msg, "currentPassword") ?? "",
                Str(msg, "newPassword") ?? Str(msg, "newPs") ?? "");
            Send(s.SessionId, new
            {
                proto = "MsgUpdatePs",
                result = result.Success,
                authToken = result.AuthToken,
                logStr = result.Message,
            });
        }
        catch (Exception ex)
        {
            LogErr($"修改密码异常 {s.Account}: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgUpdatePs", result = false, logStr = "密码修改失败，请稍后再试。" });
        }
    }

    private static void OnSaveDeck(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var deck = DeserializeDeck(msg, "deck");
            ValidatePlayableDeck(deck);
            SendPlayerData(s, _playerDataStore.SaveDeck(s.Account!, deck));
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "保存卡组失败"); }
    }

    private static void OnDeleteDeck(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            SendPlayerData(s, _playerDataStore.DeleteDeck(s.Account!, Str(msg, "name") ?? ""));
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "删除卡组失败"); }
    }

    private static void OnSelectDeck(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            SendPlayerData(s, _playerDataStore.SelectDeck(s.Account!, Str(msg, "name")));
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "选择卡组失败"); }
    }

    private static void OnUpdateProfile(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var snapshot = _playerDataStore.UpdateProfile(
                s.Account!,
                Str(msg, "displayName") ?? "",
                Str(msg, "avatar") ?? "");
            s.PlayerName = snapshot.DisplayName;
            SendPlayerData(s, snapshot);
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "更新玩家资料失败"); }
    }

    private static void OnUpdateChampionTitle(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            if (!s.TryConsumeRateLimit("champion-title-update", capacity: 6, refillPerSecond: 0.5))
                throw new PlayerDataValidationException("称号操作过于频繁，请稍后再试。");
            var leaderNumber = PlayerDataStore.NormalizeChampionLeaderNumber(Str(msg, "leaderNumber"));
            if (!LeaderChampionStore.Default.IsChampion(s.Account, leaderNumber))
                throw new PlayerDataValidationException("你当前未持有该 Leader 的“最强”称号。");
            var snapshot = _playerDataStore.UpdateEquippedChampionTitle(s.Account!, leaderNumber);
            SendPlayerData(s, snapshot, $"已装备“最强{leaderNumber}”称号");
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "装备最强称号失败"); }
    }

    private static void OnUpdateCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var result = _playerDataStore.UpdateCardBack(s.Account!, Str(msg, "cardBackId") ?? "");
            s.CardBackId = result.Snapshot.CardBackId;
            SendPlayerData(s, result.Snapshot, "卡背已保存并点亮了红心");
            if (result.GalleryItem is not null) SendCardBackLikeUpdate(s, result.GalleryItem);
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "保存卡背失败"); }
    }

    private static void OnCardBackGallery(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        var cursor = Str(msg, "cursor");
        var sortOrder = Str(msg, "sortOrder");
        try
        {
            var page = _playerDataStore.GetCardBackGalleryPage(
                s.Account!,
                cursor,
                Int(msg, "pageSize", PlayerDataStore.DefaultCardBackGalleryPageSize),
                sortOrder);
            SendCardBackGalleryPage(s, page, cursor);
        }
        catch (Exception ex)
        {
            SendCardBackGalleryError(s, ex, "读取卡背广场失败", cursor, sortOrder);
        }
    }

    private static void OnUploadCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        var sortOrder = Str(msg, "sortOrder");
        try
        {
            _playerDataStore.UploadCardBack(
                s.Account!,
                Str(msg, "name") ?? "",
                Str(msg, "mimeType") ?? "",
                Str(msg, "imageBase64") ?? "");
            SendCardBackGalleryPage(
                s,
                _playerDataStore.GetCardBackGalleryPage(s.Account!, sortOrder: sortOrder),
                requestCursor: null,
                logStr: "卡背已提交审核，通过后将在广场展示");
        }
        catch (Exception ex) { SendCardBackGalleryError(s, ex, "上传卡背失败", sortOrder: sortOrder); }
    }

    private static void OnLikeCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var item = _playerDataStore.ToggleCardBackLike(s.Account!, Str(msg, "cardBackId") ?? "");
            SendCardBackLikeUpdate(s, item);
        }
        catch (Exception ex) { SendCardBackLikeError(s, ex); }
    }

    private static void OnDeleteCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        var cardBackId = Str(msg, "cardBackId") ?? "";
        var sortOrder = Str(msg, "sortOrder");
        var administrator = AdministratorPolicy.IsAuthorized(s.Account);
        var auditCorrelation = "";
        try
        {
            if (administrator)
            {
                auditCorrelation = RequirePrivilegedAuditIntent(
                    s.Account!, "card_back_delete", cardBackId, Str(msg, "requestId"),
                    new { cardBackId });
            }
            var result = _playerDataStore.DeleteCardBack(
                s.Account!,
                cardBackId,
                canManagePublishedCardBacks: administrator);
            if (administrator)
            {
                CompletePrivilegedAudit(
                    s.Account!, "card_back_delete", cardBackId, auditCorrelation, "success",
                    new { result.DeletedCardBackId });
            }
            s.CardBackId = result.Snapshot.CardBackId;
            SendPlayerData(s, result.Snapshot, "卡背已删除并从广场下架");
            SendCardBackGalleryPage(
                s,
                _playerDataStore.GetCardBackGalleryPage(s.Account!, sortOrder: sortOrder),
                requestCursor: null);

            foreach (var session in Sessions.Values.Where(IsCurrentAccountSession))
            {
                if (session.SessionId == s.SessionId || session.Account is null) continue;
                try
                {
                    if (session.CardBackId == result.DeletedCardBackId)
                    {
                        var snapshot = _playerDataStore.GetPlayerData(session.Account);
                        session.CardBackId = snapshot.CardBackId;
                        SendPlayerData(session, snapshot, "正在使用的卡背已下架，已恢复为经典卡背");
                    }
                    SendCardBackGalleryPage(
                        session,
                        _playerDataStore.GetCardBackGalleryPage(session.Account),
                        requestCursor: null);
                }
                catch (Exception ex) { LogErr($"同步卡背删除结果 {session.Account}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            if (administrator)
            {
                CompletePrivilegedAudit(
                    s.Account!, "card_back_delete", cardBackId, auditCorrelation, "failed",
                    new { error = ex.Message });
            }
            SendCardBackGalleryError(s, ex, "删除卡背失败", sortOrder: sortOrder);
        }
    }

    private static void OnCardBackReviewQueue(WsSession s)
    {
        if (!TryRequirePlayer(s)) return;
        if (!AdministratorPolicy.IsAuthorized(s.Account))
        {
            Send(s.SessionId, new
            {
                proto = "MsgCardBackReviewQueue",
                result = false,
                canReview = false,
                logStr = "没有审核卡背的权限",
            });
            return;
        }

        try { SendCardBackReviewQueue(s, _playerDataStore.GetPendingCardBackReviews()); }
        catch (Exception ex) { SendCardBackReviewError(s, ex, "读取卡背审核队列失败"); }
    }

    private static void OnReviewCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        if (!AdministratorPolicy.IsAuthorized(s.Account))
        {
            Send(s.SessionId, new
            {
                proto = "MsgCardBackReviewQueue",
                result = false,
                canReview = false,
                logStr = "没有审核卡背的权限",
            });
            return;
        }

        var cardBackId = Str(msg, "cardBackId") ?? "";
        var approved = Bool(msg, "approved");
        var auditCorrelation = "";
        try
        {
            auditCorrelation = RequirePrivilegedAuditIntent(
                s.Account!, "card_back_review", cardBackId, Str(msg, "requestId"),
                new { approved });
            _playerDataStore.ReviewCardBack(
                s.Account!,
                cardBackId,
                approved,
                Str(msg, "reason"));
            CompletePrivilegedAudit(
                s.Account!, "card_back_review", cardBackId, auditCorrelation, "success",
                new { approved });
            SendCardBackReviewQueue(
                s,
                _playerDataStore.GetPendingCardBackReviews(),
                approved ? "卡背已审核通过" : "卡背已标记为未通过");
        }
        catch (Exception ex)
        {
            CompletePrivilegedAudit(
                s.Account!, "card_back_review", cardBackId, auditCorrelation, "failed",
                new { approved, error = ex.Message });
            SendCardBackReviewError(s, ex, "审核卡背失败");
        }
    }

    private static void OnImportDecks(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            if (!msg.TryGetValue("decks", out var value) || value.ValueKind != JsonValueKind.Array)
                throw new PlayerDataValidationException("导入卡组数据无效。");

            var sourceDecks = JsonSerializer.Deserialize<List<StoredDeck>>(value.GetRawText(), JsonOpts) ?? [];
            var validDecks = new List<StoredDeck>();
            var invalid = 0;
            foreach (var deck in sourceDecks.Take(PlayerDataStore.MaxDecksPerPlayer))
            {
                try
                {
                    ValidatePlayableDeck(deck);
                    validDecks.Add(deck);
                }
                catch (PlayerDataValidationException) { invalid++; }
            }

            var result = _playerDataStore.ImportDecks(s.Account!, validDecks);
            var details = $"已导入 {result.Imported} 副本地卡组";
            if (result.Renamed > 0) details += $"，{result.Renamed} 副因重名已改名";
            var skipped = result.Skipped + invalid;
            if (skipped > 0) details += $"，跳过 {skipped} 副";
            SendPlayerData(s, result.Snapshot, details);
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "导入本地卡组失败"); }
    }

    private static void OnDeckPlazaList(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var page = _playerDataStore.GetDeckPlaza(
                s.Account!,
                Int(msg, "page", 1),
                Int(msg, "pageSize", 20),
                Str(msg, "sort") ?? "popular",
                Str(msg, "query"),
                Str(msg, "color"),
                Bool(msg, "mineOnly"));
            SendDeckPlazaPage(s, page);
        }
        catch (Exception ex) { SendDeckPlazaError(s, "MsgDeckPlazaList", ex, "读取卡组广场失败"); }
    }

    private static void OnPublishDeckPlaza(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        if (!s.TryConsumeRateLimit("deck-plaza-publish", capacity: 4, refillPerSecond: 0.1))
        {
            Send(s.SessionId, new { proto = "MsgPublishDeckPlaza", result = false, logStr = "操作过于频繁，请稍后再试" });
            return;
        }
        try
        {
            var sourceDeckName = Str(msg, "sourceDeckName") ?? "";
            var deck = _playerDataStore.GetPlayerData(s.Account!).Decks
                .FirstOrDefault(item => string.Equals(item.Name, sourceDeckName, StringComparison.OrdinalIgnoreCase))
                ?? throw new PlayerDataValidationException("要发布的卡组不存在。");
            ValidatePlayableDeck(deck);
            var leader = CardDatabase.Get(deck.Leader)
                ?? throw new PlayerDataValidationException("领航卡不存在。");
            var id = _playerDataStore.PublishDeckToPlaza(
                s.Account!,
                sourceDeckName,
                Str(msg, "title") ?? sourceDeckName,
                leader.Color,
                Str(msg, "publicationId"));
            Send(s.SessionId, new
            {
                proto = "MsgPublishDeckPlaza",
                result = true,
                publicationId = id,
                logStr = string.IsNullOrWhiteSpace(Str(msg, "publicationId")) ? "卡组已发布到广场" : "广场卡组已更新",
            });
        }
        catch (Exception ex) { SendDeckPlazaError(s, "MsgPublishDeckPlaza", ex, "发布卡组失败"); }
    }

    private static void OnLikeDeckPlaza(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            _playerDataStore.ToggleDeckPlazaLike(s.Account!, Str(msg, "publicationId") ?? "");
            Send(s.SessionId, new { proto = "MsgLikeDeckPlaza", result = true });
        }
        catch (Exception ex) { SendDeckPlazaError(s, "MsgLikeDeckPlaza", ex, "更新点赞失败"); }
    }

    private static void OnCopyDeckPlaza(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var result = _playerDataStore.CopyDeckFromPlaza(s.Account!, Str(msg, "publicationId") ?? "");
            SendPlayerData(s, result.Snapshot);
            Send(s.SessionId, new
            {
                proto = "MsgCopyDeckPlaza",
                result = true,
                deckName = result.DeckName,
                logStr = $"已复制到我的卡组：{result.DeckName}",
            });
        }
        catch (Exception ex) { SendDeckPlazaError(s, "MsgCopyDeckPlaza", ex, "复制卡组失败"); }
    }

    private static void OnDeleteDeckPlaza(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            _playerDataStore.DeleteDeckPublication(s.Account!, Str(msg, "publicationId") ?? "");
            Send(s.SessionId, new { proto = "MsgDeleteDeckPlaza", result = true, logStr = "卡组投稿已删除" });
        }
        catch (Exception ex) { SendDeckPlazaError(s, "MsgDeleteDeckPlaza", ex, "删除卡组投稿失败"); }
    }

    // ── 匹配相关 ──────────────────────────────────────────────────────────

    private static bool TryRequireNewGameAccess(WsSession session, string responseProto, bool sendFailure = true)
    {
        if (!session.IsLoggedIn || !IsCurrentAccountSession(session))
        {
            if (sendFailure)
                Send(session.SessionId, new { proto = responseProto, result = false, logStr = "请先重新登录。" });
            return false;
        }

        // 仅兼容未调用 Initialize 的纯匹配算法单元测试。生产 WebSocket 在完成
        // Initialize（其中 QqAccessStore 为必填项）前不会进入可接收状态。
        if (_qqAccessStore is null) return true;

        if (_operationsCenterStore is not null)
        {
            try
            {
                var restrictions = _operationsCenterStore.GetRestrictions(session.Account!);
                if (restrictions.MatchBanned)
                {
                    if (session.IsMatching) RebuildMatchQueue(session);
                    if (sendFailure)
                        Send(session.SessionId, new
                        {
                            proto = responseProto,
                            result = false,
                            logStr = "当前账号处于限时比赛禁赛期，请在处罚到期或申诉处理后重试。",
                            restrictionExpiresAt = restrictions.EarliestExpiry,
                        });
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogErr($"比赛处罚状态读取失败 {session.Account}: {ex.Message}");
                if (sendFailure)
                    Send(session.SessionId, new { proto = responseProto, result = false, logStr = "处罚状态暂时无法确认，请稍后重试。" });
                return false;
            }
        }

        QqLoginAccessResult access;
        try { access = _qqAccessStore.CheckNewGameAccess(session.Account!); }
        catch (Exception ex)
        {
            LogErr($"QQ 新对局准入检查失败 {session.Account}: {ex.Message}");
            if (sendFailure)
                Send(session.SessionId, new { proto = responseProto, result = false, logStr = "QQ 群白名单暂时无法验证，请稍后重试。" });
            return false;
        }
        if (access.Allowed) return true;

        if (session.IsMatching) RebuildMatchQueue(session);
        if (sendFailure)
            Send(session.SessionId, new
            {
                proto = responseProto,
                result = false,
                logStr = QqAccessStore.NewGameDeniedMessage,
            });
        return false;
    }

    private static void OnEnterMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequireNewGameAccess(s, "MsgEnterMatch")) return;
        if (RejectForMaintenance(s, "MsgEnterMatch")) return;
        if (StatusOf(s) != "idle") { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "你正在房间、观战或对局中" }); return; }

        var queueKind = Str(msg, "queueKind") switch
        {
            var value when string.Equals(value, "hex", StringComparison.OrdinalIgnoreCase) => "hex",
            var value when string.Equals(value, "casualStandard", StringComparison.OrdinalIgnoreCase) => "casualStandard",
            var value when string.Equals(value, "rankedWild", StringComparison.OrdinalIgnoreCase) => "rankedWild",
            var value when string.Equals(value, "ranked", StringComparison.OrdinalIgnoreCase) => "ranked",
            _ => "casual",
        };
        var ranked = IsRankedQueue(queueKind);
        var rankedMode = RankedModeForQueue(queueKind);
        var deck = Str(msg, "deck") ?? "";
        var v = DeckValidator.Validate(deck, DeckFormatForQueue(queueKind));
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"匹配拒绝 {s.Account}: {v.Reason}");
            return;
        }

        var rankedProfile = ranked
            ? RankedStore.ForMode(rankedMode).GetMatchmakingProfile(s.Account!, s.PlayerName)
            : null;
        if (ranked && rankedProfile?.Faction is null)
        {
            Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "开始排位前请先选择阵营，阵营选定后不可更改" });
            return;
        }
        var queue = QueueFor(queueKind);
        lock (MatchQueueGate)
        {
            // 从“校验空闲”到“加入队列”必须是账号级原子操作。配对取出后、房间索引建立前，
            // IsMatching 会短暂为 false，账号占位仍保留，防止另一条并发请求进入其它队列。
            if (StatusOf(s) != "idle" || !TryReserveMatchAccount(s))
            {
                Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "你正在匹配、房间或对局中" });
                return;
            }
            s.Deck = deck;
            s.DeckName = Str(msg, "deckName");
            s.MatchQueueKind = queueKind;
            s.MatchEnqueuedAtUtc = DateTime.UtcNow;
            s.MatchRating = rankedProfile?.Rating ?? 1500;
            s.MatchPlacementGames = rankedProfile?.PlacementGames ?? 0;
            s.MatchRankPoints = rankedProfile?.RankPoints ?? 0;
            s.IsMatching = true;
            queue.Enqueue(s);
        }
        Send(s.SessionId, new { proto = "MsgEnterMatch", result = true, queueKind });
        Log($"{QueueLabel(queueKind)}匹配加入 {s.Account}");
        TryMatch(queueKind);
        if (ranked) _ = RetryRankedMatchAsync(s, queueKind);
    }

    /// <summary>单人测试模式：人类(P0,先手) vs 机器人(P1,同卡组)，立即建房</summary>
    private static void OnEnterBotMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequireNewGameAccess(s, "MsgEnterBotMatch")) return;
        if (RejectForMaintenance(s, "MsgEnterBotMatch")) return;
        if (StatusOf(s) != "idle") { Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "你正在匹配、房间、观战或对局中" }); return; }

        var deck = Str(msg, "deck") ?? "";
        var deckName = Str(msg, "deckName");
        // 单人测试先后手（前端可选）：默认人类先手，仅显式 goFirst=false 时人类后手
        bool goFirst = !(msg.TryGetValue("goFirst", out var gfEl) && gfEl.ValueKind == JsonValueKind.False);
        var v = DeckValidator.Validate(deck, DeckFormatForMatchKind(MatchKind.Bot));
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = $"卡组不合法: {v.Reason}" });
            return;
        }

        if (!TryReserveMatchAccount(s))
        {
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "你正在匹配、房间或对局中" });
            return;
        }
        s.IsMatching = false;
        var botSid = "BOT-" + Guid.NewGuid().ToString("N")[..8];
        const string botName = "测试机器人";

        try
        {
            var room = _qqAccessStore.ExecuteNewGameAdmission(
                new[] { s.Account },
                () => GameRoomManager.CreateRoom(
                s.SessionId, s.Account ?? "玩家", deck,
                botSid, botName, deck,        // 机器人用同一套卡组
                p0First: goFirst,             // 单人测试先后手（前端可选，默认先手）
                p0AlwaysPrompt: s.AlwaysPromptOnLifeReveal,
                p1AlwaysPrompt: false,
                p0CardBackId: s.CardBackId,
                p1CardBackId: PlayerDataStore.DefaultCardBackId,
                p0SpriteMap: ResolveDeckSpriteMap(s.Account ?? "", deckName, deck),
                p1SpriteMap: ResolveDeckSpriteMap(s.Account ?? "", deckName, deck),
                vsBot: true,
                matchKind: MatchKind.Bot,
                broadcastInitialState: false,
                p0DisplayName: s.PlayerName,
                p0SpectateMode: s.SpectateMode,
                p0SpectatorHandsPublic: s.SpectatorHandsPublic,
                p0SpectateCode: s.SpectateCode,
                p1SpectateMode: SpectatingRules.Closed));
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = true });
            Send(s.SessionId, new { proto = "MsgMatchFound", opponentName = botName });
            Send(s.SessionId, new { proto = "MsgGameStart", IsFirst = goFirst });
            room.Engine.BroadcastInitialState();
            Log($"单人测试开局 {s.Account} vs 机器人");
        }
        catch (QqAccessDeniedException ex)
        {
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = ex.Message });
        }
        catch (GameMaintenanceException ex)
        {
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = ex.Message });
        }
        catch (Exception ex)
        {
            LogErr($"单人测试建房失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "服务器繁忙，请稍后重试" });
        }
        finally { ReleaseMatchAccountReservation(s); }
    }

    private static void OnCancelMatch(WsSession s, Dictionary<string, JsonElement> _)
    {
        s.IsMatching = false;
        s.Deck       = null;
        s.DeckName   = null;
        RebuildMatchQueue(s);
        Send(s.SessionId, new { proto = "MsgCancelMatch" });
        Log($"匹配取消 {s.Account}");
    }

    private static void TryMatch(string queueKind)
    {
        if (GameRoomManager.GetMaintenanceSnapshot().Enabled)
        {
            CancelMatchingSessions();
            return;
        }
        var ranked = IsRankedQueue(queueKind);
        var queue = QueueFor(queueKind);
        while (TryTakeMatchPairFromQueue(queue, ranked, out var p1, out var p2))
        {
            var deck1 = p1.Deck ?? "";
            var deck2 = p2.Deck ?? "";
            try
            {
                var room = _qqAccessStore.ExecuteNewGameAdmission(
                    new[] { p1.Account, p2.Account },
                    () => GameRoomManager.CreateRoom(
                    p1.SessionId, p1.Account ?? "?", deck1,
                    p2.SessionId, p2.Account ?? "?", deck2,
                    p0AlwaysPrompt: p1.AlwaysPromptOnLifeReveal,
                    p1AlwaysPrompt: p2.AlwaysPromptOnLifeReveal,
                    p0CardBackId: p1.CardBackId,
                    p1CardBackId: p2.CardBackId,
                    p0SpriteMap: ResolveDeckSpriteMap(p1.Account ?? "", p1.DeckName, deck1),
                    p1SpriteMap: ResolveDeckSpriteMap(p2.Account ?? "", p2.DeckName, deck2),
                    matchKind: MatchKindForQueue(queueKind),
                    broadcastInitialState: false,
                    p0DisplayName: p1.PlayerName,
                    p1DisplayName: p2.PlayerName,
                    p0SpectateMode: p1.SpectateMode,
                    p1SpectateMode: p2.SpectateMode,
                    p0SpectatorHandsPublic: p1.SpectatorHandsPublic,
                    p1SpectatorHandsPublic: p2.SpectatorHandsPublic,
                    p0SpectateCode: p1.SpectateCode,
                    p1SpectateCode: p2.SpectateCode));

                GameOpponent[p1.SessionId] = p2.SessionId;
                GameOpponent[p2.SessionId] = p1.SessionId;
                Send(p1.SessionId, new { proto = "MsgMatchFound", opponentName = p2.PlayerName ?? "?", queueKind });
                Send(p2.SessionId, new { proto = "MsgMatchFound", opponentName = p1.PlayerName ?? "?", queueKind });
                Send(p1.SessionId, new { proto = "MsgGameStart" });
                Send(p2.SessionId, new { proto = "MsgGameStart" });
                room.Engine.BroadcastInitialState();
                Log($"{QueueLabel(queueKind)}匹配成功: {p1.Account} vs {p2.Account}，等待骰点选择先后手");
            }
            catch (QqAccessDeniedException ex)
            {
                Send(p1.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = ex.Message });
                Send(p2.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = ex.Message });
            }
            catch (GameMaintenanceException ex)
            {
                Send(p1.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = ex.Message });
                Send(p2.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = ex.Message });
            }
            catch (Exception ex)
            {
                LogErr($"创建房间失败: {ex.Message}");
                Send(p1.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "服务器繁忙，请稍后重试" });
                Send(p2.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "服务器繁忙，请稍后重试" });
            }
            finally
            {
                ReleaseMatchAccountReservation(p1);
                ReleaseMatchAccountReservation(p2);
            }
        }
    }

    private static async Task RetryRankedMatchAsync(WsSession session, string queueKind)
    {
        foreach (var delay in new[] { 15, 15, 30, 30 })
        {
            try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token); }
            catch (OperationCanceledException) { return; }
            if (!session.IsMatching || session.MatchQueueKind != queueKind) return;
            TryMatch(queueKind);
        }
    }

    private static bool IsRankedQueue(string queueKind)
        => queueKind is "ranked" or "rankedWild";

    private static RankedMode RankedModeForQueue(string queueKind)
        => queueKind == "rankedWild" ? RankedMode.Wild : RankedMode.Standard;

    private static string DeckFormatForQueue(string queueKind)
        => DeckFormatForMatchKind(MatchKindForQueue(queueKind));

    private static string DeckFormatForMatchKind(MatchKind matchKind)
        => matchKind switch
        {
            MatchKind.Ranked => DeckValidator.FormatStandardRanked,
            MatchKind.CasualStandard => DeckValidator.FormatStandard,
            MatchKind.Friendly or MatchKind.RoomCode => DeckValidator.FormatUnrestricted,
            _ => DeckValidator.FormatPublicUnrestricted,
        };

    private static ConcurrentQueue<WsSession> QueueFor(string queueKind) => queueKind switch
    {
        "hex" => HexMatchQueue,
        "casualStandard" => StandardCasualMatchQueue,
        "ranked" => RankedMatchQueue,
        "rankedWild" => WildRankedMatchQueue,
        _ => MatchQueue,
    };

    private static MatchKind MatchKindForQueue(string queueKind) => queueKind switch
    {
        "hex" => MatchKind.Hex,
        "casualStandard" => MatchKind.CasualStandard,
        "ranked" => MatchKind.Ranked,
        "rankedWild" => MatchKind.RankedWild,
        _ => MatchKind.CasualWild,
    };

    private static string QueueLabel(string queueKind) => queueKind switch
    {
        "hex" => "海克斯",
        "casualStandard" => "标准休闲",
        "ranked" => "标准排位",
        "rankedWild" => "狂野排位",
        _ => "狂野休闲",
    };

    /// <summary>
    /// 在同一临界区内取出并占用一对不同玩家，避免并发 TryMatch 或重复队列项让同一会话进入两个座位。
    /// 若暂时没有合法对手，只把第一名玩家重新入队一次。
    /// </summary>
    private static bool TryTakeMatchPair(out WsSession p1, out WsSession p2)
        => TryTakeMatchPairFromQueue(MatchQueue, ranked: false, out p1, out p2);

    private static bool TryTakeMatchPairFromQueue(
        ConcurrentQueue<WsSession> queue,
        bool ranked,
        out WsSession p1,
        out WsSession p2)
    {
        lock (MatchQueueGate)
        {
            var waiting = new List<WsSession>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (TryTakeMatchingSession(queue, out var candidate))
            {
                if (seen.Add(candidate.SessionId)) waiting.Add(candidate);
            }

            waiting.Sort((left, right) => left.MatchEnqueuedAtUtc.CompareTo(right.MatchEnqueuedAtUtc));
            for (var firstIndex = 0; firstIndex < waiting.Count; firstIndex++)
            {
                var first = waiting[firstIndex];
                var bestIndex = -1;
                var bestGap = double.MaxValue;
                for (var secondIndex = firstIndex + 1; secondIndex < waiting.Count; secondIndex++)
                {
                    var second = waiting[secondIndex];
                    if (first.SessionId == second.SessionId || ReferenceEquals(first, second))
                        continue;
                    if (string.Equals(first.Account, second.Account, StringComparison.OrdinalIgnoreCase))
                    {
                        second.IsMatching = false;
                        ReleaseMatchAccountReservation(second);
                        continue;
                    }
                    var gap = Math.Abs(first.MatchRating - second.MatchRating);
                    if (ranked && !CanRankedPlayersMatch(first, second, DateTime.UtcNow)) continue;
                    if (!ranked) { bestIndex = secondIndex; break; }
                    if (gap < bestGap) { bestGap = gap; bestIndex = secondIndex; }
                }
                if (bestIndex < 0) continue;

                var secondPlayer = waiting[bestIndex];
                first.IsMatching = false;
                secondPlayer.IsMatching = false;
                for (var i = 0; i < waiting.Count; i++)
                    if (i != firstIndex && i != bestIndex && waiting[i].IsMatching)
                        queue.Enqueue(waiting[i]);
                p1 = first;
                p2 = secondPlayer;
                return true;
            }

            foreach (var session in waiting.Where(item => item.IsMatching && IsCurrentAccountSession(item)))
                queue.Enqueue(session);
        }

        p1 = null!;
        p2 = null!;
        return false;
    }

    internal static bool CanRankedPlayersMatch(WsSession first, WsSession second, DateTime nowUtc)
    {
        var commonWaitSeconds = Math.Min(
            Math.Max(0, (nowUtc - first.MatchEnqueuedAtUtc).TotalSeconds),
            Math.Max(0, (nowUtc - second.MatchEnqueuedAtUtc).TotalSeconds));
        var gap = Math.Abs(first.MatchRating - second.MatchRating);
        if (!double.IsFinite(gap) || gap > AllowedRankGap(commonWaitSeconds)) return false;

        var placementAgainstHighRankedPlayer =
            IsPlacementPlayer(first) && IsMatureHighRankedPlayer(second)
            || IsPlacementPlayer(second) && IsMatureHighRankedPlayer(first);
        return !placementAgainstHighRankedPlayer || commonWaitSeconds >= 90;
    }

    internal static double AllowedRankGap(double commonWaitSeconds)
        => commonWaitSeconds switch { < 15 => 100, < 30 => 175, < 60 => 275, < 90 => 400, _ => 500 };

    private static bool IsPlacementPlayer(WsSession session)
        => session.MatchPlacementGames < RankedStore.PlacementRequired;

    private static bool IsMatureHighRankedPlayer(WsSession session)
        => session.MatchPlacementGames >= RankedStore.PlacementRequired
           && session.MatchRankPoints >= RankedStore.NewWorldRankPoints;

    private static bool TryTakeMatchingSession(out WsSession session)
        => TryTakeMatchingSession(MatchQueue, out session);

    private static bool TryTakeMatchingSession(ConcurrentQueue<WsSession> queue, out WsSession session)
    {
        while (queue.TryDequeue(out var candidate))
        {
            if (!candidate.IsMatching || candidate.Socket.State != WebSocketState.Open)
            {
                if (candidate.Socket.State != WebSocketState.Open)
                    ReleaseMatchAccountReservation(candidate);
                continue;
            }
            if (!IsCurrentAccountSession(candidate))
            {
                candidate.IsMatching = false;
                ReleaseMatchAccountReservation(candidate);
                continue;
            }
            if (!TryRequireNewGameAccess(candidate, "MsgEnterMatch"))
            {
                candidate.IsMatching = false;
                ReleaseMatchAccountReservation(candidate);
                continue;
            }
            session = candidate;
            return true;
        }
        session = null!;
        return false;
    }

    private static bool IsCurrentAccountSession(WsSession session)
    {
        if (session.Account is null || session.IsSuperseded) return false;
        lock (AccountIndexGate)
        {
            return AccountIndex.TryGetValue(session.Account, out var currentSessionId)
                   && currentSessionId == session.SessionId;
        }
    }

    private static WsSession[] SnapshotCurrentAccountSessions()
    {
        lock (AccountIndexGate)
            return SnapshotCurrentAccountSessionsLocked();
    }

    private static WsSession[] SnapshotCurrentAccountSessionsLocked()
    {
        var current = new List<WsSession>(AccountIndex.Count);
        foreach (var binding in AccountIndex)
        {
            if (!Sessions.TryGetValue(binding.Value, out var session) ||
                session.IsSuperseded ||
                !session.IsLoggedIn ||
                !string.Equals(session.Account, binding.Key, StringComparison.OrdinalIgnoreCase))
                continue;
            current.Add(session);
        }
        return current.ToArray();
    }

    private static void RebuildMatchQueue(WsSession exclude)
    {
        // ConcurrentQueue 不支持按项删除；把取消项标记为墓碑，由下一次匹配 O(1) 跳过。
        // 避免每次取消或断线都重建整条队列，形成高峰期 O(N²) 开销。
        exclude.IsMatching = false;
        ReleaseMatchAccountReservation(exclude);
    }

    private static bool TryReserveMatchAccount(WsSession session)
        => session.Account is { Length: > 0 } account
           && MatchAccountReservations.TryAdd(account, session.SessionId);

    private static void ReleaseMatchAccountReservation(WsSession session)
    {
        if (session.Account is not { Length: > 0 } account) return;
        if (MatchAccountReservations.TryGetValue(account, out var ownerSessionId)
            && ownerSessionId == session.SessionId)
            MatchAccountReservations.TryRemove(new KeyValuePair<string, string>(account, ownerSessionId));
    }

    private static void SendRankSnapshot(
        WsSession session,
        RankedMode mode = RankedMode.Standard,
        string? requestId = null)
    {
        if (session.Account is null)
        {
            Send(session.SessionId, new
            {
                proto = "MsgRankSnapshot",
                mode = RankedModeWire.Value(mode),
                requestId,
                result = false,
                error = "请先登录后再加载排位榜",
                retryable = false,
            });
            return;
        }
        try
        {
            var snapshot = RankedStore.ForMode(mode).GetSnapshot(session.Account, session.PlayerName);
            Send(session.SessionId, new
            {
                proto = "MsgRankSnapshot",
                mode = RankedModeWire.Value(mode),
                requestId,
                result = true,
                profile = RankWire.Profile(snapshot.Profile),
                leaderboard = RankWire.Leaderboard(snapshot.Leaderboard),
                factionStandings = RankWire.FactionStandings(snapshot.FactionStandings),
                snapshotVersion = snapshot.SnapshotVersion,
                generatedAtUtc = snapshot.GeneratedAtUtc,
            });
        }
        catch (Exception ex)
        {
            LogErr($"排位资料读取失败 {session.Account}: {ex.Message}");
            Send(session.SessionId, new
            {
                proto = "MsgRankSnapshot",
                mode = RankedModeWire.Value(mode),
                requestId,
                result = false,
                error = "排位榜暂时不可用，请稍后重试",
                retryable = true,
            });
        }
    }

    private static void OnSelectRankFaction(WsSession session, Dictionary<string, JsonElement> msg)
    {
        if (!session.IsLoggedIn || session.Account is null)
        {
            Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "请先登录" });
            return;
        }
        if (StatusOf(session) != "idle")
        {
            Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "请在开始匹配前选择阵营" });
            return;
        }

        var requested = Str(msg, "faction") ?? string.Empty;
        var mode = RankedModeWire.Parse(Str(msg, "mode"));
        try
        {
            var snapshot = RankedStore.ForMode(mode).SelectFaction(session.Account, session.PlayerName, requested,
                resetRankProgress: Bool(msg, "resetRankProgress"));
            if (snapshot is null)
            {
                Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "无效的阵营选择" });
                return;
            }
            if (!string.Equals(snapshot.Profile.Faction, requested, StringComparison.OrdinalIgnoreCase))
            {
                Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "更换阵营会清空本赛季排位数据，请确认后重试" });
                return;
            }
            Send(session.SessionId, new
            {
                proto = "MsgSelectRankFaction",
                result = true,
                mode = RankedModeWire.Value(mode),
                profile = RankWire.Profile(snapshot.Profile),
                leaderboard = RankWire.Leaderboard(snapshot.Leaderboard),
                factionStandings = RankWire.FactionStandings(snapshot.FactionStandings),
                snapshotVersion = snapshot.SnapshotVersion,
                generatedAtUtc = snapshot.GeneratedAtUtc,
            });
        }
        catch (Exception ex)
        {
            LogErr($"排位阵营选择失败 {session.Account}: {ex.Message}");
            Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "阵营选择暂时不可用，请稍后重试" });
        }
    }

    // ── 房间码对战 ────────────────────────────────────────────────────────

    private static readonly char[] RoomCodeChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray(); // 排除容易混淆的 0O1I

    private static string GenerateRoomCode()
    {
        var sb = new StringBuilder(6);
        for (int i = 0; i < 6; i++)
            sb.Append(RoomCodeChars[Random.Shared.Next(RoomCodeChars.Length)]);
        return sb.ToString();
    }

    private static void OnCreateRoom(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequireNewGameAccess(s, "MsgCreateRoom")) return;
        if (RejectForMaintenance(s, "MsgCreateRoom")) return;

        var existingRoom = GetFriendlyRoomOf(s);
        if (existingRoom is not null && existingRoom.IsRoomCode && existingRoom.State == "lobby")
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", roomCode = existingRoom.JoinCode, result = true });
            PushFriendlyRoom(existingRoom);
            return;
        }
        if (StatusOf(s) != "idle")
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = "你正在匹配、房间或对局中" });
            return;
        }

        var deck = Str(msg, "deck") ?? "";
        var deckName = Str(msg, "deckName") ?? "大厅所选卡组";
        var v = DeckValidator.Validate(deck, DeckFormatForMatchKind(MatchKind.RoomCode));
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"创建房间拒绝 {s.Account}: {v.Reason}");
            return;
        }
        var roomId = Guid.NewGuid().ToString("N")[..12];
        var room = new DuelLobby
        {
            RoomId = roomId,
            MatchKind = MatchKind.RoomCode,
            JoinCode = GenerateRoomCode(),
        };
        room.Accounts[0] = s.Account!;
        room.Names[0] = s.PlayerName ?? s.Account!;
        room.Decks[0] = deck;
        room.DeckNames[0] = deckName;

        bool registered;
        try
        {
            registered = _qqAccessStore.ExecuteNewGameAdmission(new[] { s.Account }, () =>
            {
                FriendlyRooms[roomId] = room;
                if (!FriendlyByAccount.TryAdd(s.Account!, roomId))
                {
                    FriendlyRooms.TryRemove(roomId, out _);
                    return false;
                }
                while (!PendingRooms.TryAdd(room.JoinCode!, roomId))
                    room = ReplaceRoomCode(room, GenerateRoomCode());
                return true;
            });
        }
        catch (QqAccessDeniedException ex)
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = ex.Message });
            return;
        }
        if (!registered)
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = "你已经在其他房间中" });
            return;
        }

        Send(s.SessionId, new { proto = "MsgCreateRoom", roomCode = room.JoinCode, result = true });
        PushFriendlyRoom(room);
        ScheduleRoomCodeExpiry(room);
        Log($"创建房间码友谊战 {s.Account} → {room.JoinCode} ({roomId})");
    }

    private static void OnJoinRoom(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequireNewGameAccess(s, "MsgJoinRoom")) return;
        if (RejectForMaintenance(s, "MsgJoinRoom")) return;

        var code = Str(msg, "roomCode")?.ToUpperInvariant() ?? "";
        var deck = Str(msg, "deck") ?? "";
        var deckName = Str(msg, "deckName") ?? "大厅所选卡组";

        if (code.Length != 6 || code.Any(ch => !RoomCodeChars.Contains(ch)))
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "房间码格式不正确" });
            return;
        }

        var v = DeckValidator.Validate(deck, DeckFormatForMatchKind(MatchKind.RoomCode));
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"加入房间拒绝 {s.Account}: {v.Reason}");
            return;
        }
        var existingRoom = GetFriendlyRoomOf(s);
        if (existingRoom is not null)
        {
            if (existingRoom.IsRoomCode && string.Equals(existingRoom.JoinCode, code, StringComparison.OrdinalIgnoreCase))
            {
                Send(s.SessionId, new { proto = "MsgJoinRoom", result = true });
                PushFriendlyRoom(existingRoom);
                return;
            }
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "你已经在其他房间中" });
            return;
        }
        if (StatusOf(s) != "idle")
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "你正在匹配、观战或对局中" });
            return;
        }

        if (!PendingRooms.TryGetValue(code, out var roomId) ||
            !FriendlyRooms.TryGetValue(roomId, out var room))
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "房间不存在或已失效" });
            return;
        }

        string? joinError = null;
        WsSession? host = null;
        try
        {
            _qqAccessStore.ExecuteNewGameAdmission(new[] { s.Account, room.Accounts[0] }, () =>
            {
                lock (room.Gate)
                {
                    var hostAccount = room.Accounts[0];
                    if (hostAccount is null ||
                        !PendingRooms.TryGetValue(code, out var currentRoomId) || currentRoomId != room.RoomId)
                    {
                        joinError = "房间不存在或已失效";
                        host = null;
                    }
                    else if (!TryGetActiveSession(hostAccount, out host))
                    {
                        AbortInactiveSession(hostAccount);
                        joinError = "房主正在重连，请稍后重试";
                    }
                    else if (!room.TryAddGuest(s.Account!, s.PlayerName ?? s.Account!, deck, deckName, out joinError))
                    {
                        // TryAddGuest 已返回具体原因。
                    }
                    else if (!FriendlyByAccount.TryAdd(s.Account!, room.RoomId))
                    {
                        room.Accounts[1] = null;
                        room.Names[1] = null;
                        room.Decks[1] = null;
                        room.DeckNames[1] = null;
                        joinError = "你已经在其他房间中";
                    }
                    else
                    {
                        PendingRooms.TryRemove(code, out _);
                    }
                }
                return true;
            });
        }
        catch (QqAccessDeniedException ex)
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = ex.Message });
            return;
        }

        if (joinError is not null)
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = joinError });
            return;
        }

        Send(s.SessionId, new { proto = "MsgJoinRoom", result = true,
            opponentName = host!.PlayerName ?? host.Account ?? "?" });
        PushFriendlyRoom(room);
        Log($"加入房间码友谊战: {host.Account} & {s.Account} code={code} ({room.RoomId})");
    }

    private static void OnCancelRoom(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is not null && room.IsRoomCode && room.State == "lobby")
            DisbandFriendlyRoom(room, leaverAccount: s.Account);
        Send(s.SessionId, new { proto = "MsgCancelRoom" });
        Log($"取消房间 {s.Account}");
    }

    private static DuelLobby ReplaceRoomCode(DuelLobby source, string roomCode)
    {
        var replacement = new DuelLobby
        {
            RoomId = source.RoomId,
            MatchKind = source.MatchKind,
            JoinCode = roomCode,
            State = source.State,
        };
        for (var i = 0; i < 2; i++)
        {
            replacement.Accounts[i] = source.Accounts[i];
            replacement.Names[i] = source.Names[i];
            replacement.Decks[i] = source.Decks[i];
            replacement.DeckNames[i] = source.DeckNames[i];
            replacement.Ready[i] = source.Ready[i];
            replacement.Scores[i] = source.Scores[i];
        }
        FriendlyRooms[source.RoomId] = replacement;
        return replacement;
    }

    // ── 在线玩家列表 + 邀请对战 ──────────────────────────────────────────────

    /// <summary>玩家当前状态:对战中 / 匹配中 / 空闲</summary>
    private static string StatusOf(WsSession s)
    {
        if (GameOpponent.ContainsKey(s.SessionId)) return "playing";
        if (s.Account is not null && FriendlyByAccount.ContainsKey(s.Account)) return "playing";
        if (s.IsMatching) return "matching";
        var room = GameRoomManager.GetRoomBySession(s.SessionId);
        if (room is not null && Array.IndexOf(room.PlayerSessionIds, s.SessionId) < 0) return "spectating";
        return "idle";
    }

    private static void OnPlayerList(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgPlayerList", players = Array.Empty<object>(), offset = 0, total = 0, hasMore = false });
            return;
        }

        if (!s.TryConsumeRateLimit("player-list", capacity: 2, refillPerSecond: 0.25))
        {
            Send(s.SessionId, new { proto = "MsgRateLimited", scope = "player-list", retryAfterMs = 4_000 });
            return;
        }

        var offset = msg.TryGetValue("offset", out var offsetValue) && offsetValue.TryGetInt32(out var parsedOffset)
            ? Math.Max(0, parsedOffset)
            : 0;
        var limit = msg.TryGetValue("limit", out var limitValue) && limitValue.TryGetInt32(out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, 200)
            : 100;
        var blockedAccounts = s.Account is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : _playerDataStore.GetBlockedRelatedAccountKeys(s.Account);
        var loggedIn = SnapshotCurrentAccountSessions()
            .Where(x => x.Account is null || !blockedAccounts.Contains(x.Account))
            .OrderBy(x => x.Account, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var players = loggedIn
            .Skip(offset)
            .Take(limit)
            .Select(x =>
            {
                var status = StatusOf(x);
                // 仅对战中玩家附带其所在对局房间ID，供前端一键观战；
                // 友谊战房内(lobby)虽也判为 playing，但尚无对局房间，GetRoomBySession 返回 null。
                var gameRoom = status == "playing"
                    ? GameRoomManager.GetRoomBySession(x.SessionId)
                    : null;
                var seatIndex = gameRoom is null
                    ? (int?)null
                    : Array.IndexOf(gameRoom.PlayerSessionIds, x.SessionId);
                return new
                {
                    account = x.Account,
                    name = x.PlayerName ?? x.Account,
                    championLeaderNumbers = LeaderChampionStore.Default.GetChampionLeaderNumbers(x.Account),
                    status,
                    roomId = gameRoom?.RoomId,
                    seatIndex = seatIndex is >= 0 ? seatIndex : null,
                    spectateMode = gameRoom is not null && seatIndex is >= 0
                        ? gameRoom.SpectateModes[seatIndex ?? 0]
                        : null,
                };
            })
            .ToArray();
        Send(s.SessionId, new
        {
            proto = "MsgPlayerList",
            players,
            offset,
            limit,
            total = loggedIn.Length,
            hasMore = offset + players.Length < loggedIn.Length,
        });
    }

    private static void OnFriendList(WsSession s)
    {
        if (!s.IsLoggedIn || s.Account is null)
        {
            Send(s.SessionId, new { proto = "MsgFriendList", result = false, logStr = "请先登录" });
            return;
        }

        try
        {
            SendFriendData(s, _playerDataStore.GetFriendData(s.Account));
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendList", ex, "好友列表暂时不可用");
        }
    }

    private static void OnPlayerSafety(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || s.Account is null || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgPlayerSafety", result = false, logStr = "请先登录" });
            return;
        }
        if (!s.TryConsumeRateLimit("player-safety", capacity: 6, refillPerSecond: 0.5))
        {
            Send(s.SessionId, new { proto = "MsgPlayerSafety", result = false, logStr = "操作过于频繁，请稍后再试" });
            return;
        }

        try
        {
            var action = (Str(msg, "action") ?? "list").Trim().ToLowerInvariant();
            var targetAccount = ResolvePlayerSafetyTarget(s, msg);
            string? logStr = null;
            string? caseId = null;
            switch (action)
            {
                case "list":
                    break;
                case "block":
                    _playerDataStore.BlockPlayer(s.Account, targetAccount);
                    logStr = "已屏蔽该玩家，并清除双方好友与待处理消息";
                    PushFriendDataToAccount(s.Account);
                    PushFriendDataToAccount(targetAccount);
                    break;
                case "unblock":
                    _playerDataStore.UnblockPlayer(s.Account, targetAccount);
                    logStr = "已解除屏蔽";
                    break;
                case "report":
                    if (!s.TryConsumeRateLimit("player-report", capacity: 3, refillPerSecond: 1.0 / 120))
                        throw new PlayerDataValidationException("举报提交过于频繁，请稍后再试。");
                    var description = Str(msg, "description") ?? "";
                    var category = Str(msg, "category") ?? "harassment";
                    var context = BuildPlayerReportContext(s, targetAccount);
                    if (_operationsCenterStore is null)
                        throw new InvalidOperationException("统一 Case 服务尚未初始化。");
                    var room = GameRoomManager.GetRoomBySession(s.SessionId);
                    var roomId = room?.RoomId;
                    var chatEvidence = room is not null
                        ? SnapshotGameChatEvidence(room.RoomId)
                        : TryGetRecentOpponentContext(s.SessionId, out var recent)
                            ? recent.RecentGameChat
                            : [];
                    if (roomId is null && TryGetRecentOpponentContext(s.SessionId, out var recentRoom))
                        roomId = recentRoom.RoomId;
                    var evidence = new List<OperationsCaseEvidenceInput>
                    {
                        new("report_context", context),
                    };
                    if (chatEvidence.Length > 0)
                        evidence.Add(new OperationsCaseEvidenceInput(
                            "game_chat",
                            JsonSerializer.Serialize(chatEvidence)));
                    caseId = _operationsCenterStore.CreateCase(new OperationsCaseCreate(
                        OperationsCaseSources.PlayerReport,
                        category,
                        $"玩家举报：{category}",
                        description,
                        s.Account,
                        targetAccount,
                        targetAccount,
                        roomId,
                        roomId,
                        null,
                        Str(msg, "requestId"),
                        evidence,
                        "high"));
                    try
                    {
                        _playerDataStore.CreatePlayerReport(
                            s.Account,
                            targetAccount,
                            category,
                            description,
                            context);
                    }
                    catch (Exception legacyError)
                    {
                        LogErr($"旧版玩家举报兼容记录失败，统一 Case 已保留 {caseId}: {legacyError.Message}");
                    }
                    logStr = "举报已提交，感谢你协助维护社区环境";
                    break;
                default:
                    throw new PlayerDataValidationException("不支持的安全操作。");
            }

            SendPlayerSafetyState(s, logStr, caseId);
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgPlayerSafety", ex, "安全操作暂时不可用");
        }
    }

    private static string ResolvePlayerSafetyTarget(
        WsSession session,
        IReadOnlyDictionary<string, JsonElement> msg)
    {
        var targetAccount = (Str(msg, "targetAccount") ?? "").Trim();
        if (!Bool(msg, "currentOpponent")) return targetAccount;
        var room = GameRoomManager.GetRoomBySession(session.SessionId);
        if (room is not null)
        {
            var reporterSeat = Array.IndexOf(room.PlayerSessionIds, session.SessionId);
            if (reporterSeat is 0 or 1 && !string.IsNullOrWhiteSpace(room.PlayerAccounts[1 - reporterSeat]))
                return room.PlayerAccounts[1 - reporterSeat];
        }
        if (GameOpponent.TryGetValue(session.SessionId, out var opponentSessionId)
            && Sessions.TryGetValue(opponentSessionId, out var opponent)
            && opponent.IsLoggedIn
            && !string.IsNullOrWhiteSpace(opponent.Account))
            return opponent.Account;

        if (TryGetRecentOpponentContext(session.SessionId, out var recent))
            return recent.OpponentAccount;

        throw new PlayerDataValidationException("当前没有可操作的交战对手。");
    }

    private static string BuildPlayerReportContext(WsSession session, string targetAccount)
    {
        var reportedAtUtc = DateTime.UtcNow;
        var room = GameRoomManager.GetRoomBySession(session.SessionId);
        if (room is not null)
        {
            var state = room.Engine.State;
            var reporterSeat = Array.IndexOf(room.PlayerSessionIds, session.SessionId);
            var reportedSeat = Array.FindIndex(
                room.PlayerAccounts,
                account => string.Equals(account, targetAccount, StringComparison.OrdinalIgnoreCase));
            return JsonSerializer.Serialize(new
            {
                evidenceVersion = 1,
                source = "active_match",
                roomId = room.RoomId,
                matchKind = room.MatchKind.ToString(),
                roomCreatedAtUtc = room.CreatedAt,
                reportedAtUtc,
                reporterSeat,
                reportedSeat,
                turnCount = state.TurnCount,
                phase = state.Phase.ToString(),
                currentTurnPlayer = state.CurrentTurnPlayer,
                gameOverReason = state.GameOverReason,
                operationClockEnabled = state.OperationClockEnabled,
                operationClockRemainingMs = state.OperationClockRemainingMs.ToArray(),
                operationTurnClockRemainingMs = state.OperationTurnClockRemainingMs.ToArray(),
                operationClockActivePlayer = state.OperationClockActivePlayer,
                operationClockPaused = state.OperationClockPaused,
                recentGameChat = SnapshotGameChatEvidence(room.RoomId),
            });
        }

        if (TryGetRecentOpponentContext(session.SessionId, out var recent)
            && string.Equals(recent.OpponentAccount, targetAccount, StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new
            {
                evidenceVersion = 1,
                source = "recent_match",
                roomId = recent.RoomId,
                matchKind = recent.MatchKind,
                reportedAtUtc,
                turnCount = recent.TurnCount,
                gameOverReason = recent.GameOverReason,
                recentGameChat = recent.RecentGameChat,
            });

        return JsonSerializer.Serialize(new
        {
            evidenceVersion = 1,
            source = "player_directory",
            reportedAtUtc,
        });
    }

    private static bool TryGetRecentOpponentContext(string sessionId, out RecentOpponentContext context)
    {
        if (RecentOpponentContexts.TryGetValue(sessionId, out context!)
            && context.ExpiresAtUtc > DateTime.UtcNow)
            return true;
        RecentOpponentContexts.TryRemove(sessionId, out _);
        context = null!;
        return false;
    }

    private static GameChatEvidence[] SnapshotGameChatEvidence(string roomId)
        => GameChatEvidenceByRoom.TryGetValue(roomId, out var queue)
            ? queue.ToArray()
            : [];

    private static void SendPlayerSafetyState(WsSession session, string? logStr = null, string? caseId = null)
    {
        var blockedPlayers = _playerDataStore.GetBlockedPlayers(session.Account!)
            .Select(player => new
            {
                account = player.Account,
                name = player.DisplayName,
                createdAt = player.BlockedAt,
            })
            .ToArray();
        Send(session.SessionId, new
        {
            proto = "MsgPlayerSafety",
            result = true,
            logStr,
            caseId,
            blockedPlayers,
        });
    }

    private static void OnFriendSearch(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || s.Account is null)
        {
            Send(s.SessionId, new { proto = "MsgFriendSearch", result = false, logStr = "请先登录", players = Array.Empty<object>() });
            return;
        }
        if (!s.TryConsumeRateLimit("friend-search", capacity: 4, refillPerSecond: 0.5))
        {
            Send(s.SessionId, new { proto = "MsgFriendSearch", result = false, logStr = "搜索过于频繁，请稍后再试", players = Array.Empty<object>() });
            return;
        }

        try
        {
            var players = _playerDataStore.SearchPlayers(s.Account, Str(msg, "query") ?? "")
                .Select(player =>
                {
                    var presence = PresenceOf(player.Account);
                    return new
                    {
                        account = player.Account,
                        name = player.DisplayName,
                        avatar = player.Avatar,
                        championLeaderNumbers = LeaderChampionStore.Default.GetChampionLeaderNumbers(player.Account),
                        relationship = player.Relationship,
                        online = presence.Online,
                        status = presence.Status,
                    };
                })
                .ToArray();
            Send(s.SessionId, new { proto = "MsgFriendSearch", result = true, players });
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendSearch", ex, "搜索玩家失败");
        }
    }

    private static void OnFriendRequest(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || s.Account is null)
        {
            Send(s.SessionId, new { proto = "MsgFriendRequest", result = false, logStr = "请先登录" });
            return;
        }
        if (!s.TryConsumeRateLimit("friend-request", capacity: 4, refillPerSecond: 0.2))
        {
            Send(s.SessionId, new { proto = "MsgFriendRequest", result = false, logStr = "操作过于频繁，请稍后再试" });
            return;
        }

        try
        {
            var targetAccount = Str(msg, "toAccount") ?? "";
            if (Bool(msg, "currentOpponent"))
            {
                if (!GameOpponent.TryGetValue(s.SessionId, out var opponentSessionId) ||
                    !Sessions.TryGetValue(opponentSessionId, out var opponent) ||
                    !opponent.IsLoggedIn || string.IsNullOrWhiteSpace(opponent.Account))
                    throw new PlayerDataValidationException("当前没有可添加的交战对手");
                targetAccount = opponent.Account;
            }

            var result = _playerDataStore.SendFriendRequest(s.Account, targetAccount);
            var text = result.AutoAccepted ? "对方也申请了你，已自动成为好友" : "好友申请已发送";
            Send(s.SessionId, new { proto = "MsgFriendRequest", result = true, autoAccepted = result.AutoAccepted, logStr = text });
            SendFriendData(s, result.Snapshot);
            PushFriendDataToAccount(
                result.OtherAccount,
                result.AutoAccepted ? $"你和 {s.PlayerName ?? s.Account} 已成为好友" : $"收到来自 {s.PlayerName ?? s.Account} 的好友申请");
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendRequest", ex, "发送好友申请失败");
        }
    }

    private static void OnFriendRespond(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || s.Account is null)
        {
            Send(s.SessionId, new { proto = "MsgFriendRespond", result = false, logStr = "请先登录" });
            return;
        }

        var requestId = msg.TryGetValue("requestId", out var requestValue) && requestValue.TryGetInt64(out var parsedId)
            ? parsedId
            : 0;
        var accept = Bool(msg, "accept");
        try
        {
            var result = _playerDataStore.RespondFriendRequest(s.Account, requestId, accept);
            Send(s.SessionId, new
            {
                proto = "MsgFriendRespond",
                result = true,
                accepted = accept,
                logStr = accept ? "已添加好友" : "已拒绝好友申请",
            });
            SendFriendData(s, result.Snapshot);
            PushFriendDataToAccount(
                result.OtherAccount,
                accept ? $"{s.PlayerName ?? s.Account} 接受了你的好友申请" : $"{s.PlayerName ?? s.Account} 拒绝了你的好友申请");
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendRespond", ex, "处理好友申请失败");
        }
    }

    private static void OnFriendRemove(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || s.Account is null)
        {
            Send(s.SessionId, new { proto = "MsgFriendRemove", result = false, logStr = "请先登录" });
            return;
        }

        try
        {
            var result = _playerDataStore.RemoveFriend(s.Account, Str(msg, "account") ?? "");
            Send(s.SessionId, new { proto = "MsgFriendRemove", result = true, logStr = "好友已删除" });
            SendFriendData(s, result.Snapshot);
            PushFriendDataToAccount(result.OtherAccount);
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendRemove", ex, "删除好友失败");
        }
    }

    private static void OnFriendCancel(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || s.Account is null)
        {
            Send(s.SessionId, new { proto = "MsgFriendCancel", result = false, logStr = "请先登录" });
            return;
        }

        var requestId = msg.TryGetValue("requestId", out var requestValue) && requestValue.TryGetInt64(out var parsedId)
            ? parsedId
            : 0;
        try
        {
            var result = _playerDataStore.CancelFriendRequest(s.Account, requestId);
            Send(s.SessionId, new { proto = "MsgFriendCancel", result = true, logStr = "好友申请已撤回" });
            SendFriendData(s, result.Snapshot);
            PushFriendDataToAccount(result.OtherAccount);
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendCancel", ex, "撤回好友申请失败");
        }
    }

    private static void OnLeaderLeaderboard(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedPeriod = Str(msg, "period") ?? "7d";
        var filterTier = LeaderStatsStore.NormalizeFilterTier(Str(msg, "filterTier"));
        var requestId = Str(msg, "requestId");
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderLeaderboard",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                error = "请先登录",
            });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetLeaderboard(
                requestedPeriod,
                requestedFilterTier: filterTier);
            var championsByLeader = snapshot.Items
                .Select(item => LeaderChampionStore.Default.GetChampion(item.LeaderNumber))
                .Where(champion => champion is not null)
                .Cast<LeaderChampion>()
                .ToDictionary(champion => champion.LeaderNumber, StringComparer.Ordinal);
            var championNames = _playerDataStore.GetDisplayNamesByLeaderStatKeys(
                championsByLeader.Values.Select(champion => champion.PlayerKey));
            Send(s.SessionId, new
            {
                proto = "MsgLeaderLeaderboard",
                result = true,
                period = snapshot.Period,
                filterTier = snapshot.FilterTier,
                requestId,
                generatedAtUtc = snapshot.GeneratedAtUtc,
                sinceUtc = snapshot.SinceUtc,
                totalMatches = snapshot.TotalMatches,
                minimumGames = snapshot.MinimumGames,
                items = snapshot.Items.Select(x =>
                {
                    championsByLeader.TryGetValue(x.LeaderNumber, out var champion);
                    championNames.TryGetValue(champion?.PlayerKey ?? "", out var championName);
                    return new
                    {
                        rank = x.Rank,
                        leaderNumber = x.LeaderNumber,
                        games = x.Games,
                        wins = x.Wins,
                        losses = x.Losses,
                        winRate = x.WinRate,
                        usageRate = x.UsageRate,
                        firstGames = x.FirstGames,
                        firstWinRate = x.FirstWinRate,
                        secondGames = x.SecondGames,
                        secondWinRate = x.SecondWinRate,
                        insufficientSample = x.InsufficientSample,
                        champion = champion is null ? null : new
                        {
                            displayName = championName ?? "神秘玩家",
                        },
                    };
                }),
            });
        }
        catch (Exception ex)
        {
            LogErr($"读取 Leader 排行榜失败: {ex.Message}");
            Send(s.SessionId, new
            {
                proto = "MsgLeaderLeaderboard",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                error = "排行榜暂时不可用",
            });
        }
    }

    private static void OnLeaderChampionQuery(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedLeader = (Str(msg, "leaderNumber") ?? "").Trim().ToUpperInvariant();
        if (!TryRequirePlayer(s)) return;
        if (!s.TryConsumeRateLimit("leader-champion-query", capacity: 10, refillPerSecond: 0.5))
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderChampionQuery",
                result = false,
                leaderNumber = requestedLeader,
                error = "查询过于频繁，请稍后再试。",
            });
            return;
        }

        try
        {
            var leaderNumber = PlayerDataStore.NormalizeChampionLeaderNumber(requestedLeader);
            var champion = LeaderChampionStore.Default.GetChampion(leaderNumber);
            Send(s.SessionId, new
            {
                proto = "MsgLeaderChampionQuery",
                result = true,
                leaderNumber,
                generatedAtUtc = DateTime.UtcNow,
                champion = champion is null ? null : new
                {
                    games = champion.Games,
                    winRate = champion.Games == 0 ? 0 : champion.Wins / (double)champion.Games,
                },
            });
        }
        catch (Exception ex)
        {
            var message = ex is PlayerDataValidationException ? ex.Message : "最强称号查询暂时不可用。";
            if (ex is not PlayerDataValidationException)
                LogErr($"查询 Leader 最强称号失败: {ex.Message}");
            Send(s.SessionId, new
            {
                proto = "MsgLeaderChampionQuery",
                result = false,
                leaderNumber = requestedLeader,
                error = message,
            });
        }
    }

    private static void OnLeaderMatchups(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedPeriod = Str(msg, "period") ?? "7d";
        var filterTier = LeaderStatsStore.NormalizeFilterTier(Str(msg, "filterTier"));
        var requestId = Str(msg, "requestId");
        var requestedLeader = (Str(msg, "leaderNumber") ?? "").Trim();
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchups",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                leaderNumber = requestedLeader,
                error = "请先登录",
            });
            return;
        }

        if (requestedLeader.Length == 0)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchups",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                leaderNumber = requestedLeader,
                error = "请选择有效的 Leader",
            });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetMatchups(
                requestedLeader,
                requestedPeriod,
                requestedFilterTier: filterTier);
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchups",
                result = true,
                period = snapshot.Period,
                filterTier = snapshot.FilterTier,
                requestId,
                generatedAtUtc = snapshot.GeneratedAtUtc,
                sinceUtc = snapshot.SinceUtc,
                leaderNumber = snapshot.LeaderNumber,
                startingHandSampleGames = snapshot.StartingHandSampleGames,
                startingHandItems = snapshot.StartingHandItems.Select(x => new
                {
                    cardNumber = x.CardNumber,
                    games = x.Games,
                    percentage = x.Percentage,
                }),
                items = snapshot.Items.Select(x => new
                {
                    rank = x.Rank,
                    leaderNumber = x.LeaderNumber,
                    games = x.Games,
                    wins = x.Wins,
                    losses = x.Losses,
                    winRate = x.WinRate,
                    firstGames = x.FirstGames,
                    firstWins = x.FirstWins,
                    firstLosses = x.FirstLosses,
                    firstWinRate = x.FirstWinRate,
                    secondGames = x.SecondGames,
                    secondWins = x.SecondWins,
                    secondLosses = x.SecondLosses,
                    secondWinRate = x.SecondWinRate,
                    isMirror = x.IsMirror,
                }),
            });
        }
        catch (Exception ex)
        {
            LogErr($"读取 Leader 对战统计失败: {ex.Message}");
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchups",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                leaderNumber = requestedLeader,
                error = "对战统计暂时不可用",
            });
        }
    }

    private static void OnLeaderMatchupMatrix(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedPeriod = Str(msg, "period") ?? "7d";
        var filterTier = LeaderStatsStore.NormalizeFilterTier(Str(msg, "filterTier"));
        var requestId = Str(msg, "requestId");
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchupMatrix",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                error = "请先登录",
            });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetMatchupMatrix(
                requestedPeriod,
                requestedFilterTier: filterTier);
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchupMatrix",
                result = true,
                period = snapshot.Period,
                filterTier = snapshot.FilterTier,
                requestId,
                generatedAtUtc = snapshot.GeneratedAtUtc,
                sinceUtc = snapshot.SinceUtc,
                rows = snapshot.Rows.Select(row => new
                {
                    leaderNumber = row.LeaderNumber,
                    items = row.Items.Select(x => new
                    {
                        rank = x.Rank,
                        leaderNumber = x.LeaderNumber,
                        games = x.Games,
                        wins = x.Wins,
                        losses = x.Losses,
                        winRate = x.WinRate,
                        firstGames = x.FirstGames,
                        firstWins = x.FirstWins,
                        firstLosses = x.FirstLosses,
                        firstWinRate = x.FirstWinRate,
                        secondGames = x.SecondGames,
                        secondWins = x.SecondWins,
                        secondLosses = x.SecondLosses,
                        secondWinRate = x.SecondWinRate,
                        isMirror = x.IsMirror,
                    }),
                }),
            });
        }
        catch (Exception ex)
        {
            LogErr($"读取 Leader 对阵矩阵失败: {ex.Message}");
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchupMatrix",
                result = false,
                period = requestedPeriod,
                filterTier,
                requestId,
                error = "对阵矩阵暂时不可用",
            });
        }
    }

    private static void OnPlayerProfileStats(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedPeriod = Str(msg, "period") ?? "30d";
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new
            {
                proto = "MsgPlayerProfileStats",
                result = false,
                period = requestedPeriod,
                error = "请先登录",
            });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetPlayerProfile(s.Account!, requestedPeriod);
            Send(s.SessionId, BuildPlayerProfileStatsResponse(snapshot));
        }
        catch (Exception ex)
        {
            LogErr($"读取个人统计失败: {ex.Message}");
            Send(s.SessionId, new
            {
                proto = "MsgPlayerProfileStats",
                result = false,
                period = requestedPeriod,
                error = "个人统计暂时不可用",
            });
        }
    }

    /// <summary>显式映射嵌套字段，确保 WebSocket 响应与前端 camelCase 协议一致。</summary>
    private static object BuildPlayerProfileStatsResponse(PlayerProfileStatsSnapshot snapshot)
        => new
        {
            proto = "MsgPlayerProfileStats",
            result = true,
            period = snapshot.Period,
            generatedAtUtc = snapshot.GeneratedAtUtc,
            sinceUtc = snapshot.SinceUtc,
            games = snapshot.Games,
            wins = snapshot.Wins,
            losses = snapshot.Losses,
            winRate = snapshot.WinRate,
            firstGames = snapshot.FirstGames,
            firstWinRate = snapshot.FirstWinRate,
            secondGames = snapshot.SecondGames,
            secondWinRate = snapshot.SecondWinRate,
            topLeaders = snapshot.TopLeaders.Select(item => new
            {
                leaderNumber = item.LeaderNumber,
                games = item.Games,
                wins = item.Wins,
                losses = item.Losses,
                winRate = item.WinRate,
                usageRate = item.UsageRate,
                firstGames = item.FirstGames,
                firstWinRate = item.FirstWinRate,
                secondGames = item.SecondGames,
                secondWinRate = item.SecondWinRate,
            }),
            trend = snapshot.Trend.Select(point => new
            {
                label = point.Label,
                games = point.Games,
                wins = point.Wins,
                winRate = point.WinRate,
            }),
        };

    private static void OnInvitePlayer(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequireNewGameAccess(s, "MsgInvitePlayer")) return;
        if (RejectForMaintenance(s, "MsgInvitePlayer")) return;
        var toAccount = Str(msg, "toAccount") ?? "";

        if (string.Equals(toAccount, s.Account, StringComparison.OrdinalIgnoreCase))
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "不能邀请自己" });
            return;
        }

        if (!AccountIndex.TryGetValue(toAccount, out var toSid) ||
            !Sessions.TryGetValue(toSid, out var target) || !target.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "对方不在线" });
            return;
        }
        if (!TryRequireNewGameAccess(target, "MsgInviteResult", sendFailure: false))
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "对方当前不具备新对局准入资格" });
            return;
        }
        if (_playerDataStore.GetBlockedRelatedAccountKeys(s.Account!).Contains(toAccount))
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "你与该玩家之间已启用屏蔽" });
            return;
        }

        if (StatusOf(target) != "idle")
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "对方正忙(对战或房间中)" });
            return;
        }
        if (StatusOf(s) != "idle")
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "你正忙,无法发起邀请" });
            return;
        }

        var inviteId = Guid.NewGuid().ToString("N")[..12];
        try
        {
            _qqAccessStore.ExecuteNewGameAdmission(new[] { s.Account, target.Account }, () =>
            {
                PendingInvites[inviteId] = new InviteInfo(
                    inviteId, s.SessionId, s.Account!, s.PlayerName ?? s.Account!, toSid);
                return true;
            });
        }
        catch (QqAccessDeniedException ex)
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = ex.Message });
            return;
        }

        Send(toSid, new { proto = "MsgInviteNotify", inviteId, fromName = s.PlayerName ?? s.Account });
        Send(s.SessionId, new { proto = "MsgInvitePlayer", result = true, toName = target.PlayerName ?? target.Account });
        Log($"邀请 {s.Account} → {toAccount} ({inviteId})");
    }

    private static void OnInviteResponse(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var inviteId = Str(msg, "inviteId") ?? "";
        bool accept  = Bool(msg, "accept");

        if (!PendingInvites.TryRemove(inviteId, out var inv))
        {
            Send(s.SessionId, new { proto = "MsgInviteResult", accepted = false, logStr = "邀请已失效" });
            return;
        }
        if (inv.ToSid != s.SessionId) return; // 不是被邀请者本人

        if (!Sessions.TryGetValue(inv.FromSid, out var from) || !from.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgInviteResult", accepted = false, logStr = "对方已离线" });
            return;
        }

        if (!accept)
        {
            Send(inv.FromSid, new { proto = "MsgInviteResult", accepted = false, byName = s.PlayerName ?? s.Account });
            Log($"邀请被拒 {s.Account} ✗ {from.Account}");
            return;
        }

        if (RejectForMaintenance(s, "MsgInviteResult"))
        {
            Send(inv.FromSid, new { proto = "MsgInviteResult", accepted = false, logStr = GameMaintenanceState.PlayerMessage });
            return;
        }

        if (!TryRequireNewGameAccess(s, "MsgInviteResult")
            || !TryRequireNewGameAccess(from, "MsgInviteResult", sendFailure: false))
        {
            Send(inv.FromSid, new { proto = "MsgInviteResult", accepted = false, logStr = QqAccessStore.NewGameDeniedMessage });
            return;
        }

        // 接受:校验双方仍空闲 → 建立友谊战房间(不直接开战,房间内选卡组+准备)
        if (StatusOf(from) != "idle" || StatusOf(s) != "idle")
        {
            Send(s.SessionId,    new { proto = "MsgInviteResult", accepted = false, logStr = "一方正忙,无法进入房间" });
            Send(inv.FromSid,    new { proto = "MsgInviteResult", accepted = false, logStr = "对方正忙,房间未建立" });
            return;
        }

        bool created;
        try { created = CreateFriendlyRoom(from, s); }
        catch (QqAccessDeniedException ex)
        {
            Send(s.SessionId, new { proto = "MsgInviteResult", accepted = false, logStr = ex.Message });
            Send(inv.FromSid, new { proto = "MsgInviteResult", accepted = false, logStr = ex.Message });
            return;
        }
        if (!created)
        {
            Send(s.SessionId, new { proto = "MsgInviteResult", accepted = false, logStr = "一方已进入其他房间" });
            Send(inv.FromSid, new { proto = "MsgInviteResult", accepted = false, logStr = "一方已进入其他房间" });
            return;
        }
        Log($"友谊战房间建立 {from.Account} & {s.Account}");
    }

    /// <summary>清理与某会话相关的全部待回应邀请,并通知另一方</summary>
    private static void CleanupInvites(string sid)
    {
        foreach (var kv in PendingInvites)
        {
            if (kv.Value.FromSid != sid && kv.Value.ToSid != sid) continue;
            if (PendingInvites.TryRemove(kv.Key, out var inv))
            {
                var other = inv.FromSid == sid ? inv.ToSid : inv.FromSid;
                Send(other, new { proto = "MsgInviteResult", accepted = false, logStr = "对方已离线,邀请取消" });
            }
        }
    }

    /// <summary>共用赛前房间开局：先注册权威对局，再通知双方切换页面并广播首份快照。</summary>
    private static IReadOnlyDictionary<string, string> ResolveDeckSpriteMap(
        string account,
        string? deckName,
        string deckRaw)
    {
        if (string.IsNullOrWhiteSpace(account))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var snapshot = _playerDataStore.GetPlayerData(account);
            var matchingDecks = snapshot.Decks
                .Where(deck => DeckContentsMatch(deck, deckRaw))
                .ToArray();
            var selected = matchingDecks.FirstOrDefault(deck =>
                    !string.IsNullOrWhiteSpace(deckName)
                    && string.Equals(deck.Name, deckName, StringComparison.OrdinalIgnoreCase))
                ?? matchingDecks.FirstOrDefault(deck =>
                    !string.IsNullOrWhiteSpace(snapshot.SelectedDeckName)
                    && string.Equals(deck.Name, snapshot.SelectedDeckName, StringComparison.OrdinalIgnoreCase))
                ?? matchingDecks.FirstOrDefault();

            return selected is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(selected.SpriteMap, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogErr($"读取 {account} 的异画选择失败: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool DeckContentsMatch(StoredDeck deck, string deckRaw)
    {
        var submitted = deckRaw
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (submitted.Length != deck.Cards.Length + 1) return false;
        if (!string.Equals(submitted[0], deck.Leader, StringComparison.OrdinalIgnoreCase)) return false;
        return submitted.Skip(1).SequenceEqual(deck.Cards, StringComparer.OrdinalIgnoreCase);
    }

    private static string? StartDuel(
        WsSession host,
        string hostDeck,
        string? hostDeckName,
        WsSession guest,
        string guestDeck,
        string? guestDeckName,
        string friendlyRoomId,
        MatchKind matchKind)
    {
        try
        {
            var room = _qqAccessStore.ExecuteNewGameAdmission(
                new[] { host.Account, guest.Account },
                () => GameRoomManager.CreateRoom(
                host.SessionId,  host.Account  ?? "?", hostDeck,
                guest.SessionId, guest.Account ?? "?", guestDeck,
                p0AlwaysPrompt: host.AlwaysPromptOnLifeReveal,
                p1AlwaysPrompt: guest.AlwaysPromptOnLifeReveal,
                p0CardBackId: host.CardBackId,
                p1CardBackId: guest.CardBackId,
                p0SpriteMap: ResolveDeckSpriteMap(host.Account ?? "", hostDeckName, hostDeck),
                p1SpriteMap: ResolveDeckSpriteMap(guest.Account ?? "", guestDeckName, guestDeck),
                friendlyRoomId: friendlyRoomId,
                matchKind: matchKind,
                broadcastInitialState: false,
                p0DisplayName: host.PlayerName,
                p1DisplayName: guest.PlayerName,
                p0SpectateMode: host.SpectateMode,
                p1SpectateMode: guest.SpectateMode,
                p0SpectatorHandsPublic: host.SpectatorHandsPublic,
                p1SpectatorHandsPublic: guest.SpectatorHandsPublic,
                p0SpectateCode: host.SpectateCode,
                p1SpectateCode: guest.SpectateCode));
            GameOpponent[host.SessionId]  = guest.SessionId;
            GameOpponent[guest.SessionId] = host.SessionId;
            Send(host.SessionId,  new { proto = "MsgGameStart" });
            Send(guest.SessionId, new { proto = "MsgGameStart" });
            room.Engine.BroadcastInitialState();
            return room.RoomId;
        }
        catch (QqAccessDeniedException ex)
        {
            if (FriendlyRooms.TryGetValue(friendlyRoomId, out var lobby))
                PushFriendlyRoom(lobby, ex.Message);
            return null;
        }
        catch (GameMaintenanceException ex)
        {
            if (FriendlyRooms.TryGetValue(friendlyRoomId, out var lobby))
                PushFriendlyRoom(lobby, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            LogErr($"创建房间失败: {ex.Message}");
            Send(host.SessionId,  new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
            Send(guest.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
            return null;
        }
    }

    // ── 友谊战房间 ──────────────────────────────────────────────────────────

    private static int FriendlyIndexOf(DuelLobby room, string account)
        => Array.FindIndex(room.Accounts, a => string.Equals(a, account, StringComparison.OrdinalIgnoreCase));

    private static DuelLobby? GetFriendlyRoomOf(WsSession s)
        => s.Account is not null && FriendlyByAccount.TryGetValue(s.Account, out var rid)
           && FriendlyRooms.TryGetValue(rid, out var room) ? room : null;

    private static object[] FriendlyPlayers(DuelLobby room)
    {
        var players = new List<object>(2);
        for (int i = 0; i < 2; i++)
        {
            var account = room.Accounts[i];
            if (account is null) continue;
            players.Add(new
            {
                account,
                name = room.Names[i] ?? account,
                deckName = room.DeckNames[i],
                ready = room.Ready[i],
                connected = TryGetActiveSession(account, out _),
            });
        }
        return players.ToArray();
    }

    private static void PushFriendlyRoom(DuelLobby room, string? error = null)
    {
        lock (room.Gate)
        {
            if (room.State == "closed" ||
                !FriendlyRooms.TryGetValue(room.RoomId, out var current) ||
                !ReferenceEquals(current, room))
                return;

            var payload = new
            {
                proto = "MsgFriendlyRoom",
                roomId = room.RoomId,
                origin = room.IsRoomCode ? "roomCode" : "invite",
                roomCode = room.IsRoomCode && !room.IsFull ? room.JoinCode : null,
                players = FriendlyPlayers(room),
                scores = room.Scores.ToArray(),
                state = room.State,
                error,
            };
            foreach (var acc in room.Accounts)
                if (acc is not null && AccountIndex.TryGetValue(acc, out var sid))
                    Send(sid, payload);
        }
    }

    private static bool CreateFriendlyRoom(WsSession host, WsSession guest)
    {
        return _qqAccessStore.ExecuteNewGameAdmission(new[] { host.Account, guest.Account }, () =>
        {
            var roomId = Guid.NewGuid().ToString("N")[..12];
            var room = new DuelLobby
            {
                RoomId = roomId,
                MatchKind = MatchKind.Friendly,
            };
            room.Accounts[0] = host.Account!;
            room.Accounts[1] = guest.Account!;
            room.Names[0] = host.PlayerName ?? host.Account!;
            room.Names[1] = guest.PlayerName ?? guest.Account!;
            FriendlyRooms[roomId] = room;
            if (!FriendlyByAccount.TryAdd(host.Account!, roomId))
            {
                FriendlyRooms.TryRemove(roomId, out _);
                return false;
            }
            if (!FriendlyByAccount.TryAdd(guest.Account!, roomId))
            {
                if (FriendlyByAccount.TryGetValue(host.Account!, out var hostRoomId) && hostRoomId == roomId)
                    FriendlyByAccount.TryRemove(host.Account!, out _);
                FriendlyRooms.TryRemove(roomId, out _);
                return false;
            }
            PushFriendlyRoom(room);
            return true;
        });
    }

    private static void OnFriendlySelectDeck(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null) return;
        if (!TryRequireNewGameAccess(s, "MsgFriendlyRoom", sendFailure: false))
        {
            PushFriendlyRoom(room, QqAccessStore.NewGameDeniedMessage);
            return;
        }

        var deck = Str(msg, "deck") ?? "";
        var v = DeckValidator.Validate(deck, DeckFormatForMatchKind(MatchKind.Friendly));
        if (!v.Ok) { PushFriendlyRoom(room, $"卡组不合法: {v.Reason}"); return; }

        lock (room.Gate)
        {
            if (room.State != "lobby") return;
            int idx = FriendlyIndexOf(room, s.Account);
            if (idx < 0) return;
            room.Decks[idx]     = deck;
            room.DeckNames[idx] = Str(msg, "deckName") ?? "卡组";
            room.Ready[idx]     = false; // 换卡组需重新准备
        }
        PushFriendlyRoom(room);
    }

    private static void OnFriendlyReady(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null) return;
        if (!TryRequireNewGameAccess(s, "MsgFriendlyRoom", sendFailure: false))
        {
            PushFriendlyRoom(room, QqAccessStore.NewGameDeniedMessage);
            return;
        }
        if (GameRoomManager.GetMaintenanceSnapshot().Enabled)
        {
            PushFriendlyRoom(room, GameMaintenanceState.PlayerMessage);
            return;
        }

        bool ready = Bool(msg, "ready");
        lock (room.Gate)
        {
            if (room.State != "lobby") return;
            int idx = FriendlyIndexOf(room, s.Account);
            if (idx < 0) return;
            if (ready && room.Decks[idx] is null) { PushFriendlyRoom(room, "请先选择卡组"); return; }
            room.Ready[idx] = ready;
        }
        PushFriendlyRoom(room);
        TryStartFriendlyGame(room);
    }

    private static void TryStartFriendlyGame(DuelLobby room)
    {
        if (GameRoomManager.GetMaintenanceSnapshot().Enabled)
        {
            PushFriendlyRoom(room, GameMaintenanceState.PlayerMessage);
            return;
        }
        if (!room.TryBeginStart(out var start) || start is null) return;
        if (!TryGetActiveSession(start.HostAccount, out var host) ||
            !TryGetActiveSession(start.GuestAccount, out var guest))
        {
            room.CompleteStart(success: false);
            PushFriendlyRoom(room, "有玩家连接中断，请等待重连后重新准备");
            return;
        }
        if (!TryRequireNewGameAccess(host, "MsgFriendlyRoom", sendFailure: false)
            || !TryRequireNewGameAccess(guest, "MsgFriendlyRoom", sendFailure: false))
        {
            room.CompleteStart(success: false);
            PushFriendlyRoom(room, QqAccessStore.NewGameDeniedMessage);
            return;
        }

        PushFriendlyRoom(room);
        var gameRoomId = StartDuel(
            host, start.HostDeck, start.HostDeckName,
            guest, start.GuestDeck, start.GuestDeckName,
            room.RoomId, room.MatchKind);
        room.CompleteStart(gameRoomId is not null);
        if (gameRoomId is null && GameRoomManager.GetMaintenanceSnapshot().Enabled)
        {
            DisbandFriendlyRoom(room, leaverAccount: null, otherMessage: GameMaintenanceState.PlayerMessage);
            return;
        }
        PushFriendlyRoom(room);
        if (gameRoomId is null)
        {
            foreach (var account in room.Accounts)
                if (account is not null && !TryGetActiveSession(account, out _))
                    HandleFriendlyDisconnect(account);
        }
    }

    private static void OnFriendlyLeave(WsSession s)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null) return;
        if (room.State != "lobby")
        {
            PushFriendlyRoom(room, "对局正在开始或进行中，无法退出赛前房间");
            return;
        }
        Send(s.SessionId, new { proto = "MsgFriendlyLeft", logStr = "已退出房间" });
        DisbandFriendlyRoom(room, leaverAccount: s.Account);
    }

    /// <summary>对局结束回调(GameRoomManager 调用):更新比分,双方退回房间</summary>
    public static void OnFriendlyGameEnd(string friendlyRoomId, string? winnerAccount)
    {
        if (!FriendlyRooms.TryGetValue(friendlyRoomId, out var room)) return;
        if (GameRoomManager.GetMaintenanceSnapshot().Enabled)
        {
            DisbandFriendlyRoom(room, leaverAccount: null, otherMessage: "维护更新中，本场结束后房间已关闭");
            return;
        }
        lock (room.Gate)
        {
            if (winnerAccount is not null)
            {
                int wi = FriendlyIndexOf(room, winnerAccount);
                if (wi >= 0) room.Scores[wi]++;
            }
            room.State    = "lobby";
            room.Ready[0] = false;
            room.Ready[1] = false;
        }
        // 确保对手映射清理,使双方能再次开战
        foreach (var acc in room.Accounts)
            if (acc is not null && AccountIndex.TryGetValue(acc, out var sid))
                GameOpponent.TryRemove(sid, out _);
        if (room.Accounts.Where(static account => account is not null)
            .Any(account => !HasNewGameAccess(account)))
        {
            DisbandFriendlyRoom(
                room,
                leaverAccount: null,
                otherMessage: "QQ 群白名单资格已变化，本场结束后房间已关闭。");
            return;
        }
        PushFriendlyRoom(room);
    }

    /// <summary>赛前房间断线保留 30 秒等待同账号重连；对战中由正式对局的 90 秒宽限期处理。</summary>
    private static void HandleFriendlyDisconnect(string account)
    {
        if (!FriendlyByAccount.TryGetValue(account, out var roomId)) return;
        if (!FriendlyRooms.TryGetValue(roomId, out var room)) return;
        if (room.State is "playing" or "starting") return;

        CancelFriendlyDisconnectGrace(account);
        var cts = new CancellationTokenSource();
        FriendlyDisconnectGrace[account] = cts;
        PushFriendlyRoom(room);
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(LobbyReconnectGrace, cts.Token); }
            catch (TaskCanceledException) { return; }

            if (TryGetActiveSession(account, out _)) return;
            if (!FriendlyByAccount.TryGetValue(account, out var currentRoomId) || currentRoomId != roomId) return;
            if (!FriendlyRooms.TryGetValue(roomId, out var currentRoom) || currentRoom.State != "lobby") return;
            DisbandFriendlyRoom(currentRoom, leaverAccount: account);
        });
    }

    private static void DisbandFriendlyRoom(
        DuelLobby room,
        string? leaverAccount,
        string otherMessage = "对方已离开房间",
        bool onlyIfWaitingForGuest = false)
    {
        if (!room.TryClose(onlyIfWaitingForGuest)) return;
        FriendlyRooms.TryRemove(room.RoomId, out _);
        if (room.JoinCode is not null &&
            PendingRooms.TryGetValue(room.JoinCode, out var pendingRoomId) && pendingRoomId == room.RoomId)
            PendingRooms.TryRemove(room.JoinCode, out _);
        foreach (var acc in room.Accounts)
        {
            if (acc is null) continue;
            CancelFriendlyDisconnectGrace(acc);
            if (FriendlyByAccount.TryGetValue(acc, out var currentRoomId) && currentRoomId == room.RoomId)
                FriendlyByAccount.TryRemove(acc, out _);
        }
        foreach (var acc in room.Accounts)
        {
            if (acc is null) continue;
            if (string.Equals(acc, leaverAccount, StringComparison.OrdinalIgnoreCase)) continue;
            if (AccountIndex.TryGetValue(acc, out var sid))
                Send(sid, new { proto = "MsgFriendlyLeft", logStr = otherMessage });
        }
    }

    private static bool TryGetActiveSession(string account, out WsSession session)
    {
        session = null!;
        if (!AccountIndex.TryGetValue(account, out var sid) ||
            !Sessions.TryGetValue(sid, out var candidate) ||
            candidate.Socket.State != WebSocketState.Open ||
            !candidate.IsRecentlyActive(ActiveSessionMaxIdle))
            return false;
        session = candidate;
        return true;
    }

    private static void AbortInactiveSession(string account)
    {
        if (!AccountIndex.TryGetValue(account, out var sid) || !Sessions.TryGetValue(sid, out var session)) return;
        if (session.Socket.State == WebSocketState.Open && session.IsRecentlyActive(ActiveSessionMaxIdle)) return;
        try { session.Socket.Abort(); } catch { }
    }

    private static void ScheduleRoomCodeExpiry(DuelLobby room)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(10));
            if (!FriendlyRooms.TryGetValue(room.RoomId, out var current) || !ReferenceEquals(current, room)) return;
            if (room.JoinCode is null ||
                !PendingRooms.TryGetValue(room.JoinCode, out var roomId) || roomId != room.RoomId)
                return;
            DisbandFriendlyRoom(
                room,
                leaverAccount: null,
                otherMessage: "房间码已过期，请重新创建",
                onlyIfWaitingForGuest: true);
        });
    }

    private static void TryRestoreFriendlyRoom(WsSession session, string account)
    {
        CancelFriendlyDisconnectGrace(account);
        if (!FriendlyByAccount.TryGetValue(account, out var roomId)) return;
        if (!FriendlyRooms.TryGetValue(roomId, out var room))
        {
            FriendlyByAccount.TryRemove(account, out _);
            return;
        }
        if (room.State == "lobby")
        {
            PushFriendlyRoom(room);
            Log($"赛前房间重连成功 {account} → {roomId}");
        }
        else if (room.State == "starting")
        {
            // 开局注册与新连接登录可能恰好并发；短暂轮询权威对局，避免新连接错过开局后只能刷新。
            _ = Task.Run(async () =>
            {
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    await Task.Delay(100);
                    if (!AccountIndex.TryGetValue(account, out var currentSessionId) ||
                        currentSessionId != session.SessionId)
                        return;
                    if (GameRoomManager.TryReclaim(session.SessionId, account))
                    {
                        Log($"开局期间重连成功 {account} → {roomId}");
                        return;
                    }
                    if (!FriendlyRooms.TryGetValue(roomId, out var currentRoom)) return;
                    if (currentRoom.State == "lobby")
                    {
                        PushFriendlyRoom(currentRoom);
                        return;
                    }
                    if (currentRoom.State == "closed") return;
                }
            });
        }
    }

    private static void CancelFriendlyDisconnectGrace(string account)
    {
        if (FriendlyDisconnectGrace.TryRemove(account, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static void OnTransmit(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var content     = Str(msg, "Msg") ?? "";
        var opponentSid = GetOpponentSid(s.SessionId);
        if (opponentSid is not null)
            Send(opponentSid, new { proto = "MsgTransmit", Msg = content });
    }

    private static void OnSurrender(WsSession s)
    {
        var opp = GetOpponentSid(s.SessionId);
        if (opp is not null)
        {
            Send(opp, new { proto = "MsgDuelOver", IsWin = true, Description = "对手投降" });
            GameOpponent.TryRemove(opp, out _);
        }
        Send(s.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "你已投降" });
        GameOpponent.TryRemove(s.SessionId, out _);
        Log($"{s.Account} 投降");
    }

    /// <summary>MsgEndByDisconnect — 在线方在对手断线宽限期内主动结束对局（判对手负）</summary>
    private static void OnEndByDisconnect(WsSession s)
    {
        GameRoomManager.RequestEndByDisconnect(s.SessionId);
        Log($"{s.Account ?? "?"} 请求结束断线对局");
    }

    // ── Sprint 3: 游戏动作与状态同步 ────────────────────────────────────────

    /// <summary>
    /// MsgGameAction — 客户端游戏动作 → 由权威 GameEngine 结算
    /// </summary>
    private static void OnGameAction(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var receivedAt = LatencyDiagnostics.Start();
        var action = Str(msg, "action") ?? "";
        var requestId = Str(msg, "requestId");
        var data   = msg.TryGetValue("data", out var d) ? d : default;
        GameRoomManager.HandleAction(s.SessionId, action, data, requestId, receivedAt);
        if (ProtocolLogEnabled)
            Log($"GameAction {s.Account ?? "?"} action={action}");
    }

    /// <summary>
    /// MsgBugReport — 客户端只提供非权威白名单诊断；房间状态由服务端串行采集后落盘。
    /// </summary>
    private static void OnBugReport(WsSession s, Dictionary<string, JsonElement> msg)
        => _ = OnBugReportAsync(s, msg);

    private static async Task OnBugReportAsync(WsSession s, Dictionary<string, JsonElement> msg)
    {
        try
        {
            if (!s.TryConsumeRateLimit("bug-report", capacity: 5, refillPerSecond: 1d / 30d))
            {
                Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "提交过于频繁，请稍后再试" });
                return;
            }

            var description = (Str(msg, "description") ?? "").Trim();
            if (description.Length == 0)
            {
                Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "反馈内容不能为空" });
                return;
            }
            if (description.Length > 4000)
            {
                Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "反馈内容不能超过 4000 字" });
                return;
            }

            var categoryRaw = Str(msg, "category");
            var category = categoryRaw switch
            {
                null or "" or "bug" => "bug",
                "suggestion" => "suggestion",
                _ => null,
            };
            if (category is null)
            {
                Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "反馈类型无效" });
                return;
            }

            var reportRequestId = Str(msg, "requestId")?.Trim();
            if (reportRequestId is { Length: > 128 })
            {
                Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "反馈请求标识无效" });
                return;
            }
            var submitterAccount = s.IsLoggedIn ? s.Account : null;
            var requestIdentity = FeedbackRequestIdentityFactory.Create(
                submitterAccount,
                s.SessionId,
                reportRequestId);
            var feedbackId = requestIdentity.FeedbackId;

            string? requestedReplayId = null;
            var requestedReplayValue = Str(msg, "replayId");
            if (!string.IsNullOrWhiteSpace(requestedReplayValue))
            {
                if (!CloudReplayStore.TryNormalizeReplayId(requestedReplayValue, out var normalizedReplayId))
                {
                    Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "回放 ID 无效" });
                    return;
                }
                requestedReplayId = normalizedReplayId;
            }

            var room = GameRoomManager.GetRoomBySession(s.SessionId);
            string? replayId = requestedReplayId;
            if (replayId is null
                && room is not null
                && CloudReplayStore.TryNormalizeReplayId(room.RoomId, out var normalizedRoomReplayId))
                replayId = normalizedRoomReplayId;

            JsonElement? submittedClientEvidence = msg.TryGetValue("clientEvidence", out var evidenceElement)
                ? evidenceElement
                : null;
            var clientEvidence = FeedbackEvidenceSanitizer.Sanitize(
                submittedClientEvidence,
                Str(msg, "clientInfo"));

            var authorityEvidence = await GameRoomManager.CaptureFeedbackEvidenceAsync(s.SessionId);
            var evidence = new
            {
                schema = "grandumi.feedback.evidence.v1",
                authority = authorityEvidence,
                client = clientEvidence,
            };
            var evidenceJson = JsonSerializer.Serialize(evidence);
            if (Encoding.UTF8.GetByteCount(evidenceJson) > 48 * 1024)
                throw new InvalidDataException("反馈证据超过持久化上限");
            var report = new
            {
                schema = "grandumi.feedback.report.v1",
                feedbackId,
                savedAtUtc = DateTime.UtcNow.ToString("O"),
                category,
                description,
                replayId,
                evidence,
            };

            if (_operationsCenterStore is null)
                throw new InvalidOperationException("统一 Case 服务尚未初始化。");
            var caseId = _operationsCenterStore.CreateCase(new OperationsCaseCreate(
                OperationsCaseSources.BugReport,
                category,
                category == "suggestion" ? "玩家优化建议" : "玩家 Bug 反馈",
                description,
                // 玩家反馈 Case 只关联房间/回放；提交者账号不进入持久化证据或 Case 元数据。
                null,
                null,
                null,
                room?.RoomId,
                replayId,
                feedbackId,
                requestIdentity.SourceRequestId,
                [new OperationsCaseEvidenceInput(
                    "bug_report_evidence_v1",
                    evidenceJson)],
                category == "bug" ? "high" : "normal"));
            string? legacyPath = null;
            try { legacyPath = BugReportStore.Save(report, feedbackId, category); }
            catch (Exception legacyError)
            {
                LogErr($"旧版 Bug 反馈文件保存失败，统一 Case 已保留 {caseId}: {legacyError.Message}");
            }
            var replayLinked = false;
            if (submitterAccount is not null && replayId is not null && _cloudReplayStore is not null)
            {
                try { replayLinked = _cloudReplayStore.AssociateFeedback(submitterAccount, replayId, feedbackId); }
                catch (Exception replayError)
                {
                    // Case 与兼容文件已经持久化；回放关联失败不得把成功反馈伪装为整体失败。
                    LogErr($"反馈 {feedbackId} 的云回放关联失败，Case {caseId} 已保留: {replayError.Message}");
                }
            }
            var categoryName = category == "suggestion" ? "优化建议" : "Bug";
            Log($"{categoryName} 反馈已进入统一 Case: {caseId}；兼容文件={legacyPath ?? "未写入"}");
            Send(s.SessionId, new { proto = "MsgBugReport", result = true, feedbackId, caseId, replayId, replayLinked });
        }
        catch (Exception ex)
        {
            LogErr($"BugReport 保存失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = "反馈暂时无法保存，请稍后重试" });
        }
    }

    private static bool TryRequireCloudReplay(WsSession session, string proto, out CloudReplayStore store)
    {
        if (!session.IsLoggedIn || !IsCurrentAccountSession(session))
        {
            Send(session.SessionId, new { proto, result = false, errorCode = "login_required", logStr = "请先登录。" });
            store = null!;
            return false;
        }
        if (_cloudReplayStore is null)
        {
            Send(session.SessionId, new { proto, result = false, errorCode = "unavailable", logStr = "云回放服务暂不可用。" });
            store = null!;
            return false;
        }
        store = _cloudReplayStore;
        return true;
    }

    private static void OnCloudReplayList(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgCloudReplayList";
        if (!TryRequireCloudReplay(s, proto, out var store)) return;
        if (!s.TryConsumeRateLimit("cloud-replay-list", capacity: 6, refillPerSecond: 0.5))
        {
            SendCloudReplayError(s, proto, "rate_limited", "读取过于频繁，请稍后重试。", Str(msg, "requestId"));
            return;
        }
        try
        {
            var opponent = Str(msg, "opponent")?.Trim();
            if (opponent?.Length > 80) throw new CloudReplayException("invalid_request", "对手筛选文字过长。");
            var fromMs = Long(msg, "from", -1);
            var toMs = Long(msg, "to", -1);
            var page = store.List(s.Account!, new CloudReplayListQuery(
                opponent,
                Str(msg, "outcome"),
                Str(msg, "matchKind"),
                Bool(msg, "bookmarkedOnly"),
                fromMs >= 0 ? DateTimeOffset.FromUnixTimeMilliseconds(fromMs).UtcDateTime : null,
                toMs >= 0 ? DateTimeOffset.FromUnixTimeMilliseconds(toMs).UtcDateTime : null,
                Int(msg, "offset"),
                Int(msg, "limit", 30)));
            Send(s.SessionId, new
            {
                proto = "MsgCloudReplayList",
                result = true,
                requestId = Str(msg, "requestId"),
                items = page.Items.Select(item => new
                {
                    replayId = item.ReplayId,
                    startedAt = item.StartedAt,
                    completedAt = item.CompletedAt,
                    myName = item.MyName,
                    opponentName = item.OpponentName,
                    myLeader = item.MyLeader,
                    opponentLeader = item.OpponentLeader,
                    winnerIsMe = item.WinnerIsMe,
                    isDraw = item.IsDraw,
                    gameOverReason = item.GameOverReason,
                    turnCount = item.TurnCount,
                    matchKind = item.MatchKind,
                    bookmarked = item.Bookmarked,
                    shared = item.Shared,
                    sharePolicy = item.SharePolicy,
                    feedbackCount = item.FeedbackCount,
                    sizeBytes = item.SizeBytes,
                    runtimeArtifactId = item.RuntimeArtifactId,
                }).ToArray(),
                total = page.Total,
                usedBytes = page.UsedBytes,
                quotaBytes = page.QuotaBytes,
                retentionDays = page.RetentionDays,
                maximumReplays = page.MaximumReplays,
            });
        }
        catch (Exception ex) { SendCloudReplayException(s, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnCloudReplayLoad(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgCloudReplayLoad";
        if (!TryRequireCloudReplay(s, proto, out var store)) return;
        if (!s.TryConsumeRateLimit("cloud-replay-load", capacity: 3, refillPerSecond: 0.1))
        {
            SendCloudReplayError(s, proto, "rate_limited", "读取回放过于频繁，请稍后重试。", Str(msg, "requestId"));
            return;
        }
        try
        {
            var loaded = store.Load(s.Account!, Str(msg, "replayId") ?? "", Str(msg, "shareToken"));
            Send(s.SessionId, new
            {
                proto = "MsgCloudReplayLoad",
                result = true,
                requestId = Str(msg, "requestId"),
                replayId = loaded.ReplayId,
                sharedAccess = loaded.SharedAccess,
                sharePolicy = loaded.SharePolicy,
                document = loaded.Document,
            });
        }
        catch (Exception ex) { SendCloudReplayException(s, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnCloudReplayBookmark(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgCloudReplayBookmark";
        if (!TryRequireCloudReplay(s, proto, out var store)) return;
        if (!s.TryConsumeRateLimit("cloud-replay-mutation", capacity: 10, refillPerSecond: 0.5))
        {
            SendCloudReplayError(s, proto, "rate_limited", "操作过于频繁，请稍后重试。", Str(msg, "requestId"));
            return;
        }
        try
        {
            var bookmarked = store.SetBookmark(
                s.Account!, Str(msg, "replayId") ?? "", Bool(msg, "bookmarked"), Str(msg, "requestId") ?? "");
            Send(s.SessionId, new { proto = "MsgCloudReplayBookmark", result = true, requestId = Str(msg, "requestId"), replayId = Str(msg, "replayId"), bookmarked });
        }
        catch (Exception ex) { SendCloudReplayException(s, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnCloudReplayShare(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgCloudReplayShare";
        if (!TryRequireCloudReplay(s, proto, out var store)) return;
        if (!s.TryConsumeRateLimit("cloud-replay-mutation", capacity: 10, refillPerSecond: 0.5))
        {
            SendCloudReplayError(s, proto, "rate_limited", "操作过于频繁，请稍后重试。", Str(msg, "requestId"));
            return;
        }
        try
        {
            var result = store.SetShare(
                s.Account!, Str(msg, "replayId") ?? "", Bool(msg, "enabled"), Str(msg, "sharePolicy"), Str(msg, "requestId") ?? "");
            Send(s.SessionId, new
            {
                proto = "MsgCloudReplayShare",
                result = true,
                requestId = Str(msg, "requestId"),
                replayId = result.ReplayId,
                shared = result.Shared,
                sharePolicy = result.SharePolicy,
                shareToken = result.ShareToken,
            });
        }
        catch (Exception ex) { SendCloudReplayException(s, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnCloudReplayDelete(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgCloudReplayDelete";
        if (!TryRequireCloudReplay(s, proto, out var store)) return;
        if (!s.TryConsumeRateLimit("cloud-replay-mutation", capacity: 10, refillPerSecond: 0.5))
        {
            SendCloudReplayError(s, proto, "rate_limited", "操作过于频繁，请稍后重试。", Str(msg, "requestId"));
            return;
        }
        try
        {
            _ = store.Delete(s.Account!, Str(msg, "replayId") ?? "", Str(msg, "requestId") ?? "");
            Send(s.SessionId, new { proto = "MsgCloudReplayDelete", result = true, requestId = Str(msg, "requestId"), replayId = Str(msg, "replayId") });
        }
        catch (Exception ex) { SendCloudReplayException(s, proto, ex, Str(msg, "requestId")); }
    }

    private static void SendCloudReplayException(WsSession session, string proto, Exception exception, string? requestId)
    {
        if (exception is CloudReplayException known)
        {
            SendCloudReplayError(session, proto, known.Code, known.Message, requestId);
            return;
        }
        LogErr($"{proto} 失败: {exception.Message}");
        SendCloudReplayError(session, proto, "storage_error", "云回放服务暂时不可用，请稍后重试。", requestId);
    }

    private static void SendCloudReplayError(WsSession session, string proto, string errorCode, string message, string? requestId)
        => Send(session.SessionId, new { proto, result = false, requestId, errorCode, logStr = message });

    /// <summary>
    /// MsgRequestState — 客户端重连后请求完整游戏状态快照（服务端权威）
    /// </summary>
    private static void OnRequestState(WsSession s)
    {
        GameRoomManager.HandleRequestState(s.SessionId);
        Log($"RequestState {s.Account ?? "?"}");
    }

    /// <summary>MsgSpectateRoom — 加入观战</summary>
    private static void OnSpectateRoom(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "请先登录" });
            return;
        }
        if (!TryRequireCommunicationAccess(s, "MsgSpectateRoom", forSpectating: true)) return;
        if (s.IsMatching)
        {
            Send(s.SessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "请先取消匹配再观战" });
            return;
        }
        if (s.Account is not null && FriendlyByAccount.ContainsKey(s.Account))
        {
            Send(s.SessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "你正在友谊战房间中，无法观战" });
            return;
        }
        if (GameOpponent.ContainsKey(s.SessionId))
        {
            Send(s.SessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "对战中的玩家无法观战" });
            return;
        }
        if (!s.TryConsumeRateLimit("spectate-join", capacity: 5, refillPerSecond: 0.2))
        {
            Send(s.SessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "观战尝试过于频繁，请稍后再试" });
            return;
        }

        var roomId = Str(msg, "roomId") ?? "";
        var viewPlayerIndex = msg.TryGetValue("viewPlayerIndex", out var viewPlayerIndexValue)
            && viewPlayerIndexValue.TryGetInt32(out var parsedViewPlayerIndex)
            && parsedViewPlayerIndex == 1
                ? 1
                : 0;
        var room = GameRoomManager.GetRoom(roomId);
        var targetAccount = room is null ? null : room.PlayerAccounts[viewPlayerIndex];
        var isFriend = false;
        if (s.Account is not null && targetAccount is not null)
        {
            try { isFriend = _playerDataStore.AreFriends(s.Account, targetAccount); }
            catch { isFriend = false; }
        }
        GameRoomManager.AddSpectator(
            roomId,
            s.SessionId,
            s.Account!,
            s.PlayerName ?? s.Account!,
            viewPlayerIndex,
            Str(msg, "spectateCode"),
            isFriend);
    }

    /// <summary>MsgLeaveSpectate — 主动退出观战</summary>
    private static void OnLeaveSpectate(WsSession s)
    {
        PostGameChats.Leave(s.SessionId);
        GameRoomManager.RemoveSpectator(s.SessionId);
    }

    /// <summary>
    /// 向对战双方和观战者推送当前观战列表。
    /// 账号、主视角归属和手牌授权只对对战玩家可见。
    /// </summary>
    public static void BroadcastSpectatorList(GameRoomManager.RoomEntry room)
    {
        var spectators = room.Spectators.Values
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var details = spectators.Select(item => new
            {
                account = item.Account,
                name = item.DisplayName,
                viewingYou = item.ViewPlayerIndex == playerIndex,
                handVisible = item.HandVisible,
            }).ToArray();
            Send(room.PlayerSessionIds[playerIndex], new
            {
                proto = "MsgSpectatorList",
                spectators = details.Select(item => item.name).ToArray(),
                details,
            });
        }

        var publicPayload = new
        {
            proto = "MsgSpectatorList",
            spectators = spectators.Select(item => item.DisplayName).ToArray(),
        };
        foreach (var spectator in spectators)
            Send(spectator.SessionId, publicPayload);
    }

    private static void OnRequestSpectatorHand(WsSession s)
    {
        if (!s.IsLoggedIn || !s.TryConsumeRateLimit("spectator-hand-request", capacity: 2, refillPerSecond: 1d / 30d))
        {
            Send(s.SessionId, new { proto = "MsgSpectatorHandStatus", status = "denied", logStr = "申请过于频繁，请稍后再试", retryAfterMs = 30_000 });
            return;
        }
        GameRoomManager.RequestSpectatorHand(s.SessionId);
    }

    private static void OnRespondSpectatorHand(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        GameRoomManager.RespondSpectatorHand(s.SessionId, Str(msg, "requestId") ?? "", Bool(msg, "accept"));
    }

    private static void OnKickSpectator(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        GameRoomManager.KickSpectator(s.SessionId, Str(msg, "spectatorAccount") ?? "");
    }

    /// <summary>MsgPromptResponse — 玩家响应服务端 prompt</summary>
    private static void OnPromptResponse(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var receivedAt = LatencyDiagnostics.Start();
        var requestId = Str(msg, "requestId");
        var data = JsonSerializer.SerializeToElement(msg);
        GameRoomManager.HandleAction(s.SessionId, "PromptResponse", data, requestId, receivedAt);
    }

    /// <summary>MsgUpdateSettings — 同步玩家设置到服务端（防触发信息泄露等）</summary>
    private static void OnUpdateSettings(WsSession s, Dictionary<string, JsonElement> msg)
    {
        bool alwaysPrompt = Bool(msg, "alwaysPromptOnLifeReveal");
        s.AlwaysPromptOnLifeReveal = alwaysPrompt;
        // 若已在对局中，把设置同步到 PlayerState
        var room = GameRoomManager.GetRoomBySession(s.SessionId);
        if (room is not null)
        {
            int idx = Array.IndexOf(room.PlayerSessionIds, s.SessionId);
            if (idx >= 0) room.Engine.State.Players[idx].AlwaysPromptOnLifeReveal = alwaysPrompt;
        }
    }

    private static void OnUpdateSpectateSettings(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgUpdateSpectateSettings", result = false, logStr = "请先登录" });
            return;
        }
        if (s.IsMatching || GameRoomManager.GetRoomBySession(s.SessionId) is not null
            || (s.Account is not null && FriendlyByAccount.ContainsKey(s.Account)))
        {
            Send(s.SessionId, new { proto = "MsgUpdateSpectateSettings", result = false, logStr = "匹配、房间或对局中不能修改观战设置" });
            return;
        }

        var mode = SpectatingRules.NormalizeMode(Str(msg, "mode"));
        var regenerate = Bool(msg, "regenerateCode");
        if (mode == SpectatingRules.Password && (regenerate || string.IsNullOrWhiteSpace(s.SpectateCode)))
            s.SpectateCode = SpectatingRules.GenerateCode();
        if (mode != SpectatingRules.Password)
            s.SpectateCode = null;

        s.SpectateMode = mode;
        s.SpectatorHandsPublic = Bool(msg, "handsPublic");
        Send(s.SessionId, new
        {
            proto = "MsgUpdateSpectateSettings",
            result = true,
            mode = s.SpectateMode,
            handsPublic = s.SpectatorHandsPublic,
            spectateCode = s.SpectateCode,
        });
    }

    // ── 聊天 ────────────────────────────────────────────────────────────────

    private static bool TryRequireCommunicationAccess(
        WsSession session,
        string responseProto,
        bool forSpectating = false)
    {
        if (_operationsCenterStore is null || string.IsNullOrWhiteSpace(session.Account)) return true;
        try
        {
            var restrictions = _operationsCenterStore.GetRestrictions(session.Account);
            var denied = restrictions.Muted || restrictions.SpectateOrChatBanned;
            if (forSpectating) denied = restrictions.SpectateOrChatBanned;
            if (!denied) return true;
            Send(session.SessionId, new
            {
                proto = responseProto,
                result = false,
                logStr = forSpectating
                    ? "当前账号处于限时观战/聊天限制期，暂时不能观战。"
                    : "当前账号处于限时聊天限制期，暂时不能发送消息。",
                restrictionExpiresAt = restrictions.EarliestExpiry,
            });
            return false;
        }
        catch (Exception ex)
        {
            LogErr($"沟通处罚状态读取失败 {session.Account}: {ex.Message}");
            Send(session.SessionId, new { proto = responseProto, result = false, logStr = "处罚状态暂时无法确认，请稍后重试。" });
            return false;
        }
    }

    private static void OnChatMsg(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) return;
        if (!TryRequireCommunicationAccess(s, "MsgChatMsg")) return;
        if (!s.TryConsumeRateLimit("lobby-chat", capacity: 3, refillPerSecond: 0.5))
        {
            Send(s.SessionId, new { proto = "MsgRateLimited", scope = "lobby-chat", retryAfterMs = 2_000 });
            return;
        }

        int type = msg.TryGetValue("type", out var t) && t.TryGetInt32(out var parsedType) ? parsedType : 0;
        var name = s.PlayerName ?? s.Account ?? "";
        var text = (Str(msg, "Msg") ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 200) text = text[..200];
        var pkt  = new { proto = "MsgChatMsg", type, Name = name, account = s.Account, Msg = text };
        var blockedAccounts = _playerDataStore.GetBlockedRelatedAccountKeys(s.Account!);
        foreach (var recipient in Sessions.Values)
            if (recipient.IsLoggedIn
                && (recipient.Account is null || !blockedAccounts.Contains(recipient.Account)))
                Send(recipient.SessionId, pkt);
    }

    /// <summary>仅允许指定管理员向全部在线会话发送的瞬时滚动公告。</summary>
    private static void OnGlobalAnnouncement(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgGlobalAnnouncement", result = false, logStr = "请先登录" });
            return;
        }
        if (!GlobalAnnouncementPolicy.IsAuthorized(s.Account))
        {
            Send(s.SessionId, new { proto = "MsgGlobalAnnouncement", result = false, logStr = "没有发送全服公告的权限" });
            return;
        }
        if (!s.TryConsumeRateLimit("global-announcement", capacity: 1, refillPerSecond: 0.2))
        {
            Send(s.SessionId, new { proto = "MsgGlobalAnnouncement", result = false, logStr = "公告发送过于频繁，请稍后再试" });
            return;
        }

        var content = GlobalAnnouncementPolicy.Normalize(Str(msg, "content"));
        if (content is null)
        {
            Send(s.SessionId, new { proto = "MsgGlobalAnnouncement", result = false, logStr = "公告内容不能为空" });
            return;
        }
        var auditCorrelation = "";
        try
        {
            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            auditCorrelation = RequirePrivilegedAuditIntent(
                s.Account!, "global_announcement", "all_sessions", Str(msg, "requestId"),
                new { contentLength = content.Length, contentHash });
            BroadcastAll(new
            {
                proto = "MsgGlobalAnnouncement",
                content,
                issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            CompletePrivilegedAudit(
                s.Account!, "global_announcement", "all_sessions", auditCorrelation, "success",
                new { contentLength = content.Length, contentHash });
            Send(s.SessionId, new { proto = "MsgGlobalAnnouncement", result = true, logStr = "全服公告已发送" });
        }
        catch (Exception ex)
        {
            CompletePrivilegedAudit(
                s.Account!, "global_announcement", "all_sessions", auditCorrelation, "failed",
                new { error = ex.Message });
            LogErr($"全服公告发送失败：{ex.Message}");
            Send(s.SessionId, new { proto = "MsgGlobalAnnouncement", result = false, logStr = "全服公告发送失败，未执行变更" });
        }
    }

    private static void OnMaintenanceState(WsSession s)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s)) return;
        SendMaintenanceState(s);
    }

    private static void OnSetMaintenance(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgMaintenanceState", result = false, logStr = "请先登录" });
            return;
        }
        if (!GlobalAnnouncementPolicy.IsAuthorized(s.Account))
        {
            Send(s.SessionId, new { proto = "MsgMaintenanceState", result = false, logStr = "没有管理维护模式的权限" });
            return;
        }

        var enabled = Bool(msg, "enabled");
        var auditCorrelation = "";
        try
        {
            auditCorrelation = RequirePrivilegedAuditIntent(
                s.Account!, "maintenance_set", enabled ? "enabled" : "disabled", Str(msg, "requestId"),
                new { enabled });
            GameRoomManager.SetMaintenanceMode(enabled);
            if (enabled) CancelWaitingGameActivities();
            BroadcastMaintenanceState();
            CompletePrivilegedAudit(
                s.Account!, "maintenance_set", enabled ? "enabled" : "disabled", auditCorrelation, "success",
                new { enabled });
            SendMaintenanceState(s, result: true, logStr: enabled
                ? "维护模式已开启，新的对局已停止"
                : "维护模式已关闭，玩家可以开始新对局");
            Log($"维护模式 {(enabled ? "开启" : "关闭")}：{s.Account}");
        }
        catch (Exception ex)
        {
            CompletePrivilegedAudit(
                s.Account!, "maintenance_set", enabled ? "enabled" : "disabled", auditCorrelation, "failed",
                new { enabled, error = ex.Message });
            LogErr($"维护状态持久化失败: {ex.Message}");
            SendMaintenanceState(s, result: false, logStr: "维护状态保存失败，未执行变更");
        }
    }

    private static bool RejectForMaintenance(WsSession s, string proto)
    {
        if (!GameRoomManager.GetMaintenanceSnapshot().Enabled) return false;
        Send(s.SessionId, new { proto, result = false, accepted = false, logStr = GameMaintenanceState.PlayerMessage });
        return true;
    }

    private static void CancelWaitingGameActivities()
    {
        CancelMatchingSessions();

        foreach (var item in PendingInvites.ToArray())
        {
            if (!PendingInvites.TryRemove(item.Key, out var invite)) continue;
            Send(invite.FromSid, new { proto = "MsgInviteResult", accepted = false, logStr = GameMaintenanceState.PlayerMessage });
            Send(invite.ToSid, new { proto = "MsgInviteResult", accepted = false, logStr = GameMaintenanceState.PlayerMessage });
        }

        foreach (var room in FriendlyRooms.Values.ToArray())
        {
            if (room.State == "lobby")
                DisbandFriendlyRoom(room, leaverAccount: null, otherMessage: GameMaintenanceState.PlayerMessage);
        }
    }

    /// <summary>
    /// 白名单整表替换提交后清理所有尚未注册为正式对局的等待路径。
    /// 已经出现在 GameRoomManager 中的对局保持运行；结束回到赛前房间时会再次复核并关闭。
    /// </summary>
    private static void EvictIneligibleWaitingActivities()
    {
        foreach (var session in Sessions.Values)
        {
            if (!session.IsMatching || HasNewGameAccess(session.Account)) continue;
            RebuildMatchQueue(session);
            Send(session.SessionId, new
            {
                proto = "MsgEnterMatch",
                result = false,
                logStr = QqAccessStore.NewGameDeniedMessage,
            });
        }

        foreach (var entry in PendingInvites.ToArray())
        {
            var invite = entry.Value;
            Sessions.TryGetValue(invite.ToSid, out var target);
            if (HasNewGameAccess(invite.FromAccount) && HasNewGameAccess(target?.Account)) continue;
            if (!PendingInvites.TryRemove(entry.Key, out _)) continue;
            Send(invite.FromSid, new
            {
                proto = "MsgInviteResult",
                accepted = false,
                logStr = QqAccessStore.NewGameDeniedMessage,
            });
            Send(invite.ToSid, new
            {
                proto = "MsgInviteResult",
                accepted = false,
                logStr = QqAccessStore.NewGameDeniedMessage,
            });
        }

        foreach (var room in FriendlyRooms.Values.Distinct().ToArray())
        {
            string state;
            string?[] accounts;
            lock (room.Gate)
            {
                state = room.State;
                accounts = room.Accounts.ToArray();
            }
            if (state is "closed" or "playing" || HasRegisteredGame(accounts)) continue;
            if (accounts.Where(static account => account is not null).All(HasNewGameAccess)) continue;
            DisbandFriendlyRoom(
                room,
                leaverAccount: null,
                otherMessage: "QQ 群白名单资格已变化，赛前房间已关闭。");
        }
    }

    private static bool HasNewGameAccess(string? account)
    {
        if (string.IsNullOrWhiteSpace(account)) return false;
        try { return _qqAccessStore.CheckNewGameAccess(account).Allowed; }
        catch (Exception ex)
        {
            LogErr($"QQ 新对局资格读取失败 {account}: {ex.Message}");
            return false;
        }
    }

    private static bool HasRegisteredGame(IEnumerable<string?> accounts)
    {
        foreach (var account in accounts)
        {
            if (account is null || !AccountIndex.TryGetValue(account, out var sessionId)) continue;
            var gameRoom = GameRoomManager.GetRoomBySession(sessionId);
            if (gameRoom is not null && Array.IndexOf(gameRoom.PlayerSessionIds, sessionId) >= 0)
                return true;
        }
        return false;
    }

    private static void CancelMatchingSessions()
    {
        foreach (var session in Sessions.Values)
        {
            if (!session.IsMatching) continue;
            session.IsMatching = false;
            ReleaseMatchAccountReservation(session);
            session.Deck = null;
            session.DeckName = null;
            Send(session.SessionId, new
            {
                proto = "MsgEnterMatch",
                result = false,
                logStr = GameMaintenanceState.PlayerMessage,
            });
        }
    }

    private static void SendMaintenanceState(WsSession session, bool? result = null, string? logStr = null)
    {
        var snapshot = GameRoomManager.GetMaintenanceSnapshot();
        Send(session.SessionId, new
        {
            proto = "MsgMaintenanceState",
            enabled = snapshot.Enabled,
            activeRoomCount = snapshot.ActiveRoomCount,
            startedAt = snapshot.StartedAt?.ToUnixTimeMilliseconds(),
            canManage = GlobalAnnouncementPolicy.IsAuthorized(session.Account),
            result,
            logStr,
        });
    }

    public static void BroadcastMaintenanceState()
    {
        foreach (var session in Sessions.Values)
            if (session.IsLoggedIn) SendMaintenanceState(session);
    }

    private static void OnRulesetState(WsSession s)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = "请先登录" });
            return;
        }
        if (!GlobalAnnouncementPolicy.IsAuthorized(s.Account))
        {
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = "没有管理卡效规则的权限" });
            return;
        }

        try
        {
            CardRulesetManager.RefreshPackages();
            SendRulesetState(s);
        }
        catch (Exception ex)
        {
            LogErr($"刷新卡效规则包失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = $"刷新规则包失败：{ex.Message}" });
        }
    }

    private static void OnActivateRuleset(WsSession s, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = "请先登录" });
            return;
        }
        if (!GlobalAnnouncementPolicy.IsAuthorized(s.Account))
        {
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = "没有管理卡效规则的权限" });
            return;
        }
        if (!s.TryConsumeRateLimit("ruleset-activation", capacity: 2, refillPerSecond: 0.1))
        {
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = "规则切换过于频繁，请稍后再试" });
            return;
        }

        var rulesetId = Str(msg, "rulesetId")?.Trim();
        if (string.IsNullOrWhiteSpace(rulesetId))
        {
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = "缺少规则版本 ID" });
            return;
        }

        var auditCorrelation = "";
        try
        {
            auditCorrelation = RequirePrivilegedAuditIntent(
                s.Account!, "ruleset_activate", rulesetId, Str(msg, "requestId"),
                new { rulesetId });
            CardRulesetManager.RefreshPackages();
            var activated = CardRulesetManager.Activate(rulesetId);
            var oldRoomCount = GameRoomManager.RoomCountsByRuleset.TryGetValue(activated.PreviousRulesetId, out var count)
                ? count
                : 0;
            SendRulesetState(
                s,
                result: true,
                logStr: $"已激活 {activated.CurrentRulesetId}；{oldRoomCount} 场进行中的旧版对局不受影响，新对局立即使用新版");
            BroadcastRulesetStateExcept(s.SessionId);
            CompletePrivilegedAudit(
                s.Account!, "ruleset_activate", activated.CurrentRulesetId, auditCorrelation, "success",
                new { activated.PreviousRulesetId, activated.CurrentRulesetId, oldRoomCount });
            Log($"规则集 {activated.PreviousRulesetId} -> {activated.CurrentRulesetId}：{s.Account}");
        }
        catch (Exception ex)
        {
            CompletePrivilegedAudit(
                s.Account!, "ruleset_activate", rulesetId, auditCorrelation, "failed",
                new { rulesetId, error = ex.Message });
            LogErr($"激活卡效规则失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgRulesetState", result = false, logStr = $"激活规则失败：{ex.Message}" });
        }
    }

    private static void SendRulesetState(WsSession session, bool? result = null, string? logStr = null)
    {
        Send(session.SessionId, new
        {
            proto = "MsgRulesetState",
            activeRulesetId = CardRulesetManager.Current.Id,
            availableRulesets = CardRulesetManager.Snapshot(),
            activeRoomCounts = GameRoomManager.RoomCountsByRuleset,
            result,
            logStr,
        });
    }

    private static void BroadcastRulesetStateExcept(string excludedSessionId)
    {
        foreach (var session in Sessions.Values)
            if (session.SessionId != excludedSessionId
                && session.IsLoggedIn
                && GlobalAnnouncementPolicy.IsAuthorized(session.Account))
                SendRulesetState(session);
    }

    private static void OnQqWhitelistStatus(WsSession session)
    {
        if (!TryResolveQqAdministrator(session, out _, out _))
        {
            Send(session.SessionId, new
            {
                proto = "MsgQqWhitelistStatus",
                result = false,
                logStr = "没有查看 QQ 白名单状态的权限。",
            });
            return;
        }
        SendQqWhitelistStatus(session);
    }

    private static void OnQqWhitelistImport(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!TryResolveQqAdministrator(session, out var adminAccount, out var bootstrapOnly))
        {
            Send(session.SessionId, new
            {
                proto = "MsgQqWhitelistImport",
                result = false,
                logStr = "没有导入 QQ 白名单的权限。",
            });
            return;
        }
        if (!session.TryConsumeRateLimit("admin-qq-whitelist-import", capacity: 3, refillPerSecond: 1d / 60d))
        {
            Send(session.SessionId, new
            {
                proto = "MsgQqWhitelistImport",
                result = false,
                logStr = "白名单导入请求过于频繁，请稍后再试。",
            });
            return;
        }
        if (!msg.TryGetValue("json", out var jsonElement) || jsonElement.ValueKind != JsonValueKind.String)
        {
            Send(session.SessionId, new
            {
                proto = "MsgQqWhitelistImport",
                result = false,
                logStr = "请选择有效的 .json 文件，QQ 必须按字符串语义处理。",
            });
            return;
        }

        var rawJson = jsonElement.GetString() ?? "";
        var auditCorrelation = "";
        try
        {
            var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson))).ToLowerInvariant();
            auditCorrelation = RequirePrivilegedAuditIntent(
                adminAccount, "qq_whitelist_import", "shared_qq_whitelist", Str(msg, "requestId"),
                new { bootstrapOnly, payloadBytes = Encoding.UTF8.GetByteCount(rawJson), payloadHash });
            var result = _qqAccessStore.Import(
                adminAccount,
                rawJson,
                initializationOnly: bootstrapOnly);
            EvictIneligibleWaitingActivities();
            CompletePrivilegedAudit(
                adminAccount, "qq_whitelist_import", "shared_qq_whitelist", auditCorrelation, "success",
                new { result.Version, result.MemberCount, result.DuplicateCount, result.AddedCount, result.RemovedCount, result.RemovedBoundCount, bootstrapOnly });
            Send(session.SessionId, new
            {
                proto = "MsgQqWhitelistImport",
                result = true,
                version = result.Version,
                importedAt = result.ImportedAt,
                memberCount = result.MemberCount,
                duplicateCount = result.DuplicateCount,
                addedCount = result.AddedCount,
                removedCount = result.RemovedCount,
                removedBoundCount = result.RemovedBoundCount,
                requiresQqBinding = bootstrapOnly,
                logStr = bootstrapOnly
                    ? "首份 QQ 白名单已导入。请立即绑定名单内 QQ，完成后才能正常登录。"
                    : "QQ 白名单已原子替换。",
            });
            foreach (var current in Sessions.Values)
                if (TryResolveQqAdministrator(current, out _, out _))
                    SendQqWhitelistStatus(current);
            Log($"QQ 白名单导入完成：操作者={adminAccount}，版本={result.Version}，人数={result.MemberCount}，新增={result.AddedCount}，移除={result.RemovedCount}");
        }
        catch (Exception ex) when (ex is QqAccessValidationException or SqliteException or InvalidOperationException or OperationsCenterException)
        {
            var message = ex is QqAccessValidationException or OperationsCenterException
                ? ex.Message
                : "QQ 白名单导入失败，请稍后重试。";
            CompletePrivilegedAudit(
                adminAccount, "qq_whitelist_import", "shared_qq_whitelist", auditCorrelation, "failed",
                new { bootstrapOnly, error = ex.Message });
            try
            {
                _qqAccessStore.RecordManualWhitelistFailure(adminAccount, message);
            }
            catch (Exception eventException)
            {
                LogErr($"QQ 白名单失败事件落库失败：操作者={adminAccount}，原因={eventException.Message}");
            }
            Send(session.SessionId, new { proto = "MsgQqWhitelistImport", result = false, logStr = message });
            foreach (var current in Sessions.Values)
                if (TryResolveQqAdministrator(current, out _, out _))
                    SendQqWhitelistStatus(current);
            LogErr($"QQ 白名单导入失败：操作者={adminAccount}，原因={ex.Message}");
        }
    }

    private static bool TryResolveQqAdministrator(
        WsSession session,
        out string adminAccount,
        out bool bootstrapOnly)
    {
        adminAccount = "";
        bootstrapOnly = false;
        if (session.QqBootstrapAccount is { Length: > 0 } bootstrapAccount
            && AdministratorPolicy.IsAuthorized(bootstrapAccount)
            && _qqAccessStore.IsBootstrapAdministrator(bootstrapAccount))
        {
            adminAccount = bootstrapAccount;
            bootstrapOnly = true;
            return true;
        }
        if (session.Account is { Length: > 0 } account
            && IsCurrentAccountSession(session)
            && AdministratorPolicy.IsAuthorized(account))
        {
            adminAccount = account;
            return true;
        }
        return false;
    }

    private static void SendQqWhitelistStatus(WsSession session)
    {
        if (!TryResolveQqAdministrator(session, out var account, out var bootstrapOnly)) return;
        var status = _qqAccessStore.GetStatus();
        var recentUpdates = _qqAccessStore.GetRecentWhitelistUpdateEvents()
            .Select(update => new
            {
                id = update.Id,
                eventKey = update.EventKey,
                outcome = update.Outcome,
                source = update.Source,
                operationKey = update.OperationKey,
                occurredAt = update.OccurredAt,
                scheduledHour = update.ScheduledHour,
                version = update.Version,
                memberCount = update.MemberCount,
                addedCount = update.AddedCount,
                removedCount = update.RemovedCount,
                removedBoundCount = update.RemovedBoundCount,
                error = update.Error,
            })
            .ToArray();
        var binding = _qqAccessStore.GetAccountBindingStatus(account);
        Send(session.SessionId, new
        {
            proto = "MsgQqWhitelistStatus",
            result = true,
            initialized = status.Initialized,
            version = status.Version,
            memberCount = status.MemberCount,
            importedAt = status.ImportedAt,
            importedBy = status.ImportedBy,
            duplicateCount = status.DuplicateCount,
            addedCount = status.AddedCount,
            removedCount = status.RemovedCount,
            removedBoundCount = status.RemovedBoundCount,
            maxImportBytes = QqAccessStore.MaxImportBytes,
            maxImportMembers = QqAccessStore.MaxImportMembers,
            bootstrapOnly,
            canImport = !bootstrapOnly || !status.Initialized,
            recentUpdates,
            accountBinding = new
            {
                bound = binding.Bound,
                maskedQq = binding.MaskedQq,
                currentlyWhitelisted = binding.CurrentlyWhitelisted,
                boundAt = binding.BoundAt,
            },
        });
    }

    private static void OnOperationsCases(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgOperationsCases";
        if (!TryRequireOperationsStore(session, proto, out var store)) return;
        var canManage = AdministratorPolicy.IsAuthorized(session.Account);
        try
        {
            var query = canManage
                ? new OperationsCaseQuery(
                    Str(msg, "status"),
                    Str(msg, "source"),
                    Str(msg, "assignee"),
                    Str(msg, "account"),
                    Int(msg, "offset"),
                    Int(msg, "limit", 50))
                : new OperationsCaseQuery(Account: session.Account, Offset: Int(msg, "offset"), Limit: Int(msg, "limit", 50));
            var page = store.ListCases(query);
            var metrics = canManage ? store.GetCaseMetrics() : null;
            Send(session.SessionId, new
            {
                proto = "MsgOperationsCases",
                result = true,
                requestId = Str(msg, "requestId"),
                canManage,
                items = page.Items.Select(ToOperationsCaseSummaryPayload).ToArray(),
                total = page.Total,
                metrics = metrics is null ? null : new
                {
                    total = metrics.Total,
                    awaitingFirstAction = metrics.AwaitingFirstAction,
                    firstActionP90Ms = metrics.FirstActionP90Milliseconds,
                    byStatus = metrics.ByStatus,
                },
            });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnOperationsCaseDetail(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgOperationsCaseDetail";
        if (!TryRequireOperationsStore(session, proto, out var store)) return;
        try
        {
            var detail = store.GetCase(Str(msg, "caseId") ?? "");
            var canManage = AdministratorPolicy.IsAuthorized(session.Account);
            var involved = string.Equals(detail.Summary.ReporterAccount, session.Account, StringComparison.OrdinalIgnoreCase)
                || string.Equals(detail.Summary.SubjectAccount, session.Account, StringComparison.OrdinalIgnoreCase)
                || string.Equals(detail.Summary.RelatedAccount, session.Account, StringComparison.OrdinalIgnoreCase);
            if (!canManage && !involved)
                throw new OperationsCenterException("forbidden", "没有查看该 Case 的权限。");
            Send(session.SessionId, new
            {
                proto = "MsgOperationsCaseDetail",
                result = true,
                requestId = Str(msg, "requestId"),
                canManage,
                detail = ToOperationsCaseDetailPayload(detail),
            });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnOperationsCaseUpdate(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgOperationsCaseUpdate";
        if (!TryRequireOperationsAdmin(session, proto, out var store)) return;
        var requestId = Str(msg, "requestId") ?? "";
        try
        {
            var detail = store.TransitionCase(
                session.Account!,
                "web_admin",
                Str(msg, "caseId") ?? "",
                Str(msg, "toStatus") ?? "",
                Str(msg, "assignee"),
                Str(msg, "disposition"),
                Str(msg, "note") ?? "",
                requestId);
            Send(session.SessionId, new { proto = "MsgOperationsCaseUpdate", result = true, requestId, detail = ToOperationsCaseDetailPayload(detail) });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, requestId); }
    }

    private static void OnOperationsCaseAppeal(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgOperationsCaseAppeal";
        if (!TryRequireOperationsStore(session, proto, out var store)) return;
        var requestId = Str(msg, "requestId") ?? "";
        try
        {
            var detail = store.SubmitAppeal(
                session.Account!,
                Str(msg, "caseId") ?? "",
                Str(msg, "appealText") ?? "",
                requestId);
            Send(session.SessionId, new { proto = "MsgOperationsCaseAppeal", result = true, requestId, detail = ToOperationsCaseDetailPayload(detail) });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, requestId); }
    }

    private static void OnOperationsPenalty(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgOperationsPenalty";
        if (!TryRequireOperationsAdmin(session, proto, out var store)) return;
        var requestId = Str(msg, "requestId") ?? "";
        try
        {
            OperationsPenalty penalty;
            var action = Str(msg, "action") ?? "apply";
            if (string.Equals(action, "revoke", StringComparison.Ordinal))
            {
                penalty = store.RevokePenalty(
                    session.Account!, "web_admin", Str(msg, "penaltyId") ?? "",
                    Str(msg, "reason") ?? "", requestId);
            }
            else
            {
                var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(Long(msg, "expiresAt", -1)).UtcDateTime;
                penalty = store.ApplyPenalty(
                    session.Account!, "web_admin", Str(msg, "caseId") ?? "",
                    Str(msg, "account") ?? "", Str(msg, "kind") ?? "", expiresAt,
                    Str(msg, "reason") ?? "", requestId);
            }
            Send(session.SessionId, new { proto = "MsgOperationsPenalty", result = true, requestId, penalty = ToOperationsPenaltyPayload(penalty) });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, requestId); }
    }

    private static void OnPrivilegedAudit(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgPrivilegedAudit";
        if (!TryRequireOperationsAdmin(session, proto, out var store)) return;
        try
        {
            var entries = store.ListPrivilegedAudit(Int(msg, "offset"), Int(msg, "limit", 100));
            Send(session.SessionId, new
            {
                proto = "MsgPrivilegedAudit",
                result = true,
                requestId = Str(msg, "requestId"),
                chainValid = store.VerifyAuditChain(),
                entries = entries.Select(item => new
                {
                    id = item.Id,
                    actorAccount = item.ActorAccount,
                    source = item.Source,
                    operation = item.Operation,
                    target = item.Target,
                    requestId = item.RequestId,
                    result = item.Result,
                    detailJson = item.DetailJson,
                    createdAt = item.CreatedAt,
                    previousHash = item.PreviousHash,
                    eventHash = item.EventHash,
                }).ToArray(),
            });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, Str(msg, "requestId")); }
    }

    private static void OnConsistencyDoctor(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgConsistencyDoctor";
        if (!TryRequireOperationsAdmin(session, proto, out var store)) return;
        if (_consistencyDoctor is null)
        {
            Send(session.SessionId, new { proto, result = false, errorCode = "unavailable", logStr = "一致性 Doctor 尚未初始化。" });
            return;
        }
        var requestId = Str(msg, "requestId") ?? "";
        try
        {
            var action = Str(msg, "action") ?? "snapshot";
            if (string.Equals(action, "repair", StringComparison.Ordinal))
            {
                var findingId = Long(msg, "findingId", -1);
                store.ConsumeHighRiskChallenge(
                    session.Account!, "web_admin", "database_repair", findingId.ToString(),
                    Str(msg, "challengeId") ?? "", Str(msg, "confirmationToken") ?? "");
                _consistencyDoctor.QueueDisplayNameRepair(findingId, session.Account!, "web_admin", requestId);
            }
            var snapshot = _consistencyDoctor.RunOnce();
            Send(session.SessionId, new
            {
                proto = "MsgConsistencyDoctor",
                result = true,
                requestId,
                snapshot = new
                {
                    checkedAt = snapshot.CheckedAt,
                    processed = snapshot.Processed,
                    succeeded = snapshot.Succeeded,
                    retried = snapshot.Retried,
                    openFindings = snapshot.OpenFindings,
                    outboxCounts = snapshot.OutboxCounts,
                    schemas = snapshot.Schemas.Select(schema => new
                    {
                        name = schema.Name,
                        path = schema.Path,
                        exists = schema.Exists,
                        healthy = schema.Healthy,
                        integrity = schema.Integrity,
                        userVersion = schema.UserVersion,
                        migrationTables = schema.MigrationTables,
                        sizeBytes = schema.SizeBytes,
                        lastWriteAt = schema.LastWriteAt,
                    }).ToArray(),
                },
                findings = store.ListConsistencyFindings().Select(item => new
                {
                    id = item.Id,
                    scope = item.Scope,
                    findingKey = item.FindingKey,
                    status = item.Status,
                    severity = item.Severity,
                    authoritativeJson = item.AuthoritativeJson,
                    observedJson = item.ObservedJson,
                    repairAction = item.RepairAction,
                    lastError = item.LastError,
                    firstSeenAt = item.FirstSeenAt,
                    lastSeenAt = item.LastSeenAt,
                    resolvedAt = item.ResolvedAt,
                }).ToArray(),
            });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, requestId); }
    }

    private static void OnAdminApproval(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        const string proto = "MsgAdminApproval";
        if (!TryRequireOperationsAdmin(session, proto, out var store)) return;
        var requestId = Str(msg, "requestId") ?? "";
        try
        {
            var challenge = store.IssueHighRiskChallenge(
                session.Account!, "web_admin", Str(msg, "operation") ?? "",
                Str(msg, "target") ?? "", requestId);
            Send(session.SessionId, new
            {
                proto = "MsgAdminApproval",
                result = true,
                requestId,
                challengeId = challenge.ChallengeId,
                confirmationToken = challenge.ConfirmationToken,
                operation = challenge.Operation,
                target = challenge.Target,
                expiresAt = challenge.ExpiresAt,
            });
        }
        catch (Exception ex) { SendOperationsError(session, proto, ex, requestId); }
    }

    private static bool TryRequireOperationsStore(
        WsSession session,
        string proto,
        out OperationsCenterStore store)
    {
        if (!session.IsLoggedIn || !IsCurrentAccountSession(session))
        {
            Send(session.SessionId, new { proto, result = false, errorCode = "login_required", logStr = "请先登录。" });
            store = null!;
            return false;
        }
        if (_operationsCenterStore is null)
        {
            Send(session.SessionId, new { proto, result = false, errorCode = "unavailable", logStr = "运营中心暂不可用。" });
            store = null!;
            return false;
        }
        store = _operationsCenterStore;
        return true;
    }

    private static bool TryRequireOperationsAdmin(
        WsSession session,
        string proto,
        out OperationsCenterStore store)
    {
        if (!TryRequireOperationsStore(session, proto, out store)) return false;
        if (AdministratorPolicy.IsAuthorized(session.Account)) return true;
        Send(session.SessionId, new { proto, result = false, errorCode = "forbidden", logStr = "没有运营管理权限。" });
        store = null!;
        return false;
    }

    /// <summary>
    /// 在任何旧式管理员写操作发生前先写入不可变审计意图。审计库不可用或写入失败时
    /// 必须拒绝执行，避免出现“操作已经生效但完全没有审计痕迹”的窗口。
    /// </summary>
    private static string RequirePrivilegedAuditIntent(
        string actorAccount,
        string operation,
        string? target,
        string? clientRequestId,
        object detail)
    {
        var store = _operationsCenterStore
            ?? throw new OperationsCenterException("audit_unavailable", "特权审计不可用，已拒绝管理员操作。");
        var correlationId = string.IsNullOrWhiteSpace(clientRequestId)
            ? $"admin-{Guid.NewGuid():N}"
            : clientRequestId.Trim();
        store.AppendPrivilegedAudit(
            actorAccount,
            "web_admin",
            $"{operation}_intent",
            target,
            $"audit-{Guid.NewGuid():N}",
            "requested",
            JsonSerializer.Serialize(new { correlationId, detail }));
        return correlationId;
    }

    /// <summary>
    /// 结果审计使用独立事件 ID，与意图事件形成相关联的两条追加记录。结果落库失败时
    /// 保留已写入的意图事件并记录错误，避免诱导客户端重试已成功的非幂等操作。
    /// </summary>
    private static void CompletePrivilegedAudit(
        string actorAccount,
        string operation,
        string? target,
        string correlationId,
        string result,
        object detail)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) return;
        try
        {
            _operationsCenterStore?.AppendPrivilegedAudit(
                actorAccount,
                "web_admin",
                operation,
                target,
                $"audit-{Guid.NewGuid():N}",
                result,
                JsonSerializer.Serialize(new { correlationId, detail }));
        }
        catch (Exception auditError)
        {
            LogErr($"特权操作结果审计写入失败：operation={operation}，target={target}，correlation={correlationId}，原因={auditError.Message}");
        }
    }

    private static void SendOperationsError(WsSession session, string proto, Exception error, string? requestId)
    {
        var errorCode = error is OperationsCenterException operationsError
            ? operationsError.Code
            : error is ArgumentOutOfRangeException ? "invalid_request" : "operation_failed";
        var message = error is OperationsCenterException or PlayerDataValidationException or ArgumentOutOfRangeException
            ? error.Message
            : "运营操作失败，请稍后重试。";
        LogErr($"运营协议 {proto} 失败 {session.Account}: {error.Message}");
        Send(session.SessionId, new { proto, result = false, requestId, errorCode, logStr = message });
    }

    private static object ToOperationsCaseSummaryPayload(OperationsCaseSummary item) => new
    {
        caseId = item.CaseId,
        source = item.Source,
        category = item.Category,
        title = item.Title,
        status = item.Status,
        priority = item.Priority,
        reporterAccount = item.ReporterAccount,
        subjectAccount = item.SubjectAccount,
        relatedAccount = item.RelatedAccount,
        roomId = item.RoomId,
        replayId = item.ReplayId,
        assignee = item.Assignee,
        disposition = item.Disposition,
        createdAt = item.CreatedAt,
        firstActionAt = item.FirstActionAt,
        updatedAt = item.UpdatedAt,
        evidenceCount = item.EvidenceCount,
        activePenaltyCount = item.ActivePenaltyCount,
    };

    private static object ToOperationsPenaltyPayload(OperationsPenalty item) => new
    {
        penaltyId = item.PenaltyId,
        caseId = item.CaseId,
        account = item.Account,
        kind = item.Kind,
        reason = item.Reason,
        operatorAccount = item.OperatorAccount,
        source = item.Source,
        startsAt = item.StartsAt,
        expiresAt = item.ExpiresAt,
        revokedAt = item.RevokedAt,
        revokedBy = item.RevokedBy,
        revokeReason = item.RevokeReason,
    };

    private static object ToOperationsCaseDetailPayload(OperationsCaseDetail item) => new
    {
        summary = ToOperationsCaseSummaryPayload(item.Summary),
        description = item.Description,
        externalEventId = item.ExternalEventId,
        appealText = item.AppealText,
        evidence = item.Evidence.Select(evidence => new
        {
            id = evidence.Id,
            type = evidence.Type,
            payloadJson = evidence.PayloadJson,
            createdAt = evidence.CreatedAt,
            expiresAt = evidence.ExpiresAt,
        }).ToArray(),
        events = item.Events.Select(entry => new
        {
            id = entry.Id,
            eventType = entry.EventType,
            fromStatus = entry.FromStatus,
            toStatus = entry.ToStatus,
            actorAccount = entry.ActorAccount,
            source = entry.Source,
            requestId = entry.RequestId,
            note = entry.Note,
            createdAt = entry.CreatedAt,
        }).ToArray(),
        penalties = item.Penalties.Select(ToOperationsPenaltyPayload).ToArray(),
    };

    private static void OnAdminOperations(WsSession session)
    {
        if (!session.IsLoggedIn || !IsCurrentAccountSession(session))
        {
            Send(session.SessionId, new { proto = "MsgAdminOperations", result = false, logStr = "请先登录" });
            return;
        }
        if (!GlobalAnnouncementPolicy.IsAuthorized(session.Account))
        {
            Send(session.SessionId, new { proto = "MsgAdminOperations", result = false, logStr = "没有查看管理员运维状态的权限" });
            return;
        }
        SendAdminOperations(session);
    }

    private static void OnAdminDeploy(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!session.IsLoggedIn || !IsCurrentAccountSession(session))
        {
            Send(session.SessionId, new { proto = "MsgAdminOperations", result = false, logStr = "请先登录" });
            return;
        }
        if (!GlobalAnnouncementPolicy.IsAuthorized(session.Account))
        {
            Send(session.SessionId, new { proto = "MsgAdminOperations", result = false, logStr = "没有执行版本发布的权限" });
            return;
        }
        if (!session.TryConsumeRateLimit("admin-deploy", capacity: 1, refillPerSecond: 1d / 30d))
        {
            Send(session.SessionId, new { proto = "MsgAdminOperations", result = false, logStr = "发布请求过于频繁，请稍后再试" });
            return;
        }
        if (_adminDeploymentCoordinator is null)
        {
            Send(session.SessionId, new { proto = "MsgAdminOperations", result = false, logStr = "当前服务器尚未配置网页发布执行器" });
            return;
        }

        var environment = Str(msg, "environment")?.Trim().ToLowerInvariant() ?? "";
        var requestId = Str(msg, "requestId") ?? "";
        var highRiskOperation = environment == "production" ? "deploy_production" : "deploy_test";
        try
        {
            if (_operationsCenterStore is null)
                throw new InvalidOperationException("运营中心尚未初始化，拒绝执行发布。");
            _operationsCenterStore.ConsumeHighRiskChallenge(
                session.Account!,
                "web_admin",
                highRiskOperation,
                environment,
                Str(msg, "challengeId") ?? "",
                Str(msg, "confirmationToken") ?? "");
            if (environment == "production")
            {
                var maintenance = GameRoomManager.GetMaintenanceSnapshot();
                if (maintenance.ActiveRoomCount > 0)
                    throw new InvalidOperationException($"正式发布前仍有 {maintenance.ActiveRoomCount} 个进行中房间；请先启动维护并等待对局结束。");
                if (!maintenance.Enabled)
                {
                    GameRoomManager.SetMaintenanceMode(true);
                    CancelWaitingGameActivities();
                    BroadcastMaintenanceState();
                }
            }
            _adminDeploymentCoordinator.Queue(environment);
            _operationsCenterStore.AppendPrivilegedAudit(
                session.Account!, "web_admin", "deploy_queue", environment,
                requestId, "success", JsonSerializer.Serialize(new { environment }));
            SendAdminOperations(session, result: true, logStr: environment == "production"
                ? "正式服发布任务已排队；执行器仍会检查测试服版本、更新日志和进行中房间。"
                : "测试服更新任务已排队，将部署远端 main 的最新提交。");
            Log($"管理员发布任务已排队：{environment}，操作者={session.Account}");
        }
        catch (Exception ex)
        {
            try
            {
                _operationsCenterStore?.AppendPrivilegedAudit(
                    session.Account!, "web_admin", "deploy_queue", environment,
                    string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("D") : requestId,
                    "failed", JsonSerializer.Serialize(new { environment, error = ex.Message }));
            }
            catch (Exception auditError) { LogErr($"发布失败审计写入失败: {auditError.Message}"); }
            LogErr($"管理员发布任务排队失败：{ex.Message}");
            SendAdminOperations(session, result: false, logStr: ex.Message);
        }
    }

    private static void OnAdminPlayerSearch(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(session)) return;
        if (!AdministratorPolicy.IsAuthorized(session.Account))
        {
            Send(session.SessionId, new { proto = "MsgAdminPlayerSearch", result = false, logStr = "没有管理玩家账号的权限" });
            return;
        }
        if (!session.TryConsumeRateLimit("admin-player-search", capacity: 10, refillPerSecond: 0.5))
        {
            Send(session.SessionId, new { proto = "MsgAdminPlayerSearch", result = false, logStr = "搜索过于频繁，请稍后再试" });
            return;
        }

        var searchBy = Str(msg, "searchBy") ?? "player";
        var query = Str(msg, "query") ?? "";
        var auditCorrelation = "";
        try
        {
            var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
            auditCorrelation = RequirePrivilegedAuditIntent(
                session.Account!, "admin_player_search", searchBy, Str(msg, "requestId"),
                new { searchBy, queryLength = query.Length, queryHash });
            var players = _qqAccessStore.SearchAccountsForAdmin(query, searchBy)
                .Select(ToAdminPlayerPayload)
                .ToArray();
            CompletePrivilegedAudit(
                session.Account!, "admin_player_search", searchBy, auditCorrelation, "success",
                new { searchBy, resultCount = players.Length });
            Send(session.SessionId, new
            {
                proto = "MsgAdminPlayerSearch",
                result = true,
                searchBy = string.Equals(searchBy, "qq", StringComparison.OrdinalIgnoreCase) ? "qq" : "player",
                players,
            });
        }
        catch (Exception ex)
        {
            CompletePrivilegedAudit(
                session.Account!, "admin_player_search", searchBy, auditCorrelation, "failed",
                new { searchBy, error = ex.Message });
            var message = ex is PlayerDataValidationException or QqAccessValidationException or OperationsCenterException
                ? ex.Message
                : "搜索玩家失败，请稍后再试。";
            LogErr($"管理员搜索玩家失败 {session.Account}: {ex.Message}");
            Send(session.SessionId, new { proto = "MsgAdminPlayerSearch", result = false, logStr = message });
        }
    }

    private static void OnAdminPlayerUpdate(WsSession session, IReadOnlyDictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(session)) return;
        if (!AdministratorPolicy.IsAuthorized(session.Account))
        {
            Send(session.SessionId, new { proto = "MsgAdminPlayerUpdate", result = false, logStr = "没有管理玩家账号的权限" });
            return;
        }
        if (!session.TryConsumeRateLimit("admin-player-update", capacity: 4, refillPerSecond: 1d / 30d))
        {
            Send(session.SessionId, new { proto = "MsgAdminPlayerUpdate", result = false, logStr = "账号操作过于频繁，请稍后再试" });
            return;
        }

        var action = Str(msg, "action")?.Trim();
        var targetAccount = Str(msg, "targetAccount")?.Trim() ?? "";
        var requestId = Str(msg, "requestId") ?? Guid.NewGuid().ToString("D");
        var auditOperation = $"admin_player_{action ?? "unknown"}";
        var auditCorrelation = "";
        try
        {
            auditCorrelation = RequirePrivilegedAuditIntent(
                session.Account!, auditOperation, targetAccount, requestId,
                new { action, targetAccount });
            string? temporaryPassword = null;
            var replayed = false;
            if (string.Equals(action, "rename", StringComparison.Ordinal))
            {
                var snapshot = _playerDataStore.AdminRenamePlayer(
                    session.Account!,
                    targetAccount,
                    Str(msg, "displayName") ?? "");
                if (TryGetActiveSession(snapshot.Account, out var targetSession))
                {
                    targetSession.PlayerName = snapshot.DisplayName;
                    SendPlayerData(targetSession, snapshot, "管理员已更新你的昵称");
                    PushFriendPresenceToFriends(snapshot.Account);
                }
                targetAccount = snapshot.Account;
                Log($"管理员玩家操作 rename：{session.Account} -> {targetAccount}");
            }
            else if (string.Equals(action, "resetPassword", StringComparison.Ordinal))
            {
                if (_operationsCenterStore is null)
                    throw new InvalidOperationException("运营中心尚未初始化，拒绝重置密码。");
                _operationsCenterStore.ConsumeHighRiskChallenge(
                    session.Account!, "web_admin", "reset_password", targetAccount,
                    Str(msg, "challengeId") ?? "", Str(msg, "confirmationToken") ?? "");
                var reset = _accountAuthenticationStore.AdminResetPassword(session.Account!, targetAccount);
                targetAccount = reset.Account;
                temporaryPassword = reset.TemporaryPassword;
                if (TryGetActiveSession(reset.Account, out var targetSession))
                    SupersedeSession(targetSession, "管理员已重置此账号密码，请使用临时密码重新登录。");
                Log($"管理员玩家操作 reset_password：{session.Account} -> {targetAccount}");
            }
            else if (string.Equals(action, "setQq", StringComparison.Ordinal)
                     || string.Equals(action, "unbindQq", StringComparison.Ordinal))
            {
                var mutation = _qqAccessStore.AdminUpdateBinding(
                    session.Account!,
                    targetAccount,
                    action == "setQq" ? "set" : "unbind",
                    Str(msg, "qq"),
                    Long(msg, "expectedBindingRevision", -1),
                    requestId);
                targetAccount = mutation.Account;
                replayed = mutation.Replayed;
                if (!mutation.Replayed) ApplyQqBindingSecurityMutation(mutation.Account);
                Log($"管理员玩家操作 {(action == "setQq" ? "set_qq_binding" : "unbind_qq")}：{session.Account} -> {targetAccount}，版本={mutation.Revision}，QQ={mutation.QqMasked ?? "未绑定"}");
            }
            else
            {
                throw new PlayerDataValidationException("不支持的玩家管理操作。");
            }

            CompletePrivilegedAudit(
                session.Account!, auditOperation, targetAccount, auditCorrelation,
                replayed ? "replayed" : "success", new { action, targetAccount });

            var player = _qqAccessStore.SearchAccountsForAdmin(targetAccount, "player")
                .FirstOrDefault(item => string.Equals(item.Account, targetAccount, StringComparison.OrdinalIgnoreCase));
            Send(session.SessionId, new
            {
                proto = "MsgAdminPlayerUpdate",
                result = true,
                action,
                player = player is null ? null : ToAdminPlayerPayload(player),
                temporaryPassword,
                replayed,
                logStr = action switch
                {
                    "rename" => "当前环境的玩法昵称已更新，跨环境检索同步已进入可靠队列",
                    "resetPassword" => "密码已重置；临时密码只在本次结果中显示",
                    "setQq" => replayed ? "该改绑请求已经执行，已返回当前结果" : "玩家 QQ 绑定已原子更新",
                    "unbindQq" => replayed ? "该解绑请求已经执行，已返回当前结果" : "玩家 QQ 已解绑，旧登录令牌已失效",
                    _ => "账号操作已完成",
                },
            });
        }
        catch (Exception ex)
        {
            CompletePrivilegedAudit(
                session.Account!, auditOperation, targetAccount, auditCorrelation, "failed",
                new { action, targetAccount, error = ex.Message });
            var message = ex is PlayerDataValidationException or QqAccessValidationException or OperationsCenterException
                ? ex.Message
                : "玩家账号操作失败，请稍后再试。";
            LogErr($"管理员玩家操作失败 {session.Account} -> {targetAccount}: {ex.Message}");
            Send(session.SessionId, new { proto = "MsgAdminPlayerUpdate", result = false, action, logStr = message });
        }
    }

    private static void SendAdminOperations(WsSession session, bool? result = null, string? logStr = null)
    {
        IReadOnlyList<OnlinePlayerPeakPoint> peaks7 = [];
        IReadOnlyList<OnlinePlayerPeakPoint> peaks30 = [];
        IReadOnlyList<DailyActivePlayerPoint> dailyActive7 = [];
        IReadOnlyList<DailyActivePlayerPoint> dailyActive30 = [];
        int? authoritativeOnlineCount = null;
        long? playerTrafficUpdatedAt = null;
        IReadOnlyList<DailyMatchCountPoint> matches7 = [];
        IReadOnlyList<DailyMatchCountPoint> matches30 = [];
        long? matchesUpdatedAt = null;
        CachedStorageHealth? storage = null;
        try
        {
            if (_adminOperationsMetricsCache is not null)
            {
                var cachedTraffic = _adminOperationsMetricsCache.GetPlayerTraffic();
                authoritativeOnlineCount = cachedTraffic.Snapshot.CurrentOnlineCount;
                peaks30 = cachedTraffic.Snapshot.Peaks;
                peaks7 = peaks30.TakeLast(7).ToArray();
                dailyActive30 = cachedTraffic.Snapshot.DailyActivePlayers;
                dailyActive7 = dailyActive30.TakeLast(7).ToArray();
                playerTrafficUpdatedAt = cachedTraffic.RefreshedAt.ToUnixTimeMilliseconds();
            }
            else if (_onlinePlayerHistoryReadStore is not null)
            {
                var traffic = _onlinePlayerHistoryReadStore.GetSnapshot(30);
                authoritativeOnlineCount = traffic.CurrentOnlineCount;
                peaks30 = traffic.Peaks;
                peaks7 = peaks30.TakeLast(7).ToArray();
                dailyActive30 = traffic.DailyActivePlayers;
                dailyActive7 = dailyActive30.TakeLast(7).ToArray();
            }
        }
        catch (Exception ex)
        {
            LogErr($"读取在线峰值失败：{ex.Message}");
        }

        try
        {
            if (_adminOperationsMetricsCache is not null)
            {
                var cachedMatches = _adminOperationsMetricsCache.GetDailyMatchCounts();
                matches30 = cachedMatches.Points;
                matches7 = matches30.TakeLast(7).ToArray();
                matchesUpdatedAt = cachedMatches.RefreshedAt.ToUnixTimeMilliseconds();
            }
        }
        catch (Exception ex)
        {
            LogErr($"读取每日场次统计失败：{ex.Message}");
        }

        try
        {
            storage = _adminOperationsMetricsCache?.GetStorageHealth();
        }
        catch (Exception ex)
        {
            LogErr($"读取服务端磁盘状态失败：{ex.Message}");
        }

        AdminDeploymentStatus? test = null;
        AdminDeploymentStatus? production = null;
        try
        {
            test = _adminDeploymentCoordinator?.GetStatus("test");
            production = _adminDeploymentCoordinator?.GetStatus("production");
        }
        catch (Exception ex)
        {
            LogErr($"读取管理员发布状态失败：{ex.Message}");
        }

        Send(session.SessionId, new
        {
            proto = "MsgAdminOperations",
            result,
            logStr,
            currentCommit = BuildInfo.Commit,
            deploymentAvailable = _adminDeploymentCoordinator is not null,
            onlineCount = authoritativeOnlineCount,
            peaks7 = peaks7.Select(point => new { date = point.Date, peak = point.Peak }).ToArray(),
            peaks30 = peaks30.Select(point => new { date = point.Date, peak = point.Peak }).ToArray(),
            dailyActive7 = dailyActive7.Select(point => new { date = point.Date, count = point.Count }).ToArray(),
            dailyActive30 = dailyActive30.Select(point => new { date = point.Date, count = point.Count }).ToArray(),
            playerTrafficUpdatedAt,
            matches7 = matches7.Select(point => new { date = point.Date, count = point.Count }).ToArray(),
            matches30 = matches30.Select(point => new { date = point.Date, count = point.Count }).ToArray(),
            matchesUpdatedAt,
            storage = storage is null ? null : new
            {
                healthy = storage.Snapshot.Healthy,
                reason = storage.Snapshot.Reason,
                totalBytes = storage.Snapshot.TotalBytes,
                availableBytes = storage.Snapshot.AvailableBytes,
                updatedAt = storage.RefreshedAt.ToUnixTimeMilliseconds(),
                refreshIntervalHours = (int)storage.RefreshInterval.TotalHours,
            },
            test = ToDeploymentPayload(test, "test"),
            production = ToDeploymentPayload(production, "production"),
        });
    }

    private static object ToDeploymentPayload(AdminDeploymentStatus? status, string environment) => new
    {
        environment,
        state = status?.State ?? "unavailable",
        targetCommit = status?.TargetCommit,
        deployedCommit = status?.DeployedCommit,
        message = status?.Message ?? "发布执行器未配置。",
        updatedAt = status?.UpdatedAt,
    };

    private static object ToAdminPlayerPayload(AdminPlayerSummary player)
    {
        var presence = PresenceOf(player.Account);
        var qqBinding = _qqAccessStore.GetAccountBindingStatus(player.Account);
        return new
        {
            account = player.Account,
            displayName = player.DisplayName,
            createdAt = player.CreatedAt,
            lastLoginAt = player.LastLoginAt,
            hasPassword = player.HasPassword,
            online = presence.Online,
            qqBound = qqBinding.Bound,
            qqMasked = qqBinding.MaskedQq,
            qqCurrentlyWhitelisted = qqBinding.CurrentlyWhitelisted,
            qqBoundAt = qqBinding.BoundAt,
            bindingRevision = qqBinding.Revision,
        };
    }

    private static object ToAdminPlayerPayload(AdminQqAccountSummary player)
    {
        var presence = PresenceOf(player.Account);
        return new
        {
            account = player.Account,
            displayName = player.DisplayName,
            createdAt = player.CreatedAt,
            lastLoginAt = player.LastLoginAt,
            hasPassword = player.HasPassword,
            online = presence.Online,
            qqBound = player.Qq is not null,
            qq = player.Qq,
            qqMasked = player.QqMasked,
            qqCurrentlyWhitelisted = player.QqCurrentlyWhitelisted,
            qqBoundAt = player.QqBoundAt,
            bindingRevision = player.BindingRevision,
            matchKind = player.MatchKind,
        };
    }

    /// <summary>局内聊天(房间内):预设短语 + 自由文字,只发给本对局房间的双方 + 观战者。
    /// 限频(1.2s/条)+长度上限(100)防刷屏。瞬时消息,不进对局状态/快照。区别于大厅全局 OnChatMsg(BroadcastAll)。</summary>
    private static void OnGameChat(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequireCommunicationAccess(s, "MsgGameChat")) return;
        var room = GameRoomManager.GetRoomBySession(s.SessionId);
        string[] playerSessionIds;
        string[] recipients;
        if (room is not null)
        {
            playerSessionIds = room.PlayerSessionIds;
            recipients = room.PlayerSessionIds.Concat(room.Spectators.Keys).Distinct(StringComparer.Ordinal).ToArray();
        }
        else
        {
            var audience = PostGameChats.GetAudience(s.SessionId);
            if (audience is null) return;
            playerSessionIds = audience.PlayerSessionIds;
            recipients = audience.RecipientSessionIds;
        }

        var now = DateTime.UtcNow;
        if (GameChatAt.TryGetValue(s.SessionId, out var last) && (now - last).TotalMilliseconds < 1200) return;

        var text = (Str(msg, "Text") ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 100) text = text[..100];   // 长度上限
        var code = Str(msg, "Code");                  // 预设短语编号(可空,仅供客户端样式)

        GameChatAt[s.SessionId] = now;

        int seat = Array.IndexOf(playerSessionIds, s.SessionId); // 0/1=玩家, -1=观战
        if (room is not null && seat >= 0)
        {
            var evidence = GameChatEvidenceByRoom.GetOrAdd(room.RoomId, _ => new ConcurrentQueue<GameChatEvidence>());
            evidence.Enqueue(new GameChatEvidence(
                now,
                s.Account,
                s.PlayerName ?? s.Account ?? "玩家",
                "player",
                text,
                code));
            while (evidence.Count > 24) evidence.TryDequeue(out _);
        }
        var pkt = new
        {
            proto = "MsgGameChat",
            text,
            code,
            fromSeat = seat,
            fromAccount = s.Account,
            fromName = s.PlayerName ?? s.Account ?? "玩家",
            fromRole = seat >= 0 ? "player" : "spectator",
        };
        var blockedAccounts = string.IsNullOrWhiteSpace(s.Account)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : _playerDataStore.GetBlockedRelatedAccountKeys(s.Account);
        foreach (var recipient in recipients)
        {
            if (!Sessions.TryGetValue(recipient, out var recipientSession)
                || recipientSession.Account is null
                || !blockedAccounts.Contains(recipientSession.Account))
                Send(recipient, pkt);
        }
    }

    /// <summary>好友实时私聊：仅允许已建立好友关系的双方互发，消息不会广播给对局或大厅。</summary>
    private static void OnFriendChat(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgFriendChat", result = false, logStr = "请先登录" });
            return;
        }
        if (!TryRequireCommunicationAccess(s, "MsgFriendChat")) return;
        if (!s.TryConsumeRateLimit("friend-chat", capacity: 4, refillPerSecond: 0.75))
        {
            Send(s.SessionId, new { proto = "MsgFriendChat", result = false, logStr = "消息发送过于频繁，请稍后再试" });
            return;
        }

        var toAccount = (Str(msg, "toAccount") ?? "").Trim();
        var text = (Str(msg, "text") ?? "").Trim();
        if (toAccount.Length == 0 || text.Length == 0) return;
        if (text.Length > 100) text = text[..100];

        try
        {
            var queued = _playerDataStore.QueueFriendMessage(
                s.Account!,
                toAccount,
                Guid.NewGuid().ToString("N"),
                text,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var packet = new
            {
                proto = "MsgFriendChat",
                result = true,
                id = queued.Id,
                text = queued.Text,
                fromAccount = queued.FromAccount,
                fromName = queued.FromName,
                toAccount = queued.ToAccount,
                toName = queued.ToName,
                sentAt = queued.SentAt,
            };
            Send(s.SessionId, packet);
            if (TryGetActiveSession(queued.ToAccount, out var target))
                PushQueuedFriendMessages(target);
        }
        catch (Exception ex)
        {
            SendFriendError(s, "MsgFriendChat", ex, "好友消息发送失败");
        }
    }

    /// <summary>客户端离开结算页后主动解绑，避免旧对局消息串入后续页面。</summary>
    private static void OnLeaveGameChat(WsSession s)
    {
        PostGameChats.Leave(s.SessionId);
        RecentOpponentContexts.TryRemove(s.SessionId, out _);
    }

    // ── 对手查找 ──────────────────────────────────────────────────────────

    private static bool TryRequirePlayer(WsSession session)
    {
        if (session.IsLoggedIn) return true;
        Send(session.SessionId, new { proto = "MsgPlayerData", result = false, logStr = "请先登录" });
        return false;
    }

    private static StoredDeck DeserializeDeck(IReadOnlyDictionary<string, JsonElement> msg, string key)
    {
        if (!msg.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new PlayerDataValidationException("卡组数据无效。");
        return JsonSerializer.Deserialize<StoredDeck>(value.GetRawText(), JsonOpts)
               ?? throw new PlayerDataValidationException("卡组数据无效。");
    }

    private static void ValidatePlayableDeck(StoredDeck deck)
    {
        var deckText = string.Join('\n', new[] { deck.Leader }.Concat(deck.Cards ?? []));
        // 保存卡组不等于参加公开对战；保留禁卡卡组，供好友邀请或房间码对战选择。
        var validation = DeckValidator.Validate(deckText, DeckValidator.FormatUnrestricted);
        if (!validation.Ok)
            throw new PlayerDataValidationException($"卡组不合法: {validation.Reason}");
    }

    private static void SendPlayerData(WsSession session, PlayerDataSnapshot snapshot, string? logStr = null)
    {
        var (championLeaderNumbers, equippedChampionLeaderNumber) = ResolveChampionTitleSnapshot(snapshot);
        Send(session.SessionId, new
        {
            proto = "MsgPlayerData",
            result = true,
            logStr,
            account = snapshot.Account,
            displayName = snapshot.DisplayName,
            avatar = snapshot.Avatar,
            cardBackId = snapshot.CardBackId,
            canChangeDisplayName = snapshot.CanChangeDisplayName,
            selectedDeckName = snapshot.SelectedDeckName,
            championLeaderNumbers,
            equippedChampionLeaderNumber,
            decks = snapshot.Decks,
        });
    }

    private static (IReadOnlyList<string> Owned, string? Equipped) ResolveChampionTitleSnapshot(
        PlayerDataSnapshot snapshot)
    {
        LeaderChampionStore.Default.RememberEquippedChampionLeaderNumber(
            snapshot.Account,
            snapshot.EquippedChampionLeaderNumber);
        try
        {
            var owned = LeaderChampionStore.Default.GetChampionLeaderNumbers(snapshot.Account);
            var equipped = LeaderChampionStore.Default.ResolveEquippedChampionLeaderNumber(snapshot.Account);
            return (owned, equipped);
        }
        catch (Exception ex)
        {
            // 称号属于附加画像；数据源短暂不可用时只隐藏称号，不阻断登录、卡组或个人资料操作。
            LogErr($"读取 {snapshot.Account} 的最强称号失败: {ex.Message}");
            return (Array.Empty<string>(), null);
        }
    }

    private static (bool Online, string Status) PresenceOf(string account)
    {
        if (!TryGetActiveSession(account, out var session)) return (false, "offline");
        return (true, StatusOf(session));
    }

    private static void SendFriendData(WsSession session, FriendDataSnapshot snapshot, string? logStr = null)
    {
        var friends = snapshot.Friends.Select(friend =>
        {
            var presence = PresenceOf(friend.Account);
            string? roomId = null;
            int? seatIndex = null;
            if (presence.Status == "playing" && TryGetActiveSession(friend.Account, out var friendSession))
            {
                var gameRoom = GameRoomManager.GetRoomBySession(friendSession.SessionId);
                var resolvedSeatIndex = gameRoom is null
                    ? -1
                    : Array.IndexOf(gameRoom.PlayerSessionIds, friendSession.SessionId);
                if (gameRoom is not null && resolvedSeatIndex >= 0)
                {
                    roomId = gameRoom.RoomId;
                    seatIndex = resolvedSeatIndex;
                }
            }
            return new
            {
                account = friend.Account,
                name = friend.DisplayName,
                avatar = friend.Avatar,
                championLeaderNumbers = LeaderChampionStore.Default.GetChampionLeaderNumbers(friend.Account),
                friendsSince = friend.FriendsSince,
                online = presence.Online,
                status = presence.Status,
                roomId,
                seatIndex,
                spectateMode = roomId is not null && seatIndex is not null
                    ? GameRoomManager.GetRoom(roomId)?.SpectateModes[seatIndex.Value]
                    : null,
            };
        }).ToArray();
        var incomingRequests = snapshot.IncomingRequests.Select(request =>
        {
            var presence = PresenceOf(request.Account);
            return new
            {
                id = request.Id,
                account = request.Account,
                name = request.DisplayName,
                avatar = request.Avatar,
                createdAt = request.CreatedAt,
                online = presence.Online,
            };
        }).ToArray();
        var outgoingRequests = snapshot.OutgoingRequests.Select(request =>
        {
            var presence = PresenceOf(request.Account);
            return new
            {
                id = request.Id,
                account = request.Account,
                name = request.DisplayName,
                avatar = request.Avatar,
                createdAt = request.CreatedAt,
                online = presence.Online,
            };
        }).ToArray();

        Send(session.SessionId, new
        {
            proto = "MsgFriendList",
            result = true,
            logStr,
            friends,
            incomingRequests,
            outgoingRequests,
        });
    }

    private static void PushFriendDataToAccount(string account, string? logStr = null)
    {
        if (!TryGetActiveSession(account, out var session)) return;
        try
        {
            SendFriendData(session, _playerDataStore.GetFriendData(account), logStr);
        }
        catch (Exception ex)
        {
            LogErr($"推送好友状态失败 {account}: {ex.Message}");
        }
    }

    private static void PushFriendPresenceToFriends(string account)
    {
        try
        {
            var snapshot = _playerDataStore.GetFriendData(account);
            foreach (var friend in snapshot.Friends)
                PushFriendDataToAccount(friend.Account);
        }
        catch (Exception ex)
        {
            LogErr($"更新好友在线状态失败 {account}: {ex.Message}");
        }
    }

    private static void PushQueuedFriendMessages(WsSession session)
    {
        if (!session.IsLoggedIn || session.Account is null) return;
        try
        {
            foreach (var message in _playerDataStore.TakeQueuedFriendMessages(session.Account))
            {
                Send(session.SessionId, new
                {
                    proto = "MsgFriendChat",
                    result = true,
                    id = message.Id,
                    text = message.Text,
                    fromAccount = message.FromAccount,
                    fromName = message.FromName,
                    toAccount = message.ToAccount,
                    toName = message.ToName,
                    sentAt = message.SentAt,
                });
            }
        }
        catch (Exception ex)
        {
            LogErr($"补发好友离线消息失败 {session.Account}: {ex.Message}");
        }
    }

    private static void SendFriendError(WsSession session, string proto, Exception exception, string fallback)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new { proto, result = false, logStr = message });
    }

    private static void SendCardBackGalleryPage(
        WsSession session,
        CardBackGalleryPage page,
        string? requestCursor,
        string? logStr = null)
    {
        Send(session.SessionId, new
        {
            proto = "MsgCardBackGallery",
            result = true,
            logStr,
            cursor = requestCursor,
            items = page.Items.Select(ToCardBackGalleryPayload).ToArray(),
            ownedItems = page.OwnedItems.Select(ToCardBackGalleryPayload).ToArray(),
            pageSize = page.PageSize,
            total = page.Total,
            hasMore = page.HasMore,
            nextCursor = page.NextCursor,
            sortOrder = page.SortOrder,
        });
    }

    private static object ToCardBackGalleryPayload(CardBackGalleryItem item) => new
    {
        id = item.Id,
        name = item.Name,
        authorName = item.AuthorName,
        imageUrl = item.ImageUrl,
        likes = item.Likes,
        liked = item.Liked,
        owned = item.Owned,
        publiclyListed = item.PubliclyListed,
        createdAt = item.CreatedAt,
        reviewStatus = item.ReviewStatus,
        reviewReason = item.ReviewReason,
    };

    private static void SendCardBackLikeUpdate(WsSession session, CardBackGalleryItem item)
    {
        Send(session.SessionId, new
        {
            proto = "MsgLikeCardBack",
            result = true,
            item = ToCardBackGalleryPayload(item),
        });
    }

    private static void SendCardBackLikeError(WsSession session, Exception exception)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : "更新红心失败";
        if (exception is not PlayerDataValidationException)
            LogErr($"更新红心失败 {session.Account}: {exception.Message}");
        Send(session.SessionId, new { proto = "MsgLikeCardBack", result = false, logStr = message });
    }

    private static void SendCardBackGalleryError(
        WsSession session,
        Exception exception,
        string fallback,
        string? requestCursor = null,
        string? sortOrder = null)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new
        {
            proto = "MsgCardBackGallery",
            result = false,
            logStr = message,
            cursor = requestCursor,
            sortOrder,
        });
    }

    private static void SendCardBackReviewQueue(
        WsSession session,
        IReadOnlyList<CardBackReviewItem> items,
        string? logStr = null)
    {
        Send(session.SessionId, new
        {
            proto = "MsgCardBackReviewQueue",
            result = true,
            canReview = true,
            logStr,
            items = items.Select(item => new
            {
                id = item.Id,
                name = item.Name,
                authorName = item.AuthorName,
                imageUrl = item.ImageUrl,
                createdAt = item.CreatedAt,
            }).ToArray(),
        });
    }

    private static void SendCardBackReviewError(WsSession session, Exception exception, string fallback)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new
        {
            proto = "MsgCardBackReviewQueue",
            result = false,
            canReview = true,
            logStr = message,
        });
    }

    private static void SendDeckPlazaPage(WsSession session, DeckPlazaPage page)
    {
        var items = page.Items.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            authorName = item.AuthorName,
            leader = item.Leader,
            leaderName = item.LeaderName,
            leaderSprite = item.LeaderSprite,
            leaderColor = item.LeaderColor,
            charCount = item.CharCount,
            eventCount = item.EventCount,
            stageCount = item.StageCount,
            cards = item.Cards,
            spriteMap = item.SpriteMap,
            likes = item.Likes,
            liked = item.Liked,
            owned = item.Owned,
            copies = item.Copies,
            createdAt = item.CreatedAt,
            updatedAt = item.UpdatedAt,
        }).ToArray();
        Send(session.SessionId, new
        {
            proto = "MsgDeckPlazaList",
            result = true,
            items,
            page = page.Page,
            pageSize = page.PageSize,
            total = page.Total,
            hasMore = page.HasMore,
        });
    }

    private static void SendDeckPlazaError(WsSession session, string proto, Exception exception, string fallback)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new { proto, result = false, logStr = message });
    }

    private static void SendPlayerDataError(WsSession session, Exception exception, string fallback)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new { proto = "MsgPlayerData", result = false, logStr = message });
    }

    private static string? GetOpponentSid(string selfSid)
        => GameOpponent.TryGetValue(selfSid, out var opp) ? opp : null;

    /// <summary>正式对局按账号恢复到新连接时同步会话级对手索引。</summary>
    public static void OnGameSessionRebound(string oldSessionId, string newSessionId, string opponentSessionId)
    {
        GameOpponent.TryRemove(oldSessionId, out _);
        GameOpponent[newSessionId] = opponentSessionId;
        if (GameOpponent.TryGetValue(opponentSessionId, out var mappedOpponent) && mappedOpponent == oldSessionId)
            GameOpponent[opponentSessionId] = newSessionId;
    }

    /// <summary>加入新对局或新观战时，先退出旧的赛后聊天组。</summary>
    public static void OnGameChatParticipantJoined(string sessionId)
    {
        PostGameChats.Leave(sessionId);
        RecentOpponentContexts.TryRemove(sessionId, out _);
    }

    /// <summary>权威对局清理时保留轻量赛后聊天组，并清除会话级对手索引。</summary>
    public static void OnGameRoomClosed(
        string roomId,
        IEnumerable<string> playerSessionIds,
        IEnumerable<string> playerAccounts,
        IEnumerable<string> spectatorSessionIds,
        bool preservePostGameChat,
        MatchKind matchKind,
        int turnCount,
        string? gameOverReason)
    {
        var players = playerSessionIds.ToArray();
        var accounts = playerAccounts.ToArray();
        if (preservePostGameChat)
        {
            PostGameChats.Register(players, spectatorSessionIds);
            if (players.Length >= 2 && accounts.Length >= 2)
            {
                var expiresAtUtc = DateTime.UtcNow.AddMinutes(30);
                var recentGameChat = SnapshotGameChatEvidence(roomId);
                RecentOpponentContexts[players[0]] = new RecentOpponentContext(
                    accounts[1], roomId, matchKind.ToString(), turnCount, gameOverReason, recentGameChat, expiresAtUtc);
                RecentOpponentContexts[players[1]] = new RecentOpponentContext(
                    accounts[0], roomId, matchKind.ToString(), turnCount, gameOverReason, recentGameChat, expiresAtUtc);
            }
        }
        GameChatEvidenceByRoom.TryRemove(roomId, out _);
        foreach (var sessionId in players)
            GameOpponent.TryRemove(sessionId, out _);
    }

    // ── 发送工具 ──────────────────────────────────────────────────────────

    public static void Send(string sessionId, object data)
    {
        if (Sessions.TryGetValue(sessionId, out var s))
            EnqueueForSession(s, data);
    }

    /// <summary>排位连胜达到门槛后，向所有在线会话发送滚动公告。</summary>
    public static void BroadcastRankedWinStreak(
        string? playerName,
        string? defeatedFaction,
        string? defeatedTier,
        int winStreak)
    {
        var content = GlobalAnnouncementPolicy.FormatRankedWinStreak(
            playerName,
            defeatedFaction,
            defeatedTier,
            winStreak);
        if (content is null) return;
        BroadcastAll(new
        {
            proto = "MsgGlobalAnnouncement",
            content,
            kind = "rankedStreak",
            issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    /// <summary>排位玩家达到门槛的连胜被终结后，向所有在线会话发送滚动公告。</summary>
    public static void BroadcastRankedWinStreakEnded(
        string? defeatedPlayerName,
        int endedWinStreak,
        string? winnerFaction,
        string? winnerName)
    {
        var content = GlobalAnnouncementPolicy.FormatRankedWinStreakEnded(
            defeatedPlayerName,
            endedWinStreak,
            winnerFaction,
            winnerName);
        if (content is null) return;
        BroadcastAll(new
        {
            proto = "MsgGlobalAnnouncement",
            content,
            kind = "rankedStreak",
            issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private static void BroadcastAll(object data)
    {
        foreach (var kv in Sessions) EnqueueForSession(kv.Value, data);
    }

    private static void EnqueueForSession(WsSession session, object data)
    {
        var type = data.GetType();
        var proto = type.GetProperty("proto")?.GetValue(data) as string ?? "";
        var isStateSnapshot = string.Equals(proto, "MsgGameState", StringComparison.Ordinal);
        string? coalesceKey = null;
        // 除明确标记为可丢弃的通知外，协议回包默认必须可靠送达；
        // 如果关键消息已积压到上限，断开慢连接比静默制造客户端状态分叉更安全。
        var priority = WsSession.OutboundPriority.Critical;

        if (isStateSnapshot)
        {
            if (IsReplaceableStateSnapshot(data))
            {
                coalesceKey = WsSession.GameStateCoalesceKey;
                priority = WsSession.OutboundPriority.BestEffort;
            }
            else
            {
                priority = WsSession.OutboundPriority.Critical;
            }
        }
        else if (string.Equals(proto, "MsgOnlineCount", StringComparison.Ordinal))
        {
            coalesceKey = "online-count";
            priority = WsSession.OutboundPriority.BestEffort;
        }
        else if (string.Equals(proto, "MsgPlayerList", StringComparison.Ordinal))
        {
            coalesceKey = "player-list";
            priority = WsSession.OutboundPriority.BestEffort;
        }
        else if (CriticalOutboundProtocols.Contains(proto))
        {
            priority = WsSession.OutboundPriority.Critical;
        }
        else if (BestEffortOutboundProtocols.Contains(proto))
        {
            priority = WsSession.OutboundPriority.BestEffort;
        }

        if (!session.Enqueue(data, coalesceKey, priority, isStateSnapshot)
            && priority == WsSession.OutboundPriority.Critical)
        {
            try { session.Socket.Abort(); } catch { }
        }
    }

    /// <summary>广播当前在线人数（账号索引中的唯一权威会话数）给所有客户端</summary>
    private static void BroadcastOnlineCount()
    {
        Interlocked.Increment(ref _onlineBroadcastVersion);
        if (Interlocked.Exchange(ref _onlineBroadcastScheduled, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            var deliveredVersion = 0;
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    deliveredVersion = Volatile.Read(ref _onlineBroadcastVersion);
                    await Task.Delay(500, _cts.Token);
                    int count = LoggedInCount;
                    RecordOnlinePlayerCount(count);
                    BroadcastAll(new { proto = "MsgOnlineCount", count });
                    if (deliveredVersion == Volatile.Read(ref _onlineBroadcastVersion)) break;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                Interlocked.Exchange(ref _onlineBroadcastScheduled, 0);
                if (!_cts.IsCancellationRequested
                    && deliveredVersion != Volatile.Read(ref _onlineBroadcastVersion))
                    BroadcastOnlineCount();
            }
        });
    }

    private static async Task RunOnlinePlayerSamplingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            RecordOnlinePlayerCount(LoggedInCount);
            try { await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task RunQqSecurityEventPollingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var events = _qqAccessStore.GetSecurityEventsAfter(
                    Volatile.Read(ref _qqSecurityEventCursor), 100);
                foreach (var securityEvent in events)
                {
                    ApplyAccountSecurityEvent(securityEvent);
                    Volatile.Write(ref _qqSecurityEventCursor, securityEvent.Id);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LogErr($"同步共享账号安全事件失败：{ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static void ApplyAccountSecurityEvent(AccountSecurityEvent securityEvent)
    {
        if (string.Equals(securityEvent.EventType, "credentials_reset", StringComparison.Ordinal))
        {
            if (securityEvent.TargetAccount is { Length: > 0 } target
                && TryGetActiveSession(target, out var targetSession))
                SupersedeSession(targetSession, "管理员已重置此账号密码，请使用临时密码重新登录。");
            return;
        }

        if (string.Equals(securityEvent.EventType, "whitelist_replaced", StringComparison.Ordinal))
        {
            EvictIneligibleWaitingActivities();
            foreach (var currentSession in Sessions.Values.Where(IsCurrentAccountSession).ToArray())
                ReconcileQqAccessForSession(currentSession);
            return;
        }

        if (string.Equals(securityEvent.EventType, "qq_binding_changed", StringComparison.Ordinal)
            && securityEvent.TargetAccount is { Length: > 0 } account)
        {
            ApplyQqBindingSecurityMutation(account);
        }
    }

    private static void ApplyQqBindingSecurityMutation(string account)
    {
        EvictIneligibleWaitingActivities();
        if (!TryGetActiveSession(account, out var session)) return;
        if (IsRegisteredPlayerSession(session))
        {
            session.MarkQqAccessRevoked();
            Send(session.SessionId, new
            {
                proto = "MsgQqAccessDenied",
                result = false,
                currentGameContinues = true,
                logStr = "QQ 绑定已被管理员更新。当前对局不会中断；对局结束后请重新认证。",
            });
            ScheduleQqRevokedSessionClose(session);
            return;
        }

        SupersedeSession(session, "QQ 绑定已被管理员更新，请重新登录认证。");
    }

    private static void ReconcileQqAccessForSession(WsSession session)
    {
        if (session.Account is null || !IsCurrentAccountSession(session)) return;
        QqLoginAccessResult access;
        try { access = _qqAccessStore.CheckNewGameAccess(session.Account); }
        catch (Exception ex)
        {
            LogErr($"同步会话 QQ 资格失败 {session.Account}: {ex.Message}");
            return;
        }

        if (access.Allowed)
        {
            session.ClearQqAccessRevoked();
            Send(session.SessionId, new
            {
                proto = "MsgQqBindingChanged",
                result = true,
                bound = true,
                qqMasked = access.MaskedQq,
                currentlyWhitelisted = true,
                logStr = "管理员已更新 QQ 绑定；当前会话可继续使用，后续重连需要重新认证。",
            });
            return;
        }

        if (IsRegisteredPlayerSession(session))
        {
            session.MarkQqAccessRevoked();
            Send(session.SessionId, new
            {
                proto = "MsgQqAccessDenied",
                result = false,
                currentGameContinues = true,
                logStr = "QQ 绑定资格已变化。当前对局不会被中断；对局结束后请重新登录并完成绑定。",
            });
            ScheduleQqRevokedSessionClose(session);
            return;
        }

        SupersedeSession(session, "QQ 绑定资格已变化，请重新登录并完成绑定。");
    }

    private static bool IsRegisteredPlayerSession(WsSession session)
    {
        var room = GameRoomManager.GetRoomBySession(session.SessionId);
        return room is not null && Array.IndexOf(room.PlayerSessionIds, session.SessionId) >= 0;
    }

    private static void ScheduleQqRevokedSessionClose(WsSession session)
    {
        if (!session.TryStartQqRevokedCloseMonitor()) return;
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested && !session.IsSuperseded)
            {
                if (session.IsQqAccessRevoked && !IsRegisteredPlayerSession(session))
                {
                    SupersedeSession(session, "当前对局已结束；请重新登录并完成 QQ 绑定。");
                    return;
                }
                try { await Task.Delay(TimeSpan.FromSeconds(1), _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    private static void RecordOnlinePlayerCount(int count)
    {
        try { _onlinePlayerHistoryStore?.Record(count); }
        catch (Exception ex) { LogErr($"记录在线峰值失败：{ex.Message}"); }
    }

    private static bool IsReplaceableStateSnapshot(object data)
    {
        var type = data.GetType();
        if (!string.Equals(type.GetProperty("proto")?.GetValue(data) as string, "MsgGameState", StringComparison.Ordinal))
            return false;
        // 效果发动表现是一次性事件；若与普通状态一起被后续快照合并，客户端将永远无法补播。
        // 因此只要队列非空，就与攻击、Prompt 等关键动画屏障一样可靠保留。
        if (type.GetProperty("effectActivations")?.GetValue(data) is Array { Length: > 0 })
            return false;
        var lastAction = type.GetProperty("lastAction")?.GetValue(data) as string ?? "";
        return !NonReplaceableStateActions.Contains(lastAction);
    }

    private static async Task SendDirectAsync(WsSession s, WsSession.OutboundMessage message)
    {
        if (s.Socket.State != WebSocketState.Open) return;
        var totalStartedAt = LatencyDiagnostics.Start();
        var serializeStartedAt = totalStartedAt;
        var encoded = SnapshotWireCodec.Encode(
            message.Data,
            s.SupportsDeltaSnapshots,
            s.SnapshotBaseline,
            s.SnapshotBaselineTick,
            s.SnapshotDeltasSinceFull);
        var bytes = encoded.Bytes;
        LatencyDiagnostics.Observe("WebSocket 序列化", serializeStartedAt, $"会话={s.SessionId[..8]}，字节={bytes.Length}");
        LatencyDiagnostics.RecordMetric("WebSocket 消息大小", bytes.Length / 1024d, "KB");
        try
        {
            using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await s.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, sendTimeout.Token);
            s.CommitSnapshotBaseline(encoded);
        }
        catch (OperationCanceledException)
        {
            LogWarn($"Send {s.SessionId}: 超过 5 秒，终止慢连接");
            s.Socket.Abort();
        }
        catch (Exception ex) { LogErr($"Send {s.SessionId}: {ex.Message}"); }
        LatencyDiagnostics.Observe("WebSocket 发送总耗时", totalStartedAt, $"会话={s.SessionId[..8]}，字节={bytes.Length}");
    }

    private static bool ReadBooleanEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    // ── 日志工具 ──────────────────────────────────────────────────────────

    private static void Log(string msg)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    private static void LogWarn(string msg)
    {
        var c = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ {msg}");
        Console.ForegroundColor = c;
    }

    private static void LogErr(string msg)
    {
        var c = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ {msg}");
        Console.ForegroundColor = c;
    }
}
