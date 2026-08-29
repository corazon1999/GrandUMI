using GrandUMI;
using GrandUMI.Cards;
using GrandUMI.Diagnostics;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Ranked;
using GrandUMI.Game.Stats;
using GrandUMI.Effects.Rules;
using GrandUMI.Persistence;
using GrandUMI.Training;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

if (args.Length > 0 && string.Equals(args[0], "--replay-artifact", StringComparison.Ordinal))
{
    Environment.ExitCode = await ReplayArtifactCommand.RunAsync(args[1..]);
    return;
}

if (args.Length > 0 && string.Equals(args[0], "--training-synthetic", StringComparison.Ordinal))
{
    Environment.ExitCode = await SyntheticTrainingCommand.RunAsync(args[1..]);
    return;
}

if (args.Length > 0 && string.Equals(args[0], "--training-human", StringComparison.Ordinal))
{
    Environment.ExitCode = await HumanTrainingCommand.RunAsync(args[1..]);
    return;
}

if (args.Length > 0 && string.Equals(args[0], "--migrate-shared-accounts", StringComparison.Ordinal))
{
    if (args.Length is < 3 or > 4)
    {
        Console.Error.WriteLine(
            "用法：GrandUMIServer --migrate-shared-accounts <新共享库路径> <正式 players.db> [测试 players.db]");
        Environment.ExitCode = 2;
        return;
    }

    var targetPath = Path.GetFullPath(args[1]);
    var primaryPath = Path.GetFullPath(args[2]);
    if (File.Exists(targetPath))
    {
        Console.Error.WriteLine("共享账号迁移目标已存在；为避免覆盖，必须使用全新的 .next 路径。");
        Environment.ExitCode = 2;
        return;
    }
    if (!File.Exists(primaryPath))
    {
        Console.Error.WriteLine("正式账号源数据库不存在，拒绝创建共享账号库。");
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        var sources = new List<LegacyAccountSource>
        {
            new("production", primaryPath, Authoritative: true),
        };
        if (args.Length == 4 && File.Exists(args[3]))
            sources.Add(new LegacyAccountSource("test", Path.GetFullPath(args[3]), Authoritative: false));
        var database = new SharedAccountDatabase(targetPath);
        var summary = database.Initialize(sources, AdministratorPolicy.GetAuthorizedAccounts());
        Console.WriteLine(JsonSerializer.Serialize(summary));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"共享账号迁移失败：{ex.Message}");
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Length > 0 && string.Equals(args[0], "--backfill-leader-stats", StringComparison.Ordinal))
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("用法：GrandUMIServer --backfill-leader-stats <对局日志目录> <排行榜数据库路径>");
        Environment.ExitCode = 2;
        return;
    }

    using var backfillStore = new LeaderStatsStore(args[2]);
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
var playerDatabasePath = PlayerDataStore.ResolveDefaultPath();
var rulesPackagePath = Path.Combine(
    Path.GetDirectoryName(playerDatabasePath)!,
    "Rulesets");
// 测试服先验证归档与当前只读内容，再加载任何规则插件或初始化持久数据库。
ReplayArtifactRuntimeBinding.VerifyFilesFromEnvironment(
    AppContext.BaseDirectory,
    rulesPackagePath);
var cardDataPath = ResolveCardDataPath();
CardDatabase.LoadFrom(cardDataPath);
GrandUMI.Effects.Dsl.DslInterpreter.LoadDirectory(
    ResolveDslDir(),
    $"builtin-{BuildInfo.Commit}");
CardRulesetManager.InitializePackages(rulesPackagePath);
ReplayRuntimeIdentityProvider.InitializeFromCurrentProcess(BuildInfo.Commit, CardDatabase.ContentHash);
var replayRuntimeIdentity = ReplayRuntimeIdentityProvider.For(CardRulesetManager.Current);
ReplayArtifactRuntimeBinding.VerifyFromEnvironment(
    replayRuntimeIdentity,
    AppContext.BaseDirectory,
    rulesPackagePath);
Console.WriteLine($"[训练重放身份] binary={replayRuntimeIdentity.BinarySha256}，cardDb={CardDatabase.ContentHash}，rules={CardRulesetManager.Current.ManifestHash}");
using var writerLease = SingleWriterLease.IsRequired
    ? SingleWriterLease.Acquire(Path.GetDirectoryName(playerDatabasePath)!, BuildInfo.NodeId)
    : null;
if (writerLease is not null)
    Console.WriteLine($"[单写者] 已锁定正式数据目录：{writerLease.LeasePath}");
var playerDataStore = new PlayerDataStore(playerDatabasePath, deferLoginWrites: true);
var accountDatabasePath = SharedAccountDatabase.ResolveDefaultPath(playerDataStore.DatabasePath);
playerDataStore.Initialize();
var sharedAccountDatabase = new SharedAccountDatabase(accountDatabasePath);
var usesIndependentSharedAccountDatabase = !string.Equals(
    Path.GetFullPath(accountDatabasePath),
    Path.GetFullPath(playerDataStore.DatabasePath),
    StringComparison.OrdinalIgnoreCase);
