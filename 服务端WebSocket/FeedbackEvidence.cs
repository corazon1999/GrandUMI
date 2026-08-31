using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GrandUMI;

internal sealed record FeedbackRequestIdentity(string SourceRequestId, string FeedbackId);

/// <summary>
/// 将客户端 requestId 绑定到服务端已认证提交者。持久化值只包含域分离摘要，
/// 不保留账号、会话或客户端原始 requestId。
/// </summary>
internal static class FeedbackRequestIdentityFactory
{
    internal static FeedbackRequestIdentity Create(string? authenticatedAccount, string sessionId, string? clientRequestId)
    {
        var hasAccount = !string.IsNullOrWhiteSpace(authenticatedAccount);
        var scopeKind = hasAccount ? "account" : "session";
        var scopeValue = hasAccount
            ? authenticatedAccount!.Trim().ToUpperInvariant()
            : string.IsNullOrWhiteSpace(sessionId)
                ? throw new ArgumentException("反馈提交者会话无效。", nameof(sessionId))
                : sessionId.Trim();
        var requestValue = string.IsNullOrWhiteSpace(clientRequestId)
            ? $"server-{Guid.NewGuid():N}"
            : clientRequestId.Trim();
        if (requestValue.Length > 128)
            throw new ArgumentException("反馈请求标识过长。", nameof(clientRequestId));

        var submitterDigest = Hash("grandumi.feedback.submitter.v1", scopeKind, scopeValue);
        var scopedDigest = Hash("grandumi.feedback.request.v1", submitterDigest, requestValue);
        var feedbackDigest = Hash("grandumi.feedback.id.v1", scopedDigest);
        return new FeedbackRequestIdentity(
            $"bug-report-{scopedDigest[..40]}",
            $"feedback-{feedbackDigest[..40]}");
    }

