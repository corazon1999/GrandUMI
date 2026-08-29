using System.Text.Json;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;

namespace GrandUMI.Training;

public sealed record ArtifactReplayAction(
    int ActionIndex,
    long OrderSeq,
    long SourceSeq,
    long? ResultSeq,
    int ActorSeat,
    string Action,
    JsonElement Data,
    ReplayActionSource Source,
    string StableHash);

/// <summary>可 JSON 序列化的 worker 请求；未来独立进程实现复用同一边界。</summary>
public sealed record ArtifactReplayWorkerRequest(
    string Schema,
    string RequestHash,
    string SourceId,
    string SourceFileHash,
    string PreparedHash,
    string TapeHash,
    string RegistryVersion,
    string RegistryHash,
    ReplayArtifactDescriptor Artifact,
    string ArtifactFingerprint,
    ReplayMatchHeader Header,
    IReadOnlyList<ArtifactReplayAction> Actions,
    ExpectedReplayCheckpointContract CheckpointContract,
    int StableTimeoutMilliseconds);

public sealed record ReplayRandomTraceEvent(int? Actor, JsonElement Payload);

public sealed record ReplayCheckpointContext(
    ReplayCheckpointPosition Position,
    int ActionIndex,
    long? ActionOrderSeq,
    string? ActionStableHash);

/// <summary>
/// artifact 自己提供的 checkpoint 算法。P0-A worker 只做逐点编排和比较，不假定不同历史
/// artifact 可以共享当前 main 的状态编码；测试 fixture 可注入当前版本实现。
/// </summary>
public interface IReplayCheckpointProvider
{
    ReplayCheckpointDigest Capture(
        GameEngine engine,
        ReplayCheckpointContext context,
        IReadOnlyList<ReplayRandomTraceEvent> randomTrace);
}

public sealed record ReplayCheckpointDigest(
    string StateDigest,
    string PublicStateDigest,
    string RandomTraceDigest,
    int RandomEventCount);

public sealed record VerifiedReplayCheckpoint(
    ReplayCheckpointPosition Position,
    int ActionIndex,
    long? ActionOrderSeq,
    string? ActionStableHash,
    string StateDigest,
    string PublicStateDigest,
    string RandomTraceDigest,
    int RandomEventCount,
    string StableHash);

public sealed record VerifiedReplayTerminal(
    int? WinnerIndex,
    bool IsDraw,
    string Reason,
    int TurnCount,
    string StableHash);

public sealed record VerifiedArtifactReplay(
    string SourceId,
    string SourceFileHash,
    string MatchId,
    string PreparedHash,
    string TapeHash,
    string CheckpointContractHash,
    string RegistryVersion,
    string RegistryHash,
    string EngineArtifactId,
    string ArtifactFingerprint,
    string WorkerId,
    string RequestHash,
    IReadOnlyList<VerifiedReplayCheckpoint> Checkpoints,
    VerifiedReplayTerminal Terminal,
    string ReplayDigest,
    string StableHash);

public sealed record ArtifactReplayWorkerFailure(
    string ReasonCode,
    string Stage,
    string Message,
    long? SourceSeq,
    int? ActionIndex);

public sealed record ArtifactReplayWorkerResponse(
    string Schema,
    string RequestHash,
    string ArtifactFingerprint,
    string WorkerId,
    VerifiedArtifactReplay? Verified,
    ArtifactReplayWorkerFailure? Failure,
    string StableHash);

/// <summary>可由进程代理、容器代理或本轮进程内实现替换的隔离边界。</summary>
public interface IArtifactReplayWorker
{
    string WorkerId { get; }
    string EngineArtifactId { get; }
    string ArtifactFingerprint { get; }

    Task<ArtifactReplayWorkerResponse> ExecuteAsync(
        ArtifactReplayWorkerRequest request,
        CancellationToken cancellationToken);
}

public sealed class ArtifactReplayExecutionResult
{
    private ArtifactReplayExecutionResult(
        VerifiedArtifactReplay? verified,
        QuarantinedReplayMatch? quarantine)
    {
        Verified = verified;
        Quarantine = quarantine;
    }

    public VerifiedArtifactReplay? Verified { get; }
    public QuarantinedReplayMatch? Quarantine { get; }
    public bool IsVerified => Verified is not null;

    internal static ArtifactReplayExecutionResult Success(VerifiedArtifactReplay verified)
        => new(verified, quarantine: null);

    internal static ArtifactReplayExecutionResult Isolated(QuarantinedReplayMatch quarantine)
        => new(verified: null, quarantine);
}

/// <summary>按完整 artifact 指纹路由；同 ID 但任一哈希不同都不会启动 worker。</summary>
public sealed class ArtifactReplayWorkerDispatcher
{
    public const string RequestSchema = "grandumi.artifact_replay_request.v2";
    public const string ResponseSchema = "grandumi.artifact_replay_response.v2";
    private readonly IReadOnlyDictionary<string, IArtifactReplayWorker> _workersByArtifactId;

    public ArtifactReplayWorkerDispatcher(IEnumerable<IArtifactReplayWorker> workers)
    {
        ArgumentNullException.ThrowIfNull(workers);
        var byId = new Dictionary<string, IArtifactReplayWorker>(StringComparer.Ordinal);
        foreach (var worker in workers)
        {
            ArgumentNullException.ThrowIfNull(worker);
            if (!byId.TryAdd(worker.EngineArtifactId, worker))
                throw new InvalidOperationException(
                    $"同一 engineArtifactId 只能登记一个 worker：{worker.EngineArtifactId}");
        }
        _workersByArtifactId = byId;
    }

