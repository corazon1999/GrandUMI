using System.Text.Json;
using GrandUMI.Game;

namespace GrandUMI.Training;

public static class ReplayQuarantineCodes
{
    public const string EmptyLog = "empty_log";
    public const string InvalidUtf8 = "invalid_utf8";
    public const string IncompleteTail = "incomplete_tail";
    public const string EmptyLine = "empty_line";
    public const string MalformedJson = "malformed_json";
    public const string MalformedEvent = "malformed_event";
    public const string UnsupportedSchema = "unsupported_schema";
    public const string MixedMatchId = "mixed_match_id";
    public const string InvalidSequence = "invalid_sequence";
    public const string SequenceGap = "sequence_gap";
    public const string MissingMatchStart = "missing_match_start";
    public const string DuplicateMatchStart = "duplicate_match_start";
    public const string MissingMatchEnd = "missing_match_end";
    public const string InvalidMatchEnd = "invalid_match_end";
    public const string MissingVersionIdentity = "missing_version_identity";
    public const string InvalidMatchStart = "invalid_match_start";
    public const string MissingReplayConfig = "missing_replay_config";
    public const string UnsupportedArtifact = "unsupported_artifact";
    public const string ArtifactIdentityMismatch = "artifact_identity_mismatch";
    public const string UnsupportedEventAdapter = "unsupported_event_adapter";
    public const string MissingActionData = "missing_action_data";
    public const string AcceptedActionDataMismatch = "accepted_action_data_mismatch";
    public const string DuplicateRequestCorrelation = "duplicate_request_correlation";
    public const string MalformedSystemEvent = "malformed_system_event";
    public const string UnsupportedSystemEvent = "unsupported_system_event";
    public const string OrphanActionResult = "orphan_action_result";
    public const string AmbiguousActionPairing = "ambiguous_action_pairing";
    public const string SystemActionRejected = "system_action_rejected";
    public const string PromptResponseMismatch = "prompt_response_mismatch";
    public const string UnresolvedAction = "unresolved_action";
    public const string OrphanPromptResponse = "orphan_prompt_response";
    public const string AmbiguousActionOrder = "ambiguous_action_order";
    public const string NonCanonicalPayload = "non_canonical_payload";
    public const string InvalidActor = "invalid_actor";
    public const string MalformedActionResult = "malformed_action_result";
    public const string InvalidCheckpointContract = "invalid_checkpoint_contract";
    public const string CheckpointContinuityDisabled = "checkpoint_continuity_disabled";
    public const string MissingCheckpointContract = "missing_checkpoint_contract";
    public const string WorkerNotRegistered = "worker_not_registered";
    public const string WorkerArtifactMismatch = "worker_artifact_mismatch";
    public const string WorkerProtocolMismatch = "worker_protocol_mismatch";
    public const string WorkerTimeout = "worker_timeout";
    public const string WorkerCancelled = "worker_cancelled";
    public const string WorkerFailure = "worker_failure";
    public const string StableWaitTimeout = "stable_wait_timeout";
    public const string ReplayActionRejected = "replay_action_rejected";
    public const string TapeContinuesAfterGameOver = "tape_continues_after_game_over";
    public const string StateCheckpointMismatch = "state_checkpoint_mismatch";
    public const string PublicCheckpointMismatch = "public_checkpoint_mismatch";
    public const string RandomTraceMismatch = "random_trace_mismatch";
    public const string TerminalMismatch = "terminal_mismatch";
}

public sealed class ReplayQuarantineException : Exception
{
    public ReplayQuarantineException(
        string reasonCode,
        string stage,
        string message,
        string? matchId = null,
        long? sourceSeq = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        Stage = stage;
        MatchId = matchId;
        SourceSeq = sourceSeq;
    }

    public string ReasonCode { get; }
    public string Stage { get; }
    public string? MatchId { get; }
    public long? SourceSeq { get; }
}

public sealed record QuarantinedReplayMatch(
    string SourceId,
    string SourceFileHash,
    string? MatchId,
    string ReasonCode,
    string Stage,
    long? SourceSeq,
    string Message,
    string StableHash);

public sealed class PreparedReplayMatch
{
    internal PreparedReplayMatch(
        string sourceId,
        string sourceFileHash,
        ReplayMatchHeader header,
        ReplayArtifactDescriptor artifact,
        AcceptedActionTape tape,
        ExpectedReplayCheckpointContract? checkpointContract,
        string registryVersion,
        string registryHash,
        string stableHash)
    {
        SourceId = sourceId;
        SourceFileHash = sourceFileHash;
        Header = header;
        Artifact = artifact;
        Tape = tape;
        CheckpointContract = checkpointContract;
        RegistryVersion = registryVersion;
        RegistryHash = registryHash;
        StableHash = stableHash;
    }

