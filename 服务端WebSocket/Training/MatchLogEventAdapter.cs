using System.Text;
using System.Text.Json;

namespace GrandUMI.Training;

public sealed record ReplayVersionIdentity(
    string MatchLogSchema,
    string EngineArtifactId,
    string EngineCommit,
    string RulesVersion,
    string RulesetManifestHash,
    string CardDbContentHash,
    string RngAlgorithmVersion,
    string DeterministicIdVersion,
    string OpeningProtocolVersion,
    string ReplayConfigSchema);

public sealed record ReplayPlayerDeck(
    int Seat,
    string DeckRaw,
    bool AlwaysPromptOnLifeReveal);

public sealed record ReplayConfiguration(
    bool LeaderKeywordWildcard,
    bool OpeningSetupAfterFirstPlayerChoice);

public sealed record ReplayMatchHeader(
    string MatchId,
    int RngSeed,
    int FirstPlayer,
    ReplayPlayerDeck Player0,
    ReplayPlayerDeck Player1,
    ReplayConfiguration Configuration,
    ReplayVersionIdentity VersionIdentity);

public sealed class AdaptedMatchLogEvent
{
    public AdaptedMatchLogEvent(long seq, string kind, int? actor, JsonElement payload)
    {
        Seq = seq;
        Kind = kind;
        Actor = actor;
        Payload = payload.Clone();
    }

    public long Seq { get; }
    public string Kind { get; }
    public int? Actor { get; }
    public JsonElement Payload { get; }
}

public sealed record AdaptedMatchLog(
    string SourceId,
    string SourceFileHash,
    ReplayMatchHeader Header,
    IReadOnlyList<AdaptedMatchLogEvent> Events);

/// <summary>grandumi.matchlog.v1 的严格整局结构适配器。</summary>
public static class MatchLogEventAdapter
{
    public const string SupportedSchema = "grandumi.matchlog.v1";
    public const string SupportedAdapterVersion = "grandumi.matchlog.v1.accepted-pairing.v1";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static AdaptedMatchLog Adapt(ReadOnlyMemory<byte> sourceBytes, string sourceId)
    {
        var sourceHash = CanonicalJson.Sha256(sourceBytes.Span);
        if (sourceBytes.IsEmpty)
            throw Quarantine(ReplayQuarantineCodes.EmptyLog, "matchlog_adapter", "日志为空");
        if (sourceBytes.Span[^1] != (byte)'\n')
            throw Quarantine(
                ReplayQuarantineCodes.IncompleteTail,
                "matchlog_adapter",
                "JSONL 最后一行没有换行终止符，可能仍在追加");

        string text;
        try
        {
            text = StrictUtf8.GetString(sourceBytes.Span);
        }
        catch (DecoderFallbackException ex)
        {
            throw Quarantine(ReplayQuarantineCodes.InvalidUtf8, "matchlog_adapter", ex.Message);
        }

        var lines = text.Split('\n');
        var events = new List<AdaptedMatchLogEvent>(Math.Max(0, lines.Length - 1));
        string? matchId = null;
        long expectedSeq = 1;

        for (var index = 0; index < lines.Length - 1; index++)
        {
            var line = lines[index];
            if (line.EndsWith('\r')) line = line[..^1];
            if (index == 0 && line.StartsWith('\uFEFF')) line = line[1..];
            if (string.IsNullOrWhiteSpace(line))
                throw Quarantine(
                    ReplayQuarantineCodes.EmptyLine,
                    "matchlog_adapter",
                    $"第 {index + 1} 行为空",
                    matchId,
                    expectedSeq);

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw Quarantine(
                        ReplayQuarantineCodes.MalformedEvent,
                        "matchlog_adapter",
                        $"第 {index + 1} 行不是 JSON 对象",
                        matchId,
                        expectedSeq);

                var schema = RequiredString(root, "schema", ReplayQuarantineCodes.MalformedEvent, expectedSeq);
                if (!string.Equals(schema, SupportedSchema, StringComparison.Ordinal))
                    throw Quarantine(
                        ReplayQuarantineCodes.UnsupportedSchema,
                        "matchlog_adapter",
                        $"不支持的 matchlog schema：{schema}",
                        matchId,
                        expectedSeq);

                var currentMatchId = RequiredString(root, "matchId", ReplayQuarantineCodes.MalformedEvent, expectedSeq);
                matchId ??= currentMatchId;
                if (!string.Equals(matchId, currentMatchId, StringComparison.Ordinal))
                    throw Quarantine(
                        ReplayQuarantineCodes.MixedMatchId,
                        "matchlog_adapter",
                        "同一文件出现多个 matchId",
                        matchId,
                        expectedSeq);

                if (!root.TryGetProperty("seq", out var seqElement)
                    || seqElement.ValueKind != JsonValueKind.Number
                    || !seqElement.TryGetInt64(out var seq))
                    throw Quarantine(
                        ReplayQuarantineCodes.InvalidSequence,
                        "matchlog_adapter",
                        "seq 必须是整数",
                        matchId,
                        expectedSeq);
                if (seq != expectedSeq)
                    throw Quarantine(
                        ReplayQuarantineCodes.SequenceGap,
                        "matchlog_adapter",
                        $"seq 不连续：期望 {expectedSeq}，实际 {seq}",
                        matchId,
                        seq);

                var kind = RequiredString(root, "kind", ReplayQuarantineCodes.MalformedEvent, seq);
                int? actor = null;
                if (root.TryGetProperty("actor", out var actorElement)
                    && actorElement.ValueKind != JsonValueKind.Null)
                {
                    if (actorElement.ValueKind != JsonValueKind.Number
                        || !actorElement.TryGetInt32(out var parsedActor))
                        throw Quarantine(
                            ReplayQuarantineCodes.MalformedEvent,
                            "matchlog_adapter",
                            "actor 必须是整数或 null",
                            matchId,
                            seq);
                    actor = parsedActor;
                }

                if (!root.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object)
                    throw Quarantine(
                        ReplayQuarantineCodes.MalformedEvent,
                        "matchlog_adapter",
                        "payload 必须是 JSON 对象",
                        matchId,
                        seq);
                events.Add(new AdaptedMatchLogEvent(seq, kind, actor, payload));
                expectedSeq++;
            }
            catch (ReplayQuarantineException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw Quarantine(
                    ReplayQuarantineCodes.MalformedJson,
                    "matchlog_adapter",
                    $"第 {index + 1} 行 JSON 无效：{ex.Message}",
                    matchId,
                    expectedSeq);
            }
        }