    public async Task<ArtifactReplayExecutionResult> ExecuteAsync(
        PreparedReplayMatch prepared,
        int stableTimeoutMilliseconds = 15_000,
        int workerTimeoutMilliseconds = 120_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (stableTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(stableTimeoutMilliseconds));
        if (workerTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerTimeoutMilliseconds));

        if (prepared.CheckpointContract is null)
            return Isolate(
                prepared,
                ReplayQuarantineCodes.MissingCheckpointContract,
                "artifact_replay_dispatch",
                "日志没有显式、完整的 checkpoint 契约；禁止用当前状态反向生成期望值",
                sourceSeq: null,
                requestHash: null);

        var artifactFingerprint = ReplayArtifactIdentity.Fingerprint(prepared.Artifact);
        if (!_workersByArtifactId.TryGetValue(
                prepared.Artifact.EngineArtifactId,
                out var worker))
            return Isolate(
                prepared,
                ReplayQuarantineCodes.WorkerNotRegistered,
                "artifact_replay_dispatch",
                $"未登记工件 worker：{prepared.Artifact.EngineArtifactId}",
                sourceSeq: null,
                requestHash: null);

        if (!string.Equals(worker.ArtifactFingerprint, artifactFingerprint, StringComparison.Ordinal))
            return Isolate(
                prepared,
                ReplayQuarantineCodes.WorkerArtifactMismatch,
                "artifact_replay_dispatch",
                "worker 声明的完整工件指纹与注册表不一致",
                sourceSeq: null,
                requestHash: null);

        var request = BuildRequest(prepared, artifactFingerprint, stableTimeoutMilliseconds);
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        workerCancellation.CancelAfter(workerTimeoutMilliseconds);
        try
        {
            var execution = worker.ExecuteAsync(request, workerCancellation.Token);
            // 独立进程代理必须亲自完成 Kill(entireProcessTree)+有界 WaitForExit 后才能返回；
            // 对它再套 WaitAsync 会让 dispatcher 提前结束并把清理中的子进程遗留到批次之后。
            var response = worker is ProcessArtifactReplayWorker
                ? await execution
                : await execution.WaitAsync(workerCancellation.Token);
            if (workerCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                return Isolate(
                    prepared,
                    ReplayQuarantineCodes.WorkerTimeout,
                    "artifact_replay_dispatch",
                    "artifact worker 超过整局执行时限并已收到取消信号",
                    sourceSeq: null,
                    request.RequestHash);
            if (!IsValidResponseEnvelope(response, request, worker))
                return Isolate(
                    prepared,
                    ReplayQuarantineCodes.WorkerProtocolMismatch,
                    "artifact_replay_dispatch",
                    "worker 响应的 schema/requestHash/artifactFingerprint 或成功失败互斥关系无效",
                    sourceSeq: null,
                    request.RequestHash);

            if (response.Failure is { } failure)
                return Isolate(
                    prepared,
                    failure.ReasonCode,
                    failure.Stage,
                    failure.Message,
                    failure.SourceSeq,
                    request.RequestHash);

            return ArtifactReplayExecutionResult.Success(response.Verified!);
        }
        catch (TimeoutException ex)
        {
            return Isolate(
                prepared,
                ReplayQuarantineCodes.WorkerTimeout,
                "artifact_replay_dispatch",
                ex.Message,
                sourceSeq: null,
                request.RequestHash);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Isolate(
                prepared,
                ReplayQuarantineCodes.WorkerTimeout,
                "artifact_replay_dispatch",
                ex.Message,
                sourceSeq: null,
                request.RequestHash);
        }
        catch (OperationCanceledException ex)
        {
            return Isolate(
                prepared,
                ReplayQuarantineCodes.WorkerCancelled,
                "artifact_replay_dispatch",
                ex.Message,
                sourceSeq: null,
                request.RequestHash);
        }
        catch (ArtifactReplayProcessException ex)
        {
            return Isolate(
                prepared,
                ex.ReasonCode,
                "artifact_process_worker",
                ex.Message,
                sourceSeq: null,
                request.RequestHash);
        }
        catch (Exception ex)
        {
            return Isolate(
                prepared,
                ReplayQuarantineCodes.WorkerFailure,
                "artifact_replay_dispatch",
                ex.Message,
                sourceSeq: null,
                request.RequestHash);
        }
    }

    internal static ArtifactReplayWorkerRequest BuildRequest(
        PreparedReplayMatch prepared,
        string artifactFingerprint,
        int stableTimeoutMilliseconds)
    {
        // 这里是 P0-0 PreparedReplayMatch 到真正 MatchReplay 动作入口的唯一物化点。
        var materialized = prepared.MaterializeActionEntries();
        var actions = materialized.Select((entry, index) =>
        {
            var lineage = prepared.Tape.Actions[index];
            return new ArtifactReplayAction(
                index,
                lineage.OrderSeq,
                lineage.SourceSeq,
                lineage.ResultSeq,
                entry.PlayerIndex,
                entry.Action,
                entry.Data.Clone(),
                lineage.Source,
                lineage.StableHash);
        }).ToArray();

        var requestWithoutHash = new ArtifactReplayWorkerRequest(
            RequestSchema,
            string.Empty,
            prepared.SourceId,
            prepared.SourceFileHash,
            prepared.StableHash,
            prepared.Tape.StableHash,
            prepared.RegistryVersion,
            prepared.RegistryHash,
            prepared.Artifact,
            artifactFingerprint,
            prepared.Header,
            Array.AsReadOnly(actions),
            prepared.CheckpointContract!,
            stableTimeoutMilliseconds);
        return requestWithoutHash with
        {
            RequestHash = HashRequest(requestWithoutHash),
        };
    }

