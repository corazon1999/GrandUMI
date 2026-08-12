using GrandUMI;
using GrandUMI.Cards;
using GrandUMI.Diagnostics;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Ranked;
using GrandUMI.Game.Stats;
using GrandUMI.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

if (args.Length > 0 && string.Equals(args[0], "--backfill-leader-stats", StringComparison.Ordinal))
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("用法：GrandUMIServer --backfill-leader-stats <对局日志目录> <排行榜数据库路径>");
        Environment.ExitCode = 2;
        return;
    }

    var backfillStore = new LeaderStatsStore(args[2]);
    backfillStore.Initialize();
    var report = LeaderStatsBackfill.ImportDirectory(args[1], backfillStore);
    var backfillChampionStore = new LeaderChampionStore(args[2]);
    backfillChampionStore.Initialize();
    Console.WriteLine(
        $"[LeaderStats 回填] 扫描 {report.FilesScanned}，新增 {report.Imported}，已存在 {report.AlreadyRecorded}，" +
        $"未结束 {report.SkippedIncomplete}，无效 {report.SkippedInvalid}，错误 {report.Errors.Count}");
    foreach (var error in report.Errors.Take(20)) Console.Error.WriteLine($"[LeaderStats 回填错误] {error}");
    if (report.Errors.Count > 0) Environment.ExitCode = 1;
    return;
}

try { Console.Title = "GrandUMI WebSocket 服务器"; } catch { }
Console.OutputEncoding = System.Text.Encoding.UTF8;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 8080;
var playerDataStore = new PlayerDataStore(PlayerDataStore.ResolveDefaultPath(), deferLoginWrites: true);
playerDataStore.Initialize();
var accountAuthenticationStore = new AccountAuthenticationStore(playerDataStore);
accountAuthenticationStore.Initialize();
Console.WriteLine($"[玩家数据] SQLite: {playerDataStore.DatabasePath}");

CardDatabase.LoadFrom(ResolveCardDataPath());
GrandUMI.Effects.Dsl.DslInterpreter.LoadDirectory(ResolveDslDir());
LeaderStatsStore.Default.Initialize();
Console.WriteLine($"[LeaderStats] 写入 SQLite: {LeaderStatsStore.Default.DatabasePath}");
Console.WriteLine($"[LeaderStats] 榜单 SQLite: {LeaderStatsStore.Default.LeaderboardDatabasePath}");
LeaderChampionStore.Default.Initialize();
Console.WriteLine($"[LeaderChampion] 写入 SQLite: {LeaderChampionStore.Default.DatabasePath}");
Console.WriteLine($"[LeaderChampion] 榜单 SQLite: {LeaderChampionStore.Default.LeaderboardDatabasePath}");
RankedStore.Default.Initialize();
Console.WriteLine($"[排位] SQLite: {RankedStore.Default.DatabasePath}");

GameRoomManager.InitializeMaintenance(Path.Combine(
    Path.GetDirectoryName(playerDataStore.DatabasePath)!,
    "maintenance-state.json"));
await GameRoomManager.RestoreAll();
WebSocketBridge.Initialize(playerDataStore, accountAuthenticationStore);

var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = ServerCapacity.MaxConnections + 256;
    options.Limits.MaxConcurrentUpgradedConnections = ServerCapacity.MaxConnections;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.MaxRequestBodySize = 256 * 1024;
});

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
});

app.MapGet("/live", () => Results.Json(new
{
    status = "live",
    nodeId = BuildInfo.NodeId,
    version = BuildInfo.Version,
}));

app.MapGet("/ready", () =>
{
    var overloaded = ServerCapacity.IsOverloaded(out var reason);
    var ready = WebSocketBridge.IsReady && !overloaded;
    return Results.Json(new
    {
        status = ready ? "ready" : "not_ready",
        overloaded,
        reason = ready ? null : reason,
        connections = WebSocketBridge.ConnectionCount,
        rooms = GameRoomManager.RoomCount,
    }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/version", () => Results.Json(new
{
    version = BuildInfo.Version,
    commit = BuildInfo.Commit,
    buildTimeUtc = BuildInfo.BuildTimeUtc,
    nodeId = BuildInfo.NodeId,
}));

app.MapGet("/metrics", () => Results.Text(
    ServerMetrics.RenderPrometheus(playerDataStore),
    "text/plain; version=0.0.4; charset=utf-8"));

app.MapGet("/card-back-images/{id:long}", (long id, HttpContext context) =>
{
    var image = playerDataStore.GetCardBackImage(id);
    if (image is not null)
    {
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        context.Response.Headers.XContentTypeOptions = "nosniff";
    }
    return image is null
        ? Results.NotFound()
        : Results.File(image.Data, image.MimeType, lastModified: null, entityTag: null, enableRangeProcessing: false);
});

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("需要 WebSocket 握手");
        return;
    }
    if (!ServerCapacity.CanAcceptConnection(out var reason))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "5";
        await context.Response.WriteAsync($"服务器过载：{reason}");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await WebSocketBridge.AcceptClientAsync(socket, context.RequestAborted);
});

app.Lifetime.ApplicationStopping.Register(WebSocketBridge.Stop);
Console.WriteLine($"[网络] Kestrel 监听 http://127.0.0.1:{port}，WebSocket 路径 /ws");
Console.WriteLine($"[构建] version={BuildInfo.Version}, commit={BuildInfo.Commit}, node={BuildInfo.NodeId}");

try
{
    await app.RunAsync();
}
finally
{
    WebSocketBridge.Stop();
    MatchLogRecorder.Shutdown();
    RoomJournal.Shutdown();
    playerDataStore.Shutdown();
    Console.WriteLine("[服务器] 已停止");
}

static string ResolveCardDataPath()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "卡牌数据");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "卡牌数据"));
}

static string ResolveDslDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "Effects", "Definitions");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Effects", "Definitions"));
}