var sharedAccountSummary = sharedAccountDatabase.Initialize(
    usesIndependentSharedAccountDatabase
        ? null
        : [new LegacyAccountSource("current-environment", playerDataStore.DatabasePath, Authoritative: true)],
    AdministratorPolicy.GetAuthorizedAccounts(),
    requirePreparedMigration: usesIndependentSharedAccountDatabase);
var accountAuthenticationStore = new AccountAuthenticationStore(playerDataStore, sharedAccountDatabase);
var qqAccessStore = new QqAccessStore(sharedAccountDatabase, AdministratorPolicy.GetAuthorizedAccounts());
Console.WriteLine($"[玩家数据] SQLite: {playerDataStore.DatabasePath}");
Console.WriteLine($"[共享账号] SQLite: {sharedAccountDatabase.DatabasePath}；账号 {sharedAccountSummary.AccountCount}，绑定 {sharedAccountSummary.BindingCount}");
var qqWhitelistStatus = qqAccessStore.GetStatus();
Console.WriteLine(qqWhitelistStatus.Initialized
    ? $"[QQ 准入] 白名单 v{qqWhitelistStatus.Version}，{qqWhitelistStatus.MemberCount} 人"
    : "[QQ 准入] 白名单尚未初始化，仅既有授权管理员可导入首份名单");
var qqWhitelistSyncOptions = QqWhitelistSyncOptions.FromEnvironment();
Console.WriteLine(qqWhitelistSyncOptions is null
    ? "[QQ 白名单同步] 自动整点同步安全关闭"
    : $"[QQ 白名单同步] 已授权群 {qqWhitelistSyncOptions.GroupName}（{qqWhitelistSyncOptions.GroupId}）");
var onlinePlayerHistoryStore = new OnlinePlayerHistoryStore(Path.Combine(
    Path.GetDirectoryName(playerDataStore.DatabasePath)!,
    "online-player-history.db"));
onlinePlayerHistoryStore.Initialize();
Console.WriteLine($"[在线峰值] SQLite: {onlinePlayerHistoryStore.DatabasePath}");
var recordDailyActivePlayers = string.Equals(
    Environment.GetEnvironmentVariable("GRANDUMI_RECORD_DAILY_ACTIVE"),
    "1",
    StringComparison.Ordinal);
if (recordDailyActivePlayers)
{
    Console.WriteLine("[日活玩家] 正式统计写入已启用");
}
else
{
    Console.WriteLine("[日活玩家] 本环境仅展示权威数据，不记录登录活动");
}
var onlinePlayerHistoryReadPath = Environment.GetEnvironmentVariable("GRANDUMI_ONLINE_PLAYER_HISTORY_READ_PATH");
var onlinePlayerHistoryReadStore = string.IsNullOrWhiteSpace(onlinePlayerHistoryReadPath)
    || string.Equals(
        Path.GetFullPath(onlinePlayerHistoryReadPath),
        onlinePlayerHistoryStore.DatabasePath,
        StringComparison.OrdinalIgnoreCase)
    ? onlinePlayerHistoryStore
    : new OnlinePlayerHistoryStore(onlinePlayerHistoryReadPath, readOnly: true);
Console.WriteLine($"[在线峰值读取源] SQLite: {onlinePlayerHistoryReadStore.DatabasePath}");
var adminDeploymentCoordinator = AdminDeploymentCoordinator.FromEnvironment();
adminDeploymentCoordinator?.Initialize();
if (adminDeploymentCoordinator is not null)
    Console.WriteLine("[管理员发布] 已连接受限发布队列");

