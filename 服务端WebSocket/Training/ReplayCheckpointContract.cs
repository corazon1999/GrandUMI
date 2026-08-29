using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrandUMI.Training;

public enum ReplayCheckpointPosition
{
    Opening,
    AfterAction,
    Terminal,
}

/// <summary>原始日志明确提供的一个稳定点期望；worker 只能核对，不能自行补写。</summary>
public sealed record ExpectedReplayCheckpoint(
    ReplayCheckpointPosition Position,
    long SourceSeq,
    long? ActionOrderSeq,
    string? ActionStableHash,
    string StateDigest,
    string PublicStateDigest,
    string RandomTraceDigest,
    int RandomEventCount,
    string StableHash);

/// <summary>match_end 中冻结的终局语义。</summary>
public sealed record ExpectedReplayTerminal(
    int? WinnerIndex,
    bool IsDraw,
    string Reason,
    int TurnCount,
    string StableHash);

public sealed record ExpectedReplayCheckpointContract(
    string Schema,
    IReadOnlyList<ExpectedReplayCheckpoint> Checkpoints,
    ExpectedReplayTerminal Terminal,
    string StableHash)
{
    public ExpectedReplayCheckpoint Opening => Checkpoints[0];
    public IReadOnlyList<ExpectedReplayCheckpoint> AfterActions
        => Checkpoints.Skip(1).Take(Checkpoints.Count - 2).ToArray();
    public ExpectedReplayCheckpoint TerminalCheckpoint => Checkpoints[^1];
}

/// <summary>
/// 从显式 replay_checkpoint 事件构建不可猜测的期望契约。完全没有 checkpoint 的旧日志
/// 仍可完成动作磁带准备，但 worker 会因契约缺失而 No-Go；部分或畸形契约立即整局隔离。
/// </summary>
internal static class ReplayCheckpointContractParser
{
    public const string Schema = "grandumi.replay_checkpoint.v1";
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    public static ExpectedReplayCheckpointContract? Parse(
        AdaptedMatchLog log,
        AcceptedActionTape tape)
    {
        var disabled = log.Events.FirstOrDefault(entry =>
            string.Equals(entry.Kind, "replay_checkpoint_status", StringComparison.Ordinal));
        if (disabled is not null)
            throw new ReplayQuarantineException(
                ReplayQuarantineCodes.CheckpointContinuityDisabled,
                "checkpoint_contract",
                "日志明确标记 checkpoint 连续性已停用，整局禁止进入训练",
                log.Header.MatchId,
                disabled.Seq);

        var checkpointEvents = log.Events
            .Where(entry => string.Equals(entry.Kind, "replay_checkpoint", StringComparison.Ordinal))
            .ToArray();
        if (checkpointEvents.Length == 0) return null;

        var expectedCount = tape.Actions.Count + 2;
        if (checkpointEvents.Length != expectedCount)
            throw Quarantine(
                $"checkpoint 数量不完整：期望 {expectedCount}（开局 + {tape.Actions.Count} 动作 + 终局），实际 {checkpointEvents.Length}",
                log.Header.MatchId,
                checkpointEvents[0].Seq);

        var checkpoints = checkpointEvents
            .Select(entry => ParseCheckpoint(entry, log.Header.MatchId))
            .ToArray();
        ValidateShape(checkpoints, tape, log);

        var matchEnd = log.Events[^1];
        var terminal = ParseTerminal(matchEnd, log.Header.MatchId);
        var contractHash = HashContract(checkpoints, terminal);
        return new ExpectedReplayCheckpointContract(
            Schema,
            Array.AsReadOnly(checkpoints),
            terminal,
            contractHash);
    }

