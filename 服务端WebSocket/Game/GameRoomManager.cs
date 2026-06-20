using System.Collections.Concurrent;
using System.Text.Json;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Snapshot;

namespace GrandUMI.Game;

/// <summary>
/// 房间池：管理活跃的 GameEngine 实例 + 会话↔房间映射 + 断线宽限期
/// </summary>
public static class GameRoomManager
{
    private const int GracePeriodSeconds = 90;

    /// <summary>对局存活上限：自最后一次操作起超过此时长即弃局（重启不恢复）。</summary>
    private static readonly TimeSpan RestoreTtl = TimeSpan.FromMinutes(30);

    /// <summary>房间池</summary>
    private static readonly ConcurrentDictionary<string, RoomEntry> _rooms = new();

    /// <summary>sessionId → roomId</summary>
    private static readonly ConcurrentDictionary<string, string> _sessionRoom = new();

    /// <summary>roomId → 断线计时器</summary>
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _grace = new();

    public class RoomEntry
    {
        public required string RoomId { get; init; }
        public required GameEngine Engine { get; init; }
        public required string[] PlayerSessionIds { get; init; }  // [P0, P1]
        public required string[] PlayerAccounts   { get; init; }
        public List<string> Spectators { get; } = new();
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public string? ReplayPath { get; set; }
        public string? MatchLogPath { get; set; }
        /// <summary>是否为单人测试模式（P1 为机器人）</summary>
        public bool VsBot { get; init; }
        /// <summary>机器人思考是否已排队（去抖）</summary>
        public bool BotScheduled { get; set; }
        /// <summary>关联的友谊战房间 ID(非友谊战为 null);对局结束时回调更新比分并退回房间</summary>
        public string? FriendlyRoomId { get; set; }
    }

