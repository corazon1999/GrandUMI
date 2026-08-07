using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GrandUMI.Diagnostics;
using GrandUMI.Game;
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
    private const int MaxInboundMessageChars = 1_000_000;
    private static readonly HashSet<string> NonReplaceableStateActions = new(StringComparer.Ordinal)
    {
        "GameStart", "Resync", "SpectateJoin", "FirstPlayerChosen",
        "Prompt", "PromptTimeout", "RevealCards",
        "Attack", "AwaitBlock", "AwaitCounter", "DeclareBlocker", "CounterIcon", "PlayCard",
        "MulliganComplete", "MulliganUpdate", "DuelOver", "Surrender", "DisconnectTimeout",
    };
    // ── 会话注册表 ────────────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, WsSession> Sessions    = new();
    private static readonly ConcurrentDictionary<string, string>    AccountIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object                                  AccountIndexGate = new();
    private static readonly ConcurrentQueue<WsSession>              MatchQueue   = new();
    private static readonly ConcurrentDictionary<string, string>    GameOpponent = new();
    private static readonly ConcurrentDictionary<string, string>    PendingRooms = new(); // roomCode → 赛前房间ID
    private static readonly ConcurrentDictionary<string, InviteInfo> PendingInvites = new(); // inviteId → 邀请对战
    private static readonly ConcurrentDictionary<string, DuelLobby> FriendlyRooms = new(); // roomId → 共用赛前房间
    private static readonly ConcurrentDictionary<string, string> FriendlyByAccount = new(StringComparer.OrdinalIgnoreCase); // account → roomId
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> FriendlyDisconnectGrace = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> GameChatAt = new(); // sessionId → 上次局内聊天时间(限频防刷屏)
    private static readonly TimeSpan LobbyReconnectGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveSessionMaxIdle = TimeSpan.FromSeconds(35);

    private sealed record InviteInfo(string Id, string FromSid, string FromAccount, string FromName, string ToSid);

    private static HttpListener?          _listener;
    private static CancellationTokenSource _cts = new();
    private static PlayerDataStore _playerDataStore = null!;

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

    private static string Json(object obj) => JsonSerializer.Serialize(obj, JsonOpts);

    // ── 生命周期 ──────────────────────────────────────────────────────────
    public static void Start(int port, PlayerDataStore playerDataStore)
    {
        _playerDataStore = playerDataStore ?? throw new ArgumentNullException(nameof(playerDataStore));
        _cts      = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/ws/");
        _listener.Start();
        _ = AcceptLoop(_cts.Token);
        Log($"监听 ws://localhost:{port}/ws/");
    }

    public static void Stop()
    {
        _cts.Cancel();
        _listener?.Stop();
    }

    // ── 连接接受 ──────────────────────────────────────────────────────────
    private static async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener!.GetContextAsync();
                if (ctx.Request.IsWebSocketRequest)
                    _ = HandleClient(ctx, ct);
                else { ctx.Response.StatusCode = 400; ctx.Response.Close(); }
            }
            catch when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { LogErr($"AcceptLoop: {ex.Message}"); }
        }
    }

    private static async Task HandleClient(HttpListenerContext ctx, CancellationToken ct)
    {
        WebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(null); }
        catch (Exception ex) { LogErr($"握手失败: {ex.Message}"); return; }

        var session = new WsSession { Socket = wsCtx.WebSocket };
        session.StartSender(message => SendDirectAsync(session, message));
        Sessions[session.SessionId] = session;
        Log($"连接 {session.SessionId}");

        await ReceiveLoop(session, ct);
        await session.StopSenderAsync();
        CloseSession(session);
    }

    private static async Task ReceiveLoop(WsSession session, CancellationToken ct)
    {
        var buffer = new byte[32768];
        var ws     = session.Socket;
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var sb     = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (sb.Length > MaxInboundMessageChars)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "消息体过大", CancellationToken.None);
                        return;
                    }
                } while (!result.EndOfMessage);

                // 单连接按接收顺序路由；游戏动作进入房间队列后会立即返回，
                // 无需再为每条消息创建可能乱序的独立线程池任务。
                Route(session.SessionId, sb.ToString());
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
        CleanupInvites(session.SessionId);
        if (session.Account is not null && wasCurrentAccountSession)
            HandleFriendlyDisconnect(session.Account);
        GameRoomManager.OnPlayerDisconnect(session.SessionId);
        Log($"断开 {session.SessionId} ({session.Account ?? "未登录"})");
        // 在线人数变化，广播给剩余客户端
        BroadcastOnlineCount();
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

        Log($"← {proto,-20} ({session.Account ?? session.SessionId[..8]})");

        switch (proto)
        {
            case "MsgSecret":      OnSecret(session, msg);      break;
            case "MsgPing":        OnPing(session);              break;
            case "MsgLogin":       OnLogin(session, msg);        break;
            case "MsgAddAccount":  OnAddAccount(session, msg);   break;
            case "MsgUpdatePs":    OnUpdatePs(session, msg);     break;
            case "MsgSaveDeck":    OnSaveDeck(session, msg);     break;
            case "MsgDeleteDeck":  OnDeleteDeck(session, msg);   break;
            case "MsgSelectDeck":  OnSelectDeck(session, msg);   break;
            case "MsgUpdateProfile": OnUpdateProfile(session, msg); break;
            case "MsgImportDecks": OnImportDecks(session, msg);  break;
            case "MsgEnterMatch":  OnEnterMatch(session, msg);   break;
            case "MsgEnterBotMatch": OnEnterBotMatch(session, msg); break;
            case "MsgCancelMatch": OnCancelMatch(session, msg);  break;
            case "MsgCreateRoom":  OnCreateRoom(session, msg);   break;
            case "MsgJoinRoom":    OnJoinRoom(session, msg);     break;
            case "MsgCancelRoom":  OnCancelRoom(session, msg);   break;
            case "MsgPlayerList":  OnPlayerList(session);        break;
            case "MsgLeaderLeaderboard": OnLeaderLeaderboard(session, msg); break;
            case "MsgLeaderMatchups": OnLeaderMatchups(session, msg); break;
            case "MsgInvitePlayer": OnInvitePlayer(session, msg); break;
            case "MsgInviteResponse": OnInviteResponse(session, msg); break;
            case "MsgFriendlySelectDeck": OnFriendlySelectDeck(session, msg); break;
            case "MsgFriendlyReady": OnFriendlyReady(session, msg); break;
            case "MsgFriendlyLeave": OnFriendlyLeave(session); break;
            case "MsgTransmit":    OnTransmit(session, msg);     break;
            case "MsgSurrender":   OnSurrender(session);         break;
            case "MsgChatMsg":     OnChatMsg(session, msg);      break;
            case "MsgGameChat":    OnGameChat(session, msg);     break;
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
        Send(s.SessionId, new { proto = "MsgSecret", Secret = "", result = true, vesion = "0.998" });
    }

    private static void OnPing(WsSession s)
        => Send(s.SessionId, new { proto = "MsgPing" });

    private static void OnLogin(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var requestedAccount = Str(msg, "account") ?? "";
        try
        {
            var playerData = _playerDataStore.Login(requestedAccount);

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
                AccountIndex[playerData.Account] = s.SessionId;
            }

            Send(s.SessionId, new
            {
                proto = "MsgLogin",
                account = playerData.Account,
                name = playerData.DisplayName,
                avatar = playerData.Avatar,
                selectedDeckName = playerData.SelectedDeckName,
                decks = playerData.Decks,
                result = true,
                logStr = "登录成功",
            });
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
            if (GameRoomManager.TryReclaim(s.SessionId, playerData.Account))
                Log($"断线重连成功 {playerData.Account}");
            else
                TryRestoreFriendlyRoom(s, playerData.Account);

            BroadcastOnlineCount();
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
        // TODO: 修改密码逻辑
        Send(s.SessionId, new { proto = "MsgUpdatePs", result = true, logStr = "密码修改成功" });
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

    // ── 匹配相关 ──────────────────────────────────────────────────────────

    private static void OnEnterMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "请先登录" }); return; }
        if (StatusOf(s) != "idle") { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "你正在房间、观战或对局中" }); return; }

        var deck = Str(msg, "deck") ?? "";
        var v    = DeckValidator.Validate(deck);
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"匹配拒绝 {s.Account}: {v.Reason}");
            return;
        }

        s.Deck       = deck;
        s.IsMatching = true;
        MatchQueue.Enqueue(s);
        Send(s.SessionId, new { proto = "MsgEnterMatch", result = true });
        Log($"匹配加入 {s.Account}");
        TryMatch();
    }

    /// <summary>单人测试模式：人类(P0,先手) vs 机器人(P1,同卡组)，立即建房</summary>
    private static void OnEnterBotMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) { Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "请先登录" }); return; }
        if (StatusOf(s) != "idle") { Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = false, logStr = "你正在匹配、房间、观战或对局中" }); return; }

        var deck = Str(msg, "deck") ?? "";
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

        Send(s.SessionId, new { proto = "MsgEnterBotMatch", result = true });
        Send(s.SessionId, new { proto = "MsgMatchFound", opponentName = botName });
        Send(s.SessionId, new { proto = "MsgGameStart", IsFirst = goFirst });

        try
        {
            GameRoomManager.CreateRoom(
                s.SessionId, s.Account ?? "玩家", deck,
                botSid, botName, deck,        // 机器人用同一套卡组
                p0First: goFirst,             // 单人测试先后手（前端可选，默认先手）
                p0AlwaysPrompt: s.AlwaysPromptOnLifeReveal,
                p1AlwaysPrompt: false,
                vsBot: true,
                matchKind: MatchKind.Bot);
            Log($"单人测试开局 {s.Account} vs 机器人");
        }
        catch (Exception ex)
        {
            LogErr($"单人测试建房失败: {ex.Message}");
            Send(s.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
        }
    }

    private static void OnCancelMatch(WsSession s, Dictionary<string, JsonElement> _)
    {
        s.IsMatching = false;
        s.Deck       = null;
        RebuildMatchQueue(s);
        Send(s.SessionId, new { proto = "MsgCancelMatch" });
        Log($"匹配取消 {s.Account}");
    }

    private static void TryMatch()
    {
        while (MatchQueue.Count >= 2)
        {
            if (!MatchQueue.TryDequeue(out var p1) || !MatchQueue.TryDequeue(out var p2))
                break;

            if (!p1.IsMatching || !p2.IsMatching) continue;

            var deck1 = p1.Deck ?? "";
            var deck2 = p2.Deck ?? "";
            // 记录对局对手关系
            GameOpponent[p1.SessionId] = p2.SessionId;
            GameOpponent[p2.SessionId] = p1.SessionId;

            // 通知匹配成功
            Send(p1.SessionId, new { proto = "MsgMatchFound", opponentName = p2.PlayerName ?? "?" });
            Send(p2.SessionId, new { proto = "MsgMatchFound", opponentName = p1.PlayerName ?? "?" });

            // MsgGameStart：客户端切换到游戏场景；具体牌面由后续 MsgGameState 推送
            Send(p1.SessionId, new { proto = "MsgGameStart" });
            Send(p2.SessionId, new { proto = "MsgGameStart" });

            // 创建引擎并广播首份快照
            try
            {
                GameRoomManager.CreateRoom(
                    p1.SessionId, p1.Account ?? "?", deck1,
                    p2.SessionId, p2.Account ?? "?", deck2,
                    p0AlwaysPrompt: p1.AlwaysPromptOnLifeReveal,
                    p1AlwaysPrompt: p2.AlwaysPromptOnLifeReveal,
                    matchKind: MatchKind.Matchmaking);
            }
            catch (Exception ex)
            {
                LogErr($"创建房间失败: {ex.Message}");
                Send(p1.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
                Send(p2.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
            }

            p1.IsMatching = false;
            p2.IsMatching = false;
            Log($"匹配成功: {p1.Account} vs {p2.Account}，等待骰点选择先后手");
        }
    }

    private static void RebuildMatchQueue(WsSession exclude)
    {
        var remaining = new ConcurrentQueue<WsSession>();
        while (MatchQueue.TryDequeue(out var s))
            if (s.SessionId != exclude.SessionId && s.IsMatching)
                remaining.Enqueue(s);
        while (remaining.TryDequeue(out var s))
            MatchQueue.Enqueue(s);
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

    private static void OnPlayerList(WsSession s)
    {
        if (!s.IsLoggedIn)
        {
            Send(s.SessionId, new { proto = "MsgPlayerList", players = Array.Empty<object>() });
            return;
        }
        var players = Sessions.Values
            .Where(x => x.IsLoggedIn)
            .Select(x =>
            {
                var status = StatusOf(x);
                // 仅对战中玩家附带其所在对局房间ID，供前端一键观战；
                // 友谊战房内(lobby)虽也判为 playing，但尚无对局房间，GetRoomBySession 返回 null。
                var roomId = status == "playing"
                    ? GameRoomManager.GetRoomBySession(x.SessionId)?.RoomId
                    : null;
                return new { account = x.Account, name = x.PlayerName ?? x.Account, status, roomId };
            })
            .ToArray();
        Send(s.SessionId, new { proto = "MsgPlayerList", players });
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
    private static string? StartDuel(
        WsSession host,
        string hostDeck,
        WsSession guest,
        string guestDeck,
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
        var gameRoomId = StartDuel(host, start.HostDeck, guest, start.GuestDeck, room.RoomId, room.MatchKind);
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
        var action = Str(msg, "action") ?? "";
        var data   = msg.TryGetValue("data", out var d) ? d : default;
        GameRoomManager.HandleAction(s.SessionId, action, data);
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
        GameRoomManager.AddSpectator(roomId, s.SessionId);
    }

    /// <summary>MsgLeaveSpectate — 主动退出观战</summary>
    private static void OnLeaveSpectate(WsSession s)
    {
        GameRoomManager.RemoveSpectator(s.SessionId);
    }

    /// <summary>MsgPromptResponse — 玩家响应服务端 prompt</summary>
    private static void OnPromptResponse(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var data = JsonSerializer.SerializeToElement(msg);
        GameRoomManager.HandleAction(s.SessionId, "PromptResponse", data);
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
        int type = msg.TryGetValue("type", out var t) ? t.GetInt32() : 0;
        var name = s.PlayerName ?? s.Account ?? "";
        var text = Str(msg, "Msg")  ?? "";
        var pkt  = new { proto = "MsgChatMsg", type, Name = name, Msg = text };

        BroadcastAll(pkt);
    }

    /// <summary>局内聊天(房间内):预设短语 + 自由文字,只发给本对局房间的双方 + 观战者。
    /// 限频(1.2s/条)+长度上限(100)防刷屏。瞬时消息,不进对局状态/快照。区别于大厅全局 OnChatMsg(BroadcastAll)。</summary>
    private static void OnGameChat(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GameRoomManager.GetRoomBySession(s.SessionId);
        if (room is null) return;

        var now = DateTime.UtcNow;
        if (GameChatAt.TryGetValue(s.SessionId, out var last) && (now - last).TotalMilliseconds < 1200) return;

        var text = (Str(msg, "Text") ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 100) text = text[..100];   // 长度上限
        var code = Str(msg, "Code");                  // 预设短语编号(可空,仅供客户端样式)

        GameChatAt[s.SessionId] = now;

        int seat = Array.IndexOf(room.PlayerSessionIds, s.SessionId); // 0/1=玩家, -1=观战
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
        Send(room.PlayerSessionIds[0], pkt);
        Send(room.PlayerSessionIds[1], pkt);
        foreach (var spec in room.Spectators.Keys) Send(spec, pkt);
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
            selectedDeckName = snapshot.SelectedDeckName,
            decks = snapshot.Decks,
        });
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

    /// <summary>权威对局清理时同步清除会话级对手索引，避免玩家赛后仍显示忙碌。</summary>
    public static void OnGameRoomClosed(IEnumerable<string> sessionIds)
    {
        foreach (var sessionId in sessionIds)
            GameOpponent.TryRemove(sessionId, out _);
    }

    // ── 发送工具 ──────────────────────────────────────────────────────────

    public static void Send(string sessionId, object data)
    {
        if (Sessions.TryGetValue(sessionId, out var s))
            s.Enqueue(data, IsReplaceableStateSnapshot(data));
    }

    private static void BroadcastAll(object data)
    {
        var isStateSnapshot = IsReplaceableStateSnapshot(data);
        foreach (var kv in Sessions) kv.Value.Enqueue(data, isStateSnapshot);
    }

    /// <summary>广播当前在线人数（已登录会话数）给所有客户端</summary>
    private static void BroadcastOnlineCount()
    {
        int count = Sessions.Count(kv => kv.Value.IsLoggedIn);
        BroadcastAll(new { proto = "MsgOnlineCount", count });
    }

    private static bool IsReplaceableStateSnapshot(object data)
    {
        var type = data.GetType();
        if (!string.Equals(type.GetProperty("proto")?.GetValue(data) as string, "MsgGameState", StringComparison.Ordinal))
            return false;
        var lastAction = type.GetProperty("lastAction")?.GetValue(data) as string ?? "";
        return !NonReplaceableStateActions.Contains(lastAction);
    }

    private static async Task SendDirectAsync(WsSession s, WsSession.OutboundMessage message)
    {
        if (s.Socket.State != WebSocketState.Open) return;
        var totalStartedAt = LatencyDiagnostics.Start();
        var serializeStartedAt = totalStartedAt;
        var bytes = Encoding.UTF8.GetBytes(Json(message.Data));
        LatencyDiagnostics.Observe("WebSocket 序列化", serializeStartedAt, $"会话={s.SessionId[..8]}，字节={bytes.Length}");
        try
        {
            using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await s.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, sendTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            LogWarn($"Send {s.SessionId}: 超过 5 秒，终止慢连接");
            s.Socket.Abort();
        }
        catch (Exception ex) { LogErr($"Send {s.SessionId}: {ex.Message}"); }
        LatencyDiagnostics.Observe("WebSocket 发送总耗时", totalStartedAt, $"会话={s.SessionId[..8]}，字节={bytes.Length}");
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
