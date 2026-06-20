using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Validation;

namespace GrandUMI;

/// <summary>
/// GrandUMI WebSocket 网关
/// 协议：JSON over WebSocket，字段名与 C# LobbyMsg / GameMsg 完全一致
/// 不依赖任何第三方库，纯 .NET 内置 API
/// </summary>
public static class WebSocketBridge
{
    // ── 会话注册表 ────────────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, WsSession> Sessions    = new();
    private static readonly ConcurrentDictionary<string, string>    AccountIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<WsSession>              MatchQueue   = new();
    private static readonly ConcurrentDictionary<string, string>    GameOpponent = new();
    private static readonly ConcurrentDictionary<string, WsSession> PendingRooms = new(); // roomCode → 房主
    private static readonly ConcurrentDictionary<string, InviteInfo> PendingInvites = new(); // inviteId → 邀请对战
    private static readonly ConcurrentDictionary<string, FriendlyRoom> FriendlyRooms = new(); // roomId → 友谊战房间
    private static readonly ConcurrentDictionary<string, string> FriendlyByAccount = new(StringComparer.OrdinalIgnoreCase); // account → roomId
    private static readonly ConcurrentDictionary<string, DateTime> GameChatAt = new(); // sessionId → 上次局内聊天时间(限频防刷屏)

    private sealed record InviteInfo(string Id, string FromSid, string FromAccount, string FromName, string ToSid);

    private sealed class FriendlyRoom
    {
        public required string RoomId;
        public required string[] Accounts;    // [0]=host [1]=guest
        public required string[] Names;
        public string?[] Decks     = new string?[2];
        public string?[] DeckNames = new string?[2];
        public bool[]    Ready     = new bool[2];
        public int[]     Scores    = new int[2];
        public string    State     = "lobby"; // lobby(房内) / playing(对战中)
    }

    private static HttpListener?          _listener;
    private static CancellationTokenSource _cts = new();

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
    public static void Start(int port = 8080)
    {
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
        Sessions[session.SessionId] = session;
        Log($"连接 {session.SessionId}");

        await ReceiveLoop(session, ct);
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
                } while (!result.EndOfMessage);