    /// <summary>双方匹配/房间码成功后创建房间</summary>
    public static RoomEntry CreateRoom(string p0Sid, string p0Account, string p0Deck,
                                        string p1Sid, string p1Account, string p1Deck,
                                        bool p0First,
                                        bool p0AlwaysPrompt = false, bool p1AlwaysPrompt = false,
                                        bool vsBot = false,
                                        string? friendlyRoomId = null)
    {
        var roomId = Guid.NewGuid().ToString("N")[..12];
        var engine = new GameEngine(roomId,
            (p0Sid, p0Account, p0Deck),
            (p1Sid, p1Account, p1Deck),
            firstPlayer: p0First ? 0 : 1,
            leaderKeywordWildcard: vsBot);
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;

        var entry = new RoomEntry
        {
            RoomId = roomId,
            Engine = engine,
            PlayerSessionIds = new[] { p0Sid, p1Sid },
            PlayerAccounts   = new[] { p0Account, p1Account },
            VsBot = vsBot,
            FriendlyRoomId = friendlyRoomId,
        };

        // 配置回调：人类走 WS 下发；单人模式下 P1(机器人) 的消息驱动 BotDriver 思考
        engine.OnSendToPlayer = (idx, payload) =>
        {
            if (idx == 1 && vsBot) { BotDriver.OnBotMessage(entry); return; }
            var sid = idx == 0 ? p0Sid : p1Sid;
            WebSocketBridge.Send(sid, payload);
        };

        engine.OnSendToSpectators = (_, payload) =>
        {
            foreach (var sid in entry.Spectators)
                WebSocketBridge.Send(sid, payload);
        };
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
            firstPlayer = p0First ? 0 : 1,
            rngSeed = engine.State.RngSeed,
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
                firstPlayer = p0First ? 0 : 1,
                p0 = new { account = p0Account, deckRaw = p0Deck, alwaysPrompt = p0AlwaysPrompt },
                p1 = new { account = p1Account, deckRaw = p1Deck, alwaysPrompt = p1AlwaysPrompt },
                vsBot,
                createdAtUtc = DateTime.UtcNow,
            });
            engine.OnPersistAction = (pi, act, data) => RoomJournal.Append(roomId, pi, act, data);
        }

        _rooms[roomId] = entry;
        _sessionRoom[p0Sid] = roomId;
        _sessionRoom[p1Sid] = roomId;

        // 推送初始状态 → 进入 mulligan
        engine.BroadcastInitialState();
        return entry;
    }

    public static RoomEntry? GetRoomBySession(string sessionId)
        => _sessionRoom.TryGetValue(sessionId, out var rid) && _rooms.TryGetValue(rid, out var e) ? e : null;

    public static RoomEntry? GetRoom(string roomId)
        => _rooms.TryGetValue(roomId, out var e) ? e : null;

    /// <summary>客户端通过 MsgGameAction 派发的入口</summary>
    public static void HandleAction(string sessionId, string action, System.Text.Json.JsonElement data)
    {
        var room = GetRoomBySession(sessionId);
        if (room is null)
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "你不在任何对局中" });
            return;
        }
        int idx = Array.IndexOf(room.PlayerSessionIds, sessionId);
        if (idx < 0)
        {
            // 观战者，禁止操作
            WebSocketBridge.Send(sessionId, new { proto = "MsgActionRejected", reason = "观战者不能操作" });
            return;
        }
        room.Engine.RecordMatchLog("player_action_requested", idx, new
        {
            action,
            data,
        });
        lock (room.Engine) // 与机器人动作串行化（引擎线程不安全）
        {
            room.Engine.HandleAction(idx, action, data);
        }
        if (room.Engine.State.IsGameOver)
        {
            CleanupRoom(room.RoomId);
        }
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
        if (idx < 0)
        {
            // 观战者重连
            WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(room.Engine.State, -1, "Resync"));
            return;
        }
        // 取消宽限期
        if (_grace.TryRemove(room.RoomId + ":" + sessionId, out var cts)) cts.Cancel();
        // 通知对手已重连
        var oppSid = room.PlayerSessionIds[1 - idx];
        WebSocketBridge.Send(oppSid, new { proto = "MsgPlayerReconnected" });
        // 重新发完整状态
        WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(room.Engine.State, idx, "Resync"));
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
            room.Spectators.Remove(sessionId);
            _sessionRoom.TryRemove(sessionId, out _);
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
            r.Engine.State.WinnerIndex = 1 - idx;
            r.Engine.State.GameOverReason = $"{r.PlayerAccounts[idx]} 断线超时";
            r.Engine.Broadcast("DisconnectTimeout", new { disconnected = idx });
            CleanupRoom(room.RoomId);
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

        lock (room.Engine)
        {
            // 判对手负：设 WinnerIndex → IsGameOver 自动为真 → 广播带 isGameOver 的快照（前端据此显示结束画面）
            if (!room.Engine.State.IsGameOver)
            {
                room.Engine.State.WinnerIndex = idx;
                room.Engine.State.GameOverReason = $"{room.PlayerAccounts[oppIdx]} 断线，对手确认结束对局";
                room.Engine.Broadcast("DisconnectTimeout", new { disconnected = oppIdx });
            }
        }
        CleanupRoom(room.RoomId); // 删恢复日志 + 友谊战回房（OnFriendlyGameEnd）；TryRemove 幂等安全
    }

    /// <summary>断线玩家在宽限期内重新连接（同账号新 sessionId）</summary>
    public static bool TryReclaim(string newSessionId, string accountName)
    {
        // 找到匹配 accountName 的房间
        foreach (var kv in _rooms)
        {
            var r = kv.Value;
            for (int i = 0; i < 2; i++)
            {
                if (r.PlayerAccounts[i] == accountName)
                {
                    var oldSid = r.PlayerSessionIds[i];
                    if (oldSid == newSessionId) return false; // 同 sid 不算重连
                    // 取消宽限期
                    if (_grace.TryRemove(r.RoomId + ":" + oldSid, out var cts)) cts.Cancel();
                    // 替换 session
                    _sessionRoom.TryRemove(oldSid, out _);
                    r.PlayerSessionIds[i] = newSessionId;
                    _sessionRoom[newSessionId] = r.RoomId;
                    // 重新绑定引擎回调（PlayerIndex 编号未变，sid 已替换）
                    var newSid0 = r.PlayerSessionIds[0];
                    var newSid1 = r.PlayerSessionIds[1];
                    r.Engine.OnSendToPlayer = (idx, payload) =>
                    {
                        var sid = idx == 0 ? newSid0 : newSid1;
                        WebSocketBridge.Send(sid, payload);
                    };
                    var oppSid = r.PlayerSessionIds[1 - i];
                    WebSocketBridge.Send(oppSid, new { proto = "MsgPlayerReconnected" });
                    // 给重连方发完整快照
                    WebSocketBridge.Send(newSessionId, StateSnapshotBuilder.Build(r.Engine.State, i, "Resync"));
                    return true;
                }
            }
        }
        return false;
    }

    public static void AddSpectator(string roomId, string sessionId)
    {
        if (!_rooms.TryGetValue(roomId, out var r))
        {
            WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = false, logStr = "房间不存在" });
            return;
        }
        r.Spectators.Add(sessionId);
        _sessionRoom[sessionId] = roomId;
        WebSocketBridge.Send(sessionId, new { proto = "MsgSpectateRoom", result = true, roomId });
        WebSocketBridge.Send(sessionId, StateSnapshotBuilder.Build(r.Engine.State, -1, "SpectateJoin"));
    }

    public static void CleanupRoom(string roomId)
    {
        if (_rooms.TryRemove(roomId, out var r))
        {
            foreach (var sid in r.PlayerSessionIds) _sessionRoom.TryRemove(sid, out _);
            foreach (var sid in r.Spectators)        _sessionRoom.TryRemove(sid, out _);
            r.Engine.RecordMatchLog("match_end", -1, new
            {
                winnerIndex = r.Engine.State.WinnerIndex,
                reason = r.Engine.State.GameOverReason,
                turnCount = r.Engine.State.TurnCount,
                finalTick = r.Engine.State.Tick,
            });
            ReplayRecorder.Close(roomId);
            MatchLogRecorder.Close(roomId);
            RoomJournal.Delete(roomId); // 对局结束 → 删除恢复日志，避免恢复已结束的局

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
        var p0          = h.GetProperty("p0");
        var p1          = h.GetProperty("p1");
        var p0Account   = p0.GetProperty("account").GetString()!;
        var p1Account   = p1.GetProperty("account").GetString()!;
        var p0Deck      = p0.GetProperty("deckRaw").GetString()!;
        var p1Deck      = p1.GetProperty("deckRaw").GetString()!;
        var p0Always    = p0.TryGetProperty("alwaysPrompt", out var a0) && a0.GetBoolean();
        var p1Always    = p1.TryGetProperty("alwaysPrompt", out var a1) && a1.GetBoolean();

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
            p0AlwaysPrompt: p0Always, p1AlwaysPrompt: p1Always);

        if (engine.State.IsGameOver) { TryDelete(file); return false; } // 已分胜负不恢复

        // 构造房间放回池：sid 用占位（真实 sid 由玩家重登时 TryReclaim 替换）
        var entry = new RoomEntry
        {
            RoomId = roomId,
            Engine = engine,
            PlayerSessionIds = new[] { "offline-0", "offline-1" },
            PlayerAccounts   = new[] { p0Account, p1Account },
            VsBot = false,
        };

        // 重新挂回回调（按当前 sid 发；日志/录像/动作日志均"续写"而非覆盖）
        engine.OnSendToPlayer = (idx, payload) =>
            WebSocketBridge.Send(entry.PlayerSessionIds[idx], payload);
        engine.OnSendToSpectators = (_, payload) =>
        {
            foreach (var sid in entry.Spectators) WebSocketBridge.Send(sid, payload);
        };
        entry.ReplayPath   = ReplayRecorder.OpenAppend(roomId);
        entry.MatchLogPath = MatchLogRecorder.OpenAppend(roomId);
        engine.OnReplay        = (entryObj) => ReplayRecorder.Append(roomId, entryObj);
        engine.OnMatchLog      = (kind, actor, payload) => MatchLogRecorder.Append(roomId, engine.State, kind, actor, payload);
        engine.OnPersistAction = (pi, act, data) => RoomJournal.Append(roomId, pi, act, data);
        RoomJournal.Reopen(roomId); // 续写新动作到同一文件（不重写 header）

        _rooms[roomId] = entry;
        // 不加 _sessionRoom（占位 sid 无意义）；不调 BroadcastInitialState（无人在线，静默重建）
        Console.WriteLine($"[Restore] 已恢复对局 {roomId}（{p0Account} vs {p1Account}，回放 {actions.Count} 个动作）。");
        return true;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
