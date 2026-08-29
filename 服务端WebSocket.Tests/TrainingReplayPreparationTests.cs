using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// P0-0/P0-A 第一批合成 golden。全部账号、牌组和版本身份均为虚构值，不包含生产日志或玩家信息。
/// </summary>
public class TrainingReplayPreparationTests
{
    private static readonly Lazy<ReplayArtifactRegistry> FixtureRegistry = new(() =>
        ReplayArtifactRegistry.Load(RepoPath(
            "服务端WebSocket.Tests",
            "Fixtures",
            "training-replay-artifact-registry.v1.json")));
    private static readonly Lazy<ReplayArtifactRegistry> CurrentFixtureRegistry = new(CreateCurrentFixtureRegistry);

    public static IEnumerable<object?[]> GoldenCases()
    {
        yield return Case(
            "01-player-accepted",
            BuildLog(
                Start("golden-01"),
                Event("golden-01", 2, "player_action_requested", 0, new { action = "Mulligan", data = new { redraw = false } }),
                Event("golden-01", 3, "player_action_accepted", 0, new { action = "Mulligan" }),
                End("golden-01", 4)),
            actionCount: 1,
            labelCount: 1,
            tapeHash: "sha256:5df74cd9c04ebff924f5d0787b650752af5208d1bf12ecc3bd1e719b6111de3d");

        yield return Case(
            "02-rejected-excluded",
            BuildLog(
                Start("golden-02"),
                Event("golden-02", 2, "player_action_requested", 0, new { action = "Attack", data = new { attackerId = "fake-card", targetIsLeader = true } }),
                Event("golden-02", 3, "player_action_rejected", 0, new { action = "Attack", reason = "fixture rejection" }),
                Event("golden-02", 4, "player_action_requested", 0, new { action = "EndTurn", data = new { } }),
                Event("golden-02", 5, "player_action_accepted", 0, new { action = "EndTurn" }),
                End("golden-02", 6)),
            actionCount: 1,
            labelCount: 1,
            tapeHash: "sha256:e17b8c127dfa13e7c030db82d513d74d660a1ccb0af816414cf4a9e11ff715f3");

        yield return Case(
            "03-system-starting-choice",
            BuildLog(
                Start("golden-03", firstPlayer: -1, deferredOpening: true),
                Event("golden-03", 2, "starting_player_choice_timeout_auto_select", 0, new { goFirst = true }),
                Event("golden-03", 3, "player_action_accepted", 0, new { action = "ChooseFirstPlayer" }),
                Event("golden-03", 4, "player_action_requested", 0, new { action = "Mulligan", data = new { redraw = false } }),
                Event("golden-03", 5, "player_action_accepted", 0, new { action = "Mulligan" }),
                Event("golden-03", 6, "player_action_requested", 1, new { action = "Mulligan", data = new { redraw = true } }),
                Event("golden-03", 7, "player_action_accepted", 1, new { action = "Mulligan" }),
                End("golden-03", 8)),
            actionCount: 3,
            labelCount: 2,
            tapeHash: "sha256:3991cc4487a7b782d22c4defc816d6cb481cdcc56b1da14f3b405d967c79c740");

        yield return Case(
            "04-system-mulligan-auto-keep",
            BuildLog(
                Start("golden-04"),
                Event("golden-04", 2, "player_action_requested", 0, new { action = "Mulligan", data = new { redraw = false } }),
                Event("golden-04", 3, "player_action_accepted", 0, new { action = "Mulligan" }),
                Event("golden-04", 4, "mulligan_timeout_auto_keep", 1, new { redraw = false }),
                End("golden-04", 5)),
            actionCount: 2,
            labelCount: 1,
            tapeHash: "sha256:37f961d34da14fe8cad48dd7a6d1b1c1897a05561818193ad41cc60fb69255bb");

        yield return Case(
            "05-prompt-response-correlated",
            BuildLog(
                Start("golden-05"),
                Event("golden-05", 2, "player_action_requested", 0, new { action = "PromptResponse", data = new { promptId = "p1", chosen = new[] { "choice-a" } } }),
                Event("golden-05", 3, "prompt_response", 0, new { promptId = "p1", chosen = new[] { "choice-a" } }),
                Event("golden-05", 4, "player_action_accepted", 0, new { action = "PromptResponse" }),
                End("golden-05", 5)),
            actionCount: 1,
            labelCount: 1,
            tapeHash: "sha256:9e0e152b5f51a30e87652f74e9045aadbcbf1849bc456bc28a3926721765fe78");

        var missingVersion = Start("golden-06");
        missingVersion["payload"]!.AsObject().Remove("engineArtifactId");
        yield return QuarantineCase(
            "06-missing-version",
            BuildLog(missingVersion, End("golden-06", 2)),
            ReplayQuarantineCodes.MissingVersionIdentity);

        var unknownArtifact = Start("golden-07");
        unknownArtifact["payload"]!["engineArtifactId"] = "fixture-server-unknown";
        yield return QuarantineCase(
            "07-unknown-artifact",
            BuildLog(unknownArtifact, End("golden-07", 2)),
            ReplayQuarantineCodes.UnsupportedArtifact);

        var identityMismatch = Start("golden-08");
        identityMismatch["payload"]!["rulesVersion"] = "fixture-rules-other";
        yield return QuarantineCase(
            "08-artifact-identity-mismatch",
            BuildLog(identityMismatch, End("golden-08", 2)),
            ReplayQuarantineCodes.ArtifactIdentityMismatch);

        yield return QuarantineCase(
            "09-sequence-gap",
            BuildLog(Start("golden-09"), End("golden-09", 3)),
            ReplayQuarantineCodes.SequenceGap);

        yield return QuarantineCase(
            "10-orphan-accepted",
            BuildLog(
                Start("golden-10"),
                Event("golden-10", 2, "player_action_accepted", 0, new { action = "EndTurn" }),
                End("golden-10", 3)),
            ReplayQuarantineCodes.OrphanActionResult);

        yield return QuarantineCase(
            "11-ambiguous-pairing",
            BuildLog(
                Start("golden-11"),
                Event("golden-11", 2, "player_action_requested", 0, new { action = "EndTurn", data = new { } }),
                Event("golden-11", 3, "player_action_requested", 0, new { action = "EndTurn", data = new { } }),
                Event("golden-11", 4, "player_action_accepted", 0, new { action = "EndTurn" }),
                End("golden-11", 5)),
            ReplayQuarantineCodes.AmbiguousActionPairing);

        yield return QuarantineCase(
            "12-request-data-missing",
            BuildLog(
                Start("golden-12"),
                Event("golden-12", 2, "player_action_requested", 0, new { action = "Mulligan" }),
                Event("golden-12", 3, "player_action_accepted", 0, new { action = "Mulligan" }),
                End("golden-12", 4)),
            ReplayQuarantineCodes.MissingActionData);

        yield return QuarantineCase(
            "13-prompt-response-mismatch",
            BuildLog(
                Start("golden-13"),
                Event("golden-13", 2, "player_action_requested", 0, new { action = "PromptResponse", data = new { promptId = "p1", chosen = new[] { "choice-a" } } }),
                Event("golden-13", 3, "prompt_response", 0, new { promptId = "p1", chosen = new[] { "choice-b" } }),
                Event("golden-13", 4, "player_action_accepted", 0, new { action = "PromptResponse" }),
                End("golden-13", 5)),
            ReplayQuarantineCodes.PromptResponseMismatch);

        yield return QuarantineCase(
            "14-legacy-prompt-timeout",
            BuildLog(
                Start("golden-14"),
                Event("golden-14", 2, "prompt_timeout", 0, new { promptId = "p1" }),
                End("golden-14", 3)),
            ReplayQuarantineCodes.UnsupportedSystemEvent);

        var unterminated = BuildLog(
            Start("golden-15"),
            Event("golden-15", 2, "player_action_requested", 0, new { action = "EndTurn", data = new { } }),
            Event("golden-15", 3, "player_action_accepted", 0, new { action = "EndTurn" }),
            End("golden-15", 4)).TrimEnd('\n');
        yield return QuarantineCase(
            "15-incomplete-tail",
            unterminated,
            ReplayQuarantineCodes.IncompleteTail);
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void 十五个合成Golden_确定性构建或整局隔离(
        string name,
        string logText,
        bool shouldPrepare,
        string? expectedReason,
        int expectedActionCount,
        int expectedLabelCount,
        string? expectedTapeHash)
    {
        var bytes = Encoding.UTF8.GetBytes(logText);
        var first = ReplayMatchPreparation.Prepare(bytes, name, FixtureRegistry.Value);
        var second = ReplayMatchPreparation.Prepare(bytes, name, FixtureRegistry.Value);

        Assert.Equal(shouldPrepare, first.IsPrepared);
        if (shouldPrepare)
        {
            var prepared = Assert.IsType<PreparedReplayMatch>(first.Prepared);
            Assert.Null(first.Quarantine);
            Assert.Equal(expectedActionCount, prepared.Tape.Actions.Count);
            Assert.Equal(expectedLabelCount, prepared.Tape.HumanLabelCandidateCount);
            Assert.True(
                string.Equals(expectedTapeHash, prepared.Tape.StableHash, StringComparison.Ordinal),
                $"{name} 的完整磁带哈希：{prepared.Tape.StableHash}");
            Assert.Equal(prepared.Tape.StableHash, second.Prepared!.Tape.StableHash);
            Assert.Equal(prepared.StableHash, second.Prepared.StableHash);
            Assert.Equal(prepared.Tape.Actions.Count, prepared.MaterializeActionEntries().Count);
            Assert.Equal(1001, prepared.Header.RngSeed);
            Assert.Equal("FIXTURE-L0\nFIXTURE-C0", prepared.Header.Player0.DeckRaw);
            Assert.Equal("FIXTURE-L1\nFIXTURE-C1", prepared.Header.Player1.DeckRaw);
        }
        else
        {
            Assert.Null(first.Prepared); // 隔离结果不暴露任何已暂存的部分磁带。
            var quarantine = Assert.IsType<QuarantinedReplayMatch>(first.Quarantine);
            Assert.Equal(expectedReason, quarantine.ReasonCode);
            Assert.Equal(quarantine.StableHash, second.Quarantine!.StableHash);
        }
    }

    [Fact]
    public void 注册表内容被篡改_自校验立即失败()
    {
        var text = File.ReadAllText(RepoPath(
            "服务端WebSocket.Tests",
            "Fixtures",
            "training-replay-artifact-registry.v1.json"));
        var tampered = text.Replace("fixture-rules-v1", "fixture-rules-v2", StringComparison.Ordinal);

        var exception = Assert.Throws<ReplayArtifactRegistryException>(
            () => ReplayArtifactRegistry.Parse(tampered));
        Assert.Contains("自校验哈希不一致", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 日志Binary哈希与注册工件不同_禁止只凭Commit回退()
    {
        var start = Start("binary-mismatch");
        start["payload"]!["binarySha256"] =
            "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(BuildLog(start, End("binary-mismatch", 2))),
            "binary-mismatch",
            FixtureRegistry.Value);

        Assert.False(result.IsPrepared);
        Assert.Equal(ReplayQuarantineCodes.ArtifactIdentityMismatch, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void 生产注册表初始为空_不会把当前Main当历史工件兜底()
    {
        var registry = ReplayArtifactRegistry.Load(RepoPath(
            "服务端WebSocket",
            "Training",
            "Artifacts",
            "replay-artifact-registry.v1.json"));

        Assert.Empty(registry.Artifacts);
        var exception = Assert.Throws<ReplayQuarantineException>(() => registry.Resolve(
            FixtureRegistry.Value.Artifacts.Single() is var artifact
                ? new ReplayVersionIdentity(
                    artifact.MatchLogSchema,
                    artifact.EventAdapterVersion,
                    artifact.EngineArtifactId,
                    artifact.EngineCommit,
                    artifact.BinarySha256,
                    artifact.RulesVersion,
                    artifact.RulesetManifestHash,
                    artifact.CardDbContentHash,
                    artifact.RngAlgorithmVersion,
                    artifact.DeterministicIdVersion,
                    artifact.OpeningProtocolVersion,
                    artifact.ReplayConfigSchema)
                : throw new InvalidOperationException()));
        Assert.Equal(ReplayQuarantineCodes.UnsupportedArtifact, exception.ReasonCode);
    }

    [Fact]
    public void 动作Data属性顺序不同_规范磁带哈希相同()
    {
        var leftData = new JsonObject { ["targetId"] = "leader", ["count"] = 2 };
        var rightData = new JsonObject { ["count"] = 2, ["targetId"] = "leader" };
        var left = BuildLog(
            Start("canonical-order"),
            Event("canonical-order", 2, "player_action_requested", 0,
                new JsonObject { ["action"] = "AttachDon", ["data"] = leftData }),
            Event("canonical-order", 3, "player_action_accepted", 0, new { action = "AttachDon" }),
            End("canonical-order", 4));
        var right = BuildLog(
            Start("canonical-order"),
            Event("canonical-order", 2, "player_action_requested", 0,
                new JsonObject { ["action"] = "AttachDon", ["data"] = rightData }),
            Event("canonical-order", 3, "player_action_accepted", 0, new { action = "AttachDon" }),
            End("canonical-order", 4));

        var leftResult = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(left), "left", FixtureRegistry.Value);
        var rightResult = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(right), "right", FixtureRegistry.Value);

        Assert.Equal(leftResult.Prepared!.Tape.StableHash, rightResult.Prepared!.Tape.StableHash);
        Assert.NotEqual(leftResult.Prepared.SourceFileHash, rightResult.Prepared.SourceFileHash);
    }

    [Fact]
    public void 整局后段发生分歧_不返回前段已Accepted动作()
    {
        var text = BuildLog(
            Start("all-or-nothing"),
            Event("all-or-nothing", 2, "player_action_requested", 0, new { action = "EndTurn", data = new { } }),
            Event("all-or-nothing", 3, "player_action_accepted", 0, new { action = "EndTurn" }),
            Event("all-or-nothing", 4, "player_action_accepted", 1, new { action = "EndTurn" }),
            End("all-or-nothing", 5));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "all-or-nothing", FixtureRegistry.Value);

        Assert.False(result.IsPrepared);
        Assert.Null(result.Prepared);
        Assert.Equal(ReplayQuarantineCodes.OrphanActionResult, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void 新Accepted自包含规范Data与RequestId_不依赖Requested取训练数据()
    {
        var text = BuildLog(
            Start(
                "self-contained-player",
                adapterVersion: MatchLogEventAdapter.CurrentAdapterVersion),
            Event("self-contained-player", 2, "player_action_requested", 0, new
            {
                requestId = "req-player-1",
                action = "AttachDon",
                data = new { targetId = "leader", count = 2 },
                source = "player",
            }),
            Event("self-contained-player", 3, "player_action_accepted", 0, new
            {
                requestId = "req-player-1",
                action = "AttachDon",
                data = new { count = 2, targetId = "leader" },
                source = "player",
            }),
            End("self-contained-player", 4));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "self-contained-player", CurrentFixtureRegistry.Value);

        var action = Assert.Single(result.Prepared!.Tape.Actions);
        Assert.Equal(3, action.OrderSeq);
        Assert.Equal(3, action.SourceSeq);
        Assert.Equal(3, action.ResultSeq);
        Assert.Equal(ReplayActionSource.Player, action.Source);
        Assert.True(action.IsTrainingLabelCandidate);
        Assert.Equal(2, action.Data.GetProperty("count").GetInt32());
    }

    [Fact]
    public void 系统超时Accepted明确标记System_绝不成为真人训练标签()
    {
        var text = BuildLog(
            Start(
                "self-contained-system",
                firstPlayer: -1,
                deferredOpening: true,
                adapterVersion: MatchLogEventAdapter.CurrentAdapterVersion),
            Event("self-contained-system", 2, "starting_player_choice_timeout_auto_select", 0, new
            {
                requestId = "system-timeout-1",
                goFirst = true,
            }),
            Event("self-contained-system", 3, "player_action_accepted", 0, new
            {
                requestId = "system-timeout-1",
                action = "ChooseFirstPlayer",
                data = new { goFirst = true },
                source = "system",
            }),
            End("self-contained-system", 4));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "self-contained-system", CurrentFixtureRegistry.Value);

        var action = Assert.Single(result.Prepared!.Tape.Actions);
        Assert.Equal(ReplayActionSource.System, action.Source);
        Assert.False(action.IsTrainingLabelCandidate);
        Assert.Equal(0, result.Prepared.Tape.HumanLabelCandidateCount);
    }

    [Fact]
    public void AcceptedData与Requested不一致_整局隔离()
    {
        var text = BuildLog(
            Start(
                "self-contained-mismatch",
                adapterVersion: MatchLogEventAdapter.CurrentAdapterVersion),
            Event("self-contained-mismatch", 2, "player_action_requested", 0, new
            {
                requestId = "req-mismatch",
                action = "Mulligan",
                data = new { redraw = false },
                source = "player",
            }),
            Event("self-contained-mismatch", 3, "player_action_accepted", 0, new
            {
                requestId = "req-mismatch",
                action = "Mulligan",
                data = new { redraw = true },
                source = "player",
            }),
            End("self-contained-mismatch", 4));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "self-contained-mismatch", CurrentFixtureRegistry.Value);

        Assert.False(result.IsPrepared);
        Assert.Equal(ReplayQuarantineCodes.AcceptedActionDataMismatch, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void 同席位重复RequestId_即使动作分开完成也整局隔离()
    {
        var text = BuildLog(
            Start(
                "duplicate-request-id",
                adapterVersion: MatchLogEventAdapter.CurrentAdapterVersion),
            Event("duplicate-request-id", 2, "player_action_requested", 0, new
            {
                requestId = "duplicate-id",
                action = "Mulligan",
                data = new { redraw = false },
                source = "player",
            }),
            Event("duplicate-request-id", 3, "player_action_accepted", 0, new
            {
                requestId = "duplicate-id",
                action = "Mulligan",
                data = new { redraw = false },
                source = "player",
            }),
            Event("duplicate-request-id", 4, "player_action_requested", 0, new
            {
                requestId = "duplicate-id",
                action = "EndTurn",
                data = new { },
                source = "player",
            }),
            End("duplicate-request-id", 5));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "duplicate-request-id", CurrentFixtureRegistry.Value);

        Assert.False(result.IsPrepared);
        Assert.Equal(ReplayQuarantineCodes.DuplicateRequestCorrelation, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void 当前Adapter缺少自包含Accepted字段_不得退回Legacy配对()
    {
        var text = BuildLog(
            Start(
                "current-old-accepted",
                adapterVersion: MatchLogEventAdapter.CurrentAdapterVersion),
            Event("current-old-accepted", 2, "player_action_requested", 0, new
            {
                requestId = "req-current-1",
                action = "Mulligan",
                data = new { redraw = false },
                source = "player",
            }),
            Event("current-old-accepted", 3, "player_action_accepted", 0, new { action = "Mulligan" }),
            End("current-old-accepted", 4));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "current-old-accepted", CurrentFixtureRegistry.Value);

        Assert.False(result.IsPrepared);
        Assert.Equal(ReplayQuarantineCodes.MalformedActionResult, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void 当前Adapter系统调度事件缺少Accepted_不得沿用Legacy隐式映射()
    {
        var text = BuildLog(
            Start(
                "current-system-missing-result",
                adapterVersion: MatchLogEventAdapter.CurrentAdapterVersion),
            Event("current-system-missing-result", 2, "mulligan_timeout_auto_keep", 1, new
            {
                requestId = "system-mulligan-1",
                redraw = false,
            }),
            End("current-system-missing-result", 3));

        var result = ReplayMatchPreparation.Prepare(
            Encoding.UTF8.GetBytes(text), "current-system-missing-result", CurrentFixtureRegistry.Value);

        Assert.False(result.IsPrepared);
        Assert.Equal(ReplayQuarantineCodes.UnresolvedAction, result.Quarantine!.ReasonCode);
    }

    private static object?[] Case(
        string name,
        string text,
        int actionCount,
        int labelCount,
        string tapeHash)
        => [name, text, true, null, actionCount, labelCount, tapeHash];

    private static object?[] QuarantineCase(string name, string text, string reason)
        => [name, text, false, reason, 0, 0, null];

    private static JsonObject Start(
        string matchId,
        int firstPlayer = 0,
        bool deferredOpening = false,
        string? adapterVersion = null)
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
                    new { index = 0, accountName = "fixture-account-0", deckRaw = "FIXTURE-L0\nFIXTURE-C0", alwaysPromptOnLifeReveal = false },
                    new { index = 1, accountName = "fixture-account-1", deckRaw = "FIXTURE-L1\nFIXTURE-C1", alwaysPromptOnLifeReveal = true },
                },
                firstPlayer,
                rngSeed = 1001,
                openingSetupAfterFirstPlayerChoice = deferredOpening,
                eventAdapterVersion = adapterVersion ?? MatchLogEventAdapter.LegacyAdapterVersion,
                engineArtifactId = "fixture-server-20260828",
                engineCommit = "1111111111111111111111111111111111111111",
                binarySha256 = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
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

    private static ReplayArtifactRegistry CreateCurrentFixtureRegistry()
    {
        var artifact = FixtureRegistry.Value.Artifacts.Single();
        var root = JsonSerializer.SerializeToNode(new
        {
            schema = ReplayArtifactRegistry.Schema,
            registryVersion = "fixture-current-v2",
            artifacts = new[]
            {
                new
                {
                    matchLogSchema = artifact.MatchLogSchema,
                    eventAdapterVersion = MatchLogEventAdapter.CurrentAdapterVersion,
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
        root["registryHash"] = CanonicalJson.Hash(JsonSerializer.SerializeToElement(root));
        return ReplayArtifactRegistry.Parse(root.ToJsonString());
    }

    private static JsonObject End(string matchId, long seq)
        => Event(matchId, seq, "match_end", -1, new
        {
            winnerIndex = 0,
            reason = "fixture_complete",
            turnCount = 1,
        });

    private static JsonObject Event(string matchId, long seq, string kind, int actor, object payload)
    {
        var payloadNode = payload as JsonNode ?? JsonSerializer.SerializeToNode(payload)!;
        return new JsonObject
        {
            ["schema"] = MatchLogEventAdapter.SupportedSchema,
            ["matchId"] = matchId,
            ["seq"] = seq,
            ["kind"] = kind,
            ["actor"] = actor,
            ["payload"] = payloadNode,
        };
    }

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
