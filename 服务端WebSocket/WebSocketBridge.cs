using System.Collections.Concurrent;
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Diagnostics;
using GrandUMI.Game;
using GrandUMI.Game.Ranked;
using GrandUMI.Game.Stats;
using GrandUMI.Game.Validation;
using GrandUMI.Persistence;

namespace GrandUMI;

/// <summary>
/// GrandUMI WebSocket 网关
/// 协议：JSON over WebSocket，字段名与 C# LobbyMsg / GameMsg 完全一致
/// 不依赖任何第三方库，纯 .NET 内置 API
/// </summary>
public static class WebSocketBridge
{
    private const int MaxInboundMessageBytes = 524_288;
    private static readonly HashSet<string> NonReplaceableStateActions = new(StringComparer.Ordinal)
    {
        "GameStart", "Resync", "SpectateJoin", "FirstPlayerChosen",
        "Prompt", "PromptTimeout", "RevealCards",
        "Attack", "AwaitBlock", "AwaitCounter", "DeclareBlocker", "CounterIcon", "PlayCard",
        "MulliganComplete", "MulliganUpdate", "DuelOver", "Surrender", "DisconnectTimeout",
        "OperationTimeout", "PlayerDisconnected", "PlayerReconnected",
    };
    private static readonly HashSet<string> CriticalOutboundProtocols = new(StringComparer.Ordinal)
    {
        "MsgLogin", "MsgSecret", "MsgPlayerData", "MsgRankSnapshot", "MsgRankResult", "MsgActionRejected", "MsgDuelOver",
        "MsgPrompt", "MsgPromptResponse", "MsgReconnect", "MsgPlayerReconnected",
    };
    private static readonly HashSet<string> BestEffortOutboundProtocols = new(StringComparer.Ordinal)
    {
        "MsgOnlineCount", "MsgPlayerList", "MsgChatMsg", "MsgFriendChat", "MsgRateLimited",
    };
    // ── 会话注册表 ────────────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, WsSession> Sessions    = new();
    private static readonly ConcurrentDictionary<string, string>    AccountIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object                                  AccountIndexGate = new();
    private static readonly ConcurrentQueue<WsSession>              MatchQueue   = new();
    private static readonly ConcurrentQueue<WsSession>              RankedMatchQueue = new();
    private static readonly object                                  MatchQueueGate = new();
    private static readonly ConcurrentDictionary<string, string>    GameOpponent = new();
    private static readonly ConcurrentDictionary<string, string>    PendingRooms = new(); // roomCode → 赛前房间ID
    private static readonly ConcurrentDictionary<string, InviteInfo> PendingInvites = new(); // inviteId → 邀请对战
    private static readonly ConcurrentDictionary<string, DuelLobby> FriendlyRooms = new(); // roomId → 共用赛前房间
    private static readonly ConcurrentDictionary<string, string> FriendlyByAccount = new(StringComparer.OrdinalIgnoreCase); // account → roomId
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> FriendlyDisconnectGrace = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> GameChatAt = new(); // sessionId → 上次局内聊天时间(限频防刷屏)
    private static readonly PostGameChatRegistry PostGameChats = new(TimeSpan.FromMinutes(30));
    private static readonly TimeSpan LobbyReconnectGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveSessionMaxIdle = TimeSpan.FromSeconds(35);
    private static readonly bool ProtocolLogEnabled = ReadBooleanEnvironment("GRANDUMI_PROTOCOL_LOG");

    private sealed record InviteInfo(string Id, string FromSid, string FromAccount, string FromName, string ToSid);

    private static CancellationTokenSource _cts = new();
    private static PlayerDataStore _playerDataStore = null!;
    private static AccountAuthenticationStore _accountAuthenticationStore = null!;
    private static int _accepting;
    private static int _onlineBroadcastScheduled;
    private static int _onlineBroadcastVersion;

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

    // ── 生命周期 ──────────────────────────────────────────────────────────
    public static void Initialize(
        PlayerDataStore playerDataStore,
        AccountAuthenticationStore accountAuthenticationStore)
    {
        _playerDataStore = playerDataStore ?? throw new ArgumentNullException(nameof(playerDataStore));
        _accountAuthenticationStore = accountAuthenticationStore
            ?? throw new ArgumentNullException(nameof(accountAuthenticationStore));
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        Volatile.Write(ref _accepting, 1);
    }

    public static void Stop()
    {
        Volatile.Write(ref _accepting, 0);
        _cts.Cancel();
    }

