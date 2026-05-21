using GrandUMI;

Console.Title = "GrandUMI WebSocket 服务器";

int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 8080;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║    GrandUMI WebSocket 服务器          ║");
Console.WriteLine($"║    ws://localhost:{port}/ws/              ║");
Console.WriteLine("║    按 Ctrl+C 停止                     ║");
Console.WriteLine("╚══════════════════════════════════════╝\n");

WebSocketBridge.Start(port);

// 等待 Ctrl+C
var tcs = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[服务器] 正在停止...");
    WebSocketBridge.Stop();
    tcs.SetResult();
};

await tcs.Task;
Console.WriteLine("[服务器] 已停止");
