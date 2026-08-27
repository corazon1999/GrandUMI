using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrandUMI.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace GrandUMI;

/// <summary>
/// QQ 机器人白名单同步的内部 HTTP 边界。应用层只接受本机反向代理转发，
/// 公网来源限制由 Nginx 的精确 location 与 allow/deny 再做一层收敛。
/// </summary>
internal sealed class QqWhitelistSyncOptions
{
    private const string EnabledVariable = "GRANDUMI_QQ_WHITELIST_SYNC_ENABLED";
    private const string SecretVariable = "GRANDUMI_QQ_WHITELIST_SYNC_SECRET";
    private readonly byte[] _secretHash;

    private QqWhitelistSyncOptions(
        string groupId,
        string groupName,
        string proxyId,
        int minimumMemberCount,
        int maximumShrinkPercent,
        int maximumDelaySeconds,
        byte[] secretHash)
    {
        GroupId = groupId;
        GroupName = groupName;
        ProxyId = proxyId;
        MinimumMemberCount = minimumMemberCount;
        MaximumShrinkPercent = maximumShrinkPercent;
        MaximumDelaySeconds = maximumDelaySeconds;
        _secretHash = secretHash;
    }

    public string GroupId { get; }
    public string GroupName { get; }
    public string ProxyId { get; }
    public int MinimumMemberCount { get; }
    public int MaximumShrinkPercent { get; }
    public int MaximumDelaySeconds { get; }

