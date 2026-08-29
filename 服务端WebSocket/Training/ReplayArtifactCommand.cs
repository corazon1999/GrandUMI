using System.Diagnostics;
using GrandUMI.Cards;
using GrandUMI.Diagnostics;
using GrandUMI.Effects.Dsl;
using GrandUMI.Effects.Rules;

namespace GrandUMI.Training;

/// <summary>测试服 service 启动时使用的 fail-closed 归档绑定。</summary>
public static class ReplayArtifactRuntimeBinding
{
    public const string RequiredEnvironmentVariable = "GRANDUMI_REQUIRE_REPLAY_ARTIFACT_ARCHIVE";
    public const string ManifestEnvironmentVariable = "GRANDUMI_REPLAY_ARTIFACT_MANIFEST";

    public static void VerifyFilesFromEnvironment(
        string currentPublishRoot,
        string currentRulesRoot)
    {
        var manifestPath = ResolveConfiguredManifest();
        if (manifestPath is null) return;
        _ = ReplayArtifactArchive.VerifyCurrentContentBinding(
            manifestPath,
            currentPublishRoot,
            currentRulesRoot);
    }

    public static void VerifyFromEnvironment(
        ReplayRuntimeIdentity currentIdentity,
        string currentPublishRoot,
        string currentRulesRoot)
    {
        var manifestPath = ResolveConfiguredManifest();
        if (manifestPath is null) return;

        ReplayArtifactArchive.VerifyCurrentRuntimeBinding(
            manifestPath,
            currentIdentity,
            currentPublishRoot,
            currentRulesRoot);
        Console.WriteLine(
            $"[重放工件归档] 已绑定并验证 {currentIdentity.EngineArtifactId}：{manifestPath}");
    }