    private static string Hash(string domain, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, domain);
        foreach (var value in values) AppendField(hash, value);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

/// <summary>
/// 玩家反馈中的客户端证据只用于排障提示，不参与任何权威规则判断。
/// 此处逐字段白名单化，兼容旧 clientInfo，但绝不保留账号、URL、UA、聊天或牌局镜像。
/// </summary>
internal static class FeedbackEvidenceSanitizer
{
    internal const int MaxSubmittedBytes = 32 * 1024;
    internal const int MaxPersistedBytes = 16 * 1024;
    private const int MaxShortText = 160;
    private const long MaxCounter = 1_000_000_000;
    private const string ClientSchema = "grandumi.feedback.client.v1";
    private static readonly HashSet<string> Contexts = new(StringComparer.Ordinal)
        { "lobby", "game" };
    private static readonly HashSet<string> ConnectionStates = new(StringComparer.Ordinal)
        { "disconnected", "connecting", "handshaking", "connected", "reconnecting", "recovering", "failed" };
    private static readonly HashSet<string> Orientations = new(StringComparer.Ordinal)
        { "portrait", "landscape" };
    private static readonly HashSet<string> DisconnectCategories = new(StringComparer.Ordinal)
    {
        "unknown", "normal", "going_away", "abnormal", "session_replaced", "timeout",
        "network", "maintenance", "access_revoked", "websocket_error", "other",
    };
    private static readonly HashSet<string> KnownEndpointHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "ygo.grand-umi.com", "test.grand-umi.com", "direct.grand-umi.com",
        "candidate.grand-umi.com",
    };
    private static readonly Regex VersionPattern = new(
        @"^(?:unknown|[0-9]{1,6}(?:\.[0-9]{1,6}){1,3})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CommitPattern = new(
        @"^(?:unknown|[0-9a-f]{40})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex WebSocketCodePattern = new(
        @"^WebSocket ([0-9]{3,5})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static JsonObject Sanitize(JsonElement? structured, string? legacyClientInfo)
    {
        var result = BaseResult();
        if (structured is { ValueKind: JsonValueKind.Object } current)
        {
            if (Encoding.UTF8.GetByteCount(current.GetRawText()) > MaxSubmittedBytes)
            {
                result["source"] = "structured_rejected_too_large";
                return result;
            }
            result["source"] = "structured";
            CopyStructured(current, result);
            return EnsureBounded(result);
        }

        if (string.IsNullOrWhiteSpace(legacyClientInfo)) return result;
        if (Encoding.UTF8.GetByteCount(legacyClientInfo) > MaxSubmittedBytes)
        {
            result["source"] = "legacy_rejected_too_large";
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(legacyClientInfo);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            result["source"] = "legacy";
            CopyLegacy(document.RootElement, result);
        }
        catch (JsonException)
        {
            result["source"] = "legacy_invalid";
        }
        return EnsureBounded(result);
    }

    private static JsonObject BaseResult() => new()
    {
        ["schema"] = "grandumi.feedback.client.normalized.v1",
        ["trust"] = "client_non_authoritative",
        ["source"] = "none",
    };

    private static void CopyStructured(JsonElement root, JsonObject output)
    {
        output["submittedSchema"] = string.Equals(Text(root, "schema", MaxShortText), ClientSchema, StringComparison.Ordinal)
            ? ClientSchema
            : "unknown";
        output["capturedAtUtc"] = CanonicalTimestamp(root, "capturedAtUtc");
        if (Object(root, "client") is { } client)
        {
            output["client"] = new JsonObject
            {
                ["version"] = SafeVersion(client),
                ["commit"] = SafeCommit(client),
                ["context"] = Allowed(Text(client, "context", 16), Contexts),
            };
        }
        if (Object(root, "connection") is { } connection)
            output["connection"] = CopyConnection(connection);
        if (Object(root, "viewport") is { } viewport)
        {
            output["viewport"] = new JsonObject
            {
                ["width"] = Integer(viewport, "width", 0, 20_000),
                ["height"] = Integer(viewport, "height", 0, 20_000),
                ["orientation"] = Allowed(Text(viewport, "orientation", 16), Orientations),
                ["devicePixelRatio"] = Number(viewport, "devicePixelRatio", 0, 8),
                ["standalone"] = Boolean(viewport, "standalone"),
                ["online"] = Boolean(viewport, "online"),
            };
        }
    }

    private static void CopyLegacy(JsonElement root, JsonObject output)
    {
        var meta = Object(root, "meta");
        if (meta is null) return;
        output["client"] = new JsonObject
        {
            ["version"] = null,
            ["commit"] = null,
            ["context"] = Allowed(Text(meta.Value, "context", 16), Contexts),
        };
        var diagnostics = Object(meta.Value, "networkDiagnostics");
        var connection = diagnostics is null ? new JsonObject() : CopyConnection(diagnostics.Value);
        connection["state"] = Allowed(Text(meta.Value, "connectionState", 24), ConnectionStates);
        output["connection"] = connection;
    }

    private static JsonObject CopyConnection(JsonElement connection) => new()
    {
        ["state"] = Allowed(Text(connection, "state", 24), ConnectionStates),
        ["endpointHost"] = SafeEndpointHost(connection),
        ["connectionGeneration"] = Integer(connection, "connectionGeneration", 0, MaxCounter),
        ["reconnectCount"] = Integer(connection, "reconnectCount", 0, MaxCounter),
        ["endpointFailureCount"] = Integer(connection, "endpointFailureCount", 0, MaxCounter),
        ["handshakeMs"] = Number(connection, "handshakeMs", 0, 3_600_000),
        ["rttMs"] = Number(connection, "rttMs", 0, 3_600_000),
        ["rttP95Ms"] = Number(connection, "rttP95Ms", 0, 3_600_000),
        ["actionRoundTripMs"] = Number(connection, "actionRoundTripMs", 0, 3_600_000),
        ["actionRoundTripP95Ms"] = Number(connection, "actionRoundTripP95Ms", 0, 3_600_000),
        ["disconnectCategory"] = SafeDisconnectCategory(connection),
        ["stateDeltaEnabled"] = Boolean(connection, "stateDeltaEnabled"),
        ["stateDeltaCount"] = Integer(connection, "stateDeltaCount", 0, MaxCounter),
        ["fullStateCount"] = Integer(connection, "fullStateCount", 0, MaxCounter),
        ["maxMessageQueueDepth"] = Integer(connection, "maxMessageQueueDepth", 0, MaxCounter),
    };

    private static JsonObject EnsureBounded(JsonObject value)
    {
        if (Encoding.UTF8.GetByteCount(value.ToJsonString()) <= MaxPersistedBytes) return value;
        return BaseResult().WithSource("normalized_rejected_too_large");
    }

    private static JsonObject WithSource(this JsonObject value, string source)
    {
        value["source"] = source;
        return value;
    }

    private static JsonElement? Object(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? Text(JsonElement parent, string property, int maxLength)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var raw = value.GetString()?.Trim();
        var text = raw is null ? null : new string(raw.Where(c => !char.IsControl(c)).ToArray());
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string Allowed(string? value, HashSet<string> allowed)
        => value is not null && allowed.Contains(value) ? value : "unknown";

    private static string SafeVersion(JsonElement client)
    {
        var value = Text(client, "version", 48);
        return value is not null && VersionPattern.IsMatch(value) ? value.ToLowerInvariant() : "unknown";
    }

    private static string SafeCommit(JsonElement client)
    {
        var value = Text(client, "commit", 64);
        return value is not null && CommitPattern.IsMatch(value) ? value.ToLowerInvariant() : "unknown";
    }

    private static string? CanonicalTimestamp(JsonElement parent, string property)
    {
        var value = Text(parent, property, 48);
        if (value is null
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return null;
        return timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string SafeEndpointHost(JsonElement connection)
    {
        var value = Text(connection, "endpointHost", MaxShortText);
        if (value is null
            || value.Any(char.IsWhiteSpace)
            || value.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0
            || !Uri.TryCreate($"ws://{value}", UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            return "unknown";

        var host = uri.Host.Trim('[', ']');
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        if (IPAddress.TryParse(host, out var address))
            return IPAddress.IsLoopback(address) ? $"loopback{port}" : $"ip{port}";
        if (KnownEndpointHosts.Contains(host)) return host.ToLowerInvariant() + port;
        if (host.EndsWith(".grand-umi.com", StringComparison.OrdinalIgnoreCase)) return "grandumi-other";
        return "other";
    }

    private static string SafeDisconnectCategory(JsonElement connection)
    {
        var submittedCategory = Text(connection, "disconnectCategory", 32);
        if (submittedCategory is not null && DisconnectCategories.Contains(submittedCategory))
            return submittedCategory;

        var reason = Text(connection, "lastDisconnectReason", MaxShortText);
        if (reason is null) return "unknown";
        var codeMatch = WebSocketCodePattern.Match(reason);
        if (codeMatch.Success && int.TryParse(codeMatch.Groups[1].Value, out var code))
        {
            return code switch
            {
                1000 => "normal",
                1001 => "going_away",
                1006 => "abnormal",
                4009 => "session_replaced",
                _ => "websocket_error",
            };
        }

        var normalized = reason.ToLowerInvariant();
        if (normalized.Contains("其他地方登录", StringComparison.Ordinal)
            || normalized.Contains("异地登录", StringComparison.Ordinal)
            || normalized.Contains("session replaced", StringComparison.Ordinal))
            return "session_replaced";
        if (normalized.Contains("timeout", StringComparison.Ordinal)
            || normalized.Contains("超时", StringComparison.Ordinal))
            return "timeout";
        if (normalized.Contains("维护", StringComparison.Ordinal)) return "maintenance";
        if (normalized.Contains("白名单", StringComparison.Ordinal)
            || normalized.Contains("准入", StringComparison.Ordinal)
            || normalized.Contains("access revoked", StringComparison.Ordinal))
            return "access_revoked";
        if (normalized.Contains("network", StringComparison.Ordinal)
            || normalized.Contains("网络", StringComparison.Ordinal)
            || normalized.Contains("offline", StringComparison.Ordinal))
            return "network";
        return "other";
    }

    private static long? Integer(JsonElement parent, string property, long min, long max)
    {
        if (!parent.TryGetProperty(property, out var value) || !value.TryGetInt64(out var number)) return null;
        return Math.Clamp(number, min, max);
    }

    private static double? Number(JsonElement parent, string property, double min, double max)
    {
        if (!parent.TryGetProperty(property, out var value) || !value.TryGetDouble(out var number) || !double.IsFinite(number)) return null;
        return Math.Clamp(number, min, max);
    }

    private static bool? Boolean(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
