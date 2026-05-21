using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

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
        // 通知对局中的对手
        if (GameOpponent.TryRemove(session.SessionId, out var oppSid))
        {
            GameOpponent.TryRemove(oppSid, out _);
            Send(oppSid, new { proto = "MsgDuelOver", IsWin = true, Description = "对手断开连接" });
        }
        Log($"断开 {session.SessionId} ({session.Account ?? "未登录"})");
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
            case "MsgCancelMatch": OnCancelMatch(session, msg);  break;
            case "MsgCreateRoom":  OnCreateRoom(session, msg);   break;
            case "MsgJoinRoom":    OnJoinRoom(session, msg);     break;
            case "MsgCancelRoom":  OnCancelRoom(session, msg);   break;
            case "MsgTransmit":    OnTransmit(session, msg);     break;
            case "MsgSurrender":   OnSurrender(session);         break;
            case "MsgChatMsg":     OnChatMsg(session, msg);      break;
            // Sprint 3: 服务端结算协议
            case "MsgGameAction":  OnGameAction(session, msg);   break;
            case "MsgRequestState": OnRequestState(session);     break;
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
        var password = Str(msg, "password") ?? "";

        // ── TODO: 替换为真实的账户验证逻辑 ──────────────────────────────
        // bool ok = AccountDB.Verify(account, password, out string name);
        bool ok   = account.Length > 0 && password.Length > 0;
        var  name = ok ? account : "";
        // ────────────────────────────────────────────────────────────────

        if (ok)
        {
            s.Account    = account;
            s.PlayerName = name;
            AccountIndex[account] = s.SessionId;
        }

        Send(s.SessionId, new { proto = "MsgLogin", account, name, result = ok,
                                logStr = ok ? "登录成功" : "账号或密码错误" });
        Log($"登录 {(ok ? "✅" : "❌")} {account}");
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
        if (!s.IsLoggedIn) { Send(s.SessionId, new { proto = "MsgEnterMatch", result = false }); return; }

        s.Deck       = Str(msg, "deck") ?? "";
        s.IsMatching = true;
        MatchQueue.Enqueue(s);
        Send(s.SessionId, new { proto = "MsgEnterMatch", result = true });
        Log($"匹配加入 {s.Account}");
        TryMatch();
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

            // 开始游戏
            Send(p1.SessionId, new { proto = "MsgGameStart", MainDeck = deck1,
                                     EnemyDeck = deck2, IsFirst = p1First });
            Send(p2.SessionId, new { proto = "MsgGameStart", MainDeck = deck2,
                                     EnemyDeck = deck1, IsFirst = !p1First });

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

        s.Deck = Str(msg, "deck") ?? "";

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
        s.Deck   = Str(msg, "deck") ?? "";

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
        bool hostFirst = Random.Shared.Next(2) == 0;

        GameOpponent[host.SessionId] = s.SessionId;
        GameOpponent[s.SessionId]    = host.SessionId;

        // 通知双方
        Send(host.SessionId, new { proto = "MsgJoinRoom", result = true,
            opponentName = s.PlayerName ?? "?" });
        Send(s.SessionId, new { proto = "MsgJoinRoom", result = true,
            opponentName = host.PlayerName ?? "?" });

        // 开始游戏
        Send(host.SessionId, new { proto = "MsgGameStart", MainDeck = deck1,
            EnemyDeck = deck2, IsFirst = hostFirst });
        Send(s.SessionId, new { proto = "MsgGameStart", MainDeck = deck2,
            EnemyDeck = deck1, IsFirst = !hostFirst });

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

    // ── Sprint 3: 游戏动作与状态同步 ────────────────────────────────────────

    /// <summary>
    /// MsgGameAction — 客户端游戏动作（新协议，替代 MsgTransmit 字符串透传）
    /// 当前阶段：服务器不验证游戏规则，仅转发给对手
    /// 后续阶段：服务器验证动作合法性 → 自行结算 → BroadcastGameState 推送权威状态
    /// </summary>
    private static void OnGameAction(WsSession s, Dictionary<string, JsonElement> msg)
    {
        var opponentSid = GetOpponentSid(s.SessionId);
        if (opponentSid is null)
        {
            Send(s.SessionId, new { proto = "MsgGameAction", error = "无对局对手" });
            return;
        }

        var action = Str(msg, "action") ?? "";
        // 转发动作给对手（当前阶段仅做消息中继）
        // TODO: 后续实现服务器端验证与结算
        Send(opponentSid, new
        {
            proto = "MsgGameAction",
            action,
            data = msg.TryGetValue("data", out var d) ? d : default,
        });
        Log($"GameAction {s.Account} → 对手: {action}");
    }

    /// <summary>
    /// MsgRequestState — 客户端重连后请求完整游戏状态快照
    /// 当前阶段：通知对手重连，由对手客户端发送当前状态
    /// 后续阶段：服务器缓存房间状态，直接回复 MsgGameState
    /// </summary>
    private static void OnRequestState(WsSession s)
    {
        var opponentSid = GetOpponentSid(s.SessionId);
        if (opponentSid is null)
        {
            Send(s.SessionId, new { proto = "MsgDuelOver", IsWin = false,
                Description = "对局已结束，无法恢复" });
            return;
        }

        // 通知对手：对方已重连，请重新发送当前状态
        Send(opponentSid, new { proto = "MsgPlayerReconnected" });
        Send(s.SessionId, new { proto = "MsgPlayerReconnected" });
        Log($"RequestState {s.Account} → 对手重连通知已发送");
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