    private static ExpectedReplayCheckpoint ParseCheckpoint(
        AdaptedMatchLogEvent entry,
        string matchId)
    {
        if (entry.Actor is not (-1) and not null)
            throw Quarantine("replay_checkpoint.actor 必须是 -1 或 null", matchId, entry.Seq);

        EnsureKnownProperties(
            entry.Payload,
            matchId,
            entry.Seq,
            "schema",
            "position",
            "actionOrderSeq",
            "actionStableHash",
            "stateDigest",
            "publicStateDigest",
            "randomTraceDigest",
            "randomEventCount");

        var schema = RequiredString(entry.Payload, "schema", matchId, entry.Seq);
        if (!string.Equals(schema, Schema, StringComparison.Ordinal))
            throw Quarantine($"不支持的 checkpoint schema：{schema}", matchId, entry.Seq);

        var positionText = RequiredString(entry.Payload, "position", matchId, entry.Seq);
        var position = positionText switch
        {
            "opening" => ReplayCheckpointPosition.Opening,
            "after_action" => ReplayCheckpointPosition.AfterAction,
            "terminal" => ReplayCheckpointPosition.Terminal,
            _ => throw Quarantine($"未知 checkpoint position：{positionText}", matchId, entry.Seq),
        };

        long? actionOrderSeq = null;
        if (entry.Payload.TryGetProperty("actionOrderSeq", out var actionOrderElement)
            && actionOrderElement.ValueKind != JsonValueKind.Null)
        {
            if (actionOrderElement.ValueKind != JsonValueKind.Number
                || !actionOrderElement.TryGetInt64(out var parsedOrder))
                throw Quarantine("actionOrderSeq 必须是 Int64 或 null", matchId, entry.Seq);
            actionOrderSeq = parsedOrder;
        }

        string? actionStableHash = null;
        if (entry.Payload.TryGetProperty("actionStableHash", out var actionHashElement)
            && actionHashElement.ValueKind != JsonValueKind.Null)
        {
            if (actionHashElement.ValueKind != JsonValueKind.String)
                throw Quarantine("actionStableHash 必须是字符串或 null", matchId, entry.Seq);
            actionStableHash = actionHashElement.GetString();
            RequireSha256(actionStableHash, "actionStableHash", matchId, entry.Seq);
        }

        var stateDigest = RequiredSha256(entry.Payload, "stateDigest", matchId, entry.Seq);
        var publicStateDigest = RequiredSha256(entry.Payload, "publicStateDigest", matchId, entry.Seq);
        var randomTraceDigest = RequiredSha256(entry.Payload, "randomTraceDigest", matchId, entry.Seq);
        if (!entry.Payload.TryGetProperty("randomEventCount", out var randomCountElement)
            || randomCountElement.ValueKind != JsonValueKind.Number
            || !randomCountElement.TryGetInt32(out var randomEventCount)
            || randomEventCount < 0)
            throw Quarantine("randomEventCount 必须是非负 Int32", matchId, entry.Seq);

        var stableHash = HashCheckpoint(
            position,
            entry.Seq,
            actionOrderSeq,
            actionStableHash,
            stateDigest,
            publicStateDigest,
            randomTraceDigest,
            randomEventCount);
        return new ExpectedReplayCheckpoint(
            position,
            entry.Seq,
            actionOrderSeq,
            actionStableHash,
            stateDigest,
            publicStateDigest,
            randomTraceDigest,
            randomEventCount,
            stableHash);
    }

    private static void ValidateShape(
        IReadOnlyList<ExpectedReplayCheckpoint> checkpoints,
        AcceptedActionTape tape,
        AdaptedMatchLog log)
    {
        if (checkpoints[0].Position != ReplayCheckpointPosition.Opening
            || checkpoints[0].ActionOrderSeq is not null
            || checkpoints[0].ActionStableHash is not null)
            throw Quarantine(
                "首个 checkpoint 必须是无动作绑定的 opening",
                log.Header.MatchId,
                checkpoints[0].SourceSeq);

        if (tape.Actions.Count > 0 && checkpoints[0].SourceSeq >= tape.Actions[0].SourceSeq)
            throw Quarantine(
                "opening checkpoint 必须位于首条动作请求/系统事件之前",
                log.Header.MatchId,
                checkpoints[0].SourceSeq);

        for (var index = 0; index < tape.Actions.Count; index++)
        {
            var action = tape.Actions[index];
            var checkpoint = checkpoints[index + 1];
            if (checkpoint.Position != ReplayCheckpointPosition.AfterAction
                || checkpoint.ActionOrderSeq != action.OrderSeq
                || !string.Equals(
                    checkpoint.ActionStableHash,
                    action.StableHash,
                    StringComparison.Ordinal))
                throw Quarantine(
                    $"第 {index} 个动作 checkpoint 未精确绑定 orderSeq/actionStableHash",
                    log.Header.MatchId,
                    checkpoint.SourceSeq);
            if (checkpoint.SourceSeq <= action.OrderSeq)
                throw Quarantine(
                    $"第 {index} 个动作 checkpoint 必须位于动作应用序号之后",
                    log.Header.MatchId,
                    checkpoint.SourceSeq);
            if (index + 1 < tape.Actions.Count
                && checkpoint.SourceSeq >= tape.Actions[index + 1].SourceSeq)
                throw Quarantine(
                    $"第 {index} 个动作 checkpoint 越过了下一条动作请求/系统事件",
                    log.Header.MatchId,
                    checkpoint.SourceSeq);
        }

        var terminal = checkpoints[^1];
        if (terminal.Position != ReplayCheckpointPosition.Terminal
            || terminal.ActionOrderSeq is not null
            || terminal.ActionStableHash is not null)
            throw Quarantine(
                "最后一个 checkpoint 必须是无动作绑定的 terminal",
                log.Header.MatchId,
                terminal.SourceSeq);
        if (terminal.SourceSeq <= checkpoints[^2].SourceSeq
            || terminal.SourceSeq >= log.Events[^1].Seq)
            throw Quarantine(
                "terminal checkpoint 必须位于最后一个稳定点之后、match_end 之前",
                log.Header.MatchId,
                terminal.SourceSeq);
    }