    internal static string HashRequest(ArtifactReplayWorkerRequest request)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            request.Schema,
            request.SourceId,
            request.SourceFileHash,
            request.PreparedHash,
            request.TapeHash,
            request.RegistryVersion,
            request.RegistryHash,
            request.Artifact,
            request.ArtifactFingerprint,
            request.Header,
            actions = request.Actions.Select(action => new
            {
                action.ActionIndex,
                action.OrderSeq,
                action.SourceSeq,
                action.ResultSeq,
                action.ActorSeat,
                action.Action,
                action.Data,
                source = action.Source.ToString().ToLowerInvariant(),
                action.StableHash,
            }),
            request.CheckpointContract,
            request.StableTimeoutMilliseconds,
        });
        return CanonicalJson.Hash(canonical);
    }

    internal static bool IsValidResponseEnvelope(
        ArtifactReplayWorkerResponse response,
        ArtifactReplayWorkerRequest request,
        IArtifactReplayWorker worker)
    {
        if (!string.Equals(response.Schema, ResponseSchema, StringComparison.Ordinal)
            || !string.Equals(response.RequestHash, request.RequestHash, StringComparison.Ordinal)
            || !string.Equals(response.ArtifactFingerprint, request.ArtifactFingerprint, StringComparison.Ordinal)
            || !string.Equals(response.WorkerId, worker.WorkerId, StringComparison.Ordinal)
            || (response.Verified is null) == (response.Failure is null)
            || !string.Equals(response.StableHash, HashResponse(
                response.RequestHash,
                response.ArtifactFingerprint,
                response.WorkerId,
                response.Verified,
                response.Failure), StringComparison.Ordinal))
            return false;

        if (response.Verified is { } verified)
        {
            return verified.Checkpoints is not null
                && verified.Terminal is not null
                && string.Equals(verified.SourceId, request.SourceId, StringComparison.Ordinal)
                && string.Equals(verified.SourceFileHash, request.SourceFileHash, StringComparison.Ordinal)
                && string.Equals(verified.MatchId, request.Header.MatchId, StringComparison.Ordinal)
                && string.Equals(verified.PreparedHash, request.PreparedHash, StringComparison.Ordinal)
                && string.Equals(verified.TapeHash, request.TapeHash, StringComparison.Ordinal)
                && string.Equals(
                    verified.CheckpointContractHash,
                    request.CheckpointContract.StableHash,
                    StringComparison.Ordinal)
                && string.Equals(verified.RegistryVersion, request.RegistryVersion, StringComparison.Ordinal)
                && string.Equals(verified.RegistryHash, request.RegistryHash, StringComparison.Ordinal)
                && string.Equals(
                    verified.EngineArtifactId,
                    request.Artifact.EngineArtifactId,
                    StringComparison.Ordinal)
                && string.Equals(verified.RequestHash, request.RequestHash, StringComparison.Ordinal)
                && string.Equals(
                    verified.ArtifactFingerprint,
                    request.ArtifactFingerprint,
                    StringComparison.Ordinal)
                && string.Equals(verified.WorkerId, worker.WorkerId, StringComparison.Ordinal)
                && ArtifactReplayMessageValidator.IsValidVerified(request, verified);
        }

        var failure = response.Failure!;
        var sourceSeqInRange = failure.SourceSeq is null
            || failure.SourceSeq is >= 1
                && failure.SourceSeq <= request.CheckpointContract.TerminalCheckpoint.SourceSeq;
        var actionIndexInRange = failure.ActionIndex is null
            || failure.ActionIndex >= 0 && failure.ActionIndex < request.Actions.Count;
        return IsValidProtocolToken(failure.ReasonCode)
            && IsValidProtocolToken(failure.Stage)
            && failure.Message is { Length: > 0 and <= 2048 }
            && sourceSeqInRange
            && actionIndexInRange;
    }

    /// <summary>
    /// 独立进程响应的规范哈希。成功响应覆盖完整 verified payload；失败响应刻意排除 Message，
    /// 只冻结稳定的分类与定位字段，避免异常文字泄露或运行时差异破坏幂等性。
    /// </summary>
    internal static string HashResponse(
        string requestHash,
        string artifactFingerprint,
        string workerId,
        VerifiedArtifactReplay? verified,
        ArtifactReplayWorkerFailure? failure)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            schema = ResponseSchema,
            requestHash,
            artifactFingerprint,
            workerId,
            verified,
            failure = failure is null
                ? null
                : new
                {
                    failure.ReasonCode,
                    failure.Stage,
                    failure.SourceSeq,
                    failure.ActionIndex,
                },
        });
        return CanonicalJson.Hash(canonical);
    }

    private static bool IsValidProtocolToken(string? value)
        => value is { Length: > 0 and <= 128 }
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '_' or '-' or '.');

    private static ArtifactReplayExecutionResult Isolate(
        PreparedReplayMatch prepared,
        string reasonCode,
        string stage,
        string message,
        long? sourceSeq,
        string? requestHash)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            prepared.SourceFileHash,
            prepared.StableHash,
            artifactFingerprint = ReplayArtifactIdentity.Fingerprint(prepared.Artifact),
            requestHash = requestHash ?? string.Empty,
            reasonCode,
            stage,
            sourceSeq,
        });
        return ArtifactReplayExecutionResult.Isolated(new QuarantinedReplayMatch(
            prepared.SourceId,
            prepared.SourceFileHash,
            prepared.Header.MatchId,
            reasonCode,
            stage,
            sourceSeq,
            message,
            CanonicalJson.Hash(canonical)));
    }
}

internal static class ArtifactReplayMessageValidator
{
    private const int MaximumActions = 200_000;