                _ = Task.Run(() => Route(session.SessionId, sb.ToString()), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogErr($"接收 {session.SessionId}: {ex.Message}"); }
    }

    private static void CloseSession(WsSession session)
    {
        Sessions.TryRemove(session.SessionId, out _);
        if (session.Account is not null) AccountIndex.TryRemove(session.Account, out _);
        if (session.IsMatching) RebuildMatchQueue(session);
        if (session.CurrentRoomCode is not null) PendingRooms.TryRemove(session.CurrentRoomCode, out _);
        // 对局中的玩家断开 → 进入 90s 宽限期（M1）
        GameOpponent.TryRemove(session.SessionId, out _);
        GameChatAt.TryRemove(session.SessionId, out _);
        CleanupInvites(session.SessionId);
        if (session.Account is not null) HandleFriendlyDisconnect(session.Account);
        GameRoomManager.OnPlayerDisconnect(session.SessionId);
        Log($"断开 {session.SessionId} ({session.Account ?? "未登录"})");
        // 在线人数变化，广播给剩余客户端
        BroadcastOnlineCount();
    }

    // ── 消息路由 ──────────────────────────────────────────────────────────
    private static void Route(string sessionId, string json)
    {
        if (!Sessions.TryGetValue(sessionId, out var session)) return;

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
            case "MsgEnterMatch":  OnEnterMatch(session, msg);   break;
            case "MsgEnterBotMatch": OnEnterBotMatch(session, msg); break;
            case "MsgCancelMatch": OnCancelMatch(session, msg);  break;
            case "MsgCreateRoom":  OnCreateRoom(session, msg);   break;
            case "MsgJoinRoom":    OnJoinRoom(session, msg);     break;
            case "MsgCancelRoom":  OnCancelRoom(session, msg);   break;
            case "MsgPlayerList":  OnPlayerList(session);        break;
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

    // ── 匹配相关 ──────────────────────────────────────────────────────────

    private static void OnEnterMatch(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (!s.IsLoggedIn) { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false, logStr = "请先登录" }); return; }

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
                vsBot: true);
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
            bool p1First = Random.Shared.Next(2) == 0;

            // 记录对局对手关系
            GameOpponent[p1.SessionId] = p2.SessionId;
            GameOpponent[p2.SessionId] = p1.SessionId;

            // 通知匹配成功
            Send(p1.SessionId, new { proto = "MsgMatchFound", opponentName = p2.PlayerName ?? "?" });
            Send(p2.SessionId, new { proto = "MsgMatchFound", opponentName = p1.PlayerName ?? "?" });

            // MsgGameStart：客户端切换到游戏场景；具体牌面由后续 MsgGameState 推送
            Send(p1.SessionId, new { proto = "MsgGameStart", IsFirst = p1First });
            Send(p2.SessionId, new { proto = "MsgGameStart", IsFirst = !p1First });

            // 创建引擎并广播首份快照
            try
            {
                GameRoomManager.CreateRoom(
                    p1.SessionId, p1.Account ?? "?", deck1,
                    p2.SessionId, p2.Account ?? "?", deck2,
                    p0First: p1First,
                    p0AlwaysPrompt: p1.AlwaysPromptOnLifeReveal,
                    p1AlwaysPrompt: p2.AlwaysPromptOnLifeReveal);
            }
            catch (Exception ex)
            {
                LogErr($"创建房间失败: {ex.Message}");
                Send(p1.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
                Send(p2.SessionId, new { proto = "MsgDuelOver", IsWin = false, Description = "服务端错误" });
            }

            p1.IsMatching = false;
            p2.IsMatching = false;
            Log($"匹配成功: {p1.Account} vs {p2.Account} 先手={p1.Account}({p1First})");
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

        var deck = Str(msg, "deck") ?? "";
        var v    = DeckValidator.Validate(deck);
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgCreateRoom", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"创建房间拒绝 {s.Account}: {v.Reason}");
            return;
        }
        s.Deck = deck;

        // 如果已在房间中则先退出旧房间
        if (s.CurrentRoomCode is not null)
            PendingRooms.TryRemove(s.CurrentRoomCode, out _);

        var code = GenerateRoomCode();
        while (PendingRooms.ContainsKey(code)) code = GenerateRoomCode();

        s.CurrentRoomCode = code;
        PendingRooms[code] = s;

        Send(s.SessionId, new { proto = "MsgCreateRoom", roomCode = code, result = true });
        Log($"创建房间 {s.Account} → {code}");
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

        var v = DeckValidator.Validate(deck);
        if (!v.Ok)
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = $"卡组不合法: {v.Reason}" });
            Log($"加入房间拒绝 {s.Account}: {v.Reason}");
            return;
        }
        s.Deck = deck;

        if (!PendingRooms.TryRemove(code, out var host))
        {
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "房间不存在或已失效" });
            return;
        }

        if (host.SessionId == s.SessionId)
        {
            // 自己加自己（极端情况）
            PendingRooms[code] = host;
            Send(s.SessionId, new { proto = "MsgJoinRoom", result = false, logStr = "不能加入自己创建的房间" });
            return;
        }

        host.CurrentRoomCode = null;
        s.CurrentRoomCode    = null;

        // 记录对局关系
        var deck1   = host.Deck ?? "";
        var deck2   = s.Deck ?? "";
        // 通知双方加入成功(房间码流程特有,带对手名)
        Send(host.SessionId, new { proto = "MsgJoinRoom", result = true,
            opponentName = s.PlayerName ?? "?" });
        Send(s.SessionId, new { proto = "MsgJoinRoom", result = true,
            opponentName = host.PlayerName ?? "?" });

        // 建房开战(与邀请对战共用)
        StartDuel(host, deck1, s, deck2);
        Log($"房间对战: {host.Account} vs {s.Account} code={code}");
    }

    private static void OnCancelRoom(WsSession s, Dictionary<string, JsonElement> msg)
    {
        if (s.CurrentRoomCode is not null)
        {
            PendingRooms.TryRemove(s.CurrentRoomCode, out _);
            s.CurrentRoomCode = null;
        }
        Send(s.SessionId, new { proto = "MsgCancelRoom" });
        Log($"取消房间 {s.Account}");
    }

    // ── 在线玩家列表 + 邀请对战 ──────────────────────────────────────────────

    /// <summary>玩家当前状态:对战中 / 匹配中 / 空闲</summary>
    private static string StatusOf(WsSession s)
    {
        if (GameOpponent.ContainsKey(s.SessionId)) return "playing";
        if (s.Account is not null && FriendlyByAccount.ContainsKey(s.Account)) return "playing";
        if (s.IsMatching) return "matching";
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

        CreateFriendlyRoom(from, s);
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

    /// <summary>建立对局(房间码/邀请/友谊战共用):随机先后手 → 记录对手 → 双方进场 → 建房。返回对局 roomId(失败 null)</summary>
    private static string? StartDuel(WsSession host, string hostDeck, WsSession guest, string guestDeck, string? friendlyRoomId = null)
    {
        host.CurrentRoomCode  = null;
        guest.CurrentRoomCode = null;
        bool hostFirst = Random.Shared.Next(2) == 0;

        GameOpponent[host.SessionId]  = guest.SessionId;
        GameOpponent[guest.SessionId] = host.SessionId;

        // MsgGameStart：客户端切换到游戏场景；具体牌面由后续 MsgGameState 推送
        Send(host.SessionId,  new { proto = "MsgGameStart", IsFirst = hostFirst });
        Send(guest.SessionId, new { proto = "MsgGameStart", IsFirst = !hostFirst });

        try
        {
            var room = GameRoomManager.CreateRoom(
                host.SessionId,  host.Account  ?? "?", hostDeck,
                guest.SessionId, guest.Account ?? "?", guestDeck,
                p0First: hostFirst,
                p0AlwaysPrompt: host.AlwaysPromptOnLifeReveal,
                p1AlwaysPrompt: guest.AlwaysPromptOnLifeReveal,
                friendlyRoomId: friendlyRoomId);
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

    private static int FriendlyIndexOf(FriendlyRoom room, string account)
        => Array.FindIndex(room.Accounts, a => string.Equals(a, account, StringComparison.OrdinalIgnoreCase));

    private static FriendlyRoom? GetFriendlyRoomOf(WsSession s)
        => s.Account is not null && FriendlyByAccount.TryGetValue(s.Account, out var rid)
           && FriendlyRooms.TryGetValue(rid, out var room) ? room : null;

    private static object[] FriendlyPlayers(FriendlyRoom room)
    {
        var players = new object[2];
        for (int i = 0; i < 2; i++)
            players[i] = new { account = room.Accounts[i], name = room.Names[i],
                               deckName = room.DeckNames[i], ready = room.Ready[i] };
        return players;
    }

    private static void PushFriendlyRoom(FriendlyRoom room, string? error = null)
    {
        var payload = new { proto = "MsgFriendlyRoom", roomId = room.RoomId,
                            players = FriendlyPlayers(room), scores = room.Scores,
                            state = room.State, error };
        foreach (var acc in room.Accounts)
            if (AccountIndex.TryGetValue(acc, out var sid))
                Send(sid, payload);
    }

    private static void CreateFriendlyRoom(WsSession host, WsSession guest)
    {
        var roomId = Guid.NewGuid().ToString("N")[..12];
        var room = new FriendlyRoom
        {
            RoomId   = roomId,
            Accounts = new[] { host.Account!, guest.Account! },
            Names    = new[] { host.PlayerName ?? host.Account!, guest.PlayerName ?? guest.Account! },
        };
        FriendlyRooms[roomId] = room;
        FriendlyByAccount[host.Account!]  = roomId;
        FriendlyByAccount[guest.Account!] = roomId;
        PushFriendlyRoom(room);
    }

    private static void OnFriendlySelectDeck(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null || room.State != "lobby") return;
        int idx = FriendlyIndexOf(room, s.Account);
        if (idx < 0) return;

        var deck = Str(msg, "deck") ?? "";
        var v = DeckValidator.Validate(deck);
        if (!v.Ok) { PushFriendlyRoom(room, $"卡组不合法: {v.Reason}"); return; }

        room.Decks[idx]     = deck;
        room.DeckNames[idx] = Str(msg, "deckName") ?? "卡组";
        room.Ready[idx]     = false; // 换卡组需重新准备
        PushFriendlyRoom(room);
    }

    private static void OnFriendlyReady(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null || room.State != "lobby") return;
        int idx = FriendlyIndexOf(room, s.Account);
        if (idx < 0) return;

        bool ready = Bool(msg, "ready");
        if (ready && room.Decks[idx] is null) { PushFriendlyRoom(room, "请先选择卡组"); return; }
        room.Ready[idx] = ready;
        PushFriendlyRoom(room);
        TryStartFriendlyGame(room);
    }

    private static void TryStartFriendlyGame(FriendlyRoom room)
    {
        if (room.State != "lobby") return;
        if (!(room.Ready[0] && room.Ready[1])) return;
        if (room.Decks[0] is null || room.Decks[1] is null) return;
        if (!AccountIndex.TryGetValue(room.Accounts[0], out var sid0) || !Sessions.TryGetValue(sid0, out var host)) return;
        if (!AccountIndex.TryGetValue(room.Accounts[1], out var sid1) || !Sessions.TryGetValue(sid1, out var guest)) return;

        room.State    = "playing";
        room.Ready[0] = false;
        room.Ready[1] = false;
        var gameRoomId = StartDuel(host, room.Decks[0]!, guest, room.Decks[1]!, friendlyRoomId: room.RoomId);
        if (gameRoomId is null) room.State = "lobby"; // 建房失败,退回房间
        PushFriendlyRoom(room);
    }

    private static void OnFriendlyLeave(WsSession s)
    {
        var room = GetFriendlyRoomOf(s);
        if (room is null || s.Account is null) return;
        Send(s.SessionId, new { proto = "MsgFriendlyLeft", logStr = "已退出房间" });
        DisbandFriendlyRoom(room, leaverAccount: s.Account);
    }

    /// <summary>对局结束回调(GameRoomManager 调用):更新比分,双方退回房间</summary>
    public static void OnFriendlyGameEnd(string friendlyRoomId, string? winnerAccount)
    {
        if (!FriendlyRooms.TryGetValue(friendlyRoomId, out var room)) return;
        if (winnerAccount is not null)
        {
            int wi = FriendlyIndexOf(room, winnerAccount);
            if (wi >= 0) room.Scores[wi]++;
        }
        room.State    = "lobby";
        room.Ready[0] = false;
        room.Ready[1] = false;
        // 确保对手映射清理,使双方能再次开战
        foreach (var acc in room.Accounts)
            if (AccountIndex.TryGetValue(acc, out var sid))
                GameOpponent.TryRemove(sid, out _);
        PushFriendlyRoom(room);
    }

    /// <summary>断线处理:房内(lobby)断线解散房间;对战中(playing)断线交给对局宽限期,房间保留</summary>
    private static void HandleFriendlyDisconnect(string account)
    {
        if (!FriendlyByAccount.TryGetValue(account, out var roomId)) return;
        if (!FriendlyRooms.TryGetValue(roomId, out var room)) return;
        if (room.State == "playing") return;
        DisbandFriendlyRoom(room, leaverAccount: account);
    }

    private static void DisbandFriendlyRoom(FriendlyRoom room, string? leaverAccount)
    {
        FriendlyRooms.TryRemove(room.RoomId, out _);
        foreach (var acc in room.Accounts) FriendlyByAccount.TryRemove(acc, out _);
        foreach (var acc in room.Accounts)
        {
            if (string.Equals(acc, leaverAccount, StringComparison.OrdinalIgnoreCase)) continue;
            if (AccountIndex.TryGetValue(acc, out var sid))
                Send(sid, new { proto = "MsgFriendlyLeft", logStr = "对方已离开房间" });
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
    /// MsgBugReport — 游戏内 F2 反馈：描述 + 客户端全量信息，服务端补充权威全量快照后落盘
    /// </summary>
    private static void OnBugReport(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var description = Str(msg, "description") ?? "";
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
            description,
            clientInfo,
            serverSnapshot,
        };

        try
        {
            var path = BugReportStore.Save(report, s.Account ?? "anon", room?.RoomId);
            Log($"BugReport 已保存: {path}");
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
        var roomId = Str(msg, "roomId") ?? "";
        GameRoomManager.AddSpectator(roomId, s.SessionId);
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
        var name = Str(msg, "Name") ?? s.PlayerName ?? "";
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
        foreach (var spec in room.Spectators) Send(spec, pkt);
    }

    // ── 对手查找 ──────────────────────────────────────────────────────────

    private static string? GetOpponentSid(string selfSid)
        => GameOpponent.TryGetValue(selfSid, out var opp) ? opp : null;

    // ── 发送工具 ──────────────────────────────────────────────────────────

    public static void Send(string sessionId, object data)
    {
        if (Sessions.TryGetValue(sessionId, out var s))
            _ = SendAsync(s, data);
    }

    private static void BroadcastAll(object data)
    {
        foreach (var kv in Sessions) _ = SendAsync(kv.Value, data);
    }

    /// <summary>广播当前在线人数（已登录会话数）给所有客户端</summary>
    private static void BroadcastOnlineCount()
    {
        int count = Sessions.Count(kv => kv.Value.IsLoggedIn);
        BroadcastAll(new { proto = "MsgOnlineCount", count });
    }

    private static async Task SendAsync(WsSession s, object data)
    {
        if (s.Socket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(Json(data));
        await s.WriteLock.WaitAsync();
        try
        {
            await s.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) { LogErr($"Send {s.SessionId}: {ex.Message}"); }
        finally { s.WriteLock.Release(); }
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
