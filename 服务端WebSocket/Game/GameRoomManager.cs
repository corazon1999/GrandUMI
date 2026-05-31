using System.Collections.Concurrent;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Snapshot;

namespace GrandUMI.Game;

/// <summary>
/// 房间池：管理活跃的 GameEngine 实例 + 会话↔房间映射 + 断线宽限期
/// </summary>
public static class GameRoomManager
{
    private const int GracePeriodSeconds = 90;

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
    }

    /// <summary>双方匹配/房间码成功后创建房间</summary>
    public static RoomEntry CreateRoom(string p0Sid, string p0Account, string p0Deck,
                                        string p1Sid, string p1Account, string p1Deck,
                                        bool p0First,
                                        bool p0AlwaysPrompt = false, bool p1AlwaysPrompt = false)
    {
        var roomId = Guid.NewGuid().ToString("N")[..12];
        var engine = new GameEngine(roomId,
            (p0Sid, p0Account, p0Deck),
            (p1Sid, p1Account, p1Deck),
            firstPlayer: p0First ? 0 : 1);
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;

        // 配置回调
        engine.OnSendToPlayer = (idx, payload) =>
        {
            var sid = idx == 0 ? p0Sid : p1Sid;
            WebSocketBridge.Send(sid, payload);
        };

        var entry = new RoomEntry
        {
            RoomId = roomId,
            Engine = engine,
            PlayerSessionIds = new[] { p0Sid, p1Sid },
            PlayerAccounts   = new[] { p0Account, p1Account },
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
        room.Engine.HandleAction(idx, action, data);
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
        }
    }
}
