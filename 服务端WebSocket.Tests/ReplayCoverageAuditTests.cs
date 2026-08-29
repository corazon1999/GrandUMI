using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ReplayCoverageAuditTests
{
    [Fact]
    public void 覆盖审计_逐类区分且重复运行输出稳定()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        fixture.Capture();
        var logs = Path.Combine(fixture.Root, "logs");
        Directory.CreateDirectory(logs);
        WriteLog(Path.Combine(logs, "01-legacy.jsonl"), fixture.Identity, LogVariant.Legacy);
        WriteLog(Path.Combine(logs, "02-missing-identity.jsonl"), fixture.Identity, LogVariant.MissingIdentity);
        WriteLog(Path.Combine(logs, "03-missing-checkpoint.jsonl"), fixture.Identity, LogVariant.MissingCheckpoint);
        WriteLog(Path.Combine(logs, "04-identity-mismatch.jsonl"), fixture.Identity, LogVariant.IdentityMismatch);
        WriteLog(Path.Combine(logs, "05-artifact-not-archived.jsonl"), fixture.Identity, LogVariant.ArtifactNotArchived);
        WriteLog(Path.Combine(logs, "06-preparation-ready.jsonl"), fixture.Identity, LogVariant.PreparationReady);
        File.WriteAllText(Path.Combine(logs, "07-invalid.jsonl"), "{broken\n");
        var catalog = ReplayArtifactArchiveCatalog.Load(fixture.ArchiveRoot);

        var first = ReplayCoverageAudit.Generate(logs, catalog);
        var second = ReplayCoverageAudit.Generate(logs, catalog);

        Assert.Equal(7, first.TotalFiles);
        Assert.Equal(1, first.Count(ReplayCoverageStatus.Legacy));
        Assert.Equal(1, first.Count(ReplayCoverageStatus.MissingIdentity));
        Assert.Equal(1, first.Count(ReplayCoverageStatus.MissingCheckpoint));
        Assert.Equal(1, first.Count(ReplayCoverageStatus.IdentityMismatch));
        Assert.Equal(1, first.Count(ReplayCoverageStatus.ArtifactNotArchived));
        Assert.Equal(1, first.Count(ReplayCoverageStatus.PreparationReady));
        Assert.Equal(0, first.Count(ReplayCoverageStatus.ReplayWorkerReady));
        Assert.Equal(1, first.Count(ReplayCoverageStatus.InvalidLog));
        Assert.Equal(first.ReportHash, second.ReportHash);
        Assert.Equal(
            ReplayArtifactArchive.SerializeCanonical(first),
            ReplayArtifactArchive.SerializeCanonical(second));
        Assert.Equal(
            first.Entries.Select(entry => entry.SourceId).Order(StringComparer.Ordinal),
            first.Entries.Select(entry => entry.SourceId));
    }

    [Fact]
    public void 覆盖报告与测试候选Catalog_原子输出且不修改生产Registry()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        fixture.Capture();
        var logs = Path.Combine(fixture.Root, "logs");
        Directory.CreateDirectory(logs);
        WriteLog(Path.Combine(logs, "ready.jsonl"), fixture.Identity, LogVariant.PreparationReady);
        var catalog = ReplayArtifactArchiveCatalog.Load(fixture.ArchiveRoot);
        var report = ReplayCoverageAudit.Generate(logs, catalog);
        var output = Path.Combine(fixture.Root, "reports");
        var json = Path.Combine(output, "coverage.json");
        var markdown = Path.Combine(output, "coverage.md");
        var candidates = Path.Combine(output, "test-candidates.json");
        var productionRegistry = RepoPath(
            "服务端WebSocket",
            "Training",
            "Artifacts",
            "replay-artifact-registry.v1.json");
        var productionBefore = File.ReadAllBytes(productionRegistry);

        ReplayCoverageAudit.WriteOutputs(report, catalog, json, markdown, candidates);
        var firstJson = File.ReadAllBytes(json);
        var firstMarkdown = File.ReadAllBytes(markdown);
        var firstCandidates = File.ReadAllBytes(candidates);
        ReplayCoverageAudit.WriteOutputs(report, catalog, json, markdown, candidates);

        Assert.Equal(firstJson, File.ReadAllBytes(json));
        Assert.Equal(firstMarkdown, File.ReadAllBytes(markdown));
        Assert.Equal(firstCandidates, File.ReadAllBytes(candidates));
        Assert.Equal(productionBefore, File.ReadAllBytes(productionRegistry));
        Assert.Contains("| `replay_worker_ready` | 0 |", File.ReadAllText(markdown), StringComparison.Ordinal);
        var candidateRoot = JsonNode.Parse(File.ReadAllText(candidates))!.AsObject();
        Assert.Equal(ReplayCoverageAudit.CandidateCatalogSchema, candidateRoot["schema"]!.GetValue<string>());
        Assert.False(candidateRoot["productionRegistryModified"]!.GetValue<bool>());
        Assert.False(candidateRoot["artifacts"]![0]!["replayWorkerAvailable"]!.GetValue<bool>());
        Assert.False(candidateRoot["artifacts"]![0]!["productionRegistryEligible"]!.GetValue<bool>());
    }

    [Fact]
    public void 没有日志_所有覆盖计数如实为零()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        fixture.Capture();
        var catalog = ReplayArtifactArchiveCatalog.Load(fixture.ArchiveRoot);
        var report = ReplayCoverageAudit.Generate(Path.Combine(fixture.Root, "missing-logs"), catalog);

        Assert.Equal(0, report.TotalFiles);
        Assert.All(report.Summary, item => Assert.Equal(0, item.Count));
        Assert.Empty(report.Entries);
        Assert.Contains(
            "| 可进入独立 replay worker | `replay_worker_ready` | 0 |",
            ReplayCoverageAudit.BuildMarkdown(report),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 日志扫描遇到符号链接_拒绝越过审计根目录()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        fixture.Capture();
        var catalog = ReplayArtifactArchiveCatalog.Load(fixture.ArchiveRoot);
        var logs = Path.Combine(fixture.Root, "logs");
        Directory.CreateDirectory(logs);
        var outside = Path.Combine(fixture.Root, "outside.jsonl");
        WriteLog(outside, fixture.Identity, LogVariant.PreparationReady);
        try
        {
            File.CreateSymbolicLink(Path.Combine(logs, "escape.jsonl"), outside);
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            // 受限 Windows token 不能创建新符号链接；用仓库现有 junction 验证根路径拒绝。
            var junctionError = Assert.Throws<ReplayArtifactArchiveException>(() =>
                ReplayCoverageAudit.Generate(
                    RepoPath("opcgpro-web", "public", "cards"),
                    catalog));
            Assert.Contains("符号链接", junctionError.Message, StringComparison.Ordinal);
            return;
        }

        var error = Assert.Throws<ReplayArtifactArchiveException>(
            () => ReplayCoverageAudit.Generate(logs, catalog));

        Assert.Contains("符号链接", error.Message, StringComparison.Ordinal);
    }

    private static void WriteLog(
        string path,
        ReplayRuntimeIdentity identity,
        LogVariant variant)
    {
        var matchId = Path.GetFileNameWithoutExtension(path);
        var effectiveIdentity = variant == LogVariant.ArtifactNotArchived
            ? ReplayRuntimeIdentityFactory.Create(
                new ReplayRuntimeBuildIdentity(
                    identity.EngineCommit,
                    Sha('8'),
                    identity.CardDbContentHash),
                GrandUMI.Effects.Rules.CardRulesetManager.Current,
                new Version(10, 0, 7))
            : identity;
        var start = Event(matchId, 1, "match_start", -1, new
        {
            players = new object[]
            {
                new { index = 0, accountName = "fixture-a", deckRaw = "L0\nC0", alwaysPromptOnLifeReveal = false },
                new { index = 1, accountName = "fixture-b", deckRaw = "L1\nC1", alwaysPromptOnLifeReveal = true },
            },
            firstPlayer = 0,
            startingPlayerChooser = 0,
            startingDiceRolls = Array.Empty<object>(),
            rngSeed = 12345,
            openingSetupAfterFirstPlayerChoice = false,
            matchKind = "Friendly",
            matchLogSchema = effectiveIdentity.MatchLogSchema,
            eventAdapterVersion = variant == LogVariant.Legacy
                ? MatchLogEventAdapter.LegacyAdapterVersion
                : effectiveIdentity.EventAdapterVersion,
            engineArtifactId = effectiveIdentity.EngineArtifactId,
            engineCommit = effectiveIdentity.EngineCommit,
            binarySha256 = variant == LogVariant.IdentityMismatch
                ? Sha('9')
                : effectiveIdentity.BinarySha256,
            rulesVersion = effectiveIdentity.RulesVersion,
            rulesetManifestHash = effectiveIdentity.RulesetManifestHash,
            cardDbContentHash = effectiveIdentity.CardDbContentHash,
            rngAlgorithmVersion = effectiveIdentity.RngAlgorithmVersion,
            deterministicIdVersion = effectiveIdentity.DeterministicIdVersion,
            openingProtocolVersion = effectiveIdentity.OpeningProtocolVersion,
            replayConfigSchema = effectiveIdentity.ReplayConfigSchema,
            replayRuntimeManifestHash = effectiveIdentity.ManifestHash,
            replayConfig = new { leaderKeywordWildcard = false },
        });
        if (variant == LogVariant.MissingIdentity)
            start["payload"]!.AsObject().Remove("engineCommit");

        var events = new List<JsonObject> { start };
        if (variant == LogVariant.PreparationReady)
        {
            events.Add(Checkpoint(matchId, 2, "opening"));
            events.Add(Checkpoint(matchId, 3, "terminal"));
            events.Add(Event(matchId, 4, "match_end", -1, new
            {
                winnerIndex = 0,
                isDraw = false,
                reason = "normal",
                turnCount = 1,
            }));
        }
        else
        {
            events.Add(Event(matchId, 2, "match_end", -1, new
            {
                winnerIndex = 0,
                isDraw = false,
                reason = "normal",
                turnCount = 1,
            }));
        }

        File.WriteAllText(
            path,
            string.Join('\n', events.Select(item => item.ToJsonString())) + "\n");
    }

    private static JsonObject Checkpoint(string matchId, long seq, string position)
        => Event(matchId, seq, "replay_checkpoint", -1, new
        {
            schema = "grandumi.replay_checkpoint.v1",
            position,
            actionOrderSeq = (long?)null,
            actionStableHash = (string?)null,
            stateDigest = Sha('a'),
            publicStateDigest = Sha('b'),
            randomTraceDigest = Sha('c'),
            randomEventCount = 0,
        });

    private static JsonObject Event(
        string matchId,
        long seq,
        string kind,
        int actor,
        object payload)
        => new()
        {
            ["schema"] = MatchLogEventAdapter.SupportedSchema,
            ["matchId"] = matchId,
            ["seq"] = seq,
            ["kind"] = kind,
            ["actor"] = actor,
            ["payload"] = JsonSerializer.SerializeToNode(payload),
        };

    private static string Sha(char value) => "sha256:" + new string(value, 64);

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

    private enum LogVariant
    {
        Legacy,
        MissingIdentity,
        MissingCheckpoint,
        IdentityMismatch,
        ArtifactNotArchived,
        PreparationReady,
    }
}
