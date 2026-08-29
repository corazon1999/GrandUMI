using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ArtifactReplayProcessWorkerTests
{
    private const string Deck =
        "OP16-080\nOP16-103\nOP16-103\nOP16-103\nOP16-103\nOP16-109\nOP16-109\nOP16-109\nOP16-109\n" +
        "OP16-110\nOP16-110\nOP16-110\nOP16-110\nOP16-115\nOP16-115\nOP09-096\nOP09-096\nOP09-096\nOP09-096\n" +
        "OP09-099\nOP09-099\nOP09-099\nOP09-099\nOP16-104\nOP16-104\nOP16-104\nOP16-104\nOP09-086\nOP09-086\n" +
        "OP09-086\nOP09-086\nEB04-058\nEB04-058\nEB04-058\nEB04-058\nOP16-108\nOP16-108\nOP16-108\nOP16-108\n" +
        "OP16-119\nOP16-119\nOP16-119\nOP16-119\nOP16-116\nOP16-116\nOP14-112\nOP14-112\nOP14-112\n" +
        "OP09-093\nOP09-093\nOP09-093";

    [Fact]
    public async Task Release归档历史Dll_独立进程真实重放并隔离Checkpoint分歧()
    {
        if (OperatingSystem.IsWindows())
            Assert.StartsWith(
                Path.GetFullPath(@"E:\"),
                Path.GetFullPath(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase);
        var root = Path.Combine(
            Path.GetTempPath(),
            "GrandUMI-ArtifactProcessE2E",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repo = RepoPath();
            var commit = (await RunAsync("git", ["rev-parse", "HEAD"], repo)).Trim();
            Assert.Matches("^[0-9a-f]{40}$", commit);
            var publish = Path.Combine(root, "publish");
            var rules = Path.Combine(root, "rules");
            var archives = Path.Combine(root, "archives");
            Directory.CreateDirectory(rules);
            await RunAsync(
                "dotnet",
                [
                    "publish",
                    Path.Combine(repo, "服务端WebSocket", "GrandUMIServer.csproj"),
                    "-c", "Release",
                    "-o", publish,
                    "--nologo",
                    "--no-restore",
                    $"-p:InformationalVersion=1.0.0+{commit}",
                    "-p:IncludeSourceRevisionInInformationalVersion=false",
                ],
                repo,
                timeout: TimeSpan.FromMinutes(3));
            var publishedDll = Path.Combine(publish, "GrandUMIServer.dll");
            Assert.True(File.Exists(publishedDll));
            await RunAsync(
                "dotnet",
                [
                    publishedDll,
                    "--replay-artifact", "capture",
                    "--publish-root", publish,
                    "--rules-root", rules,
                    "--archive-root", archives,
                    "--engine-commit", commit,
                ],
                publish,
                timeout: TimeSpan.FromMinutes(2));

            var catalog = ReplayArtifactArchiveCatalog.Load(archives);
            var archive = Assert.Single(catalog.Archives);
            Assert.True(archive.Manifest.ReplayWorkerEntrypoint.Available);
            await RunAsync(
                "dotnet",
                [
                    publishedDll,
                    "--replay-artifact", "verify",
                    "--archive", archive.ManifestPath,
                    "--dotnet", "dotnet",
                ],
                publish,
                timeout: TimeSpan.FromMinutes(2));

            _ = TestScene.New();
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            var goodLog = Path.Combine(logs, "01-good.jsonl");
            await WriteVerifiedLogAsync(goodLog, archive.Manifest.RuntimeIdentity);
            var badLog = Path.Combine(logs, "02-diverged.jsonl");
            TamperOpeningCheckpoint(goodLog, badLog);

            var executionOptions = ReplayCoverageExecutionOptions.Default with
            {
                MaximumConcurrency = 2,
            };
            var first = await ReplayCoverageAudit.GenerateAndExecuteAsync(
                logs,
                catalog,
                "dotnet",
                executionOptions);
            var second = await ReplayCoverageAudit.GenerateAndExecuteAsync(
                logs,
                catalog,
                "dotnet",
                executionOptions);

            Assert.Equal(1, first.Count(ReplayCoverageStatus.ReplayVerified));
            Assert.Equal(1, first.Count(ReplayCoverageStatus.ReplayDiverged));
            Assert.Equal(0, first.Count(ReplayCoverageStatus.ReplayWorkerFailed));
            Assert.All(first.WorkerArtifacts, worker =>
            {
                Assert.True(worker.EntrypointAvailable);
                Assert.True(worker.HandshakeVerified);
            });
            Assert.NotNull(first.Entries.Single(entry =>
                entry.Status == ReplayCoverageStatus.ReplayVerified).ReplayDigest);
            Assert.Null(first.Entries.Single(entry =>
                entry.Status == ReplayCoverageStatus.ReplayDiverged).ReplayDigest);
            Assert.Equal(first.ReportHash, second.ReportHash);
            Assert.Equal(
                ReplayArtifactArchive.SerializeCanonical(first),
                ReplayArtifactArchive.SerializeCanonical(second));

            var prepared = Assert.IsType<PreparedReplayMatch>(ReplayMatchPreparation.Prepare(
                await File.ReadAllBytesAsync(goodLog),
                "direct-process-request",
                catalog.PreparationRegistry).Prepared);
            var proxy = new ProcessArtifactReplayWorker(archive, "dotnet");
            var request = ArtifactReplayWorkerDispatcher.BuildRequest(
                prepared,
                ReplayArtifactIdentity.Fingerprint(prepared.Artifact),
                15_000);
            var badHash = request with { RequestHash = Sha('f') };
            var badHashResponse = await proxy.ExecuteAsync(badHash, CancellationToken.None);
            Assert.Equal(ReplayQuarantineCodes.WorkerArtifactMismatch, badHashResponse.Failure!.ReasonCode);

            var badArtifact = request.Artifact with { BinarySha256 = Sha('e') };
            var badArtifactRequest = request with
            {
                Artifact = badArtifact,
                RequestHash = string.Empty,
            };
            badArtifactRequest = badArtifactRequest with
            {
                RequestHash = ArtifactReplayWorkerDispatcher.HashRequest(badArtifactRequest),
            };
            var badArtifactResponse = await proxy.ExecuteAsync(badArtifactRequest, CancellationToken.None);
            Assert.Equal(ReplayQuarantineCodes.WorkerArtifactMismatch, badArtifactResponse.Failure!.ReasonCode);
        }
        finally
        {
            await DeleteDirectoryWithRetriesAsync(root);
        }
    }

    [Fact]
    public async Task 协议夹具_标准错误噪声不污染唯一响应帧()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var worker = CreateFixtureWorker(archive, "stderr-noise");

        var probe = await worker.ProbeAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(archive.Manifest.ArtifactId, probe.ArtifactId);
        Assert.Equal(worker.WorkerId, probe.WorkerId);
    }

    [Theory]
    [InlineData("extra-frame")]
    [InlineData("overlong")]
    [InlineData("truncated")]
    [InlineData("invalid-utf8")]
    [InlineData("invalid-json")]
    public async Task 协议夹具_畸形超限截断及重复响应均系统性拒绝(string scenario)
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var worker = CreateFixtureWorker(archive, scenario);

        var error = await Assert.ThrowsAsync<ArtifactReplayProcessException>(
            () => worker.ProbeAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, error.ReasonCode);
        Assert.True(error.SystemicProtocolFailure);
    }

    [Fact]
    public async Task 协议夹具_非零退出被隔离为Worker失败()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var worker = CreateFixtureWorker(archive, "nonzero");

        var error = await Assert.ThrowsAsync<ArtifactReplayProcessException>(
            () => worker.ProbeAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ReplayQuarantineCodes.WorkerFailure, error.ReasonCode);
        Assert.False(error.SystemicProtocolFailure);
        Assert.Contains("exit=7", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 协议夹具_标准错误超过上限即终止Worker()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var worker = CreateFixtureWorker(
            archive,
            "stderr-overflow",
            maximumStderrBytes: 128);

        var error = await Assert.ThrowsAsync<ArtifactReplayProcessException>(
            () => worker.ProbeAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, error.ReasonCode);
        Assert.True(error.SystemicProtocolFailure);
        Assert.Contains("stderr", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("上限", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 协议夹具_超时终止整棵子进程树()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var marker = Path.Combine(fixture.Root, "timeout-child.pid");
        var worker = CreateFixtureWorker(archive, "hang-child", marker);

        var error = await Assert.ThrowsAsync<ArtifactReplayProcessException>(
            () => worker.ProbeAsync(TimeSpan.FromMilliseconds(750)));

        Assert.Equal(ReplayQuarantineCodes.WorkerTimeout, error.ReasonCode);
        Assert.False(error.SystemicProtocolFailure);
        var childProcessId = await ReadProcessIdAsync(marker);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task 协议夹具_外部取消仍终止整棵子进程树且保留取消语义()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var marker = Path.Combine(fixture.Root, "cancel-child.pid");
        var worker = CreateFixtureWorker(archive, "hang-child", marker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.ProbeAsync(TimeSpan.FromSeconds(10), cancellation.Token));

        var childProcessId = await ReadProcessIdAsync(marker);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task 协议夹具_请求超限在启动进程前按单局失败拒绝()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var startCount = 0;
        var worker = CreateFixtureWorker(
            archive,
            "stderr-noise",
            maximumRequestBytes: 8,
            onStart: () => startCount++);

        var error = await Assert.ThrowsAsync<ArtifactReplayProcessException>(
            () => worker.ProbeAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ReplayQuarantineCodes.WorkerInputTooLarge, error.ReasonCode);
        Assert.False(error.SystemicProtocolFailure);
        Assert.Equal(0, startCount);
    }

    [Fact]
    public async Task 协议夹具_响应哈希篡改不会被记为ReplayVerified()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var archive = CaptureAndVerify(fixture);
        var worker = CreateFixtureWorker(archive, "tampered-result");
        var seed = ArtifactReplayWorkerTests.BuildDefaultPreparedFixture("process-tampered-response");
        var descriptor = ReplayArtifactArchive.CreateTestDescriptor(archive.Manifest);
        var prepared = new PreparedReplayMatch(
            seed.SourceId,
            seed.SourceFileHash,
            seed.Header,
            descriptor,
            seed.Tape,
            seed.CheckpointContract,
            seed.RegistryVersion,
            seed.RegistryHash,
            seed.StableHash);
        var dispatcher = new ArtifactReplayWorkerDispatcher([worker]);

        var result = await dispatcher.ExecuteAsync(prepared);

        Assert.Null(result.Verified);
        Assert.Equal(ReplayQuarantineCodes.WorkerProtocolMismatch, result.Quarantine!.ReasonCode);
    }

    private static async Task WriteVerifiedLogAsync(
        string path,
        ReplayRuntimeIdentity identity)
    {
        var current = CardRulesetManager.Current;
        var ruleset = new CardRuleset(
            identity.RulesVersion,
            current.Id,
            "独立进程 E2E fixture",
            current.CloneScriptedEffects(),
            current.CloneDslDefinitions(),
            []);
        var matchId = $"process-e2e-{Guid.NewGuid():N}";
        var engine = new GameEngine(
            matchId,
            ("e2e-s0", "e2e-p0", Deck),
            ("e2e-s1", "e2e-p1", Deck),
            firstPlayer: 0,
            rngSeed: 24681357,
            ruleset: ruleset);
        var coordinator = new ReplayCheckpointLogCoordinator(matchId);
        var opened = false;
        try
        {
            MatchLogRecorder.OpenAt(matchId, path);
            opened = true;
            engine.OnMatchLogWithReceipt = (kind, actor, payload) =>
            {
                var receipt = MatchLogRecorder.Append(matchId, engine.State, kind, actor, payload);
                coordinator.Observe(engine.State, kind, actor, payload, receipt);
                return receipt;
            };
            engine.RecordMatchLog(
                "match_start",
                -1,
                ReplayRuntimeIdentityFactory.CreateMatchStartPayload(
                    identity,
                    [
                        new ReplayMatchStartPlayer(0, "e2e-p0", Deck, false),
                        new ReplayMatchStartPlayer(1, "e2e-p1", Deck, false),
                    ],
                    firstPlayer: 0,
                    startingPlayerChooser: 0,
                    startingDiceRolls: [],
                    engine.State.RngSeed,
                    openingSetupAfterFirstPlayerChoice: false,
                    matchKind: "Friendly",
                    leaderKeywordWildcard: false));
            engine.FlushPendingMatchLogs();
            Assert.True(coordinator.WriteOpening(engine));

            await ApplySystemMulligan(engine, coordinator);
            await ApplyPlayer(engine, coordinator, 1, "Mulligan", new { redraw = false }, "e2e-mulligan-1");
            await ApplyPlayer(engine, coordinator, 0, "Surrender", new { }, "e2e-surrender-1");
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
        }
        finally
        {
            if (opened) MatchLogRecorder.Close(matchId);
        }
    }

    private static async Task ApplySystemMulligan(
        GameEngine engine,
        ReplayCheckpointLogCoordinator coordinator)
    {
        const string requestId = "e2e-system-mulligan-0";
        var data = JsonSerializer.SerializeToElement(new { redraw = false });
        engine.RecordMatchLog("mulligan_timeout_auto_keep", 0, new { requestId, redraw = false });
        var execution = engine.HandleActionWithReceipt(
            0,
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

    private static void TamperOpeningCheckpoint(string source, string destination)
    {
        var events = File.ReadLines(source)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToArray();
        var opening = events.Single(item =>
            item["kind"]!.GetValue<string>() == "replay_checkpoint"
            && item["payload"]!["position"]!.GetValue<string>() == "opening");
        opening["payload"]!["stateDigest"] = Sha('d');
        File.WriteAllText(
            destination,
            string.Join('\n', events.Select(item => item.ToJsonString())) + "\n",
            new UTF8Encoding(false));
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动测试进程：{executable}");
        using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(45));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            using var terminationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(terminationTimeout.Token);
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(
            process.ExitCode == 0,
            $"{executable} 退出 {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}");
        return stdout;
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

    private static async Task DeleteDirectoryWithRetriesAsync(string path)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 29)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(100);
            }
            catch (IOException) when (attempt < 29)
            {
                await Task.Delay(100);
            }
        }
    }

    private static string Sha(char value) => "sha256:" + new string(value, 64);

    private static VerifiedReplayArtifactArchive CaptureAndVerify(
        ReplayArtifactTestWorkspace fixture)
    {
        var captured = fixture.Capture();
        return ReplayArtifactArchive.Verify(captured.ManifestPath);
    }

    private static ProcessArtifactReplayWorker CreateFixtureWorker(
        VerifiedReplayArtifactArchive archive,
        string scenario,
        string? marker = null,
        int maximumRequestBytes = ProcessArtifactReplayWorker.DefaultMaximumRequestBytes,
        int maximumResponseBytes = ProcessArtifactReplayWorker.DefaultMaximumResponseBytes,
        int maximumStderrBytes = ProcessArtifactReplayWorker.DefaultMaximumStderrBytes,
        Action? onStart = null)
        => new(
            archive,
            TimeSpan.FromSeconds(10),
            maximumRequestBytes,
            maximumResponseBytes,
            maximumStderrBytes,
            () =>
            {
                onStart?.Invoke();
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = Path.GetDirectoryName(FixtureAssemblyPath())!,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add(FixtureAssemblyPath());
                startInfo.ArgumentList.Add(scenario);
                startInfo.ArgumentList.Add(archive.ManifestPath);
                if (marker is not null) startInfo.ArgumentList.Add(marker);
                return startInfo;
            });

    private static string FixtureAssemblyPath()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = baseDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("无法识别测试构建配置目录。");
        var path = RepoPath(
            "服务端WebSocket.ProcessWorkerFixture",
            "bin",
            configuration,
            "net10.0",
            "GrandUMI.ProcessWorkerFixture.dll");
        Assert.True(File.Exists(path), $"缺少进程协议夹具：{path}");
        return path;
    }

    private static async Task<int> ReadProcessIdAsync(string marker)
    {
        for (var attempt = 0; attempt < 50 && !File.Exists(marker); attempt++)
            await Task.Delay(20);
        Assert.True(File.Exists(marker), "子进程必须在超时或取消前写入 PID 标记。");
        return int.Parse(await File.ReadAllTextAsync(marker));
    }

    private static async Task AssertProcessExitedAsync(int processId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return;
            }
            catch (ArgumentException)
            {
                return;
            }
            await Task.Delay(20);
        }
        Assert.Fail($"子进程 {processId} 在 worker 结束后仍存活。");
    }
}
