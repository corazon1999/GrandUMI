using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Cards;
using GrandUMI.Effects.Dsl;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public class ArtifactReplayWorkerTests
{
    private const string Deck =
        "OP16-080\nOP16-103\nOP16-103\nOP16-103\nOP16-103\nOP16-109\nOP16-109\nOP16-109\nOP16-109\n" +
        "OP16-110\nOP16-110\nOP16-110\nOP16-110\nOP16-115\nOP16-115\nOP09-096\nOP09-096\nOP09-096\nOP09-096\n" +
        "OP09-099\nOP09-099\nOP09-099\nOP09-099\nOP16-104\nOP16-104\nOP16-104\nOP16-104\nOP09-086\nOP09-086\n" +
        "OP09-086\nOP09-086\nEB04-058\nEB04-058\nEB04-058\nEB04-058\nOP16-108\nOP16-108\nOP16-108\nOP16-108\n" +
        "OP16-119\nOP16-119\nOP16-119\nOP16-119\nOP16-116\nOP16-116\nOP14-112\nOP14-112\nOP14-112\n" +
        "OP09-093\nOP09-093\nOP09-093";

    private static readonly Lazy<ReplayArtifactRegistry> Registry = new(() =>
        ReplayArtifactRegistry.Load(RepoPath(
            "服务端WebSocket.Tests",
            "Fixtures",
            "training-replay-artifact-registry.v1.json")));

    private static readonly object LoadGate = new();
    private static bool _loaded;

    private const string RandomDigest =
        "sha256:5bfb6913540a56588d2ba78da37056d4eb621b346c6b1f8023ef9b43ce6a52e9";

    private static readonly FixtureDigest[] GoldenDigests =
    [
        new(
            "sha256:97e65fdd39100e9022c531aa8c223b9c591ea4e279bc5210545de62941b4ed4c",
            "sha256:a0fe0abe3cdcac4870a7a9c7d57514fb620d5cad7147d21637e5202acb621b61",
            RandomDigest,
            2),
        new(
            "sha256:43b4650bea6168d7353ebb072c7d2847aab573c2b8f74ae2651aa20906bdfa8e",
            "sha256:b0f6a9e50b4270d266643c540be0ff6bda682b07fb75b535148e310df9f441cc",
            RandomDigest,
            2),
        new(
            "sha256:25d8d8acc8415d7732b7ecdcbffc21855085fea48f3c2ca399abfeae4131ecf8",
            "sha256:dabecc24413813180d59f6cbfb6222ce3d47a214248430f32b6a38c834bfd524",
            RandomDigest,
            2),
        new(
            "sha256:db0adb521938f12fccee75149a54c36808ba24be3771d93ee8c89894961f383d",
            "sha256:1548b499a78a6dadec5cb6af657f82bbf6e1ed2d0cb217d777f854476433896b",
            RandomDigest,
            2),
        new(
            "sha256:69dbe2fd0751262573b5b1d20f98d02f6295654c6201197970d938f7b83a1b15",
            "sha256:400c78dc33830fc12eeb6ae39fb2288554e5e4bc949a100208e0465d27010126",
            RandomDigest,
            2),
    ];

    private static readonly string[] GoldenActionHashes =
    [
        "sha256:bc5a9f5414231c6d77ead2abeaf94398056be8f9d7a6682c0486b5e753fec485",
        "sha256:934305ec5a1dcad97636fec035e5115064d6a8f78ffb3b3d4d4bfc726f012bb7",
        "sha256:1d72f036d0b1e766b75b3aa428b2a706ed223287178742476f2e1d00235c770f",
    ];

    [Fact]
    public async Task 精确登记Fixture_逐稳定点成功重放且哈希幂等()
    {
        var prepared = BuildPreparedFixture("artifact-success");
        Assert.Equal(GoldenActionHashes, prepared.Tape.Actions.Select(action => action.StableHash));
        var dispatcher = CreateDispatcher(prepared);

        var first = await dispatcher.ExecuteAsync(prepared);
        var second = await dispatcher.ExecuteAsync(prepared);

        var verified = Assert.IsType<VerifiedArtifactReplay>(first.Verified);
        Assert.Null(first.Quarantine);
        Assert.Equal(5, verified.Checkpoints.Count);
        Assert.Equal(prepared.Tape.StableHash, verified.TapeHash);
        Assert.Equal(prepared.CheckpointContract!.StableHash, verified.CheckpointContractHash);
        Assert.Equal(1, verified.Terminal.WinnerIndex);
        Assert.False(verified.Terminal.IsDraw);
        Assert.Equal("artifact-player-0 投降", verified.Terminal.Reason);
        Assert.Equal(verified.ReplayDigest, second.Verified!.ReplayDigest);
        Assert.Equal(verified.StableHash, second.Verified.StableHash);
        Assert.Equal(
            verified.Checkpoints.Select(checkpoint => checkpoint.StableHash),
            second.Verified.Checkpoints.Select(checkpoint => checkpoint.StableHash));
    }

    [Fact]
    public async Task 旧日志缺Checkpoint契约_保持NoGo且不启动Worker()
    {
        var prepared = BuildPreparedWithoutContract("artifact-no-contract");
        var result = await CreateDispatcher(prepared).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.MissingCheckpointContract, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void 部分Checkpoint契约_准备阶段即整局隔离()
    {
        EnsureLoaded();
        var digest = GoldenDigests[0];
        var text = BuildLog(
            Start("artifact-partial-contract"),
            Checkpoint(
                "artifact-partial-contract",
                2,
                ReplayCheckpointPosition.Opening,
                null,
                null,
                digest),
            End(
                "artifact-partial-contract",
                3,
                1,
                false,
                "artifact-player-0 投降",
                1));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text),
            "partial-contract",
            Registry.Value);

        Assert.Null(result.Prepared);
        Assert.Equal(ReplayQuarantineCodes.InvalidCheckpointContract, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task 历史声称Accepted但当前工件拒绝_整局隔离()
    {
        var actions = new[]
        {
            new FixtureAction(0, "EndTurn", new { }),
            new FixtureAction(1, "Mulligan", new { redraw = false }),
            new FixtureAction(0, "Surrender", new { }),
        };
        var prepared = BuildPreparedFixture("artifact-rejected", actions: actions);

        var result = await CreateDispatcher(prepared).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.ReplayActionRejected, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task 稳定等待超时_整局隔离且不返回部分Checkpoint()
    {
        var prepared = BuildPreparedFixture("artifact-timeout");
        var worker = new InProcessArtifactReplayWorker(
            "fixture-current-v1",
            prepared.Artifact,
            new FixtureCheckpointProvider(),
            FixtureRuleset(),
            new TimeoutExecutor());

        var result = await new ArtifactReplayWorkerDispatcher([worker]).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.StableWaitTimeout, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task 整局Worker超时_取消在途执行并隔离()
    {
        var prepared = BuildPreparedFixture("artifact-worker-timeout");
        var executor = new CancellableExecutor();
        var worker = new InProcessArtifactReplayWorker(
            "fixture-current-v1",
            prepared.Artifact,
            new FixtureCheckpointProvider(),
            FixtureRuleset(),
            executor);

        var result = await new ArtifactReplayWorkerDispatcher([worker]).ExecuteAsync(
            prepared,
            workerTimeoutMilliseconds: 20);
        await executor.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerTimeout, result.Quarantine!.ReasonCode);
    }

    [Theory]
    [InlineData("state", ReplayQuarantineCodes.StateCheckpointMismatch)]
    [InlineData("public", ReplayQuarantineCodes.PublicCheckpointMismatch)]
    [InlineData("random", ReplayQuarantineCodes.RandomTraceMismatch)]
    public async Task 任一Checkpoint维度分歧_整局隔离(string dimension, string expectedReason)
    {
        var prepared = BuildPreparedFixture(
            $"artifact-{dimension}-mismatch",
            mutateDigest: (index, digest) => index == 1
                ? dimension switch
                {
                    "state" => digest with { State = Sha('0') },
                    "public" => digest with { Public = Sha('1') },
                    "random" => digest with { Random = Sha('2'), RandomCount = 3 },
                    _ => digest,
                }
                : digest);

        var result = await CreateDispatcher(prepared).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(expectedReason, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task 终局语义分歧_晚失败也不泄露前段结果且隔离哈希稳定()
    {
        var prepared = BuildPreparedFixture(
            "artifact-terminal-mismatch",
            winnerIndex: 0,
            reason: "artifact-player-1 投降");
        var dispatcher = CreateDispatcher(prepared);

        var first = await dispatcher.ExecuteAsync(prepared);
        var second = await dispatcher.ExecuteAsync(prepared);

        Assert.Null(first.Verified);
        Assert.Equal(ReplayQuarantineCodes.TerminalMismatch, first.Quarantine!.ReasonCode);
        Assert.Equal(first.Quarantine.StableHash, second.Quarantine!.StableHash);
    }

    [Fact]
    public async Task 同ArtifactId但完整指纹不同_拒绝路由到Worker()
    {
        var prepared = BuildPreparedFixture("artifact-worker-mismatch");
        var wrongArtifact = prepared.Artifact with { BinarySha256 = Sha('d') };
        var wrongWorker = new InProcessArtifactReplayWorker(
            "wrong-fixture-worker",
            wrongArtifact,
            new FixtureCheckpointProvider(),
            FixtureRuleset());

        var result = await new ArtifactReplayWorkerDispatcher([wrongWorker]).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerArtifactMismatch, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task Worker篡改成功响应StableHash_协议门禁整局隔离()
    {
        var prepared = BuildPreparedFixture("artifact-response-hash-tampered");
        var worker = new TamperingWorker(
            CreateWorker(prepared),
            (_, response) => response with { StableHash = Sha('e') });

        var result = await new ArtifactReplayWorkerDispatcher([worker]).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, result.Quarantine!.ReasonCode);
    }

    [Theory]
    [InlineData("source_id")]
    [InlineData("source_file_hash")]
    [InlineData("match_id")]
    [InlineData("prepared_hash")]
    [InlineData("tape_hash")]
    [InlineData("checkpoint_contract_hash")]
    [InlineData("registry_version")]
    [InlineData("registry_hash")]
    [InlineData("engine_artifact_id")]
    [InlineData("request_hash")]
    [InlineData("artifact_fingerprint")]
    [InlineData("worker_id")]
    public async Task Worker用正确外层携带篡改VerifiedLineage_协议门禁隔离(string field)
    {
        var prepared = BuildPreparedFixture($"artifact-lineage-{field}");
        var worker = new TamperingWorker(
            CreateWorker(prepared),
            (request, response) =>
            {
                var verified = response.Verified!;
                verified = field switch
                {
                    "source_id" => verified with { SourceId = "tampered-source" },
                    "source_file_hash" => verified with { SourceFileHash = Sha('9') },
                    "match_id" => verified with { MatchId = "tampered-match" },
                    "prepared_hash" => verified with { PreparedHash = Sha('8') },
                    "tape_hash" => verified with { TapeHash = Sha('7') },
                    "checkpoint_contract_hash" => verified with { CheckpointContractHash = Sha('6') },
                    "registry_version" => verified with { RegistryVersion = "tampered-registry" },
                    "registry_hash" => verified with { RegistryHash = Sha('5') },
                    "engine_artifact_id" => verified with { EngineArtifactId = "tampered-artifact" },
                    "request_hash" => verified with { RequestHash = Sha('4') },
                    "artifact_fingerprint" => verified with { ArtifactFingerprint = Sha('3') },
                    "worker_id" => verified with { WorkerId = "tampered-worker" },
                    _ => throw new ArgumentOutOfRangeException(nameof(field)),
                };
                return response with
                {
                    Verified = verified,
                    StableHash = ArtifactReplayWorkerDispatcher.HashResponse(
                        request.RequestHash,
                        request.ArtifactFingerprint,
                        response.WorkerId,
                        verified,
                        failure: null),
                };
            });

        var result = await new ArtifactReplayWorkerDispatcher([worker]).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task Worker篡改失败响应Hash_协议门禁整局隔离()
    {
        var prepared = BuildPreparedFixture("artifact-failure-hash-tampered");
        var failingWorker = new InProcessArtifactReplayWorker(
            "fixture-current-v1",
            prepared.Artifact,
            new FixtureCheckpointProvider(),
            FixtureRuleset(),
            new TimeoutExecutor());
        var worker = new TamperingWorker(
            failingWorker,
            (_, response) => response with { StableHash = Sha('f') });

        var result = await new ArtifactReplayWorkerDispatcher([worker]).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, result.Quarantine!.ReasonCode);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("stage")]
    [InlineData("source_low")]
    [InlineData("source_high")]
    [InlineData("action_low")]
    [InlineData("action_high")]
    public async Task Worker失败定位字段非法_即使重算Hash也被协议门禁隔离(string field)
    {
        var prepared = BuildPreparedFixture($"artifact-failure-range-{field}");
        var failingWorker = new InProcessArtifactReplayWorker(
            "fixture-current-v1",
            prepared.Artifact,
            new FixtureCheckpointProvider(),
            FixtureRuleset(),
            new TimeoutExecutor());
        var worker = new TamperingWorker(
            failingWorker,
            (request, response) =>
            {
                var failure = response.Failure!;
                failure = field switch
                {
                    "reason" => failure with { ReasonCode = " " },
                    "stage" => failure with { Stage = "INVALID STAGE" },
                    "source_low" => failure with { SourceSeq = 0 },
                    "source_high" => failure with
                    {
                        SourceSeq = request.CheckpointContract.TerminalCheckpoint.SourceSeq + 1,
                    },
                    "action_low" => failure with { ActionIndex = -1 },
                    "action_high" => failure with { ActionIndex = request.Actions.Count },
                    _ => throw new ArgumentOutOfRangeException(nameof(field)),
                };
                return response with
                {
                    Failure = failure,
                    StableHash = ArtifactReplayWorkerDispatcher.HashResponse(
                        request.RequestHash,
                        request.ArtifactFingerprint,
                        response.WorkerId,
                        verified: null,
                        failure),
                };
            });

        var result = await new ArtifactReplayWorkerDispatcher([worker]).ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public async Task 外部取消_只返回整局隔离结果()
    {
        var prepared = BuildPreparedFixture("artifact-cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await CreateDispatcher(prepared).ExecuteAsync(
            prepared,
            cancellationToken: cancellation.Token);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerCancelled, result.Quarantine!.ReasonCode);
    }

    private static ArtifactReplayWorkerDispatcher CreateDispatcher(PreparedReplayMatch prepared)
        => new([CreateWorker(prepared)]);

    private static InProcessArtifactReplayWorker CreateWorker(PreparedReplayMatch prepared)
        => new(
            "fixture-current-v1",
            prepared.Artifact,
            new FixtureCheckpointProvider(),
            FixtureRuleset());

    private static PreparedReplayMatch BuildPreparedFixture(
        string matchId,
        IReadOnlyList<FixtureAction>? actions = null,
        Func<int, FixtureDigest, FixtureDigest>? mutateDigest = null,
        int? winnerIndex = 1,
        bool isDraw = false,
        string reason = "artifact-player-0 投降",
        int turnCount = 1)
    {
        EnsureLoaded();
        actions ??= DefaultActions();
        Assert.Equal(3, actions.Count);

        // 先用同 seq 形状构建动作 lineage；placeholder 不参与磁带，也不会冒充 checkpoint。
        var skeleton = new List<JsonObject> { Start(matchId) };
        long seq = 2;
        skeleton.Add(Event(matchId, seq++, "fixture_placeholder", -1, new { }));
        foreach (var action in actions)
        {
            skeleton.Add(Request(matchId, seq++, action.Actor, action.Action, action.Data));
            skeleton.Add(Accepted(matchId, seq++, action.Actor, action.Action));
            skeleton.Add(Event(matchId, seq++, "fixture_placeholder", -1, new { }));
        }
        skeleton.Add(Event(matchId, seq++, "fixture_placeholder", -1, new { }));
        skeleton.Add(End(matchId, seq, winnerIndex, isDraw, reason, turnCount));
        var skeletonResult = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(BuildLog(skeleton.ToArray())),
            $"{matchId}-lineage",
            Registry.Value);
        var lineage = Assert.IsType<PreparedReplayMatch>(skeletonResult.Prepared);

        var finalEvents = new List<JsonObject> { Start(matchId) };
        seq = 2;
        finalEvents.Add(Checkpoint(
            matchId,
            seq++,
            ReplayCheckpointPosition.Opening,
            actionOrderSeq: null,
            actionStableHash: null,
            mutateDigest?.Invoke(0, GoldenDigests[0]) ?? GoldenDigests[0]));
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            finalEvents.Add(Request(matchId, seq++, action.Actor, action.Action, action.Data));
            finalEvents.Add(Accepted(matchId, seq++, action.Actor, action.Action));
            var tapeAction = lineage.Tape.Actions[index];
            finalEvents.Add(Checkpoint(
                matchId,
                seq++,
                ReplayCheckpointPosition.AfterAction,
                tapeAction.OrderSeq,
                tapeAction.StableHash,
                mutateDigest?.Invoke(index + 1, GoldenDigests[index + 1])
                    ?? GoldenDigests[index + 1]));
        }
        finalEvents.Add(Checkpoint(
            matchId,
            seq++,
            ReplayCheckpointPosition.Terminal,
            actionOrderSeq: null,
            actionStableHash: null,
            mutateDigest?.Invoke(GoldenDigests.Length - 1, GoldenDigests[^1])
                ?? GoldenDigests[^1]));
        finalEvents.Add(End(matchId, seq, winnerIndex, isDraw, reason, turnCount));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(BuildLog(finalEvents.ToArray())),
            matchId,
            Registry.Value);
        return Assert.IsType<PreparedReplayMatch>(result.Prepared);
    }

    private static PreparedReplayMatch BuildPreparedWithoutContract(string matchId)
    {
        EnsureLoaded();
        var actions = DefaultActions();
        var events = new List<JsonObject> { Start(matchId) };
        long seq = 2;
        foreach (var action in actions)
        {
            events.Add(Request(matchId, seq++, action.Actor, action.Action, action.Data));
            events.Add(Accepted(matchId, seq++, action.Actor, action.Action));
        }
        events.Add(End(matchId, seq, 1, false, "artifact-player-0 投降", 1));
        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(BuildLog(events.ToArray())),
            matchId,
            Registry.Value);
        return Assert.IsType<PreparedReplayMatch>(result.Prepared);
    }

    private static FixtureAction[] DefaultActions()
        =>
        [
            new(0, "Mulligan", new { redraw = false }),
            new(1, "Mulligan", new { redraw = false }),
            new(0, "Surrender", new { }),
        ];

    private static JsonObject Checkpoint(
        string matchId,
        long seq,
        ReplayCheckpointPosition position,
        long? actionOrderSeq,
        string? actionStableHash,
        FixtureDigest digest)
        => Event(matchId, seq, "replay_checkpoint", -1, new
        {
            schema = "grandumi.replay_checkpoint.v1",
            position = position switch
            {
                ReplayCheckpointPosition.Opening => "opening",
                ReplayCheckpointPosition.AfterAction => "after_action",
                ReplayCheckpointPosition.Terminal => "terminal",
                _ => throw new ArgumentOutOfRangeException(nameof(position)),
            },
            actionOrderSeq,
            actionStableHash,
            stateDigest = digest.State,
            publicStateDigest = digest.Public,
            randomTraceDigest = digest.Random,
            randomEventCount = digest.RandomCount,
        });

    private static string Sha(char value)
        => $"sha256:{new string(value, 64)}";

    private sealed record FixtureAction(int Actor, string Action, object Data);
    private sealed record FixtureDigest(
        string State,
        string Public,
        string Random,
        int RandomCount);

    private sealed class FixtureCheckpointProvider : IReplayCheckpointProvider
    {
        public ReplayCheckpointDigest Capture(
            GameEngine engine,
            ReplayCheckpointContext context,
            IReadOnlyList<ReplayRandomTraceEvent> randomTrace)
        {
            var state = engine.State;
            var stateElement = JsonSerializer.SerializeToElement(new
            {
                position = context.Position.ToString(),
                context.ActionIndex,
                state.RngSeed,
                state.RandomSeq,
                state.FirstPlayer,
                state.CurrentTurnPlayer,
                phase = state.Phase.ToString(),
                state.TurnCount,
                state.MulliganBothDone,
                state.IsGameOver,
                state.IsDraw,
                state.WinnerIndex,
                state.GameOverReason,
                players = state.Players.Select(player => new
                {
                    leader = player.Leader.Info.Number,
                    hand = player.Hand.Select(card => new { card.Id, card.Info.Number }),
                    deck = player.Deck.Select(card => new { card.Id, card.Info.Number }),
                    life = player.LifeArea.Select(card => new { card.Id, card.Info.Number }),
                    player.MulliganDone,
                }),
            });
            var publicElement = JsonSerializer.SerializeToElement(new
            {
                position = context.Position.ToString(),
                context.ActionIndex,
                state.Tick,
                phase = state.Phase.ToString(),
                state.CurrentTurnPlayer,
                state.TurnCount,
                state.FirstPlayer,
                state.MulliganBothDone,
                state.IsGameOver,
                state.IsDraw,
                state.WinnerIndex,
                state.GameOverReason,
                players = state.Players.Select(player => new
                {
                    leader = player.Leader.Info.Number,
                    handCount = player.Hand.Count,
                    deckCount = player.Deck.Count,
                    lifeCount = player.LifeArea.Count,
                    characters = player.Characters.Select(card => new
                    {
                        card.Id,
                        card.Info.Number,
                        card.IsTapped,
                    }),
                    player.MulliganDone,
                }),
            });
            var randomElement = JsonSerializer.SerializeToElement(randomTrace.Select(entry => new
            {
                entry.Actor,
                entry.Payload,
            }));
            return new ReplayCheckpointDigest(
                CanonicalJson.Hash(stateElement),
                CanonicalJson.Hash(publicElement),
                CanonicalJson.Hash(randomElement),
                randomTrace.Count);
        }
    }

    private sealed class TamperingWorker(
        IArtifactReplayWorker inner,
        Func<ArtifactReplayWorkerRequest, ArtifactReplayWorkerResponse, ArtifactReplayWorkerResponse> mutate)
        : IArtifactReplayWorker
    {
        public string WorkerId => inner.WorkerId;
        public string EngineArtifactId => inner.EngineArtifactId;
        public string ArtifactFingerprint => inner.ArtifactFingerprint;

        public async Task<ArtifactReplayWorkerResponse> ExecuteAsync(
            ArtifactReplayWorkerRequest request,
            CancellationToken cancellationToken)
        {
            var response = await inner.ExecuteAsync(request, cancellationToken);
            return mutate(request, response);
        }
    }

    private sealed class TimeoutExecutor : IArtifactMatchReplayExecutor
    {
        public Task<GameEngine> ExecuteAsync(
            ArtifactReplayWorkerRequest request,
            IReadOnlyList<MatchReplay.ActionEntry> actions,
            CardRuleset? ruleset,
            Action<GameEngine> configureEngine,
            Func<MatchReplay.SettledReplayPoint, CancellationToken, ValueTask> onSettled,
            CancellationToken cancellationToken)
            => Task.FromException<GameEngine>(new TimeoutException("fixture 稳定等待超时"));
    }

    private sealed class CancellableExecutor : IArtifactMatchReplayExecutor
    {
        private readonly TaskCompletionSource _cancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CancellationObserved => _cancellationObserved.Task;

        public async Task<GameEngine> ExecuteAsync(
            ArtifactReplayWorkerRequest request,
            IReadOnlyList<MatchReplay.ActionEntry> actions,
            CardRuleset? ruleset,
            Action<GameEngine> configureEngine,
            Func<MatchReplay.SettledReplayPoint, CancellationToken, ValueTask> onSettled,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("不可到达");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _cancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private static void EnsureLoaded()
    {
        lock (LoadGate)
        {
            if (_loaded) return;
            CardDatabase.LoadFrom(RepoPath("卡牌数据"));
            DslInterpreter.LoadDirectory(RepoPath("服务端WebSocket", "Effects", "Definitions"));
            _loaded = true;
        }
    }

    private static CardRuleset FixtureRuleset()
    {
        var current = CardRulesetManager.Current;
        return new CardRuleset(
            "fixture-rules-v1",
            current.Id,
            "P0-A 当前版本合成 fixture",
            current.CloneScriptedEffects(),
            current.CloneDslDefinitions(),
            []);
    }

    private static JsonObject Start(string matchId)
        => JsonSerializer.SerializeToNode(new
        {
            schema = MatchLogEventAdapter.SupportedSchema,
            matchId,
            seq = 1,
            kind = "match_start",
            actor = -1,
            payload = new
            {
                players = new object[]
                {
                    new { index = 0, deckRaw = Deck, alwaysPromptOnLifeReveal = false },
                    new { index = 1, deckRaw = Deck, alwaysPromptOnLifeReveal = false },
                },
                firstPlayer = 0,
                rngSeed = 24681357,
                openingSetupAfterFirstPlayerChoice = false,
                engineArtifactId = "fixture-server-20260828",
                engineCommit = "1111111111111111111111111111111111111111",
                rulesVersion = "fixture-rules-v1",
                rulesetManifestHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                cardDbContentHash = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                rngAlgorithmVersion = "dotnet-system-random-v1",
                deterministicIdVersion = "grandumi-deterministic-id-v1",
                openingProtocolVersion = "grandumi-opening-v2",
                replayConfigSchema = "grandumi.replay-config.v1",
                replayConfig = new { leaderKeywordWildcard = false },
            },
        })!.AsObject();

    private static JsonObject Request(string matchId, long seq, int actor, string action, object data)
        => Event(matchId, seq, "player_action_requested", actor, new { action, data });

    private static JsonObject Accepted(string matchId, long seq, int actor, string action)
        => Event(matchId, seq, "player_action_accepted", actor, new { action });

    private static JsonObject End(
        string matchId,
        long seq,
        int? winnerIndex,
        bool isDraw,
        string reason,
        int turnCount)
        => Event(matchId, seq, "match_end", -1, new
        {
            winnerIndex,
            isDraw,
            reason,
            turnCount,
        });

    private static JsonObject Event(string matchId, long seq, string kind, int actor, object payload)
        => new()
        {
            ["schema"] = MatchLogEventAdapter.SupportedSchema,
            ["matchId"] = matchId,
            ["seq"] = seq,
            ["kind"] = kind,
            ["actor"] = actor,
            ["payload"] = JsonSerializer.SerializeToNode(payload),
        };

    private static string BuildLog(params JsonObject[] events)
        => string.Join('\n', events.Select(item => item.ToJsonString())) + "\n";

    private static string RepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "服务端WebSocket")))
                return Path.Combine([directory.FullName, .. parts]);
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("无法定位 GrandUMI 仓库根目录");
    }
}
