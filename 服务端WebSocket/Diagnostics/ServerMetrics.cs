using System.Diagnostics;
using System.Globalization;
using System.Text;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Effects.Rules;
using GrandUMI.Persistence;

namespace GrandUMI.Diagnostics;

public static class ServerMetrics
{
    public static string RenderPrometheus(PlayerDataStore playerDataStore)
    {
        using var process = Process.GetCurrentProcess();
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        var output = new StringBuilder(LatencyDiagnostics.ExportPrometheus());

        Gauge(output, "grandumi_process_uptime_seconds", Environment.TickCount64 / 1_000d);
        Gauge(output, "grandumi_process_working_set_bytes", process.WorkingSet64);
        Gauge(output, "grandumi_gc_heap_bytes", GC.GetTotalMemory(forceFullCollection: false));
        Gauge(output, "grandumi_threadpool_busy_workers", maxWorkers - availableWorkers);
        Gauge(output, "grandumi_threadpool_busy_io", maxIo - availableIo);
        var sessions = WebSocketBridge.GetSessionMetrics();
        Gauge(output, "grandumi_connections", sessions.ConnectionCount);
        Gauge(output, "grandumi_logged_in_sessions", sessions.LoggedInSessionCount);
        // 保留原指标名兼容现有看板，但语义修正为账号索引中的唯一权威在线玩家。
        Gauge(output, "grandumi_logged_in_players", sessions.UniqueLoggedInAccountCount);
        Gauge(output, "grandumi_unique_logged_in_accounts", sessions.UniqueLoggedInAccountCount);
        Gauge(output, "grandumi_superseded_sessions", sessions.SupersededSessionCount);
        Counter(output, "grandumi_superseded_sessions_total", sessions.SupersededSessionTotal);
        Gauge(output, "grandumi_rooms", GameRoomManager.RoomCount);
        Gauge(output, "grandumi_spectators", GameRoomManager.SpectatorCount);
        Gauge(output, "grandumi_room_action_queue_depth", GameRoomManager.TotalActionQueueDepth);
        Gauge(output, "grandumi_websocket_max_queue_depth", WebSocketBridge.MaxCurrentOutboundDepth);
        Counter(output, "grandumi_websocket_dropped_messages_total", WebSocketBridge.DroppedOutboundCount);
        Gauge(output, "grandumi_room_journal_queue_depth", RoomJournal.QueueDepth);
        Counter(output, "grandumi_room_journal_dropped_total", RoomJournal.DroppedEntries);
        Gauge(output, "grandumi_matchlog_queue_depth", MatchLogRecorder.QueueDepth);
        Counter(output, "grandumi_matchlog_dropped_total", MatchLogRecorder.DroppedEntries);
        var storage = StorageHealth.GetCurrent();
        Gauge(output, "grandumi_storage_healthy", storage.Healthy ? 1 : 0);
        Gauge(output, "grandumi_storage_total_bytes", storage.TotalBytes);
        Gauge(output, "grandumi_storage_available_bytes", storage.AvailableBytes);
        Gauge(output, "grandumi_pending_login_writes", playerDataStore.PendingLoginWrites);
        Gauge(output, "grandumi_capacity_max_connections", ServerCapacity.MaxConnections);
        Gauge(output, "grandumi_capacity_max_rooms", ServerCapacity.MaxRooms);
        var overloaded = ServerCapacity.IsOverloaded(out _);
        Gauge(output, "grandumi_ready", WebSocketBridge.IsReady && !overloaded ? 1 : 0);
        Gauge(output, "grandumi_overloaded", overloaded ? 1 : 0);
        output.AppendLine("# TYPE grandumi_ruleset_info gauge")
            .Append("grandumi_ruleset_info{ruleset=\"")
            .Append(EscapeLabel(CardRulesetManager.Current.Id))
            .AppendLine("\"} 1");
        output.AppendLine("# TYPE grandumi_ruleset_rooms gauge");
        foreach (var pair in GameRoomManager.RoomCountsByRuleset.OrderBy(item => item.Key, StringComparer.Ordinal))
            output.Append("grandumi_ruleset_rooms{ruleset=\"")
                .Append(EscapeLabel(pair.Key))
                .Append("\"} ")
                .Append(pair.Value.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        return output.ToString();
    }

    private static void Gauge(StringBuilder output, string name, double value)
        => output.Append("# TYPE ").Append(name).AppendLine(" gauge")
            .Append(name).Append(' ').Append(value.ToString("G17", CultureInfo.InvariantCulture)).AppendLine();

    private static void Counter(StringBuilder output, string name, double value)
        => output.Append("# TYPE ").Append(name).AppendLine(" counter")
            .Append(name).Append(' ').Append(value.ToString("G17", CultureInfo.InvariantCulture)).AppendLine();

    private static string EscapeLabel(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