    private static string? ResolveConfiguredManifest()
    {
        var required = string.Equals(
            Environment.GetEnvironmentVariable(RequiredEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
        var manifestPath = Environment.GetEnvironmentVariable(ManifestEnvironmentVariable)?.Trim();
        if (!string.IsNullOrWhiteSpace(manifestPath)) return manifestPath;
        if (required)
            throw new ReplayArtifactArchiveException(
                $"测试服要求不可变重放工件归档，但 {ManifestEnvironmentVariable} 未配置。");
        return null;
    }
}

/// <summary>由已发布 GrandUMIServer.dll 自身执行的归档、验证与覆盖审计命令。</summary>
public static class ReplayArtifactCommand
{
    private const int RuntimeProbeTimeoutSeconds = 90;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            WriteUsage();
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "capture" => Capture(ParseOptions(args[1..])),
                "verify" => await VerifyAsync(ParseOptions(args[1..]), cancellationToken),
                "verify-self" => VerifySelf(ParseOptions(args[1..])),
                "audit" => await AuditAsync(ParseOptions(args[1..]), cancellationToken),
                _ => throw new ArgumentException($"未知重放工件命令：{args[0]}")
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"重放工件命令参数错误：{ex.Message}");
            WriteUsage();
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("重放工件命令已取消。");
            return 1;
        }
        catch (Exception ex) when (ex is ReplayArtifactArchiveException
            or ReplayArtifactRegistryException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"重放工件命令失败：{ex.Message}");
            return 1;
        }
    }

    private static int Capture(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnlyOptions(
            options,
            "publish-root",
            "rules-root",
            "archive-root",
            "engine-commit");
        var publishRoot = Required(options, "publish-root");
        var rulesRoot = Required(options, "rules-root");
        var archiveRoot = Required(options, "archive-root");
        var engineCommit = Required(options, "engine-commit");
        if (!string.Equals(BuildInfo.Commit, engineCommit, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"执行归档的二进制提交与部署目标不一致：二进制 {BuildInfo.Commit}，目标 {engineCommit}");

        var result = ReplayArtifactArchive.Capture(
            new ReplayArtifactCaptureOptions(
                publishRoot,
                rulesRoot,
                archiveRoot,
                engineCommit),
            layout => InspectRuntime(layout, engineCommit));
        Console.WriteLine(
            $"重放工件归档{(result.Disposition == ReplayArtifactCaptureDisposition.Created ? "已创建" : "幂等复用")}：{result.ArtifactId}");
        // 部署脚本只解析最后一行；artifactId 已受严格字符集约束，路径由归档根与 artifactId 唯一推导。
        Console.WriteLine(
            $"REPLAY_ARTIFACT\t{result.ArtifactId}\t{result.ManifestPath}\t{result.Disposition.ToString().ToLowerInvariant()}");
        return 0;
    }

    private static async Task<int> VerifyAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        EnsureOnlyOptions(options, "archive", "dotnet");
        var archive = Required(options, "archive");
        var dotnet = options.TryGetValue("dotnet", out var configuredDotnet)
            ? configuredDotnet
            : "dotnet";
        var verified = ReplayArtifactArchive.Verify(archive);
        await VerifyWithArchivedRuntimeAsync(verified, dotnet, cancellationToken);
        Console.WriteLine(
            $"重放工件归档验证通过：{verified.Manifest.ArtifactId}（文件、身份与历史运行时探针一致）");
        return 0;
    }

    private static int VerifySelf(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnlyOptions(options, "archive");
        var archive = Required(options, "archive");
        var verified = ReplayArtifactArchive.Verify(archive);
        var archivedPublishRoot = Path.GetFullPath(Path.Combine(
            verified.ArchiveDirectory,
            verified.Manifest.Content.PublishRoot.Replace('/', Path.DirectorySeparatorChar)));
        var currentBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (!string.Equals(
            currentBase,
            Path.TrimEndingDirectorySeparator(archivedPublishRoot),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                "verify-self 必须由归档 payload/publish 内的历史 GrandUMIServer.dll 执行。");
        if (!string.Equals(BuildInfo.Commit, verified.Manifest.EngineCommit, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"历史二进制提交与归档不一致：二进制 {BuildInfo.Commit}，归档 {verified.Manifest.EngineCommit}");

        var rulesRoot = Path.Combine(verified.ArchiveDirectory, "payload", "rules");
        var layout = new ReplayArtifactPayloadLayout(
            archivedPublishRoot,
            rulesRoot,
            Path.Combine(archivedPublishRoot, "GrandUMIServer.dll"),
            Path.Combine(archivedPublishRoot, "卡牌数据"),
            Path.Combine(archivedPublishRoot, "Effects", "Definitions"));
        var identity = InspectRuntime(layout, verified.Manifest.EngineCommit);
        ReplayArtifactArchive.VerifyCurrentRuntimeBinding(
            verified.ManifestPath,
            identity,
            archivedPublishRoot,
            rulesRoot);
        Console.WriteLine($"历史运行时自证通过：{identity.EngineArtifactId}");
        return 0;
    }

    private static async Task<int> AuditAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        EnsureOnlyOptions(
            options,
            "logs",
            "archive-root",
            "json",
            "markdown",
            "candidate-catalog",
            "dotnet");
        var logs = Required(options, "logs");
        var archiveRoot = Required(options, "archive-root");
        var json = Required(options, "json");
        var markdown = Required(options, "markdown");
        var candidateCatalog = Required(options, "candidate-catalog");
        var dotnet = options.TryGetValue("dotnet", out var configuredDotnet)
            ? configuredDotnet
            : "dotnet";

        var archives = ReplayArtifactArchiveCatalog.Load(archiveRoot);
        foreach (var archive in archives.Archives)
            await VerifyWithArchivedRuntimeAsync(archive, dotnet, cancellationToken);
        var report = ReplayCoverageAudit.Generate(logs, archives);
        ReplayCoverageAudit.WriteOutputs(report, archives, json, markdown, candidateCatalog);
        Console.WriteLine(
            $"重放覆盖审计完成：日志 {report.TotalFiles}，准备层 {report.Count(ReplayCoverageStatus.PreparationReady)}，" +
            $"独立 worker {report.Count(ReplayCoverageStatus.ReplayWorkerReady)}");
        return 0;
    }

    private static ReplayRuntimeIdentity InspectRuntime(
        ReplayArtifactPayloadLayout layout,
        string engineCommit)
    {
        CardDatabase.LoadFrom(layout.CardDatabaseRoot);
        DslInterpreter.LoadDirectory(
            layout.DslDefinitionsRoot,
            $"builtin-{engineCommit}");
        CardRulesetManager.InitializePackages(layout.RulesRoot);
        var build = new ReplayRuntimeBuildIdentity(
            engineCommit,
            ReplayContentManifest.HashFile(layout.EntryAssemblyPath),
            CardDatabase.ContentHash);
        return ReplayRuntimeIdentityFactory.Create(build, CardRulesetManager.Current);
    }

    private static async Task VerifyWithArchivedRuntimeAsync(
        VerifiedReplayArtifactArchive archive,
        string dotnetExecutable,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dotnetExecutable))
            throw new ArgumentException("--dotnet 不能为空。", nameof(dotnetExecutable));
        var publishRoot = Path.Combine(archive.ArchiveDirectory, "payload", "publish");
        var assemblyPath = Path.Combine(publishRoot, "GrandUMIServer.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetExecutable,
            WorkingDirectory = publishRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--replay-artifact");
        startInfo.ArgumentList.Add("verify-self");
        startInfo.ArgumentList.Add("--archive");
        startInfo.ArgumentList.Add(archive.ManifestPath);

        using var process = Process.Start(startInfo)
            ?? throw new ReplayArtifactArchiveException("无法启动归档历史运行时探针。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(RuntimeProbeTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new ReplayArtifactArchiveException(
                $"归档历史运行时探针超过 {RuntimeProbeTimeoutSeconds} 秒，已终止。");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            var detail = string.Join(
                " | ",
                new[] { stderr.Trim(), stdout.Trim() }.Where(value => value.Length > 0));
            if (detail.Length > 1200) detail = detail[..1200];
            throw new ReplayArtifactArchiveException(
                $"归档历史运行时探针失败（exit={process.ExitCode}）：{detail}");
        }
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        if (args.Length % 2 != 0)
            throw new ArgumentException("选项必须使用 --名称 <值> 成对提供。");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length <= 2)
                throw new ArgumentException($"选项名无效：{name}");
            var key = name[2..];
            if (!options.TryAdd(key, args[index + 1]))
                throw new ArgumentException($"选项重复：{name}");
        }
        return options;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"缺少必填选项 --{name}。");

    private static void EnsureOnlyOptions(
        IReadOnlyDictionary<string, string> options,
        params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = options.Keys.Where(key => !allowedSet.Contains(key)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"存在未知选项：{string.Join(", ", unknown.Select(key => "--" + key))}");
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "用法：GrandUMIServer --replay-artifact capture --publish-root <目录> --rules-root <目录> --archive-root <目录> --engine-commit <40位提交>\n" +
            "      GrandUMIServer --replay-artifact verify --archive <归档目录或manifest> [--dotnet <dotnet路径>]\n" +
            "      GrandUMIServer --replay-artifact audit --logs <日志目录> --archive-root <目录> --json <文件> --markdown <文件> --candidate-catalog <文件> [--dotnet <dotnet路径>]");
    }
}