    public static QqWhitelistSyncOptions? FromEnvironment()
    {
        var enabled = (Environment.GetEnvironmentVariable(EnabledVariable) ?? "").Trim();
        if (enabled is "" or "0") return null;
        if (enabled != "1")
            throw new InvalidOperationException($"{EnabledVariable} 只能是 0 或 1。");

        var groupId = RequireEnvironment("GRANDUMI_QQ_WHITELIST_SYNC_GROUP_ID");
        var groupName = RequireEnvironment("GRANDUMI_QQ_WHITELIST_SYNC_GROUP_NAME");
        var proxyId = RequireEnvironment("GRANDUMI_QQ_WHITELIST_SYNC_PROXY_ID");
        var timezone = RequireEnvironment("GRANDUMI_QQ_WHITELIST_SYNC_TIMEZONE");
        if (!string.Equals(timezone, "Asia/Singapore", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "GRANDUMI_QQ_WHITELIST_SYNC_TIMEZONE 必须是 Asia/Singapore（UTC+8）。");
        var secret = RequireEnvironment(SecretVariable);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (secretBytes.Length is < 32 or > 512)
            throw new InvalidOperationException($"{SecretVariable} 必须是 32–512 字节的随机密钥。");
        if (proxyId.Length > 100 || proxyId.Any(char.IsControl))
            throw new InvalidOperationException("QQ 白名单同步代理标识格式无效。");

        try
        {
            return new QqWhitelistSyncOptions(
                QqAccessStore.NormalizeQq(groupId),
                NormalizeGroupName(groupName),
                proxyId,
                ReadBoundedInteger("GRANDUMI_QQ_WHITELIST_SYNC_MIN_MEMBERS", 100, 1, QqAccessStore.MaxImportMembers),
                ReadBoundedInteger("GRANDUMI_QQ_WHITELIST_SYNC_MAX_SHRINK_PERCENT", 25, 0, 90),
                ReadBoundedInteger("GRANDUMI_QQ_WHITELIST_SYNC_MAX_DELAY_SECONDS", 600, 30, 1800),
                SHA256.HashData(secretBytes));
        }
        catch (QqAccessValidationException ex)
        {
            throw new InvalidOperationException($"QQ 白名单同步配置无效：{ex.Message}", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    internal static QqWhitelistSyncOptions CreateForTests(
        string groupId,
        string groupName,
        string proxyId,
        string secret,
        int minimumMemberCount = 1,
        int maximumShrinkPercent = 25,
        int maximumDelaySeconds = 600)
        => new(
            QqAccessStore.NormalizeQq(groupId),
            NormalizeGroupName(groupName),
            proxyId,
            minimumMemberCount,
            maximumShrinkPercent,
            maximumDelaySeconds,
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public bool IsAuthorized(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress)) return false;
        if (context.Request.Headers["X-GrandUMI-Internal-Source"].Count != 1
            || !string.Equals(
                context.Request.Headers["X-GrandUMI-Internal-Source"][0],
                ProxyId,
                StringComparison.Ordinal))
            return false;
        if (context.Request.Headers.Authorization.Count != 1) return false;
        var authorization = context.Request.Headers.Authorization[0];
        const string prefix = "Bearer ";
        if (authorization is null
            || !authorization.StartsWith(prefix, StringComparison.Ordinal)
            || authorization.Length > prefix.Length + 512)
            return false;
        var supplied = authorization[prefix.Length..];
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(_secretHash, suppliedHash);
    }

    private static string RequireEnvironment(string name)
    {
        var value = (Environment.GetEnvironmentVariable(name) ?? "").Trim();
        return value.Length > 0
            ? value
            : throw new InvalidOperationException($"启用 QQ 白名单同步时必须配置 {name}。");
    }

    private static int ReadBoundedInteger(string name, int defaultValue, int minimum, int maximum)
    {
        var raw = (Environment.GetEnvironmentVariable(name) ?? "").Trim();
        if (raw.Length == 0) return defaultValue;
        if (!int.TryParse(raw, out var value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"{name} 必须是 {minimum}–{maximum} 的整数。");
        return value;
    }

    private static string NormalizeGroupName(string value)
    {
        var normalized = (value ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > QqAccessStore.MaxSyncGroupNameLength
            || normalized.Any(char.IsControl))
            throw new InvalidOperationException("QQ 白名单同步群名格式无效。");
        return normalized;
    }
}

internal static class QqWhitelistSyncHttpEndpoint
{
    private const int MaximumEnvelopeBytes = QqAccessStore.MaxImportBytes;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
    };

    public static void Map(
        WebApplication app,
        QqAccessStore store,
        QqWhitelistSyncOptions options,
        Func<DateTimeOffset>? clock = null)
    {
        clock ??= static () => DateTimeOffset.UtcNow;
        app.MapPost(
            "/internal/qq-whitelist/sync",
            (Delegate)(Func<HttpContext, Task<IResult>>)(context =>
                HandleSync(context, store, options, clock)));
        app.MapPost(
            "/internal/qq-whitelist/sync/status",
            (Delegate)(Func<HttpContext, Task<IResult>>)(context =>
                HandleStatus(context, store, options)));
        app.MapPost(
            "/internal/qq-whitelist/sync/failure",
            (Delegate)(Func<HttpContext, Task<IResult>>)(context =>
                HandleFailure(context, store, options, clock)));
        app.MapPost(
            "/internal/qq-whitelist/sync/notification-ack",
            (Delegate)(Func<HttpContext, Task<IResult>>)(context =>
                HandleNotificationAck(context, store, options)));
    }

    private static async Task<IResult> HandleSync(
        HttpContext context,
        QqAccessStore store,
        QqWhitelistSyncOptions options,
        Func<DateTimeOffset> clock)
    {
        if (!options.IsAuthorized(context)) return Results.NotFound();
        if (context.Request.ContentLength is > MaximumEnvelopeBytes)
            return Results.Json(new { error = "同步请求体过大。" }, statusCode: StatusCodes.Status413PayloadTooLarge);
        try
        {
            var request = await JsonSerializer.DeserializeAsync<QqWhitelistSyncHttpRequest>(
                context.Request.Body, JsonOptions, context.RequestAborted);
            if (request?.Members is null)
                throw new QqAccessValidationException("同步请求缺少成员列表。");
            var result = store.SynchronizeScheduledGroup(
                new QqWhitelistScheduledSyncRequest(
                    request.OperationKey ?? "",
                    request.ScheduledHour,
                    request.GroupId ?? "",
                    request.GroupName ?? "",
                    request.ReportedMemberCount,
                    request.ClientInstanceId ?? "",
                    JsonSerializer.Serialize(request.Members, JsonOptions)),
                options.GroupId,
                options.GroupName,
                options.MinimumMemberCount,
                options.MaximumShrinkPercent,
                options.MaximumDelaySeconds,
                clock().ToUnixTimeSeconds());
            Console.WriteLine(
                $"[QQ 白名单同步] 群 {result.GroupId} 整点 {result.ScheduledHour} " +
                $"v{result.Import.Version} {result.Import.MemberCount} 人，重放={result.Replayed}");
            return Results.Json(ToResponse(result));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "同步请求 JSON 无效。" }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (QqAccessValidationException ex) when (IsTransientStorageFailure(ex))
        {
            Console.Error.WriteLine($"[QQ 白名单同步暂时失败] {ex.Message}");
            return Results.Json(new { error = "共享账号数据库正忙，请稍后重试。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (QqAccessValidationException ex)
        {
            Console.Error.WriteLine($"[QQ 白名单同步拒绝] {ex.Message}");
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QQ 白名单同步失败] {ex.Message}");
            return Results.Json(new { error = "游戏服务未能提交白名单，已保留上一版本。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> HandleStatus(
        HttpContext context,
        QqAccessStore store,
        QqWhitelistSyncOptions options)
    {
        if (!options.IsAuthorized(context)) return Results.NotFound();
        try
        {
            var request = await ReadControlRequest(context);
            var result = store.GetScheduledGroupSync(
                request.OperationKey ?? "", request.ClientInstanceId ?? "");
            return result is null ? Results.NotFound() : Results.Json(ToResponse(result));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (QqAccessValidationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "同步状态请求 JSON 无效。" }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QQ 白名单同步状态查询失败] {ex.Message}");
            return Results.Json(new { error = "暂时无法查询同步状态，请稍后重试。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> HandleFailure(
        HttpContext context,
        QqAccessStore store,
        QqWhitelistSyncOptions options,
        Func<DateTimeOffset> clock)
    {
        if (!options.IsAuthorized(context)) return Results.NotFound();
        if (context.Request.ContentLength is > MaximumEnvelopeBytes)
            return Results.Json(
                new { error = "失败报告请求体过大。" },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        try
        {
            var request = await JsonSerializer.DeserializeAsync<QqWhitelistSyncFailureHttpRequest>(
                context.Request.Body, JsonOptions, context.RequestAborted)
                ?? throw new QqAccessValidationException("同步失败报告为空。");
            var result = store.ReportScheduledGroupFailure(
                new QqWhitelistScheduledFailureRequest(
                    request.OperationKey ?? "",
                    request.ScheduledHour,
                    request.GroupId ?? "",
                    request.GroupName ?? "",
                    request.ClientInstanceId ?? "",
                    request.Error ?? ""),
                options.GroupId,
                options.GroupName,
                clock().ToUnixTimeSeconds());
            if (result.Committed is { } committed)
                Console.WriteLine(
                    $"[QQ 白名单同步失败核对] {result.OperationKey} 实际已提交 v{committed.Import.Version}");
            else
                Console.Error.WriteLine(
                    $"[QQ 白名单同步失败] {result.OperationKey}，重放={result.Replayed}，" +
                    $"原因={result.Failure?.Error}");
            return Results.Json(ToFailureResponse(result));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (JsonException)
        {
            return Results.Json(
                new { error = "同步失败报告 JSON 无效。" },
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (QqAccessValidationException ex) when (IsTransientStorageFailure(ex))
        {
            return Results.Json(
                new { error = "共享账号数据库正忙，请稍后重试。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (QqAccessValidationException ex)
        {
            return Results.Json(
                new { error = ex.Message },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QQ 白名单同步失败报告落库失败] {ex.Message}");
            return Results.Json(
                new { error = "同步失败报告暂时无法保存，请稍后重试。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> HandleNotificationAck(
        HttpContext context,
        QqAccessStore store,
        QqWhitelistSyncOptions options)
    {
        if (!options.IsAuthorized(context)) return Results.NotFound();
        try
        {
            var request = await ReadControlRequest(context);
            var result = store.AcknowledgeScheduledGroupNotification(
                request.OperationKey ?? "", request.ClientInstanceId ?? "", request.Version);
            Console.WriteLine(
                $"[QQ 白名单同步] 群 {result.GroupId} v{result.Import.Version} 通知已确认");
            return Results.Json(ToResponse(result));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "通知确认请求 JSON 无效。" }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (QqAccessValidationException ex) when (IsTransientStorageFailure(ex))
        {
            return Results.Json(new { error = "共享账号数据库正忙，请稍后重试。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (QqAccessValidationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QQ 白名单同步通知确认失败] {ex.Message}");
            return Results.Json(new { error = "通知确认暂时失败，请稍后重试。" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static bool IsTransientStorageFailure(QqAccessValidationException exception)
        => exception.InnerException is SqliteException { SqliteErrorCode: 5 or 6 };

    private static async Task<QqWhitelistSyncControlRequest> ReadControlRequest(HttpContext context)
        => await JsonSerializer.DeserializeAsync<QqWhitelistSyncControlRequest>(
               context.Request.Body, JsonOptions, context.RequestAborted)
           ?? throw new QqAccessValidationException("同步控制请求为空。");

    private static object ToResponse(QqWhitelistScheduledSyncResult result)
        => new
        {
            operationKey = result.OperationKey,
            scheduledHour = result.ScheduledHour,
            groupId = result.GroupId,
            groupName = result.GroupName,
            version = result.Import.Version,
            importedAt = result.Import.ImportedAt,
            memberCount = result.Import.MemberCount,
            addedCount = result.Import.AddedCount,
            removedCount = result.Import.RemovedCount,
            removedBoundCount = result.Import.RemovedBoundCount,
            replayed = result.Replayed,
            notificationOwner = result.NotificationOwner,
            notificationAcknowledgedAt = result.NotificationAcknowledgedAt,
        };

    private static object ToFailureResponse(QqWhitelistFailureReportResult result)
    {
        if (result.Committed is { } committed)
            return new
            {
                status = "committed",
                committed = true,
                operationKey = committed.OperationKey,
                scheduledHour = committed.ScheduledHour,
                groupId = committed.GroupId,
                groupName = committed.GroupName,
                version = committed.Import.Version,
                importedAt = committed.Import.ImportedAt,
                memberCount = committed.Import.MemberCount,
                addedCount = committed.Import.AddedCount,
                removedCount = committed.Import.RemovedCount,
                removedBoundCount = committed.Import.RemovedBoundCount,
                replayed = committed.Replayed,
                notificationOwner = committed.NotificationOwner,
                notificationAcknowledgedAt = committed.NotificationAcknowledgedAt,
            };

        var failure = result.Failure
            ?? throw new InvalidOperationException("同步失败报告既无提交结果也无失败事件。");
        return new
        {
            status = "failure_recorded",
            committed = false,
            operationKey = result.OperationKey,
            replayed = result.Replayed,
            update = new
            {
                id = failure.Id,
                eventKey = failure.EventKey,
                outcome = failure.Outcome,
                source = failure.Source,
                operationKey = failure.OperationKey,
                occurredAt = failure.OccurredAt,
                scheduledHour = failure.ScheduledHour,
                version = failure.Version,
                memberCount = failure.MemberCount,
                error = failure.Error,
            },
        };
    }

    private sealed class QqWhitelistSyncHttpRequest
    {
        public string? OperationKey { get; init; }
        public long ScheduledHour { get; init; }
        public string? GroupId { get; init; }
        public string? GroupName { get; init; }
        public int ReportedMemberCount { get; init; }
        public string? ClientInstanceId { get; init; }
        public List<string?>? Members { get; init; }
    }

    private sealed class QqWhitelistSyncControlRequest
    {
        public string? OperationKey { get; init; }
        public string? ClientInstanceId { get; init; }
        public long Version { get; init; }
    }

    private sealed class QqWhitelistSyncFailureHttpRequest
    {
        public string? OperationKey { get; init; }
        public long ScheduledHour { get; init; }
        public string? GroupId { get; init; }
        public string? GroupName { get; init; }
        public string? ClientInstanceId { get; init; }
        public string? Error { get; init; }
    }
}
