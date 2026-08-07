using System.Diagnostics;
using System.Globalization;
using System.Text;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
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
        Gauge(output, "grandumi_connections", WebSocketBridge.ConnectionCount);
        Gauge(output, "grandumi_logged_in_players", WebSocketBridge.LoggedInCount);
        Gauge(output, "grandumi_rooms", GameRoomManager.RoomCount);
        Gauge(output, "grandumi_spectators", GameRoomManager.SpectatorCount);
        Gauge(output, "grandumi_room_action_queue_depth", GameRoomManager.TotalActionQueueDepth);
        Gauge(output, "grandumi_websocket_max_queue_depth", WebSocketBridge.MaxCurrentOutboundDepth);
        Counter(output, "grandumi_websocket_dropped_messages_total", WebSocketBridge.DroppedOutboundCount);
        Gauge(output, "grandumi_room_journal_queue_depth", RoomJournal.QueueDepth);
        Counter(output, "grandumi_room_journal_dropped_total", RoomJournal.DroppedEntries);
        Gauge(output, "grandumi_replay_queue_depth", ReplayRecorder.QueueDepth);
        Counter(output, "grandumi_replay_dropped_total", ReplayRecorder.DroppedEntries);
        Gauge(output, "grandumi_matchlog_queue_depth", MatchLogRecorder.QueueDepth);
        Counter(output, "grandumi_matchlog_dropped_total", MatchLogRecorder.DroppedEntries);
        Gauge(output, "grandumi_pending_login_writes", playerDataStore.PendingLoginWrites);
        Gauge(output, "grandumi_capacity_max_connections", ServerCapacity.MaxConnections);
        Gauge(output, "grandumi_capacity_max_rooms", ServerCapacity.MaxRooms);
        Gauge(output, "grandumi_ready", WebSocketBridge.IsReady ? 1 : 0);
        Gauge(output, "grandumi_overloaded", ServerCapacity.IsOverloaded(out _) ? 1 : 0);
        return output.ToString();
    }

    private static void Gauge(StringBuilder output, string name, double value)
        => output.Append("# TYPE ").Append(name).AppendLine(" gauge")
            .Append(name).Append(' ').Append(value.ToString("G17", CultureInfo.InvariantCulture)).AppendLine();

    private static void Counter(StringBuilder output, string name, double value)
        => output.Append("# TYPE ").Append(name).AppendLine(" counter")
            .Append(name).Append(' ').Append(value.ToString("G17", CultureInfo.InvariantCulture)).AppendLine();
}