    public static bool IsValidRequest(
        ArtifactReplayWorkerRequest? request,
        ReplayArtifactDescriptor expectedArtifact,
        string expectedFingerprint,
        out string message)
    {
        message = "worker 请求无效";
        try
        {
            if (request is null
                || request.Artifact is null
                || request.Header is null
                || request.Header.Player0 is null
                || request.Header.Player1 is null
                || request.Header.Configuration is null
                || request.Header.VersionIdentity is null
                || request.Actions is null
                || request.CheckpointContract is null
                || request.CheckpointContract.Checkpoints is null
                || request.CheckpointContract.Terminal is null)
                return false;
            if (!string.Equals(request.Schema, ArtifactReplayWorkerDispatcher.RequestSchema, StringComparison.Ordinal)
                || !string.Equals(request.RequestHash, ArtifactReplayWorkerDispatcher.HashRequest(request), StringComparison.Ordinal)
                || !string.Equals(request.ArtifactFingerprint, expectedFingerprint, StringComparison.Ordinal)
                || !string.Equals(ReplayArtifactIdentity.Fingerprint(request.Artifact), expectedFingerprint, StringComparison.Ordinal)
                || request.Artifact != expectedArtifact)
                return false;
            if (!IsSha256(request.RequestHash)
                || !IsSha256(request.SourceFileHash)
                || !IsSha256(request.PreparedHash)
                || !IsSha256(request.TapeHash)
                || !IsSha256(request.RegistryHash)
                || !IsSha256(request.ArtifactFingerprint)
                || request.SourceId is not { Length: > 0 and <= 1024 }
                || request.RegistryVersion is not { Length: > 0 and <= 512 }
                || request.Header.MatchId is not { Length: > 0 and <= 512 }
                || request.Header.FirstPlayer is not 0 and not 1
                || request.Header.Player0.Seat != 0
                || request.Header.Player1.Seat != 1
                || request.StableTimeoutMilliseconds is <= 0 or > 120_000
                || request.Actions.Count > MaximumActions
                || !VersionMatches(request.Header.VersionIdentity, expectedArtifact))
                return false;

            var canonicalActions = new List<AcceptedActionTapeEntry>(request.Actions.Count);
            long previousOrder = 0;
            for (var index = 0; index < request.Actions.Count; index++)
            {
                var action = request.Actions[index];
                if (action is null
                    || action.ActionIndex != index
                    || action.OrderSeq <= previousOrder
                    || action.SourceSeq <= 0
                    || (action.ResultSeq is null
                        ? action.Source != ReplayActionSource.System
                            || action.OrderSeq != action.SourceSeq
                        : action.ResultSeq <= 0
                            || action.OrderSeq != action.ResultSeq
                            || action.SourceSeq > action.ResultSeq)
                    || action.ActorSeat is not 0 and not 1
                    || action.Action is not { Length: > 0 and <= 256 }
                    || action.Data.ValueKind != JsonValueKind.Object
                    || !Enum.IsDefined(action.Source))
                    return false;
                var canonical = AcceptedActionCanonicalizer.Create(
                    action.OrderSeq,
                    action.SourceSeq,
                    action.ResultSeq,
                    action.ActorSeat,
                    action.Action,
                    action.Data,
                    action.Source);
                if (!string.Equals(canonical.StableHash, action.StableHash, StringComparison.Ordinal)
                    || !canonical.Data.GetRawText().Equals(action.Data.GetRawText(), StringComparison.Ordinal))
                    return false;
                canonicalActions.Add(new AcceptedActionTapeEntry(
                    canonical.OrderSeq,
                    canonical.SourceSeq,
                    canonical.ResultSeq,
                    canonical.ActorSeat,
                    canonical.Action,
                    canonical.Data,
                    canonical.Source,
                    canonical.IsTrainingLabelCandidate,
                    canonical.StableHash));
                previousOrder = action.OrderSeq;
            }

            var tapeHash = AcceptedActionTapeBuilder.HashTape(
                canonicalActions.AsReadOnly(),
                request.Header.MatchId);
            if (!string.Equals(tapeHash, request.TapeHash, StringComparison.Ordinal)
                || !IsValidCheckpointContract(request.CheckpointContract, request.Actions))
                return false;
            var preparedHash = ReplayMatchPreparation.HashPrepared(
                request.SourceFileHash,
                request.Header,
                request.Artifact,
                request.TapeHash,
                request.CheckpointContract.StableHash,
                request.RegistryHash);
            if (!string.Equals(preparedHash, request.PreparedHash, StringComparison.Ordinal))
                return false;
            message = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            message = ex.Message;
            return false;
        }
    }

