using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Cards;
using GrandUMI.Effects.Dsl;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Training;
using Xunit;
using Xunit.Abstractions;

namespace GrandUMI.Tests;

public sealed class ReplayCheckpointProductionTests(ITestOutputHelper output)
{
    private const string Deck =
        "OP16-080\nOP16-103\nOP16-103\nOP16-103\nOP16-103\nOP16-109\nOP16-109\nOP16-109\nOP16-109\n" +
        "OP16-110\nOP16-110\nOP16-110\nOP16-110\nOP16-115\nOP16-115\nOP09-096\nOP09-096\nOP09-096\nOP09-096\n" +
        "OP09-099\nOP09-099\nOP09-099\nOP09-099\nOP16-104\nOP16-104\nOP16-104\nOP16-104\nOP09-086\nOP09-086\n" +
        "OP09-086\nOP09-086\nEB04-058\nEB04-058\nEB04-058\nEB04-058\nOP16-108\nOP16-108\nOP16-108\nOP16-108\n" +
        "OP16-119\nOP16-119\nOP16-119\nOP16-119\nOP16-116\nOP16-116\nOP14-112\nOP14-112\nOP14-112\n" +
        "OP09-093\nOP09-093\nOP09-093";

    private static readonly object LoadGate = new();
    private static bool _loaded;