        if (events.Count == 0)
            throw Quarantine(ReplayQuarantineCodes.EmptyLog, "matchlog_adapter", "日志没有事件");
        if (!string.Equals(events[0].Kind, "match_start", StringComparison.Ordinal))
            throw Quarantine(
                ReplayQuarantineCodes.MissingMatchStart,
                "matchlog_adapter",
                "首个事件必须是 match_start",
                matchId,
                events[0].Seq);
        if (events.Count(e => string.Equals(e.Kind, "match_start", StringComparison.Ordinal)) != 1)
            throw Quarantine(
                ReplayQuarantineCodes.DuplicateMatchStart,
                "matchlog_adapter",
                "整局必须且只能有一个 match_start",
                matchId);

        var matchEndEvents = events
            .Where(e => string.Equals(e.Kind, "match_end", StringComparison.Ordinal))
            .ToArray();
        if (matchEndEvents.Length == 0)
            throw Quarantine(
                ReplayQuarantineCodes.MissingMatchEnd,
                "matchlog_adapter",
                "日志没有 match_end，可能仍在进行或尾部丢失",
                matchId);
        if (matchEndEvents.Length != 1 || !ReferenceEquals(matchEndEvents[0], events[^1]))
            throw Quarantine(
                ReplayQuarantineCodes.InvalidMatchEnd,
                "matchlog_adapter",
                "match_end 必须且只能出现一次，并且是最后一个事件",
                matchId,
                matchEndEvents[0].Seq);