    public static bool IsValidVerified(
        ArtifactReplayWorkerRequest request,
        VerifiedArtifactReplay verified)
    {
        if (verified.Checkpoints.Count != request.Actions.Count + 2) return false;
        for (var index = 0; index < verified.Checkpoints.Count; index++)
        {
            var actual = verified.Checkpoints[index];
            var expected = request.CheckpointContract.Checkpoints[index];
            var expectedActionIndex = index == 0
                ? -1
                : index == verified.Checkpoints.Count - 1
                    ? request.Actions.Count
                    : index - 1;
            var action = index > 0 && index <= request.Actions.Count
                ? request.Actions[index - 1]
                : null;
            if (actual.Position != expected.Position
                || actual.ActionIndex != expectedActionIndex
                || actual.ActionOrderSeq != action?.OrderSeq
                || !string.Equals(actual.ActionStableHash, action?.StableHash, StringComparison.Ordinal)
                || !string.Equals(actual.StateDigest, expected.StateDigest, StringComparison.Ordinal)
                || !string.Equals(actual.PublicStateDigest, expected.PublicStateDigest, StringComparison.Ordinal)
                || !string.Equals(actual.RandomTraceDigest, expected.RandomTraceDigest, StringComparison.Ordinal)
                || actual.RandomEventCount != expected.RandomEventCount
                || !string.Equals(actual.StableHash, HashVerifiedCheckpoint(actual), StringComparison.Ordinal))
                return false;
        }

        var expectedTerminal = request.CheckpointContract.Terminal;
        var expectedCategory = ReplayTerminalSemantics.ReasonCategory(
            expectedTerminal.Reason,
            expectedTerminal.IsDraw);
        var actualCategory = ReplayTerminalSemantics.ReasonCategory(
            verified.Terminal.Reason,
            verified.Terminal.IsDraw);
        if (verified.Terminal.WinnerIndex != expectedTerminal.WinnerIndex
            || verified.Terminal.IsDraw != expectedTerminal.IsDraw
            || verified.Terminal.TurnCount != expectedTerminal.TurnCount
            || !string.Equals(actualCategory, expectedCategory, StringComparison.Ordinal)
            || (string.Equals(actualCategory, "unclassified", StringComparison.Ordinal)
                && !string.Equals(verified.Terminal.Reason, expectedTerminal.Reason, StringComparison.Ordinal))
            || !string.Equals(
                verified.Terminal.StableHash,
                HashVerifiedTerminal(verified.Terminal),
                StringComparison.Ordinal))
            return false;

        var replayDigest = HashReplayDigest(request, verified.Checkpoints, verified.Terminal);
        return string.Equals(verified.ReplayDigest, replayDigest, StringComparison.Ordinal)
            && string.Equals(
                verified.StableHash,
                HashVerifiedReplay(replayDigest, verified.WorkerId, request.CheckpointContract.StableHash),
                StringComparison.Ordinal);
    }

    public static string HashVerifiedCheckpoint(VerifiedReplayCheckpoint checkpoint)
        => CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            position = ReplayCheckpointContractParser.PositionName(checkpoint.Position),
            checkpoint.ActionIndex,
            checkpoint.ActionOrderSeq,
            checkpoint.ActionStableHash,
            checkpoint.StateDigest,
            checkpoint.PublicStateDigest,
            checkpoint.RandomTraceDigest,
            checkpoint.RandomEventCount,
        }));

    public static string HashVerifiedTerminal(VerifiedReplayTerminal terminal)
        => CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            terminal.WinnerIndex,
            terminal.IsDraw,
            reasonCategory = ReplayTerminalSemantics.ReasonCategory(
                terminal.Reason,
                terminal.IsDraw),
            terminal.TurnCount,
        }));

    public static string HashReplayDigest(
        ArtifactReplayWorkerRequest request,
        IReadOnlyList<VerifiedReplayCheckpoint> checkpoints,
        VerifiedReplayTerminal terminal)
        => CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            request.SourceFileHash,
            request.PreparedHash,
            request.RegistryHash,
            request.ArtifactFingerprint,
            request.RequestHash,
            checkpointHashes = checkpoints.Select(checkpoint => checkpoint.StableHash),
            terminalHash = terminal.StableHash,
        }));

    public static string HashVerifiedReplay(
        string replayDigest,
        string workerId,
        string checkpointContractHash)
        => CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            replayDigest,
            workerId,
            checkpointContractHash,
        }));

    private static bool IsValidCheckpointContract(
        ExpectedReplayCheckpointContract contract,
        IReadOnlyList<ArtifactReplayAction> actions)
    {
        if (!string.Equals(contract.Schema, ReplayCheckpointContractParser.Schema, StringComparison.Ordinal)
            || contract.Checkpoints.Count != actions.Count + 2
            || contract.Checkpoints.Count < 2
            || contract.Terminal.Reason is not { Length: <= 2048 }
            || contract.Terminal.TurnCount < 0
            || contract.Terminal.WinnerIndex is not null and not 0 and not 1
            || contract.Terminal.IsDraw == contract.Terminal.WinnerIndex.HasValue
            || !string.Equals(
                contract.Terminal.StableHash,
                ReplayCheckpointContractParser.HashTerminal(contract.Terminal),
                StringComparison.Ordinal))
            return false;
        long previousSourceSeq = 0;
        var previousRandomEventCount = 0;
        for (var index = 0; index < contract.Checkpoints.Count; index++)
        {
            var checkpoint = contract.Checkpoints[index];
            if (checkpoint is null
                || checkpoint.SourceSeq <= previousSourceSeq
                || checkpoint.RandomEventCount < 0
                || checkpoint.RandomEventCount < previousRandomEventCount
                || !IsSha256(checkpoint.StateDigest)
                || !IsSha256(checkpoint.PublicStateDigest)
                || !IsSha256(checkpoint.RandomTraceDigest)
                || !string.Equals(
                    checkpoint.StableHash,
                    ReplayCheckpointContractParser.HashCheckpoint(
                        checkpoint.Position,
                        checkpoint.SourceSeq,
                        checkpoint.ActionOrderSeq,
                        checkpoint.ActionStableHash,
                        checkpoint.StateDigest,
                        checkpoint.PublicStateDigest,
                        checkpoint.RandomTraceDigest,
                        checkpoint.RandomEventCount),
                    StringComparison.Ordinal))
                return false;
            if (index == 0
                && (checkpoint.Position != ReplayCheckpointPosition.Opening
                    || checkpoint.ActionOrderSeq is not null
                    || checkpoint.ActionStableHash is not null
                    || actions.Count > 0 && checkpoint.SourceSeq >= actions[0].SourceSeq))
                return false;
            if (index > 0 && index <= actions.Count)
            {
                var action = actions[index - 1];
                if (checkpoint.Position != ReplayCheckpointPosition.AfterAction
                    || checkpoint.ActionOrderSeq != action.OrderSeq
                    || !string.Equals(checkpoint.ActionStableHash, action.StableHash, StringComparison.Ordinal)
                    || checkpoint.SourceSeq <= action.OrderSeq
                    || index < actions.Count
                        && checkpoint.SourceSeq >= actions[index].SourceSeq)
                    return false;
            }
            if (index == contract.Checkpoints.Count - 1
                && (checkpoint.Position != ReplayCheckpointPosition.Terminal
                    || checkpoint.ActionOrderSeq is not null
                    || checkpoint.ActionStableHash is not null))
                return false;
            previousSourceSeq = checkpoint.SourceSeq;
            previousRandomEventCount = checkpoint.RandomEventCount;
        }
        return string.Equals(
            contract.StableHash,
            ReplayCheckpointContractParser.HashContract(contract.Checkpoints, contract.Terminal),
            StringComparison.Ordinal);
    }

    private static bool VersionMatches(
        ReplayVersionIdentity identity,
        ReplayArtifactDescriptor artifact)
        => string.Equals(identity.MatchLogSchema, artifact.MatchLogSchema, StringComparison.Ordinal)
            && string.Equals(identity.EventAdapterVersion, artifact.EventAdapterVersion, StringComparison.Ordinal)
            && string.Equals(identity.EngineArtifactId, artifact.EngineArtifactId, StringComparison.Ordinal)
            && string.Equals(identity.EngineCommit, artifact.EngineCommit, StringComparison.Ordinal)
            && string.Equals(identity.BinarySha256, artifact.BinarySha256, StringComparison.Ordinal)
            && string.Equals(identity.RulesVersion, artifact.RulesVersion, StringComparison.Ordinal)
            && string.Equals(identity.RulesetManifestHash, artifact.RulesetManifestHash, StringComparison.Ordinal)
            && string.Equals(identity.CardDbContentHash, artifact.CardDbContentHash, StringComparison.Ordinal)
            && string.Equals(identity.RngAlgorithmVersion, artifact.RngAlgorithmVersion, StringComparison.Ordinal)
            && string.Equals(identity.DeterministicIdVersion, artifact.DeterministicIdVersion, StringComparison.Ordinal)
            && string.Equals(identity.OpeningProtocolVersion, artifact.OpeningProtocolVersion, StringComparison.Ordinal)
            && string.Equals(identity.ReplayConfigSchema, artifact.ReplayConfigSchema, StringComparison.Ordinal);

    private static bool IsSha256(string? value)
        => value is { Length: 71 }
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value.AsSpan(7).ToArray().All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>当前进程内的可替换实现；生产是否可用仍完全由不可变 registry + 显式登记决定。</summary>
public sealed class InProcessArtifactReplayWorker : IArtifactReplayWorker
{
    private readonly ReplayArtifactDescriptor _artifact;
    private readonly IReplayCheckpointProvider _checkpointProvider;
    private readonly CardRuleset? _ruleset;
    private readonly IArtifactMatchReplayExecutor _executor;