    public string SourceId { get; }
    public string SourceFileHash { get; }
    public ReplayMatchHeader Header { get; }
    public ReplayArtifactDescriptor Artifact { get; }
    public AcceptedActionTape Tape { get; }
    public ExpectedReplayCheckpointContract? CheckpointContract { get; }
    public string RegistryVersion { get; }
    public string RegistryHash { get; }
    public string StableHash { get; }

    /// <summary>
    /// 为注册工件 worker 生成 MatchReplay 入参形态；准备层只物化动作，是否进程内执行由
    /// 精确指纹绑定的 dispatcher/worker 决定，绝不回退到当前 main。
    /// </summary>
    public IReadOnlyList<MatchReplay.ActionEntry> MaterializeActionEntries()
        => Tape.Actions
            .Select(action => new MatchReplay.ActionEntry(
                action.ActorSeat,
                action.Action,
                action.Data.Clone()))
            .ToArray();
}

public sealed class ReplayPreparationResult
{
    private ReplayPreparationResult(PreparedReplayMatch? prepared, QuarantinedReplayMatch? quarantine)
    {
        Prepared = prepared;
        Quarantine = quarantine;
    }

    public PreparedReplayMatch? Prepared { get; }
    public QuarantinedReplayMatch? Quarantine { get; }
    public bool IsPrepared => Prepared is not null;

    internal static ReplayPreparationResult Success(PreparedReplayMatch prepared)
        => new(prepared, quarantine: null);

    internal static ReplayPreparationResult Isolated(QuarantinedReplayMatch quarantine)
        => new(prepared: null, quarantine);
}

/// <summary>从整局原始字节到“可交给精确工件重放”的全有或全无准备入口。</summary>
public static class ReplayMatchPreparation
{
    public static ReplayPreparationResult Prepare(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceId,
        ReplayArtifactRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var sourceHash = CanonicalJson.Sha256(sourceBytes.Span);
        AdaptedMatchLog? log = null;
        try
        {
            log = MatchLogEventAdapter.Adapt(sourceBytes, sourceId);
            var artifact = registry.Resolve(log.Header.VersionIdentity);
            var tape = AcceptedActionTapeBuilder.Build(log, artifact);
            var checkpointContract = ReplayCheckpointContractParser.Parse(log, tape);
            var stableHash = HashPrepared(
                log,
                artifact,
                tape,
                checkpointContract,
                registry.RegistryHash);
            return ReplayPreparationResult.Success(new PreparedReplayMatch(
                log.SourceId,
                log.SourceFileHash,
                log.Header,
                artifact,
                tape,
                checkpointContract,
                registry.RegistryVersion,
                registry.RegistryHash,
                stableHash));
        }
        catch (ReplayQuarantineException ex)
        {
            var matchId = ex.MatchId ?? log?.Header.MatchId;
            var quarantineHash = HashQuarantine(
                sourceHash,
                matchId,
                ex.ReasonCode,
                ex.Stage,
                ex.SourceSeq);
            return ReplayPreparationResult.Isolated(new QuarantinedReplayMatch(
                string.IsNullOrWhiteSpace(sourceId) ? "memory" : sourceId,
                sourceHash,
                matchId,
                ex.ReasonCode,
                ex.Stage,
                ex.SourceSeq,
                ex.Message,
                quarantineHash));
        }
    }

    private static string HashPrepared(
        AdaptedMatchLog log,
        ReplayArtifactDescriptor artifact,
        AcceptedActionTape tape,
        ExpectedReplayCheckpointContract? checkpointContract,
        string registryHash)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            log.SourceFileHash,
            registryHash,
            artifact.EngineArtifactId,
            artifact.EngineCommit,
            artifact.BinarySha256,
            artifact.RulesVersion,
            artifact.RulesetManifestHash,
            artifact.CardDbContentHash,
            artifact.RngAlgorithmVersion,
            artifact.DeterministicIdVersion,
            artifact.OpeningProtocolVersion,
            artifact.ReplayConfigSchema,
            matchId = log.Header.MatchId,
            log.Header.RngSeed,
            log.Header.FirstPlayer,
            player0DeckHash = CanonicalJson.Sha256Utf8(log.Header.Player0.DeckRaw),
            player1DeckHash = CanonicalJson.Sha256Utf8(log.Header.Player1.DeckRaw),
            log.Header.Player0.AlwaysPromptOnLifeReveal,
            player1AlwaysPromptOnLifeReveal = log.Header.Player1.AlwaysPromptOnLifeReveal,
            log.Header.Configuration.LeaderKeywordWildcard,
            log.Header.Configuration.OpeningSetupAfterFirstPlayerChoice,
            tape.StableHash,
            checkpointContractHash = checkpointContract?.StableHash ?? string.Empty,
        });
        return CanonicalJson.Hash(canonical);
    }

    private static string HashQuarantine(
        string sourceFileHash,
        string? matchId,
        string reasonCode,
        string stage,
        long? sourceSeq)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            sourceFileHash,
            matchId = matchId ?? string.Empty,
            reasonCode,
            stage,
            sourceSeq,
        });
        return CanonicalJson.Hash(canonical);
    }
}