    private static ExpectedReplayTerminal ParseTerminal(
        AdaptedMatchLogEvent matchEnd,
        string matchId)
    {
        int? winnerIndex;
        if (!matchEnd.Payload.TryGetProperty("winnerIndex", out var winnerElement))
            throw Quarantine("match_end 缺少 winnerIndex", matchId, matchEnd.Seq);
        if (winnerElement.ValueKind == JsonValueKind.Null)
        {
            winnerIndex = null;
        }
        else if (winnerElement.ValueKind == JsonValueKind.Number
                 && winnerElement.TryGetInt32(out var parsedWinner)
                 && parsedWinner is 0 or 1)
        {
            winnerIndex = parsedWinner;
        }
        else
        {
            throw Quarantine("match_end.winnerIndex 必须是 0/1/null", matchId, matchEnd.Seq);
        }

        if (!matchEnd.Payload.TryGetProperty("isDraw", out var drawElement)
            || drawElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw Quarantine("带 checkpoint 契约的 match_end 必须明确记录 isDraw", matchId, matchEnd.Seq);
        var isDraw = drawElement.GetBoolean();
        if (isDraw == winnerIndex.HasValue)
            throw Quarantine(
                "match_end 的 winnerIndex/isDraw 终局语义冲突",
                matchId,
                matchEnd.Seq);

        var reason = RequiredString(matchEnd.Payload, "reason", matchId, matchEnd.Seq);
        if (!matchEnd.Payload.TryGetProperty("turnCount", out var turnElement)
            || turnElement.ValueKind != JsonValueKind.Number
            || !turnElement.TryGetInt32(out var turnCount)
            || turnCount < 0)
            throw Quarantine("match_end.turnCount 必须是非负 Int32", matchId, matchEnd.Seq);

        var canonical = JsonSerializer.SerializeToElement(new
        {
            winnerIndex,
            isDraw,
            reason,
            turnCount,
        });
        return new ExpectedReplayTerminal(
            winnerIndex,
            isDraw,
            reason,
            turnCount,
            CanonicalJson.Hash(canonical));
    }

    private static string HashCheckpoint(
        ReplayCheckpointPosition position,
        long sourceSeq,
        long? actionOrderSeq,
        string? actionStableHash,
        string stateDigest,
        string publicStateDigest,
        string randomTraceDigest,
        int randomEventCount)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            schema = Schema,
            position = PositionName(position),
            sourceSeq,
            actionOrderSeq,
            actionStableHash,
            stateDigest,
            publicStateDigest,
            randomTraceDigest,
            randomEventCount,
        });
        return CanonicalJson.Hash(canonical);
    }

    private static string HashContract(
        IReadOnlyList<ExpectedReplayCheckpoint> checkpoints,
        ExpectedReplayTerminal terminal)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            schema = Schema,
            checkpoints = checkpoints.Select(checkpoint => new
            {
                position = PositionName(checkpoint.Position),
                checkpoint.SourceSeq,
                checkpoint.ActionOrderSeq,
                checkpoint.ActionStableHash,
                checkpoint.StateDigest,
                checkpoint.PublicStateDigest,
                checkpoint.RandomTraceDigest,
                checkpoint.RandomEventCount,
                checkpoint.StableHash,
            }),
            terminal = new
            {
                terminal.WinnerIndex,
                terminal.IsDraw,
                terminal.Reason,
                terminal.TurnCount,
                terminal.StableHash,
            },
        });
        return CanonicalJson.Hash(canonical);
    }

    internal static string PositionName(ReplayCheckpointPosition position)
        => position switch
        {
            ReplayCheckpointPosition.Opening => "opening",
            ReplayCheckpointPosition.AfterAction => "after_action",
            ReplayCheckpointPosition.Terminal => "terminal",
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };

    private static string RequiredSha256(
        JsonElement element,
        string propertyName,
        string matchId,
        long sourceSeq)
    {
        var value = RequiredString(element, propertyName, matchId, sourceSeq);
        RequireSha256(value, propertyName, matchId, sourceSeq);
        return value;
    }

    private static string RequiredString(
        JsonElement element,
        string propertyName,
        string matchId,
        long sourceSeq)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || property.GetString() is not { Length: > 0 } value
            || string.IsNullOrWhiteSpace(value))
            throw Quarantine($"缺少非空字符串 {propertyName}", matchId, sourceSeq);
        return value;
    }

    private static void RequireSha256(
        string? value,
        string propertyName,
        string matchId,
        long sourceSeq)
    {
        if (value is null || !Sha256Pattern.IsMatch(value))
            throw Quarantine(
                $"{propertyName} 必须是小写 sha256: 加 64 位十六进制",
                matchId,
                sourceSeq);
    }

    private static void EnsureKnownProperties(
        JsonElement element,
        string matchId,
        long sourceSeq,
        params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw Quarantine($"checkpoint 包含重复属性：{property.Name}", matchId, sourceSeq);
            if (!allowedSet.Contains(property.Name))
                throw Quarantine($"checkpoint 包含未冻结属性：{property.Name}", matchId, sourceSeq);
        }
    }

    private static ReplayQuarantineException Quarantine(
        string message,
        string matchId,
        long? sourceSeq)
        => new(
            ReplayQuarantineCodes.InvalidCheckpointContract,
            "checkpoint_contract",
            message,
            matchId,
            sourceSeq);
}