    public InProcessArtifactReplayWorker(
        string workerId,
        ReplayArtifactDescriptor artifact,
        IReplayCheckpointProvider checkpointProvider,
        CardRuleset? ruleset = null)
        : this(workerId, artifact, checkpointProvider, ruleset, new MatchReplayArtifactExecutor())
    {
    }

    internal InProcessArtifactReplayWorker(
        string workerId,
        ReplayArtifactDescriptor artifact,
        IReplayCheckpointProvider checkpointProvider,
        CardRuleset? ruleset,
        IArtifactMatchReplayExecutor executor)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("workerId 不能为空", nameof(workerId));
        WorkerId = workerId;
        _artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        _checkpointProvider = checkpointProvider
            ?? throw new ArgumentNullException(nameof(checkpointProvider));
        _ruleset = ruleset ?? CardRulesetManager.Current;
        if (!string.Equals(_ruleset.Id, artifact.RulesVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"进程内 worker 规则集与工件不一致：artifact={artifact.RulesVersion}，worker={_ruleset.Id}");
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        ArtifactFingerprint = ReplayArtifactIdentity.Fingerprint(artifact);
    }

    public string WorkerId { get; }
    public string EngineArtifactId => _artifact.EngineArtifactId;
    public string ArtifactFingerprint { get; }