        var header = ParseHeader(matchId!, events[0]);
        return new AdaptedMatchLog(
            string.IsNullOrWhiteSpace(sourceId) ? "memory" : sourceId,
            sourceHash,
            header,
            Array.AsReadOnly(events.ToArray()));
    }

    private static ReplayMatchHeader ParseHeader(string matchId, AdaptedMatchLogEvent matchStart)
    {
        var payload = matchStart.Payload;
        var schema = SupportedSchema;
        var identity = new ReplayVersionIdentity(
            schema,
            RequiredVersionString(payload, "engineArtifactId", matchId, matchStart.Seq),
            RequiredVersionString(payload, "engineCommit", matchId, matchStart.Seq),
            RequiredVersionString(payload, "rulesVersion", matchId, matchStart.Seq),
            RequiredVersionString(payload, "rulesetManifestHash", matchId, matchStart.Seq),
            RequiredVersionString(payload, "cardDbContentHash", matchId, matchStart.Seq),
            RequiredVersionString(payload, "rngAlgorithmVersion", matchId, matchStart.Seq),
            RequiredVersionString(payload, "deterministicIdVersion", matchId, matchStart.Seq),
            RequiredVersionString(payload, "openingProtocolVersion", matchId, matchStart.Seq),
            RequiredVersionString(payload, "replayConfigSchema", matchId, matchStart.Seq));

        if (!payload.TryGetProperty("rngSeed", out var seedElement)
            || seedElement.ValueKind != JsonValueKind.Number
            || !seedElement.TryGetInt32(out var rngSeed))
            throw Quarantine(
                ReplayQuarantineCodes.InvalidMatchStart,
                "matchlog_adapter",
                "match_start.rngSeed 必须是 Int32",
                matchId,
                matchStart.Seq);
        if (!payload.TryGetProperty("firstPlayer", out var firstPlayerElement)
            || firstPlayerElement.ValueKind != JsonValueKind.Number
            || !firstPlayerElement.TryGetInt32(out var firstPlayer))
            throw Quarantine(
                ReplayQuarantineCodes.InvalidMatchStart,
                "matchlog_adapter",
                "match_start.firstPlayer 必须是整数",
                matchId,
                matchStart.Seq);
        if (!payload.TryGetProperty("openingSetupAfterFirstPlayerChoice", out var deferredElement)
            || deferredElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw Quarantine(
                ReplayQuarantineCodes.MissingReplayConfig,
                "matchlog_adapter",
                "缺少 openingSetupAfterFirstPlayerChoice",
                matchId,
                matchStart.Seq);
        var deferred = deferredElement.GetBoolean();
        if (firstPlayer is not 0 and not 1 && !(firstPlayer == -1 && deferred))
            throw Quarantine(
                ReplayQuarantineCodes.InvalidMatchStart,
                "matchlog_adapter",
                "firstPlayer 只能是 0/1；延迟开局协议允许用 -1 等待选择",
                matchId,
                matchStart.Seq);

        if (!payload.TryGetProperty("replayConfig", out var replayConfig)
            || replayConfig.ValueKind != JsonValueKind.Object
            || !replayConfig.TryGetProperty("leaderKeywordWildcard", out var wildcardElement)
            || wildcardElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw Quarantine(
                ReplayQuarantineCodes.MissingReplayConfig,
                "matchlog_adapter",
                "缺少 replayConfig.leaderKeywordWildcard",
                matchId,
                matchStart.Seq);

        if (!payload.TryGetProperty("players", out var playersElement)
            || playersElement.ValueKind != JsonValueKind.Array
            || playersElement.GetArrayLength() != 2)
            throw Quarantine(
                ReplayQuarantineCodes.InvalidMatchStart,
                "matchlog_adapter",
                "match_start.players 必须恰好包含两个席位",
                matchId,
                matchStart.Seq);

        var players = new ReplayPlayerDeck?[2];
        foreach (var player in playersElement.EnumerateArray())
        {
            if (player.ValueKind != JsonValueKind.Object
                || !player.TryGetProperty("index", out var indexElement)
                || indexElement.ValueKind != JsonValueKind.Number
                || !indexElement.TryGetInt32(out var seat)
                || seat is not 0 and not 1
                || players[seat] is not null)
                throw Quarantine(
                    ReplayQuarantineCodes.InvalidMatchStart,
                    "matchlog_adapter",
                    "players.index 必须唯一覆盖 0 和 1",
                    matchId,
                    matchStart.Seq);
            var deckRaw = RequiredString(
                player,
                "deckRaw",
                ReplayQuarantineCodes.InvalidMatchStart,
                matchStart.Seq,
                matchId);
            if (!player.TryGetProperty("alwaysPromptOnLifeReveal", out var alwaysPromptElement)
                || alwaysPromptElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                throw Quarantine(
                    ReplayQuarantineCodes.MissingReplayConfig,
                    "matchlog_adapter",
                    $"players[{seat}] 缺少 alwaysPromptOnLifeReveal",
                    matchId,
                    matchStart.Seq);
            players[seat] = new ReplayPlayerDeck(seat, deckRaw, alwaysPromptElement.GetBoolean());
        }

        return new ReplayMatchHeader(
            matchId,
            rngSeed,
            firstPlayer,
            players[0]!,
            players[1]!,
            new ReplayConfiguration(wildcardElement.GetBoolean(), deferred),
            identity);
    }

    private static string RequiredVersionString(
        JsonElement payload,
        string propertyName,
        string matchId,
        long seq)
    {
        if (!payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
            throw Quarantine(
                ReplayQuarantineCodes.MissingVersionIdentity,
                "matchlog_adapter",
                $"match_start 缺少精确版本字段 {propertyName}",
                matchId,
                seq);
        return property.GetString()!;
    }

    internal static string RequiredString(
        JsonElement element,
        string propertyName,
        string code,
        long? sourceSeq,
        string? matchId = null)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
            throw Quarantine(
                code,
                "matchlog_adapter",
                $"缺少非空字符串 {propertyName}",
                matchId,
                sourceSeq);
        return property.GetString()!;
    }

    private static ReplayQuarantineException Quarantine(
        string code,
        string stage,
        string message,
        string? matchId = null,
        long? sourceSeq = null)
        => new(code, stage, message, matchId, sourceSeq);
}