    [Fact]
    public void Provider_排除时钟身份并保持隐藏区只影响FullDigest()
    {
        EnsureLoaded();
        var ruleset = FixtureRuleset();
        var first = Engine("privacy-a", "session-canary-a", "account-canary-a", "display-canary-a", ruleset);
        var second = Engine("privacy-b", "session-canary-b", "account-canary-b", "display-canary-b", ruleset);
        var hidden = first.State.Players[0].Hand[0];
        var secondHidden = second.State.Players[0].Hand[0];

        first.State.Tick = 11;
        second.State.Tick = 987;
        first.State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddSeconds(1);
        second.State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddDays(3);
        first.State.MulliganDeadlineUtc = DateTime.UtcNow.AddSeconds(2);
        second.State.MulliganDeadlineUtc = DateTime.UtcNow.AddDays(4);
        first.State.OperationClockRemainingMs[0] = 1;
        second.State.OperationClockRemainingMs[0] = 999_999;
        first.State.OperationClockSyncUtc = DateTime.UtcNow;
        second.State.OperationClockSyncUtc = DateTime.UtcNow.AddHours(5);
        first.State.PendingPrompt = Prompt(hidden.Id, "prompt-secret-canary");
        second.State.PendingPrompt = Prompt(secondHidden.Id, "prompt-secret-canary");

        var provider = DeterministicReplayCheckpointProvider.Current;
        var context = new ReplayCheckpointContext(ReplayCheckpointPosition.Opening, -1, null, null);
        var firstDigest = provider.Capture(first, context, []);
        var secondDigest = provider.Capture(second, context, []);

        Assert.Equal(firstDigest, secondDigest);
        var publicJson = DeterministicReplayCheckpointProvider.BuildPublicState(first.State).GetRawText();
        Assert.DoesNotContain("session-canary", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("account-canary", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("display-canary", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt-secret-canary", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Id.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"\"{hidden.Info.Number}\"", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("deadline", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clock", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replayHands", publicJson, StringComparison.Ordinal);

        second.State.KOReason = "effect";
        var koReasonMutation = provider.Capture(second, context, []);
        Assert.NotEqual(firstDigest.StateDigest, koReasonMutation.StateDigest);
        Assert.Equal(firstDigest.PublicStateDigest, koReasonMutation.PublicStateDigest);
        second.State.KOReason = null;

        second.State.KOActingSide = 1;
        var koActorMutation = provider.Capture(second, context, []);
        Assert.NotEqual(firstDigest.StateDigest, koActorMutation.StateDigest);
        Assert.Equal(firstDigest.PublicStateDigest, koActorMutation.PublicStateDigest);
        second.State.KOActingSide = -1;

        second.State.KOSourceCardId = secondHidden.Id;
        var koSourceMutation = provider.Capture(second, context, []);
        Assert.NotEqual(firstDigest.StateDigest, koSourceMutation.StateDigest);
        Assert.Equal(firstDigest.PublicStateDigest, koSourceMutation.PublicStateDigest);
        second.State.KOSourceCardId = null;

        second.State.PendingPrompt = Prompt(Guid.Parse("00000000-0000-0000-0000-000000000001"), "prompt-secret-canary");
        var promptMutation = provider.Capture(second, context, []);
        Assert.NotEqual(firstDigest.StateDigest, promptMutation.StateDigest);
        Assert.Equal(firstDigest.PublicStateDigest, promptMutation.PublicStateDigest);

        second.State.PendingPrompt = Prompt(secondHidden.Id, "prompt-secret-canary");
        secondHidden.IsTapped = true;
        var hiddenMutation = provider.Capture(second, context, []);
        Assert.NotEqual(firstDigest.StateDigest, hiddenMutation.StateDigest);
        Assert.Equal(firstDigest.PublicStateDigest, hiddenMutation.PublicStateDigest);
    }

    [Fact]
    public void RandomTrace_绑定ActorPayload与原始顺序且重复确定()
    {
        EnsureLoaded();
        var engine = Engine("random-trace", "s", "p", "n", FixtureRuleset());
        var provider = DeterministicReplayCheckpointProvider.Current;
        var context = new ReplayCheckpointContext(ReplayCheckpointPosition.Opening, -1, null, null);
        var first = new ReplayRandomTraceEvent(0, JsonSerializer.SerializeToElement(new
        {
            randomSeq = 1,
            type = "shuffle",
            order = new[] { "a", "b" },
        }));
        var second = new ReplayRandomTraceEvent(1, JsonSerializer.SerializeToElement(new
        {
            randomSeq = 2,
            type = "roll",
            value = 4,
        }));

        var original = provider.Capture(engine, context, [first, second]);
        var repeated = provider.Capture(engine, context, [first, second]);
        var reordered = provider.Capture(engine, context, [second, first]);
        var actorChanged = provider.Capture(engine, context, [first with { Actor = 1 }, second]);

        Assert.Equal(original.RandomTraceDigest, repeated.RandomTraceDigest);
        Assert.Equal(2, original.RandomEventCount);
        Assert.NotEqual(original.RandomTraceDigest, reordered.RandomTraceDigest);
        Assert.NotEqual(original.RandomTraceDigest, actorChanged.RandomTraceDigest);
    }

    [Fact]
    public async Task 在线日志_Prepare_生产ProviderWorker逐CheckpointRoundtrip一致()
    {
        EnsureLoaded();
        var (registry, artifact) = Registry();
        var ruleset = FixtureRuleset();
        var matchId = $"checkpoint-{Guid.NewGuid():N}";
        var logPath = IntegrationLogPath(matchId);
        var opened = false;
        try
        {
            var engine = Engine(matchId, "live-session", "artifact-player-0", "visible-canary", ruleset);
            engine.State.Players[1].AccountName = "artifact-player-1";
            engine.EnablePrivateSnapshotLog = false;
            var coordinator = new ReplayCheckpointLogCoordinator(matchId);
            MatchLogRecorder.OpenAt(matchId, logPath);
            opened = true;
            engine.OnMatchLogWithReceipt = (kind, actor, payload) =>
            {
                var receipt = MatchLogRecorder.Append(matchId, engine.State, kind, actor, payload);
                coordinator.Observe(engine.State, kind, actor, payload, receipt);
                return receipt;
            };

            engine.RecordMatchLog("match_start", -1, MatchStartPayload(artifact, engine.State.RngSeed));
            engine.FlushPendingMatchLogs();
            Assert.True(coordinator.WriteOpening(engine));

            await ApplySystemMulligan(engine, coordinator, 0);
            await ApplyPlayer(engine, coordinator, 1, "Mulligan", new { redraw = false }, "player-mulligan-1");
            await ApplyPlayer(engine, coordinator, 0, "Surrender", new { }, "player-surrender-1");
            Assert.True(engine.State.IsGameOver);
            Assert.True(coordinator.WriteTerminal(engine));
            var terminal = ReplayTerminalSemantics.Capture(engine.State);
            engine.RecordMatchLog("match_end", -1, new
            {
                winnerIndex = terminal.WinnerIndex,
                isDraw = terminal.IsDraw,
                reason = terminal.Reason,
                turnCount = terminal.TurnCount,
            });
            MatchLogRecorder.Close(matchId);
            opened = false;

            var bytes = await File.ReadAllBytesAsync(logPath);
            var checkpointLines = File.ReadLines(logPath)
                .Where(line => line.Contains("\"kind\":\"replay_checkpoint\"", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(5, checkpointLines.Length);
            var persistedCheckpointText = string.Join('\n', checkpointLines);
            Assert.DoesNotContain("artifact-player", persistedCheckpointText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("visible-canary", persistedCheckpointText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deckRaw", persistedCheckpointText, StringComparison.Ordinal);
            Assert.DoesNotContain("replayHands", persistedCheckpointText, StringComparison.Ordinal);
            Assert.DoesNotContain("OP16-", persistedCheckpointText, StringComparison.Ordinal);

            var preparation = ReplayMatchPreparation.Prepare(bytes, "online-roundtrip", registry);
            var prepared = Assert.IsType<PreparedReplayMatch>(preparation.Prepared);
            Assert.Null(preparation.Quarantine);
            Assert.Equal(3, prepared.Tape.Actions.Count);
            Assert.Equal(ReplayActionSource.System, prepared.Tape.Actions[0].Source);
            var worker = new InProcessArtifactReplayWorker(
                "fixture-checkpoint-provider-v1",
                prepared.Artifact,
                DeterministicReplayCheckpointProvider.Current,
                ruleset);
            var dispatcher = new ArtifactReplayWorkerDispatcher([worker]);
            var firstReplay = await dispatcher.ExecuteAsync(prepared);
            var secondReplay = await dispatcher.ExecuteAsync(prepared);

            Assert.True(
                firstReplay.IsVerified,
                firstReplay.Quarantine is null
                    ? "worker 未返回 verified 或 quarantine"
                    : $"{firstReplay.Quarantine.ReasonCode}/{firstReplay.Quarantine.Stage}: {firstReplay.Quarantine.Message}");
            var verified = Assert.IsType<VerifiedArtifactReplay>(firstReplay.Verified);
            Assert.Null(firstReplay.Quarantine);
            Assert.Equal(5, verified.Checkpoints.Count);
            Assert.Equal(
                prepared.CheckpointContract!.Checkpoints.Select(checkpoint => checkpoint.StateDigest),
                verified.Checkpoints.Select(checkpoint => checkpoint.StateDigest));
            Assert.Equal(verified.ReplayDigest, secondReplay.Verified!.ReplayDigest);
        }
        finally
        {
            if (opened) MatchLogRecorder.Close(matchId);
            TryDelete(logPath);
        }
    }

    [Fact]
    public void 恢复断点状态标记_准备层FailClosed且开关默认关闭()
    {
        Assert.False(ReplayCheckpointFeature.IsEnabled(null));
        Assert.False(ReplayCheckpointFeature.IsEnabled("0"));
        Assert.False(ReplayCheckpointFeature.IsEnabled("false"));
        Assert.True(ReplayCheckpointFeature.IsEnabled("1"));
        Assert.True(ReplayCheckpointFeature.IsEnabled("TRUE"));

        EnsureLoaded();
        var (registry, artifact) = Registry();
        var matchId = $"checkpoint-disabled-{Guid.NewGuid():N}";
        var logPath = IntegrationLogPath(matchId);
        var opened = false;
        try
        {
            var engine = Engine(matchId, "s", "artifact-player-0", "n", FixtureRuleset());
            engine.State.Players[1].AccountName = "artifact-player-1";
            var coordinator = new ReplayCheckpointLogCoordinator(matchId);
            MatchLogRecorder.OpenAt(matchId, logPath);
            opened = true;
            engine.OnMatchLogWithReceipt = (kind, actor, payload) =>
            {
                var receipt = MatchLogRecorder.Append(matchId, engine.State, kind, actor, payload);
                coordinator.Observe(engine.State, kind, actor, payload, receipt);
                return receipt;
            };
            engine.RecordMatchLog("match_start", -1, MatchStartPayload(artifact, engine.State.RngSeed));
            engine.FlushPendingMatchLogs();
            Assert.True(coordinator.WriteOpening(engine));
            coordinator.Disable(engine.State, "process_recovery_random_trace_not_restored");
            engine.State.WinnerIndex = 1;
            engine.State.GameOverReason = "恢复中止 fixture";
            engine.RecordMatchLog("match_end", -1, new
            {
                winnerIndex = 1,
                isDraw = false,
                reason = engine.State.GameOverReason,
                turnCount = engine.State.TurnCount,
            });
            MatchLogRecorder.Close(matchId);
            opened = false;

            var result = ReplayMatchPreparation.Prepare(
                File.ReadAllBytes(logPath),
                "recovery-disabled",
                registry);
            Assert.Null(result.Prepared);
            Assert.Equal(
                ReplayQuarantineCodes.CheckpointContinuityDisabled,
                result.Quarantine!.ReasonCode);
        }
        finally
        {
            if (opened) MatchLogRecorder.Close(matchId);
            TryDelete(logPath);
        }
    }

    [Fact]
    public void Provider逐动作性能门_平均低于二十毫秒()
    {
        EnsureLoaded();
        var engine = Engine("checkpoint-benchmark", "s", "p", "n", FixtureRuleset());
        var provider = DeterministicReplayCheckpointProvider.Current;
        var trace = new[]
        {
            new ReplayRandomTraceEvent(-1, JsonSerializer.SerializeToElement(new
            {
                randomSeq = 1,
                type = "shuffle",
                beforeOrder = Enumerable.Range(0, 50).Select(index => new { index, id = $"c-{index}" }).ToArray(),
                afterOrder = Enumerable.Range(0, 50).Reverse().Select(index => new { index, id = $"c-{index}" }).ToArray(),
            })),
        };
        var context = new ReplayCheckpointContext(ReplayCheckpointPosition.AfterAction, 0, 1, Sha('a'));
        _ = provider.Capture(engine, context, trace);

        const int iterations = 100;
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
            _ = provider.Capture(engine, context with { ActionIndex = index }, trace);
        stopwatch.Stop();
        var averageMilliseconds = stopwatch.Elapsed.TotalMilliseconds / iterations;
        output.WriteLine($"checkpoint capture 平均耗时：{averageMilliseconds:F3} ms（{iterations} 次）");
        Assert.InRange(averageMilliseconds, 0, 20);
    }

    [Fact]
    public async Task Recorder并发Append_回执序号与JSONL物理顺序严格一致()
    {
        EnsureLoaded();
        var matchId = $"checkpoint-order-{Guid.NewGuid():N}";
        var logPath = IntegrationLogPath(matchId);
        var engine = Engine(matchId, "s", "p", "n", FixtureRuleset());
        var opened = false;
        try
        {
            MatchLogRecorder.OpenAt(matchId, logPath);
            opened = true;
            const int appendCount = 64;
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, appendCount)
                .Select(index => Task.Run(async () =>
                {
                    await start.Task;
                    return MatchLogRecorder.Append(
                        matchId,
                        engine.State,
                        "concurrent_fixture",
                        index % 2,
                        new { index });
                }))
                .ToArray();
            start.SetResult(true);
            var receipts = await Task.WhenAll(tasks);
            MatchLogRecorder.Close(matchId);
            opened = false;

            Assert.All(receipts, receipt => Assert.True(receipt.Queued));
            Assert.Equal(
                Enumerable.Range(1, appendCount).Select(value => (long)value),
                receipts.Select(receipt => receipt.Seq).Order());

            var physicalSequences = File.ReadLines(logPath)
                .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("seq").GetInt64())
                .ToArray();
            Assert.Equal(
                Enumerable.Range(1, appendCount).Select(value => (long)value),
                physicalSequences);
        }
        finally
        {
            if (opened) MatchLogRecorder.Close(matchId);
            TryDelete(logPath);
        }
    }

    private static async Task ApplySystemMulligan(
        GameEngine engine,
        ReplayCheckpointLogCoordinator coordinator,
        int actor)
    {
        const string requestId = "system-mulligan-0";
        var data = JsonSerializer.SerializeToElement(new { redraw = false });
        engine.RecordMatchLog("mulligan_timeout_auto_keep", actor, new { requestId, redraw = false });
        var execution = engine.HandleActionWithReceipt(
            actor,
            "Mulligan",
            data,
            requestId,
            GameActionSource.System);
        Assert.True(execution.Accepted);
        await engine.WaitSettledAsync();
        Assert.True(coordinator.WriteAfterAction(engine, execution.AcceptedLog));
    }

    private static async Task ApplyPlayer(
        GameEngine engine,
        ReplayCheckpointLogCoordinator coordinator,
        int actor,
        string action,
        object dataValue,
        string requestId)
    {
        var data = JsonSerializer.SerializeToElement(dataValue);
        engine.RecordMatchLog("player_action_requested", actor, new
        {
            requestId,
            action,
            data,
            source = "player",
        });
        var execution = engine.HandleActionWithReceipt(
            actor,
            action,
            data,
            requestId,
            GameActionSource.Player);
        Assert.True(execution.Accepted);
        await engine.WaitSettledAsync();
        Assert.True(coordinator.WriteAfterAction(engine, execution.AcceptedLog));
    }

    private static GameEngine Engine(
        string roomId,
        string session,
        string account,
        string displayName,
        CardRuleset ruleset)
    {
        var engine = new GameEngine(
            roomId,
            (session, account, Deck),
            ($"{session}-opponent", "artifact-player-1", Deck),
            firstPlayer: 0,
            rngSeed: 24681357,
            ruleset: ruleset);
        engine.State.Players[0].DisplayName = displayName;
        engine.State.Players[1].DisplayName = $"{displayName}-opponent";
        return engine;
    }

    private static PendingPrompt Prompt(Guid choiceId, string secret)
        => new()
        {
            PromptId = "prompt-fixed-1",
            PlayerIndex = 0,
            Kind = "Option",
            ValidChoices = [choiceId.ToString()],
            MinChoose = 1,
            MaxChoose = 1,
            PromptText = "fixture",
            ResumeKey = "fixture-resume",
            Extra = new Dictionary<string, object?> { ["secret"] = secret },
        };

    private static object MatchStartPayload(ReplayArtifactDescriptor artifact, int seed)
        => new
        {
            players = new object[]
            {
                new { index = 0, deckRaw = Deck, alwaysPromptOnLifeReveal = false },
                new { index = 1, deckRaw = Deck, alwaysPromptOnLifeReveal = false },
            },
            firstPlayer = 0,
            rngSeed = seed,
            openingSetupAfterFirstPlayerChoice = false,
            eventAdapterVersion = artifact.EventAdapterVersion,
            engineArtifactId = artifact.EngineArtifactId,
            engineCommit = artifact.EngineCommit,
            binarySha256 = artifact.BinarySha256,
            rulesVersion = artifact.RulesVersion,
            rulesetManifestHash = artifact.RulesetManifestHash,
            cardDbContentHash = artifact.CardDbContentHash,
            rngAlgorithmVersion = artifact.RngAlgorithmVersion,
            deterministicIdVersion = artifact.DeterministicIdVersion,
            openingProtocolVersion = artifact.OpeningProtocolVersion,
            replayConfigSchema = artifact.ReplayConfigSchema,
            replayConfig = new { leaderKeywordWildcard = false },
        };

    private static (ReplayArtifactRegistry Registry, ReplayArtifactDescriptor Artifact) Registry()
    {
        var artifact = new ReplayArtifactDescriptor(
            MatchLogEventAdapter.SupportedSchema,
            MatchLogEventAdapter.CurrentAdapterVersion,
            "fixture-checkpoint-v2",
            new string('1', 40),
            Sha('a'),
            "fixture-rules-v1",
            Sha('b'),
            Sha('c'),
            "dotnet-system-random-v1",
            "grandumi-deterministic-id-v1",
            "grandumi-opening-v2",
            "grandumi.replay-config.v1",
            "fixture://checkpoint-provider");
        var root = JsonSerializer.SerializeToNode(new
        {
            schema = ReplayArtifactRegistry.Schema,
            registryVersion = "fixture-checkpoint-v2.1",
            registryHash = Sha('0'),
            artifacts = new[]
            {
                new
                {
                    matchLogSchema = artifact.MatchLogSchema,
                    eventAdapterVersion = artifact.EventAdapterVersion,
                    engineArtifactId = artifact.EngineArtifactId,
                    engineCommit = artifact.EngineCommit,
                    binarySha256 = artifact.BinarySha256,
                    rulesVersion = artifact.RulesVersion,
                    rulesetManifestHash = artifact.RulesetManifestHash,
                    cardDbContentHash = artifact.CardDbContentHash,
                    rngAlgorithmVersion = artifact.RngAlgorithmVersion,
                    deterministicIdVersion = artifact.DeterministicIdVersion,
                    openingProtocolVersion = artifact.OpeningProtocolVersion,
                    replayConfigSchema = artifact.ReplayConfigSchema,
                    executable = artifact.Executable,
                },
            },
        })!.AsObject();
        var hash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(root),
            excludedTopLevelProperty: "registryHash");
        root["registryHash"] = hash;
        return (ReplayArtifactRegistry.Parse(root.ToJsonString()), artifact);
    }

    private static CardRuleset FixtureRuleset()
    {
        var current = CardRulesetManager.Current;
        return new CardRuleset(
            "fixture-rules-v1",
            current.Id,
            "checkpoint production fixture",
            current.CloneScriptedEffects(),
            current.CloneDslDefinitions(),
            []);
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

    private static string IntegrationLogPath(string matchId)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "checkpoint-integration");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{matchId}.jsonl");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

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

    private static string Sha(char value) => $"sha256:{new string(value, 64)}";
}