    public async Task<ArtifactReplayWorkerResponse> ExecuteAsync(
        ArtifactReplayWorkerRequest request,
        CancellationToken cancellationToken)
    {
        if (!ArtifactReplayMessageValidator.IsValidRequest(
                request,
                _artifact,
                ArtifactFingerprint,
                out var validationMessage))
            return Failure(
                request,
                ReplayQuarantineCodes.WorkerArtifactMismatch,
                "artifact_worker",
                $"请求 schema/hash/工件指纹或 lineage 与本 worker 不一致：{validationMessage}",
                sourceSeq: null,
                actionIndex: null);

        var randomTrace = new List<ReplayRandomTraceEvent>();
        var checkpoints = new List<VerifiedReplayCheckpoint>();
        try
        {
            var entries = request.Actions
                .Select(action => new MatchReplay.ActionEntry(
                    action.ActorSeat,
                    action.Action,
                    action.Data.Clone()))
                .ToArray();

            var engine = await _executor.ExecuteAsync(
                request,
                entries,
                _ruleset,
                configuredEngine =>
                {
                    configuredEngine.EnablePrivateSnapshotLog = false;
                    configuredEngine.State.SuppressExternalProfileLookups = true;
                    configuredEngine.OnMatchLog = (kind, actor, payload) =>
                    {
                        if (!string.Equals(kind, "random_event", StringComparison.Ordinal)) return;
                        randomTrace.Add(new ReplayRandomTraceEvent(
                            actor,
                            JsonSerializer.SerializeToElement(payload).Clone()));
                    };
                },
                async (point, token) =>
                {
                    var context = point.ActionIndex < 0
                        ? new ReplayCheckpointContext(
                            ReplayCheckpointPosition.Opening,
                            -1,
                            null,
                            null)
                        : new ReplayCheckpointContext(
                            ReplayCheckpointPosition.AfterAction,
                            point.ActionIndex,
                            request.Actions[point.ActionIndex].OrderSeq,
                            request.Actions[point.ActionIndex].StableHash);
                    var actual = _checkpointProvider.Capture(
                        point.Engine,
                        context,
                        randomTrace.AsReadOnly());
                    var expected = point.ActionIndex < 0
                        ? request.CheckpointContract.Opening
                        : request.CheckpointContract.AfterActions[point.ActionIndex];
                    checkpoints.Add(VerifyCheckpoint(request, expected, context, actual));
                    await ValueTask.CompletedTask;
                },
                cancellationToken);

            var terminalContext = new ReplayCheckpointContext(
                ReplayCheckpointPosition.Terminal,
                request.Actions.Count,
                null,
                null);
            var terminalDigest = _checkpointProvider.Capture(
                engine,
                terminalContext,
                randomTrace.AsReadOnly());
            checkpoints.Add(VerifyCheckpoint(
                request,
                request.CheckpointContract.TerminalCheckpoint,
                terminalContext,
                terminalDigest));
            var terminal = VerifyTerminal(request, engine);
            var verified = BuildVerified(request, checkpoints, terminal);
            return Success(request, verified);
        }
        catch (ReplayQuarantineException ex)
        {
            return Failure(
                request,
                ex.ReasonCode,
                ex.Stage,
                ex.Message,
                ex.SourceSeq,
                actionIndex: null);
        }
        catch (MatchReplay.ReplayActionRejectedException ex)
        {
            return Failure(
                request,
                ReplayQuarantineCodes.ReplayActionRejected,
                "artifact_match_replay",
                ex.Message,
                request.Actions[ex.ActionIndex].SourceSeq,
                ex.ActionIndex);
        }
        catch (MatchReplay.ReplayTapeAfterGameOverException ex)
        {
            return Failure(
                request,
                ReplayQuarantineCodes.TapeContinuesAfterGameOver,
                "artifact_match_replay",
                ex.Message,
                request.Actions[ex.ActionIndex].SourceSeq,
                ex.ActionIndex);
        }
        catch (TimeoutException ex)
        {
            return Failure(
                request,
                ReplayQuarantineCodes.StableWaitTimeout,
                "artifact_match_replay",
                ex.Message,
                sourceSeq: null,
                actionIndex: null);
        }
        catch (OperationCanceledException ex)
        {
            return Failure(
                request,
                ReplayQuarantineCodes.WorkerCancelled,
                "artifact_match_replay",
                ex.Message,
                sourceSeq: null,
                actionIndex: null);
        }
        catch (Exception ex)
        {
            return Failure(
                request,
                ReplayQuarantineCodes.WorkerFailure,
                "artifact_match_replay",
                ex.Message,
                sourceSeq: null,
                actionIndex: null);
        }
    }

    private static VerifiedReplayCheckpoint VerifyCheckpoint(
        ArtifactReplayWorkerRequest request,
        ExpectedReplayCheckpoint expected,
        ReplayCheckpointContext context,
        ReplayCheckpointDigest actual)
    {
        if (!string.Equals(expected.StateDigest, actual.StateDigest, StringComparison.Ordinal))
            throw Mismatch(
                ReplayQuarantineCodes.StateCheckpointMismatch,
                $"完整状态 checkpoint 分歧：position={ReplayCheckpointContractParser.PositionName(context.Position)}，actionIndex={context.ActionIndex}，expected={expected.StateDigest}，actual={actual.StateDigest}",
                request.Header.MatchId,
                expected.SourceSeq);
        if (!string.Equals(expected.PublicStateDigest, actual.PublicStateDigest, StringComparison.Ordinal))
            throw Mismatch(
                ReplayQuarantineCodes.PublicCheckpointMismatch,
                "公开状态 checkpoint 分歧",
                request.Header.MatchId,
                expected.SourceSeq);
        if (expected.RandomEventCount != actual.RandomEventCount
            || !string.Equals(expected.RandomTraceDigest, actual.RandomTraceDigest, StringComparison.Ordinal))
            throw Mismatch(
                ReplayQuarantineCodes.RandomTraceMismatch,
                "累计随机事件数量或 digest 分歧",
                request.Header.MatchId,
                expected.SourceSeq);

        var checkpoint = new VerifiedReplayCheckpoint(
            context.Position,
            context.ActionIndex,
            context.ActionOrderSeq,
            context.ActionStableHash,
            actual.StateDigest,
            actual.PublicStateDigest,
            actual.RandomTraceDigest,
            actual.RandomEventCount,
            StableHash: string.Empty);
        return checkpoint with
        {
            StableHash = ArtifactReplayMessageValidator.HashVerifiedCheckpoint(checkpoint),
        };
    }