var keepLeaderStatsWalAnchor = string.Equals(
    LeaderStatsStore.Default.DatabasePath,
    LeaderStatsStore.Default.LeaderboardDatabasePath,
    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
LeaderStatsStore.Default.Initialize(keepWalAnchor: keepLeaderStatsWalAnchor);
Console.WriteLine($"[LeaderStats] 写入 SQLite: {LeaderStatsStore.Default.DatabasePath}");
Console.WriteLine($"[LeaderStats] 榜单 SQLite: {LeaderStatsStore.Default.LeaderboardDatabasePath}");
Console.WriteLine(LeaderStatsStore.Default.WalAnchorActive
    ? "[LeaderStats] WAL 生命周期锚点已启用"
    : "[LeaderStats] 本环境使用外部只读榜单源，不持有其 WAL 生命周期");
LeaderChampionStore.Default.Initialize();
Console.WriteLine($"[LeaderChampion] 写入 SQLite: {LeaderChampionStore.Default.DatabasePath}");
Console.WriteLine($"[LeaderChampion] 榜单 SQLite: {LeaderChampionStore.Default.LeaderboardDatabasePath}");
RankedStore.Default.Initialize();
Console.WriteLine($"[排位] SQLite: {RankedStore.Default.DatabasePath}");
if (string.Equals(RankedStore.Default.DatabasePath, RankedStore.Wild.DatabasePath, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("标准排位与狂野排位数据库不能使用同一路径");
RankedStore.Wild.Initialize();
Console.WriteLine($"[狂野排位] SQLite: {RankedStore.Wild.DatabasePath}");
if (!RankedStore.Default.TryRefreshLeaderboardSnapshot())
    Console.Error.WriteLine($"[排位榜] 启动预热失败：{RankedStore.Default.LastLeaderboardRefreshError ?? "正在生成"}");
if (!RankedStore.Wild.TryRefreshLeaderboardSnapshot())
    Console.Error.WriteLine($"[狂野排位榜] 启动预热失败：{RankedStore.Wild.LastLeaderboardRefreshError ?? "正在生成"}");
var adminOperationsMetricsCache = new AdminOperationsMetricsCache(
    LeaderStatsStore.Default,
    onlinePlayerHistoryReadStore);

GameRoomManager.InitializeMaintenance(Path.Combine(
    Path.GetDirectoryName(playerDataStore.DatabasePath)!,
    "maintenance-state.json"));
await GameRoomManager.RestoreAll();
WebSocketBridge.Initialize(
    playerDataStore,
    accountAuthenticationStore,
    qqAccessStore,
    onlinePlayerHistoryStore,
    onlinePlayerHistoryReadStore,
    adminOperationsMetricsCache,
    adminDeploymentCoordinator,
    recordDailyActivePlayers);

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
    var storage = StorageHealth.GetCurrent();
    return Results.Json(new
    {
        status = ready ? "ready" : "not_ready",
        overloaded,
        reason = ready ? null : WebSocketBridge.IsReady ? reason : "initializing",
        storage = new
        {
            healthy = storage.Healthy,
            totalBytes = storage.TotalBytes,
            availableBytes = storage.AvailableBytes,
        },
        connections = WebSocketBridge.ConnectionCount,
        rooms = GameRoomManager.RoomCount,
        maintenance = GameRoomManager.GetMaintenanceSnapshot().Enabled,
        activeRuleset = CardRulesetManager.Current.Id,
        rulesetRooms = GameRoomManager.RoomCountsByRuleset,
    }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/version", () => Results.Json(new
{
    version = BuildInfo.Version,
    commit = BuildInfo.Commit,
    buildTimeUtc = BuildInfo.BuildTimeUtc,
    nodeId = BuildInfo.NodeId,
    activeRuleset = CardRulesetManager.Current.Id,
}));

app.MapGet("/metrics", () => Results.Text(
    ServerMetrics.RenderPrometheus(playerDataStore),
    "text/plain; version=0.0.4; charset=utf-8"));

if (qqWhitelistSyncOptions is not null)
    QqWhitelistSyncHttpEndpoint.Map(app, qqAccessStore, qqWhitelistSyncOptions);

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
using var roomExpirationCancellation = new CancellationTokenSource();
var roomExpirationTask = GameRoomManager.RunExpirationMonitorAsync(roomExpirationCancellation.Token);
app.Lifetime.ApplicationStopping.Register(roomExpirationCancellation.Cancel);
using var rankedLeaderboardCancellation = new CancellationTokenSource();
var standardRankedLeaderboardTask = RankedStore.Default.RunLeaderboardRefreshLoopAsync(
    RankedStore.LeaderboardRefreshInterval,
    RankedStore.LeaderboardRefreshInterval,
    rankedLeaderboardCancellation.Token,
    error => Console.Error.WriteLine($"[排位榜] 刷新失败，继续服务上一版：{error}"));
var wildRankedLeaderboardTask = RankedStore.Wild.RunLeaderboardRefreshLoopAsync(
    RankedStore.LeaderboardRefreshInterval,
    TimeSpan.FromTicks(RankedStore.LeaderboardRefreshInterval.Ticks / 2),
    rankedLeaderboardCancellation.Token,
    error => Console.Error.WriteLine($"[狂野排位榜] 刷新失败，继续服务上一版：{error}"));
app.Lifetime.ApplicationStopping.Register(rankedLeaderboardCancellation.Cancel);
Console.WriteLine($"[网络] Kestrel 监听 http://127.0.0.1:{port}，WebSocket 路径 /ws");
Console.WriteLine($"[构建] version={BuildInfo.Version}, commit={BuildInfo.Commit}, node={BuildInfo.NodeId}");

try
{
    await app.RunAsync();
}
finally
{
    roomExpirationCancellation.Cancel();
    rankedLeaderboardCancellation.Cancel();
    await roomExpirationTask;
    await Task.WhenAll(standardRankedLeaderboardTask, wildRankedLeaderboardTask);
    GameRoomManager.CaptureAllRecoverySnapshots();
    await RoomRecoverySnapshotStore.FlushAsync();
    WebSocketBridge.Stop();
    MatchLogRecorder.Shutdown();
    RoomJournal.Shutdown();
    RoomRecoverySnapshotStore.Shutdown();
    LeaderStatsStore.Default.Dispose();
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