    // ── 连接接受 ──────────────────────────────────────────────────────────
    public static bool IsReady => Volatile.Read(ref _accepting) != 0;
    public static int ConnectionCount => Sessions.Count;
    public static int LoggedInCount => Sessions.Count(item => item.Value.IsLoggedIn);
    public static long DroppedOutboundCount => Sessions.Values.Sum(item => item.DroppedOutboundCount);
    public static int MaxCurrentOutboundDepth => Sessions.IsEmpty ? 0 : Sessions.Values.Max(item => item.OutboundDepth);

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
        Sessions.TryRemove(session.SessionId, out _);
        var wasCurrentAccountSession = false;
        if (session.Account is not null)
        {
            lock (AccountIndexGate)
            {
                if (AccountIndex.TryGetValue(session.Account, out var indexedSession) &&
                    indexedSession == session.SessionId)
                {
                    AccountIndex.TryRemove(session.Account, out _);
                    wasCurrentAccountSession = true;
                }
            }
        }
        if (session.IsMatching) RebuildMatchQueue(session);
        // 对局中的玩家断开 → 进入 90s 宽限期（M1）
        GameOpponent.TryRemove(session.SessionId, out _);
        GameChatAt.TryRemove(session.SessionId, out _);
        PostGameChats.Leave(session.SessionId);
        CleanupInvites(session.SessionId);
        if (session.Account is not null && wasCurrentAccountSession)
            HandleFriendlyDisconnect(session.Account);
        GameRoomManager.OnPlayerDisconnect(session.SessionId);
        Log($"断开 {session.SessionId} ({session.Account ?? "未登录"})");
        // 在线人数变化，广播给剩余客户端
        BroadcastOnlineCount();
        if (session.Account is not null && wasCurrentAccountSession)
            PushFriendPresenceToFriends(session.Account);
    }

    // ── 消息路由 ──────────────────────────────────────────────────────────
    private static void Route(string sessionId, string json)
    {
        if (!Sessions.TryGetValue(sessionId, out var session)) return;
        session.MarkSeen();

        Dictionary<string, JsonElement>? msg;
        try { msg = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts); }
        catch { LogErr($"JSON 解析失败: {json[..Math.Min(80, json.Length)]}"); return; }

        if (msg is null) return;
        var proto = Str(msg, "proto");
        if (proto is null) return;

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
            case "MsgLogin":       OnLogin(session, msg);        break;
            case "MsgAddAccount":  OnAddAccount(session, msg);   break;
            case "MsgUpdatePs":    OnUpdatePs(session, msg);     break;
            case "MsgSaveDeck":    OnSaveDeck(session, msg);     break;
            case "MsgDeleteDeck":  OnDeleteDeck(session, msg);   break;
            case "MsgSelectDeck":  OnSelectDeck(session, msg);   break;
            case "MsgUpdateProfile": OnUpdateProfile(session, msg); break;
            case "MsgUpdateCardBack": OnUpdateCardBack(session, msg); break;
            case "MsgCardBackGallery": OnCardBackGallery(session); break;
            case "MsgUploadCardBack": OnUploadCardBack(session, msg); break;
            case "MsgLikeCardBack": OnLikeCardBack(session, msg); break;
            case "MsgDeleteCardBack": OnDeleteCardBack(session, msg); break;
            case "MsgImportDecks": OnImportDecks(session, msg);  break;
            case "MsgDeckPlazaList": OnDeckPlazaList(session, msg); break;
            case "MsgPublishDeckPlaza": OnPublishDeckPlaza(session, msg); break;
            case "MsgLikeDeckPlaza": OnLikeDeckPlaza(session, msg); break;
            case "MsgCopyDeckPlaza": OnCopyDeckPlaza(session, msg); break;
            case "MsgDeleteDeckPlaza": OnDeleteDeckPlaza(session, msg); break;
            case "MsgEnterMatch":  OnEnterMatch(session, msg);   break;
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
            case "MsgLeaderLeaderboard": OnLeaderLeaderboard(session, msg); break;
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
            // Sprint 3: 服务端结算协议
            case "MsgGameAction":  OnGameAction(session, msg);   break;
            case "MsgPromptResponse": OnPromptResponse(session, msg); break;
            case "MsgUpdateSettings": OnUpdateSettings(session, msg); break;
            case "MsgRequestState": OnRequestState(session);     break;
            case "MsgEndByDisconnect": OnEndByDisconnect(session); break;
            case "MsgSpectateRoom": OnSpectateRoom(session, msg); break;
            case "MsgLeaveSpectate": OnLeaveSpectate(session); break;
            case "MsgBugReport":   OnBugReport(session, msg);     break;
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

    private static void OnLogin(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedAccount = Str(msg, "account") ?? "";
        if (!s.TryConsumeRateLimit("account-login", capacity: 6, refillPerSecond: 0.1))
        {
            Send(s.SessionId, new
            {
                proto = "MsgLogin",
                account = requestedAccount,
                result = false,
                needsPassword = true,
                needsPasswordSetup = false,
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
                    authChallenge = authentication.IsChallenge,
                    logStr = authentication.Message,
                });
                return;
            }

            var playerData = _playerDataStore.Login(authentication.Account);

            string? supersededSessionId = null;
            lock (AccountIndexGate)
            {
                if (s.Account is not null &&
                    !string.Equals(s.Account, playerData.Account, StringComparison.OrdinalIgnoreCase) &&
                    AccountIndex.TryGetValue(s.Account, out var previousForOldAccount) &&
                    previousForOldAccount == s.SessionId)
                    AccountIndex.TryRemove(s.Account, out _);

                if (AccountIndex.TryGetValue(playerData.Account, out var previousForAccount) &&
                    previousForAccount != s.SessionId)
                    supersededSessionId = previousForAccount;

                s.Account = playerData.Account;
                s.PlayerName = playerData.DisplayName;
                s.CardBackId = playerData.CardBackId;
                AccountIndex[playerData.Account] = s.SessionId;
            }

            Send(s.SessionId, new
            {
                proto = "MsgLogin",
                account = playerData.Account,
                name = playerData.DisplayName,
                avatar = playerData.Avatar,
                cardBackId = playerData.CardBackId,
                selectedDeckName = playerData.SelectedDeckName,
                decks = playerData.Decks,
                authToken = authentication.AuthToken,
                result = true,
                logStr = authentication.Message,
            });
            SendFriendData(s, _playerDataStore.GetFriendData(playerData.Account));
            SendRankSnapshot(s);
            Log($"登录 ✅ {playerData.Account}");

            // 同账号只保留最新连接。旧连接稍后关闭时不会再清理新连接绑定的房间。
            if (supersededSessionId is not null && Sessions.TryGetValue(supersededSessionId, out var superseded))
            {
                try { superseded.Socket.Abort(); } catch { }
            }

            // 两个登录请求并发时，只有当前账号索引指向的最新连接有权恢复房间。
            lock (AccountIndexGate)
            {
                if (!AccountIndex.TryGetValue(playerData.Account, out var currentSessionId) ||
                    currentSessionId != s.SessionId)
                    return;
            }

            // 登录后尝试断线重连：如该账号还有未结束的对局，自动恢复。
            if (GameRoomManager.TryReclaim(s.SessionId, playerData.Account, playerData.CardBackId))
                Log($"断线重连成功 {playerData.Account}");
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
        catch (Exception ex)
        {
            Send(s.SessionId, new { proto = "MsgLogin", account = requestedAccount, name = "", result = false, logStr = "玩家数据库暂时不可用" });
            LogErr($"登录数据库异常 {requestedAccount}: {ex.Message}");
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
            s.Account    = account;
            s.PlayerName = name;
            AccountIndex[account] = s.SessionId;
        }

        Send(s.SessionId, new { proto = "MsgLogin", account, name, result = ok,
                                logStr = ok ? "登录成功" : "账号不能为空" });
        Log($"登录 {(ok ? "✅" : "❌")} {account}");

        // 登录后尝试断线重连：如该账号还有未结束的对局，自动恢复
        if (ok && GameRoomManager.TryReclaim(s.SessionId, account))
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

    private static void OnUpdateCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var result = _playerDataStore.UpdateCardBack(s.Account!, Str(msg, "cardBackId") ?? "");
            s.CardBackId = result.Snapshot.CardBackId;
            SendPlayerData(s, result.Snapshot, "卡背已保存并点亮了红心");
            SendCardBackGallery(s, result.Gallery);
        }
        catch (Exception ex) { SendPlayerDataError(s, ex, "保存卡背失败"); }
    }

    private static void OnCardBackGallery(WsSession s)
    {
        if (!TryRequirePlayer(s)) return;
        try { SendCardBackGallery(s, _playerDataStore.GetCardBackGallery(s.Account!)); }
        catch (Exception ex) { SendCardBackGalleryError(s, ex, "读取卡背广场失败"); }
    }

    private static void OnUploadCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var gallery = _playerDataStore.UploadCardBack(
                s.Account!,
                Str(msg, "name") ?? "",
                Str(msg, "mimeType") ?? "",
                Str(msg, "imageBase64") ?? "");
            SendCardBackGallery(s, gallery, "卡背已发布到广场");
        }
        catch (Exception ex) { SendCardBackGalleryError(s, ex, "上传卡背失败"); }
    }

    private static void OnLikeCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var gallery = _playerDataStore.ToggleCardBackLike(s.Account!, Str(msg, "cardBackId") ?? "");
            SendCardBackGallery(s, gallery);
        }
        catch (Exception ex) { SendCardBackGalleryError(s, ex, "更新红心失败"); }
    }

    private static void OnDeleteCardBack(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!TryRequirePlayer(s)) return;
        try
        {
            var result = _playerDataStore.DeleteCardBack(s.Account!, Str(msg, "cardBackId") ?? "");
            s.CardBackId = result.Snapshot.CardBackId;
            SendPlayerData(s, result.Snapshot, "卡背投稿已删除");
            SendCardBackGallery(s, result.Gallery);

            foreach (var session in Sessions.Values.Where(IsCurrentAccountSession))
            {
                if (session.SessionId == s.SessionId || session.Account is null) continue;
                try
                {
                    if (session.CardBackId == result.DeletedCardBackId)
                    {
                        var snapshot = _playerDataStore.GetPlayerData(session.Account);
                        session.CardBackId = snapshot.CardBackId;
                        SendPlayerData(session, snapshot, "正在使用的卡背已由发布者删除，已恢复为经典卡背");
                    }
                    SendCardBackGallery(session, _playerDataStore.GetCardBackGallery(session.Account));
                }
                catch (Exception ex) { LogErr($"同步卡背删除结果 {session.Account}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { SendCardBackGalleryError(s, ex, "删除卡背失败"); }
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

    private static void OnEnterMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "请先登录" }); return; }
        if (!IsCurrentAccountSession(s))
        {
            s.IsMatching = false;
            Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "登录会话已失效，请重新连接" });
            return;
        }
        if (StatusOf(s) != "idle") { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "你正在房间、观战或对局中" }); return; }

        var deck = Str(msg, "deck") ?? "";
        var v    = DeckValidator.Validate(deck);
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"匹配拒绝 {s.Account}: {v.Reason}");
            return;
        }

        var queueKind = string.Equals(Str(msg, "queueKind"), "ranked", StringComparison.OrdinalIgnoreCase)
            ? "ranked"
            : "casual";
        if (queueKind == "ranked" && RankedStore.Default.GetSnapshot(s.Account!, s.PlayerName).Profile.Faction is null)
        {
            Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "开始排位前请先选择阵营，阵营选定后不可更改" });
            return;
        }
        s.Deck = deck;
        s.DeckName = Str(msg, "deckName");
        s.MatchQueueKind = queueKind;
        s.MatchEnqueuedAtUtc = DateTime.UtcNow;
        s.MatchRating = queueKind == "ranked"
            ? RankedStore.Default.GetMatchRating(s.Account!, s.PlayerName)
            : 1500;
        s.IsMatching = true;
        var queue = queueKind == "ranked" ? RankedMatchQueue : MatchQueue;
        queue.Enqueue(s);
        Send(s.SessionId, new { proto = "MsgEnterMatch", result = true, queueKind });
        Log($"{(queueKind == "ranked" ? "排位" : "休闲")}匹配加入 {s.Account}");
        TryMatch(queueKind);
        if (queueKind == "ranked") _ = RetryRankedMatchAsync(s);
    }

    /// <summary>单人测试模式：人类(P0,先手) vs 机器人(P1,同卡组)，立即建房</summary>
    private static void OnEnterBotMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) { Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "请先登录" }); return; }
        if (StatusOf(s) != "idle") { Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "你正在匹配、房间、观战或对局中" }); return; }

        var deck = Str(msg, "deck") ?? "";
        var deckName = Str(msg, "deckName");
        // 单人测试先后手（前端可选）：默认人类先手，仅显式 goFirst=false 时人类后手
        bool goFirst = !(msg.TryGetValue("goFirst", out var gfEl) && gfEl.ValueKind == JsonValueKind.False);
        var v = DeckValidator.Validate(deck);
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = $"卡组不合法: {v.Reason}" });
            return;
        }

        s.IsMatching = false;
        var botSid = "BOT-" + Guid.NewGuid().ToString("N")[..8];
        const string botName = "测试机器人";

        try
        {
            var room = GameRoomManager.CreateRoom(
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
                broadcastInitialState: false);
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = true });
            Send(s.SessionId, new { proto = "MsgMatchFound", opponentName = botName });
            Send(s.SessionId, new { proto = "MsgGameStart", IsFirst = goFirst });
            room.Engine.BroadcastInitialState();
            Log($"单人测试开局 {s.Account} vs 机器人");
        }
        catch (Exception ex)
        {
            LogErr($"单人测试建房失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "服务器繁忙，请稍后重试" });
        }
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
        var ranked = queueKind == "ranked";
        var queue = ranked ? RankedMatchQueue : MatchQueue;
        while (TryTakeMatchPairFromQueue(queue, ranked, out var p1, out var p2))
        {
            var deck1 = p1.Deck ?? "";
            var deck2 = p2.Deck ?? "";
            try
            {
                var room = GameRoomManager.CreateRoom(
                    p1.SessionId, p1.Account ?? "?", deck1,
                    p2.SessionId, p2.Account ?? "?", deck2,
                    p0AlwaysPrompt: p1.AlwaysPromptOnLifeReveal,
                    p1AlwaysPrompt: p2.AlwaysPromptOnLifeReveal,
                    p0CardBackId: p1.CardBackId,
                    p1CardBackId: p2.CardBackId,
                    p0SpriteMap: ResolveDeckSpriteMap(p1.Account ?? "", p1.DeckName, deck1),
                    p1SpriteMap: ResolveDeckSpriteMap(p2.Account ?? "", p2.DeckName, deck2),
                    matchKind: ranked ? MatchKind.Ranked : MatchKind.Casual,
                    broadcastInitialState: false,
                    p0DisplayName: p1.PlayerName,
                    p1DisplayName: p2.PlayerName);

                GameOpponent[p1.SessionId] = p2.SessionId;
                GameOpponent[p2.SessionId] = p1.SessionId;
                Send(p1.SessionId, new { proto = "MsgMatchFound", opponentName = p2.PlayerName ?? "?", queueKind });
                Send(p2.SessionId, new { proto = "MsgMatchFound", opponentName = p1.PlayerName ?? "?", queueKind });
                Send(p1.SessionId, new { proto = "MsgGameStart" });
                Send(p2.SessionId, new { proto = "MsgGameStart" });
                room.Engine.BroadcastInitialState();
                Log($"{(ranked ? "排位" : "休闲")}匹配成功: {p1.Account} vs {p2.Account}，等待骰点选择先后手");
            }
            catch (Exception ex)
            {
                LogErr($"创建房间失败: {ex.Message}");
                Send(p1.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "服务器繁忙，请稍后重试" });
                Send(p2.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "服务器繁忙，请稍后重试" });
            }
        }
    }

    private static async Task RetryRankedMatchAsync(WsSession session)
    {
        foreach (var delay in new[] { 15, 15, 30, 30 })
        {
            try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token); }
            catch (OperationCanceledException) { return; }
            if (!session.IsMatching || session.MatchQueueKind != "ranked") return;
            TryMatch("ranked");
        }
    }

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
                        continue;
                    }
                    var gap = Math.Abs(first.MatchRating - second.MatchRating);
                    if (ranked && gap > AllowedRankGap(first, second)) continue;
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

    private static double AllowedRankGap(WsSession first, WsSession second)
    {
        var waited = Math.Max(
            (DateTime.UtcNow - first.MatchEnqueuedAtUtc).TotalSeconds,
            (DateTime.UtcNow - second.MatchEnqueuedAtUtc).TotalSeconds);
        return waited switch { < 15 => 100, < 30 => 175, < 60 => 275, < 90 => 400, _ => double.MaxValue };
    }

    private static bool TryTakeMatchingSession(out WsSession session)
        => TryTakeMatchingSession(MatchQueue, out session);

    private static bool TryTakeMatchingSession(ConcurrentQueue<WsSession> queue, out WsSession session)
    {
        while (queue.TryDequeue(out var candidate))
        {
            if (!candidate.IsMatching || candidate.Socket.State != WebSocketState.Open) continue;
            if (!IsCurrentAccountSession(candidate))
            {
                candidate.IsMatching = false;
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
        if (session.Account is null) return false;
        lock (AccountIndexGate)
        {
            return AccountIndex.TryGetValue(session.Account, out var currentSessionId)
                   && currentSessionId == session.SessionId;
        }
    }

    private static void RebuildMatchQueue(WsSession exclude)
    {
        // ConcurrentQueue 不支持按项删除；把取消项标记为墓碑，由下一次匹配 O(1) 跳过。
        // 避免每次取消或断线都重建整条队列，形成高峰期 O(N²) 开销。
        exclude.IsMatching = false;
    }

    private static void SendRankSnapshot(WsSession session)
    {
        if (session.Account is null) return;
        try
        {
            var snapshot = RankedStore.Default.GetSnapshot(session.Account, session.PlayerName);
            Send(session.SessionId, new
            {
                proto = "MsgRankSnapshot",
                profile = RankWire.Profile(snapshot.Profile),
                leaderboard = RankWire.Leaderboard(snapshot.Leaderboard),
            });
        }
        catch (Exception ex)
        {
            LogErr($"排位资料读取失败 {session.Account}: {ex.Message}");
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
        try
        {
            var snapshot = RankedStore.Default.SelectFaction(session.Account, session.PlayerName, requested);
            if (snapshot is null)
            {
                Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "无效的阵营选择" });
                return;
            }
            if (!string.Equals(snapshot.Profile.Faction, requested, StringComparison.OrdinalIgnoreCase))
            {
                Send(session.SessionId, new { proto = "MsgSelectRankFaction", result = false, logStr = "阵营已经选定，不能更换" });
                return;
            }
            Send(session.SessionId, new
            {
                proto = "MsgSelectRankFaction",
                result = true,
                profile = RankWire.Profile(snapshot.Profile),
                leaderboard = RankWire.Leaderboard(snapshot.Leaderboard),
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
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = "请先登录" });
            return;
        }

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
        var v    = DeckValidator.Validate(deck);
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

        FriendlyRooms[roomId] = room;
        if (!FriendlyByAccount.TryAdd(s.Account!, roomId))
        {
            FriendlyRooms.TryRemove(roomId, out _);
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = "你已经在其他房间中" });
            return;
        }
        while (!PendingRooms.TryAdd(room.JoinCode!, roomId))
            room = ReplaceRoomCode(room, GenerateRoomCode());

        Send(s.SessionId, new { proto = "MsgCreateRoom", roomCode = room.JoinCode, result = true });
        PushFriendlyRoom(room);
        ScheduleRoomCodeExpiry(room);
        Log($"创建房间码友谊战 {s.Account} → {room.JoinCode} ({roomId})");
    }

    private static void OnJoinRoom(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "请先登录" });
            return;
        }

        var code = Str(msg, "roomCode")?.ToUpperInvariant() ?? "";
        var deck = Str(msg, "deck") ?? "";
        var deckName = Str(msg, "deckName") ?? "大厅所选卡组";

        if (code.Length != 6 || code.Any(ch => !RoomCodeChars.Contains(ch)))
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "房间码格式不正确" });
            return;
        }

        var v = DeckValidator.Validate(deck);
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

        string? joinError;
        WsSession? host;
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
        var loggedIn = Sessions.Values
            .Where(x => x.IsLoggedIn)
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
                    status,
                    roomId = gameRoom?.RoomId,
                    seatIndex = seatIndex is >= 0 ? seatIndex : null,
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
            var result = _playerDataStore.SendFriendRequest(s.Account, Str(msg, "toAccount") ?? "");
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
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgLeaderLeaderboard", result = false, error = "请先登录" });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetLeaderboard(Str(msg, "period"));
            Send(s.SessionId, new
            {
                proto = "MsgLeaderLeaderboard",
                result = true,
                period = snapshot.Period,
                generatedAtUtc = snapshot.GeneratedAtUtc,
                sinceUtc = snapshot.SinceUtc,
                totalMatches = snapshot.TotalMatches,
                minimumGames = snapshot.MinimumGames,
                items = snapshot.Items.Select(x => new
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
                }),
            });
        }
        catch (Exception ex)
        {
            LogErr($"读取 Leader 排行榜失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgLeaderLeaderboard", result = false, error = "排行榜暂时不可用" });
        }
    }

    private static void OnLeaderMatchups(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedPeriod = Str(msg, "period") ?? "7d";
        var requestedLeader = (Str(msg, "leaderNumber") ?? "").Trim();
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchups",
                result = false,
                period = requestedPeriod,
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
                leaderNumber = requestedLeader,
                error = "请选择有效的 Leader",
            });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetMatchups(requestedLeader, requestedPeriod);
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchups",
                result = true,
                period = snapshot.Period,
                generatedAtUtc = snapshot.GeneratedAtUtc,
                sinceUtc = snapshot.SinceUtc,
                leaderNumber = snapshot.LeaderNumber,
                items = snapshot.Items.Select(x => new
                {
                    rank = x.Rank,
                    leaderNumber = x.LeaderNumber,
                    games = x.Games,
                    wins = x.Wins,
                    losses = x.Losses,
                    winRate = x.WinRate,
                    firstGames = x.FirstGames,
                    firstWinRate = x.FirstWinRate,
                    secondGames = x.SecondGames,
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
                leaderNumber = requestedLeader,
                error = "对战统计暂时不可用",
            });
        }
    }

    private static void OnLeaderMatchupMatrix(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedPeriod = Str(msg, "period") ?? "7d";
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchupMatrix",
                result = false,
                period = requestedPeriod,
                error = "请先登录",
            });
            return;
        }

        try
        {
            var snapshot = LeaderStatsStore.Default.GetMatchupMatrix(requestedPeriod);
            Send(s.SessionId, new
            {
                proto = "MsgLeaderMatchupMatrix",
                result = true,
                period = snapshot.Period,
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
                        firstWinRate = x.FirstWinRate,
                        secondGames = x.SecondGames,
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
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgInvitePlayer", result = false, logStr = "请先登录" });
            return;
        }
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
        PendingInvites[inviteId] = new InviteInfo(
            inviteId, s.SessionId, s.Account!, s.PlayerName ?? s.Account!, toSid);

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

        // 接受:校验双方仍空闲 → 建立友谊战房间(不直接开战,房间内选卡组+准备)
        if (StatusOf(from) != "idle" || StatusOf(s) != "idle")
        {
            Send(s.SessionId,    new { proto = "MsgInviteResult", accepted = false, logStr = "一方正忙,无法进入房间" });
            Send(inv.FromSid,    new { proto = "MsgInviteResult", accepted = false, logStr = "对方正忙,房间未建立" });
            return;
        }

        if (!CreateFriendlyRoom(from, s))
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
            var room = GameRoomManager.CreateRoom(
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
                broadcastInitialState: false);
            GameOpponent[host.SessionId]  = guest.SessionId;
            GameOpponent[guest.SessionId] = host.SessionId;
            Send(host.SessionId,  new { proto = "MsgGameStart" });
            Send(guest.SessionId, new { proto = "MsgGameStart" });
            room.Engine.BroadcastInitialState();
            return room.RoomId;
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
    }

    private static void OnFriendlySelectDeck(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null) return;

        var deck = Str(msg, "deck") ?? "";
        var v = DeckValidator.Validate(deck);
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
        if (!room.TryBeginStart(out var start) || start is null) return;
        if (!TryGetActiveSession(start.HostAccount, out var host) ||
            !TryGetActiveSession(start.GuestAccount, out var guest))
        {
            room.CompleteStart(success: false);
            PushFriendlyRoom(room, "有玩家连接中断，请等待重连后重新准备");
            return;
        }

        PushFriendlyRoom(room);
        var gameRoomId = StartDuel(
            host, start.HostDeck, start.HostDeckName,
            guest, start.GuestDeck, start.GuestDeckName,
            room.RoomId, room.MatchKind);
        room.CompleteStart(gameRoomId is not null);
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
        PushFriendlyRoom(room);
    }

    /// <summary>赛前房间断线保留 30 秒等待同账号重连；对战中由正式对局的 2 分钟宽限期处理。</summary>
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
    /// MsgBugReport — 游戏内反馈：类型 + 描述 + 客户端全量信息，服务端补充权威全量快照后落盘
    /// </summary>
    private static void OnBugReport(WsSession s, Dictionary<string, JsonElement> msg)
    {
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

        var clientInfoRaw = Str(msg, "clientInfo") ?? "";

        // clientInfo 是 JSON 字符串，尝试解析为对象嵌入（失败则原样作为字符串保存）
        object? clientInfo;
        try { clientInfo = JsonSerializer.Deserialize<JsonElement>(clientInfoRaw); }
        catch { clientInfo = clientInfoRaw; }

        var room = GameRoomManager.GetRoomBySession(s.SessionId);
        int playerIndex = room is null ? -1 : Array.IndexOf(room.PlayerSessionIds, s.SessionId);
        object? serverSnapshot = room is null
            ? null
            : Game.Snapshot.PrivateStateSnapshotBuilder.Build(room.Engine.State);

        var report = new
        {
            savedAt     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            account     = s.Account ?? "",
            sessionId   = s.SessionId,
            roomId      = room?.RoomId,
            playerIndex,
            category,
            description,
            clientInfo,
            serverSnapshot,
        };

        try
        {
            var path = BugReportStore.Save(report, s.Account ?? "anon", room?.RoomId, category);
            var categoryName = category == "suggestion" ? "优化建议" : "Bug";
            Log($"{categoryName} 反馈已保存: {path}");
            Send(s.SessionId, new { proto = "MsgBugReport", result = true, path });
        }
        catch (Exception ex)
        {
            LogErr($"BugReport 保存失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgBugReport", result = false, error = ex.Message });
        }
    }

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

        var roomId = Str(msg, "roomId") ?? "";
        var viewPlayerIndex = msg.TryGetValue("viewPlayerIndex", out var viewPlayerIndexValue)
            && viewPlayerIndexValue.TryGetInt32(out var parsedViewPlayerIndex)
            && parsedViewPlayerIndex == 1
                ? 1
                : 0;
        GameRoomManager.AddSpectator(roomId, s.SessionId, viewPlayerIndex);
    }

    /// <summary>MsgLeaveSpectate — 主动退出观战</summary>
    private static void OnLeaveSpectate(WsSession s)
    {
        PostGameChats.Leave(s.SessionId);
        GameRoomManager.RemoveSpectator(s.SessionId);
    }

    /// <summary>向对战双方推送当前观战者名称列表。</summary>
    public static void BroadcastSpectatorList(GameRoomManager.RoomEntry room)
    {
        var spectators = new List<string>();
        foreach (var sid in room.Spectators.Keys)
        {
            if (!Sessions.TryGetValue(sid, out var session) || !session.IsLoggedIn) continue;
            var name = session.PlayerName ?? session.Account;
            if (!string.IsNullOrWhiteSpace(name)) spectators.Add(name);
        }
        spectators.Sort(StringComparer.OrdinalIgnoreCase);

        var packet = new { proto = "MsgSpectatorList", spectators = spectators.ToArray() };
        Send(room.PlayerSessionIds[0], packet);
        Send(room.PlayerSessionIds[1], packet);
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

    // ── 聊天 ────────────────────────────────────────────────────────────────

    private static void OnChatMsg(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) return;
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
        var pkt  = new { proto = "MsgChatMsg", type, Name = name, Msg = text };

        BroadcastAll(pkt);
    }

    /// <summary>局内聊天(房间内):预设短语 + 自由文字,只发给本对局房间的双方 + 观战者。
    /// 限频(1.2s/条)+长度上限(100)防刷屏。瞬时消息,不进对局状态/快照。区别于大厅全局 OnChatMsg(BroadcastAll)。</summary>
    private static void OnGameChat(WsSession s, Dictionary<string, JsonElement> msg)
    {
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
        foreach (var recipient in recipients) Send(recipient, pkt);
    }

    /// <summary>好友实时私聊：仅允许已建立好友关系的双方互发，消息不会广播给对局或大厅。</summary>
    private static void OnFriendChat(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn || !IsCurrentAccountSession(s))
        {
            Send(s.SessionId, new { proto = "MsgFriendChat", result = false, logStr = "请先登录" });
            return;
        }
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
            if (!_playerDataStore.AreFriends(s.Account!, toAccount))
            {
                Send(s.SessionId, new { proto = "MsgFriendChat", result = false, logStr = "只能给好友发送消息" });
                return;
            }
            if (!TryGetActiveSession(toAccount, out var target))
            {
                Send(s.SessionId, new { proto = "MsgFriendChat", result = false, logStr = "好友当前不在线" });
                return;
            }

            var packet = new
            {
                proto = "MsgFriendChat",
                result = true,
                id = Guid.NewGuid().ToString("N"),
                text,
                fromAccount = s.Account,
                fromName = s.PlayerName ?? s.Account ?? "玩家",
                toAccount = target.Account,
                toName = target.PlayerName ?? target.Account ?? "好友",
                sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            Send(s.SessionId, packet);
            Send(target.SessionId, packet);
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
        var validation = DeckValidator.Validate(deckText);
        if (!validation.Ok)
            throw new PlayerDataValidationException($"卡组不合法: {validation.Reason}");
    }

    private static void SendPlayerData(WsSession session, PlayerDataSnapshot snapshot, string? logStr = null)
    {
        Send(session.SessionId, new
        {
            proto = "MsgPlayerData",
            result = true,
            logStr,
            account = snapshot.Account,
            displayName = snapshot.DisplayName,
                avatar = snapshot.Avatar,
                cardBackId = snapshot.CardBackId,
                selectedDeckName = snapshot.SelectedDeckName,
            decks = snapshot.Decks,
        });
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
                friendsSince = friend.FriendsSince,
                online = presence.Online,
                status = presence.Status,
                roomId,
                seatIndex,
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

    private static void SendFriendError(WsSession session, string proto, Exception exception, string fallback)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new { proto, result = false, logStr = message });
    }

    private static void SendCardBackGallery(
        WsSession session,
        IReadOnlyList<CardBackGalleryItem> items,
        string? logStr = null)
    {
        var payloadItems = items.Select(item => new
        {
            id = item.Id,
            name = item.Name,
            authorName = item.AuthorName,
            imageUrl = item.ImageUrl,
            likes = item.Likes,
            liked = item.Liked,
            owned = item.Owned,
            createdAt = item.CreatedAt,
        }).ToArray();
        Send(session.SessionId, new
        {
            proto = "MsgCardBackGallery",
            result = true,
            logStr,
            items = payloadItems,
        });
    }

    private static void SendCardBackGalleryError(WsSession session, Exception exception, string fallback)
    {
        var message = exception is PlayerDataValidationException ? exception.Message : fallback;
        if (exception is not PlayerDataValidationException)
            LogErr($"{fallback} {session.Account}: {exception.Message}");
        Send(session.SessionId, new { proto = "MsgCardBackGallery", result = false, logStr = message });
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
    }

    /// <summary>权威对局清理时保留轻量赛后聊天组，并清除会话级对手索引。</summary>
    public static void OnGameRoomClosed(
        IEnumerable<string> playerSessionIds,
        IEnumerable<string> spectatorSessionIds,
        bool preservePostGameChat)
    {
        var players = playerSessionIds.ToArray();
        if (preservePostGameChat)
            PostGameChats.Register(players, spectatorSessionIds);
        foreach (var sessionId in players)
            GameOpponent.TryRemove(sessionId, out _);
    }

    // ── 发送工具 ──────────────────────────────────────────────────────────

    public static void Send(string sessionId, object data)
    {
        if (Sessions.TryGetValue(sessionId, out var s))
            EnqueueForSession(s, data);
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

    /// <summary>广播当前在线人数（已登录会话数）给所有客户端</summary>
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
                    int count = Sessions.Count(kv => kv.Value.IsLoggedIn);
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