    private static VerifiedReplayTerminal VerifyTerminal(
        ArtifactReplayWorkerRequest request,
        GameEngine engine)
    {
        var expected = request.CheckpointContract.Terminal;
        var actual = ReplayTerminalSemantics.Capture(engine.State);
        var expectedReasonCategory = ReplayTerminalSemantics.ReasonCategory(
            expected.Reason,
            expected.IsDraw);
        var actualReasonCategory = ReplayTerminalSemantics.ReasonCategory(
            actual.Reason,
            actual.IsDraw);
        if (actual.WinnerIndex != expected.WinnerIndex
            || actual.IsDraw != expected.IsDraw
            || !string.Equals(actualReasonCategory, expectedReasonCategory, StringComparison.Ordinal)
            || (string.Equals(actualReasonCategory, "unclassified", StringComparison.Ordinal)
                && !string.Equals(actual.Reason, expected.Reason, StringComparison.Ordinal))
            || actual.TurnCount != expected.TurnCount)
            throw Mismatch(
                ReplayQuarantineCodes.TerminalMismatch,
                "winner/draw/reasonCategory/turnCount 与 match_end 不一致",
                request.Header.MatchId,
                request.CheckpointContract.TerminalCheckpoint.SourceSeq);

        return new VerifiedReplayTerminal(
            actual.WinnerIndex,
            actual.IsDraw,
            actual.Reason,
            actual.TurnCount,
            actual.StableHash);
    }

    private VerifiedArtifactReplay BuildVerified(
        ArtifactReplayWorkerRequest request,
        IReadOnlyList<VerifiedReplayCheckpoint> checkpoints,
        VerifiedReplayTerminal terminal)
    {
        var replayDigest = ArtifactReplayMessageValidator.HashReplayDigest(
            request,
            checkpoints,
            terminal);
        return new VerifiedArtifactReplay(
            request.SourceId,
            request.SourceFileHash,
            request.Header.MatchId,
            request.PreparedHash,
            request.TapeHash,
            request.CheckpointContract.StableHash,
            request.RegistryVersion,
            request.RegistryHash,
            request.Artifact.EngineArtifactId,
            request.ArtifactFingerprint,
            WorkerId,
            request.RequestHash,
            Array.AsReadOnly(checkpoints.ToArray()),
            terminal,
            replayDigest,
            ArtifactReplayMessageValidator.HashVerifiedReplay(
                replayDigest,
                WorkerId,
                request.CheckpointContract.StableHash));
    }

    private ArtifactReplayWorkerResponse Success(
        ArtifactReplayWorkerRequest request,
        VerifiedArtifactReplay verified)
    {
        var stableHash = ArtifactReplayWorkerDispatcher.HashResponse(
            request.RequestHash,
            request.ArtifactFingerprint,
            WorkerId,
            verified,
            failure: null);
        return new ArtifactReplayWorkerResponse(
            ArtifactReplayWorkerDispatcher.ResponseSchema,
            request.RequestHash,
            request.ArtifactFingerprint,
            WorkerId,
            verified,
            Failure: null,
            stableHash);
    }

    private ArtifactReplayWorkerResponse Failure(
        ArtifactReplayWorkerRequest request,
        string reasonCode,
        string stage,
        string message,
        long? sourceSeq,
        int? actionIndex)
    {
        var failure = new ArtifactReplayWorkerFailure(
            reasonCode,
            stage,
            SanitizeFailureMessage(message),
            sourceSeq,
            actionIndex);
        return new ArtifactReplayWorkerResponse(
            ArtifactReplayWorkerDispatcher.ResponseSchema,
            request.RequestHash,
            request.ArtifactFingerprint,
            WorkerId,
            Verified: null,
            failure,
            ArtifactReplayWorkerDispatcher.HashResponse(
                request.RequestHash,
                request.ArtifactFingerprint,
                WorkerId,
                verified: null,
                failure));
    }

    private static string SanitizeFailureMessage(string? message)
    {
        var value = (message ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (value.Length == 0) return "worker 未提供失败详情";
        return value.Length <= 2048 ? value : value[..2048];
    }

    private static ReplayQuarantineException Mismatch(
        string code,
        string message,
        string matchId,
        long sourceSeq)
        => new(code, "checkpoint_verification", message, matchId, sourceSeq);
}

internal interface IArtifactMatchReplayExecutor
{
    Task<GameEngine> ExecuteAsync(
        ArtifactReplayWorkerRequest request,
        IReadOnlyList<MatchReplay.ActionEntry> actions,
        CardRuleset? ruleset,
        Action<GameEngine> configureEngine,
        Func<MatchReplay.SettledReplayPoint, CancellationToken, ValueTask> onSettled,
        CancellationToken cancellationToken);
}

internal sealed class MatchReplayArtifactExecutor : IArtifactMatchReplayExecutor
{
    public Task<GameEngine> ExecuteAsync(
        ArtifactReplayWorkerRequest request,
        IReadOnlyList<MatchReplay.ActionEntry> actions,
        CardRuleset? ruleset,
        Action<GameEngine> configureEngine,
        Func<MatchReplay.SettledReplayPoint, CancellationToken, ValueTask> onSettled,
        CancellationToken cancellationToken)
        => MatchReplay.RebuildForArtifactWorkerAsync(
            roomId: $"artifact-{request.Header.MatchId}",
            seed: request.Header.RngSeed,
            firstPlayer: request.Header.FirstPlayer,
            p0: ("artifact-player-0", request.Header.Player0.DeckRaw),
            p1: ("artifact-player-1", request.Header.Player1.DeckRaw),
            actions,
            request.StableTimeoutMilliseconds,
            configureEngine,
            onSettled,
            cancellationToken,
            leaderKeywordWildcard: request.Header.Configuration.LeaderKeywordWildcard,
            p0AlwaysPrompt: request.Header.Player0.AlwaysPromptOnLifeReveal,
            p1AlwaysPrompt: request.Header.Player1.AlwaysPromptOnLifeReveal,
            openingSetupAfterFirstPlayerChoice: request.Header.Configuration.OpeningSetupAfterFirstPlayerChoice,
            ruleset);
}
