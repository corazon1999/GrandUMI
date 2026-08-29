using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GrandUMI.Training;

public sealed class ReplayArtifactArchiveException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record ReplayArtifactArchiveFile(
    string Path,
    long Size,
    string Sha256,
    bool Executable);

public sealed record ReplayArtifactArchiveContent(
    string PayloadContentHash,
    string PublishRoot,
    string PublishContentHash,
    string EntryAssemblyPath,
    string EntryAssemblySha256,
    string CardDatabaseRoot,
    string CardDatabaseContentHash,
    string RulesRoot,
    string RulesContentHash,
    string RulesetManifestHash,
    IReadOnlyList<string> Directories,
    IReadOnlyList<ReplayArtifactArchiveFile> Files);

public sealed record ReplayArtifactEntrypoint(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed record ReplayArtifactWorkerEntrypoint(
    bool Available,
    string Protocol,
    string? Executable,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    string Reason);

public sealed record ReplayArtifactCandidateStatus(
    string Environment,
    bool ProductionRegistryEligible,
    string Reason);

public sealed record ReplayArtifactArchiveManifest(
    string Schema,
    string ArchiveVersion,
    string ArtifactId,
    string EngineCommit,
    ReplayRuntimeIdentity RuntimeIdentity,
    ReplayArtifactArchiveContent Content,
    ReplayArtifactEntrypoint ServiceEntrypoint,
    ReplayArtifactWorkerEntrypoint ReplayWorkerEntrypoint,
    ReplayArtifactCandidateStatus CandidateStatus,
    string ManifestHash);

public sealed record ReplayArtifactCaptureOptions(
    string PublishRoot,
    string RulesRoot,
    string ArchiveRoot,
    string EngineCommit);

public sealed record ReplayArtifactPayloadLayout(
    string PublishRoot,
    string RulesRoot,
    string EntryAssemblyPath,
    string CardDatabaseRoot,
    string DslDefinitionsRoot);

public enum ReplayArtifactCaptureDisposition
{
    Created,
    Idempotent,
}

public sealed record ReplayArtifactCaptureResult(
    ReplayArtifactCaptureDisposition Disposition,
    string ArtifactId,
    string ArchiveDirectory,
    string ManifestPath,
    ReplayArtifactArchiveManifest Manifest);

public sealed record VerifiedReplayArtifactArchive(
    string ArchiveDirectory,
    string ManifestPath,
    ReplayArtifactArchiveManifest Manifest);

/// <summary>
/// 测试服重放工件的不可变归档。发布目录只会先写入同一文件系统的 staging，完整验证后
/// 再用目录 rename 发布；已存在的 artifactId 只接受逐字节相同的幂等结果。
/// </summary>
public static class ReplayArtifactArchive
{
    public const string Schema = "grandumi.replay_artifact_archive.v1";
    public const string LegacyArchiveVersion = "grandumi.test-replay-artifact-archive.2026-08-29.v1";
    public const string ArchiveVersion = "grandumi.test-replay-artifact-archive.2026-08-29.v2";
    public const string ManifestFileName = "replay-artifact-manifest.v1.json";
    public const string ReplayWorkerProtocol = "grandumi.artifact-replay-worker.v1";
    public const string ReplayWorkerExecutable = "/opt/dotnet/dotnet";

    private static readonly string[] ReplayWorkerArguments =
    [
        "GrandUMIServer.dll",
        "--replay-artifact",
        "worker-host",
    ];

    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const int MaximumEntries = 200_000;
    private static readonly Regex CommitPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ArtifactIdPattern = new(
        "^grandumi-runtime-[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ReplayArtifactCaptureResult Capture(
        ReplayArtifactCaptureOptions options,
        Func<ReplayArtifactPayloadLayout, ReplayRuntimeIdentity> inspectRuntime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inspectRuntime);
        if (!CommitPattern.IsMatch(options.EngineCommit))
            throw new ReplayArtifactArchiveException("归档 engineCommit 必须是完整的 40 位小写提交号。");

        var publishSource = RequireExistingSafeDirectory(options.PublishRoot, "publish 源目录");
        var rulesSource = RequireExistingSafeDirectory(options.RulesRoot, "规则包源目录");
        var archiveRoot = PrepareArchiveRoot(options.ArchiveRoot);
        EnsureDisjoint(publishSource, archiveRoot, "publish 源目录与归档根目录不得互相包含");
        EnsureDisjoint(rulesSource, archiveRoot, "规则包源目录与归档根目录不得互相包含");

        var stagingParent = Path.Combine(archiveRoot, ".staging");
        Directory.CreateDirectory(stagingParent);
        RequireExistingSafeDirectory(stagingParent, "归档 staging 目录");
        var staging = Path.Combine(
            stagingParent,
            $"capture-{Environment.ProcessId}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);
            var payloadRoot = Path.Combine(staging, "payload");
            var publishTarget = Path.Combine(payloadRoot, "publish");
            var rulesTarget = Path.Combine(payloadRoot, "rules");
            Directory.CreateDirectory(payloadRoot);

            CopyStableTree(publishSource, publishTarget);
            CopyStableTree(rulesSource, rulesTarget);

            var layout = new ReplayArtifactPayloadLayout(
                publishTarget,
                rulesTarget,
                Path.Combine(publishTarget, "GrandUMIServer.dll"),
                Path.Combine(publishTarget, "卡牌数据"),
                Path.Combine(publishTarget, "Effects", "Definitions"));
            RequireExistingSafeFile(layout.EntryAssemblyPath, "服务端入口程序集");
            RequireExistingSafeDirectory(layout.CardDatabaseRoot, "归档卡表目录");
            RequireExistingSafeDirectory(layout.DslDefinitionsRoot, "归档 DSL 目录");

            var identity = inspectRuntime(layout)
                ?? throw new ReplayArtifactArchiveException("运行身份探针没有返回身份。");
            try
            {
                ReplayRuntimeIdentityFactory.ValidateIdentity(identity);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw new ReplayArtifactArchiveException($"运行身份无效：{ex.Message}", ex);
            }
            if (!string.Equals(identity.EngineCommit, options.EngineCommit, StringComparison.Ordinal))
                throw new ReplayArtifactArchiveException(
                    $"运行身份提交与部署目标不一致：身份 {identity.EngineCommit}，目标 {options.EngineCommit}");

            var manifest = BuildManifest(staging, layout, identity);
            WriteManifest(staging, manifest);
            VerifyCore(staging, requireArtifactDirectoryName: false);

            var finalDirectory = ResolveInsideRoot(archiveRoot, identity.EngineArtifactId);
            if (Directory.Exists(finalDirectory) || File.Exists(finalDirectory))
                return CompleteExisting(staging, finalDirectory, manifest);

            try
            {
                Directory.Move(staging, finalDirectory);
            }
            catch (IOException) when (Directory.Exists(finalDirectory) || File.Exists(finalDirectory))
            {
                return CompleteExisting(staging, finalDirectory, manifest);
            }

            var verified = VerifyCore(finalDirectory, requireArtifactDirectoryName: true);
            return new ReplayArtifactCaptureResult(
                ReplayArtifactCaptureDisposition.Created,
                identity.EngineArtifactId,
                finalDirectory,
                verified.ManifestPath,
                verified.Manifest);
        }
        catch (ReplayArtifactArchiveException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ReplayArtifactArchiveException($"不可变重放工件归档失败：{ex.Message}", ex);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static VerifiedReplayArtifactArchive Verify(string archiveOrManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveOrManifestPath);
        var fullPath = Path.GetFullPath(archiveOrManifestPath);
        var archiveDirectory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath)
                ?? throw new ReplayArtifactArchiveException("无法解析归档 manifest 所在目录。");
        var manifestPath = Directory.Exists(fullPath)
            ? Path.Combine(fullPath, ManifestFileName)
            : fullPath;
        if (!string.Equals(Path.GetFileName(manifestPath), ManifestFileName, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException($"归档 manifest 文件名必须是 {ManifestFileName}。");
        RequireExistingSafeFile(manifestPath, "归档 manifest");
        return VerifyCore(archiveDirectory, requireArtifactDirectoryName: true);
    }

    public static void VerifyCurrentRuntimeBinding(
        string archiveOrManifestPath,
        ReplayRuntimeIdentity currentIdentity,
        string currentPublishRoot,
        string currentRulesRoot)
    {
        var verified = VerifyCurrentContentBinding(
            archiveOrManifestPath,
            currentPublishRoot,
            currentRulesRoot);
        if (verified.Manifest.RuntimeIdentity != currentIdentity)
            throw new ReplayArtifactArchiveException(
                $"当前进程运行身份与归档不一致：当前 {currentIdentity.EngineArtifactId}，归档 {verified.Manifest.ArtifactId}");
    }

    public static VerifiedReplayArtifactArchive VerifyCurrentContentBinding(
        string archiveOrManifestPath,
        string currentPublishRoot,
        string currentRulesRoot)
    {
        var verified = Verify(archiveOrManifestPath);
        var publishRoot = RequireExistingSafeDirectory(currentPublishRoot, "当前 publish 目录");
        var rulesRoot = RequireExistingSafeDirectory(currentRulesRoot, "当前规则包目录");
        _ = SnapshotTree(publishRoot);
        _ = SnapshotTree(rulesRoot);
        var publishHash = ReplayContentManifest.HashDirectory(publishRoot);
        var rulesHash = ReplayContentManifest.HashDirectory(rulesRoot);
        if (!string.Equals(
                publishHash,
                verified.Manifest.Content.PublishContentHash,
                StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"当前 publish 内容与归档不一致：当前 {publishHash}，归档 {verified.Manifest.Content.PublishContentHash}");
        if (!string.Equals(
                rulesHash,
                verified.Manifest.Content.RulesContentHash,
                StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"当前规则包内容与归档不一致：当前 {rulesHash}，归档 {verified.Manifest.Content.RulesContentHash}");
        return verified;
    }

    internal static string SerializeCanonical<T>(T value, string? excludedTopLevelProperty = null)
    {
        var element = JsonSerializer.SerializeToElement(value, JsonOptions);
        return Encoding.UTF8.GetString(CanonicalJson.Encode(element, excludedTopLevelProperty));
    }

    private static ReplayArtifactCaptureResult CompleteExisting(
        string staging,
        string finalDirectory,
        ReplayArtifactArchiveManifest expectedManifest)
    {
        if (File.Exists(finalDirectory) && !Directory.Exists(finalDirectory))
            throw new ReplayArtifactArchiveException(
                $"artifactId 已被非目录占用，拒绝覆盖：{expectedManifest.ArtifactId}");

        try
        {
            VerifyCore(finalDirectory, requireArtifactDirectoryName: true);
        }
        catch (Exception ex) when (ex is ReplayArtifactArchiveException or IOException or UnauthorizedAccessException)
        {
            throw new ReplayArtifactArchiveException(
                $"artifactId 已存在但归档无效，拒绝覆盖：{expectedManifest.ArtifactId}；{ex.Message}",
                ex);
        }

        if (!TreesAreByteIdentical(staging, finalDirectory))
            throw new ReplayArtifactArchiveException(
                $"artifactId 已存在但字节不一致，拒绝覆盖：{expectedManifest.ArtifactId}");

        var existing = VerifyCore(finalDirectory, requireArtifactDirectoryName: true);
        return new ReplayArtifactCaptureResult(
            ReplayArtifactCaptureDisposition.Idempotent,
            expectedManifest.ArtifactId,
            finalDirectory,
            existing.ManifestPath,
            existing.Manifest);
    }

    private static ReplayArtifactArchiveManifest BuildManifest(
        string staging,
        ReplayArtifactPayloadLayout layout,
        ReplayRuntimeIdentity identity)
    {
        var payloadRoot = Path.Combine(staging, "payload");
        var payloadSnapshot = SnapshotTree(payloadRoot);
        var files = payloadSnapshot.Files
            .Select(file => new ReplayArtifactArchiveFile(
                "payload/" + file.RelativePath,
                file.Size,
                file.Sha256,
                file.Executable))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        var directories = payloadSnapshot.Directories
            .Select(directory => "payload/" + directory)
            .Append("payload")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var entryAssemblyPath = ToArchiveRelativePath(staging, layout.EntryAssemblyPath);
        var cardDatabaseRoot = ToArchiveRelativePath(staging, layout.CardDatabaseRoot);
        var publishRoot = ToArchiveRelativePath(staging, layout.PublishRoot);
        var rulesRoot = ToArchiveRelativePath(staging, layout.RulesRoot);
        var entryAssemblyHash = ReplayContentManifest.HashFile(layout.EntryAssemblyPath);
        var cardDatabaseHash = HashCardDatabase(layout.CardDatabaseRoot);
        if (!string.Equals(entryAssemblyHash, identity.BinarySha256, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"入口程序集哈希与 ReplayRuntimeIdentity 不一致：文件 {entryAssemblyHash}，身份 {identity.BinarySha256}");
        if (!string.Equals(cardDatabaseHash, identity.CardDbContentHash, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"卡表内容哈希与 ReplayRuntimeIdentity 不一致：文件 {cardDatabaseHash}，身份 {identity.CardDbContentHash}");

        var content = new ReplayArtifactArchiveContent(
            ReplayContentManifest.HashDirectory(payloadRoot),
            publishRoot,
            ReplayContentManifest.HashDirectory(layout.PublishRoot),
            entryAssemblyPath,
            entryAssemblyHash,
            cardDatabaseRoot,
            cardDatabaseHash,
            rulesRoot,
            ReplayContentManifest.HashDirectory(layout.RulesRoot),
            identity.RulesetManifestHash,
            Array.AsReadOnly(directories),
            Array.AsReadOnly(files));
        var withoutHash = new ReplayArtifactArchiveManifest(
            Schema,
            ArchiveVersion,
            identity.EngineArtifactId,
            identity.EngineCommit,
            identity,
            content,
            new ReplayArtifactEntrypoint(
                "/opt/dotnet/dotnet",
                Array.AsReadOnly(new[] { "GrandUMIServer.dll", "8081" }),
                publishRoot),
            new ReplayArtifactWorkerEntrypoint(
                Available: true,
                ReplayWorkerProtocol,
                ReplayWorkerExecutable,
                Arguments: Array.AsReadOnly(ReplayWorkerArguments),
                WorkingDirectory: publishRoot,
                Reason: "历史 DLL 提供有界独立进程重放协议；当前仅有应用层进程隔离、环境清理、超时与输入输出上限，尚未提供 OS 级网络/文件系统沙箱。"),
            new ReplayArtifactCandidateStatus(
                "test",
                ProductionRegistryEligible: false,
                Reason: "仅限测试服逐局验证；未完成 OS 级网络/文件系统沙箱及生产资格审查，禁止写入生产 registry。"),
            ManifestHash: string.Empty);
        var hash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(withoutHash, JsonOptions),
            excludedTopLevelProperty: "manifestHash");
        return withoutHash with { ManifestHash = hash };
    }

    private static void WriteManifest(string staging, ReplayArtifactArchiveManifest manifest)
    {
        var element = JsonSerializer.SerializeToElement(manifest, JsonOptions);
        var bytes = CanonicalJson.Encode(element);
        var path = Path.Combine(staging, ManifestFileName);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static VerifiedReplayArtifactArchive VerifyCore(
        string archiveDirectory,
        bool requireArtifactDirectoryName)
    {
        var root = RequireExistingSafeDirectory(archiveDirectory, "归档目录");
        var manifestPath = Path.Combine(root, ManifestFileName);
        RequireExistingSafeFile(manifestPath, "归档 manifest");
        var manifestInfo = new FileInfo(manifestPath);
        if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
            throw new ReplayArtifactArchiveException(
                $"归档 manifest 大小无效：{manifestInfo.Length} 字节");

        ReplayArtifactArchiveManifest manifest;
        try
        {
            var bytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            var rootElement = document.RootElement;
            var canonicalBytes = CanonicalJson.Encode(rootElement);
            var expectedBytes = new byte[canonicalBytes.Length + 1];
            canonicalBytes.CopyTo(expectedBytes, 0);
            expectedBytes[^1] = (byte)'\n';
            if (!bytes.AsSpan().SequenceEqual(expectedBytes))
                throw new ReplayArtifactArchiveException("归档 manifest 不是唯一规范 JSON 字节编码。");
            var computedHash = CanonicalJson.Hash(rootElement, "manifestHash");
            manifest = rootElement.Deserialize<ReplayArtifactArchiveManifest>(JsonOptions)
                ?? throw new ReplayArtifactArchiveException("归档 manifest 内容为空。");
            if (!Sha256Pattern.IsMatch(manifest.ManifestHash)
                || !string.Equals(computedHash, manifest.ManifestHash, StringComparison.Ordinal))
                throw new ReplayArtifactArchiveException(
                    $"归档 manifest 自校验失败：声明 {manifest.ManifestHash}，计算 {computedHash}");
        }
        catch (ReplayArtifactArchiveException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new ReplayArtifactArchiveException($"归档 manifest 无法解析：{ex.Message}", ex);
        }

        ValidateManifestFields(manifest, root, requireArtifactDirectoryName);
        var snapshot = SnapshotTree(root);
        var actualDirectories = snapshot.Directories.Order(StringComparer.Ordinal).ToArray();
        var expectedDirectories = manifest.Content.Directories.Order(StringComparer.Ordinal).ToArray();
        if (!actualDirectories.SequenceEqual(expectedDirectories, StringComparer.Ordinal))
        {
            var missing = expectedDirectories.Except(actualDirectories, StringComparer.Ordinal).Take(8);
            var extra = actualDirectories.Except(expectedDirectories, StringComparer.Ordinal).Take(8);
            throw new ReplayArtifactArchiveException(
                "归档目录集合与 manifest 不一致，存在缺失或额外目录：" +
                $"missing=[{string.Join(",", missing)}]，extra=[{string.Join(",", extra)}]");
        }

        var actualFiles = snapshot.Files.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
        if (!actualFiles.Remove(ManifestFileName))
            throw new ReplayArtifactArchiveException("归档缺少 manifest 文件。");
        var expectedFiles = manifest.Content.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        if (!actualFiles.Keys.Order(StringComparer.Ordinal)
            .SequenceEqual(expectedFiles.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ReplayArtifactArchiveException("归档文件集合与 manifest 不一致，存在缺失或额外文件。");

        foreach (var (path, expected) in expectedFiles)
        {
            var actual = actualFiles[path];
            if (actual.Size != expected.Size
                || actual.Executable != expected.Executable
                || !string.Equals(actual.Sha256, expected.Sha256, StringComparison.Ordinal))
                throw new ReplayArtifactArchiveException($"归档文件校验失败：{path}");
        }

        var payloadRoot = ResolveManifestPath(root, "payload", expectDirectory: true);
        var publishRoot = ResolveManifestPath(root, manifest.Content.PublishRoot, expectDirectory: true);
        var rulesRoot = ResolveManifestPath(root, manifest.Content.RulesRoot, expectDirectory: true);
        var entryAssembly = ResolveManifestPath(root, manifest.Content.EntryAssemblyPath, expectDirectory: false);
        var cardDatabaseRoot = ResolveManifestPath(root, manifest.Content.CardDatabaseRoot, expectDirectory: true);
        CompareHash("payloadContentHash", ReplayContentManifest.HashDirectory(payloadRoot), manifest.Content.PayloadContentHash);
        CompareHash("publishContentHash", ReplayContentManifest.HashDirectory(publishRoot), manifest.Content.PublishContentHash);
        CompareHash("rulesContentHash", ReplayContentManifest.HashDirectory(rulesRoot), manifest.Content.RulesContentHash);
        CompareHash("entryAssemblySha256", ReplayContentManifest.HashFile(entryAssembly), manifest.Content.EntryAssemblySha256);
        CompareHash("cardDatabaseContentHash", HashCardDatabase(cardDatabaseRoot), manifest.Content.CardDatabaseContentHash);
        CompareHash("ReplayRuntimeIdentity.binarySha256", manifest.Content.EntryAssemblySha256, manifest.RuntimeIdentity.BinarySha256);
        CompareHash("ReplayRuntimeIdentity.cardDbContentHash", manifest.Content.CardDatabaseContentHash, manifest.RuntimeIdentity.CardDbContentHash);
        CompareHash(
            "ReplayRuntimeIdentity.rulesetManifestHash",
            manifest.Content.RulesetManifestHash,
            manifest.RuntimeIdentity.RulesetManifestHash);

        return new VerifiedReplayArtifactArchive(root, manifestPath, manifest);
    }

    private static void ValidateManifestFields(
        ReplayArtifactArchiveManifest manifest,
        string archiveDirectory,
        bool requireArtifactDirectoryName)
    {
        if (!string.Equals(manifest.Schema, Schema, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException($"不支持的归档 schema：{manifest.Schema}");
        if (!string.Equals(manifest.ArchiveVersion, ArchiveVersion, StringComparison.Ordinal)
            && !string.Equals(manifest.ArchiveVersion, LegacyArchiveVersion, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException($"不支持的归档版本：{manifest.ArchiveVersion}");
        if (!ArtifactIdPattern.IsMatch(manifest.ArtifactId))
            throw new ReplayArtifactArchiveException($"artifactId 格式无效：{manifest.ArtifactId}");
        if (!CommitPattern.IsMatch(manifest.EngineCommit)
            || !string.Equals(manifest.EngineCommit, manifest.RuntimeIdentity.EngineCommit, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException("manifest engineCommit 与运行身份不一致。");
        try
        {
            ReplayRuntimeIdentityFactory.ValidateIdentity(manifest.RuntimeIdentity);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new ReplayArtifactArchiveException($"manifest 运行身份无效：{ex.Message}", ex);
        }
        if (!string.Equals(manifest.ArtifactId, manifest.RuntimeIdentity.EngineArtifactId, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException("manifest artifactId 与运行身份不一致。");
        if (requireArtifactDirectoryName
            && !string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(archiveDirectory)),
                manifest.ArtifactId,
                StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException("归档目录名必须与 artifactId 完全一致。");

        ValidateRelativePath(manifest.Content.PublishRoot, "publishRoot");
        ValidateRelativePath(manifest.Content.EntryAssemblyPath, "entryAssemblyPath");
        ValidateRelativePath(manifest.Content.CardDatabaseRoot, "cardDatabaseRoot");
        ValidateRelativePath(manifest.Content.RulesRoot, "rulesRoot");
        ValidateRelativePath(manifest.ServiceEntrypoint.WorkingDirectory, "serviceEntrypoint.workingDirectory");
        if (!string.Equals(manifest.Content.PublishRoot, "payload/publish", StringComparison.Ordinal)
            || !string.Equals(manifest.Content.EntryAssemblyPath, "payload/publish/GrandUMIServer.dll", StringComparison.Ordinal)
            || !string.Equals(manifest.Content.CardDatabaseRoot, "payload/publish/卡牌数据", StringComparison.Ordinal)
            || !string.Equals(manifest.Content.RulesRoot, "payload/rules", StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException("归档固定内容路径被篡改。");
        if (!string.Equals(manifest.ServiceEntrypoint.Executable, "/opt/dotnet/dotnet", StringComparison.Ordinal)
            || !string.Equals(manifest.ServiceEntrypoint.WorkingDirectory, manifest.Content.PublishRoot, StringComparison.Ordinal)
            || !manifest.ServiceEntrypoint.Arguments.SequenceEqual(new[] { "GrandUMIServer.dll", "8081" }, StringComparer.Ordinal))
            throw new ReplayArtifactArchiveException("服务启动入口与冻结约定不一致。");
        ValidateReplayWorkerEntrypoint(manifest);
        if (!string.Equals(manifest.CandidateStatus.Environment, "test", StringComparison.Ordinal)
            || manifest.CandidateStatus.ProductionRegistryEligible)
            throw new ReplayArtifactArchiveException("当前归档只能是测试服候选且不得进入生产 registry。");

        if (manifest.Content.Files.Count > MaximumEntries
            || manifest.Content.Directories.Count > MaximumEntries)
            throw new ReplayArtifactArchiveException("归档条目数量超过安全上限。");
        EnsureUniqueNormalizedPaths(
            manifest.Content.Files.Select(file => file.Path),
            "manifest 文件");
        EnsureUniqueNormalizedPaths(manifest.Content.Directories, "manifest 目录");
        foreach (var file in manifest.Content.Files)
        {
            ValidateRelativePath(file.Path, "files.path");
            if (!file.Path.StartsWith("payload/", StringComparison.Ordinal))
                throw new ReplayArtifactArchiveException($"manifest 文件必须位于 payload 下：{file.Path}");
            if (file.Size < 0 || !Sha256Pattern.IsMatch(file.Sha256))
                throw new ReplayArtifactArchiveException($"manifest 文件条目无效：{file.Path}");
        }
        foreach (var directory in manifest.Content.Directories)
        {
            ValidateRelativePath(directory, "directories");
            if (!string.Equals(directory, "payload", StringComparison.Ordinal)
                && !directory.StartsWith("payload/", StringComparison.Ordinal))
                throw new ReplayArtifactArchiveException($"manifest 目录必须位于 payload 下：{directory}");
        }
        foreach (var hash in new[]
        {
            manifest.Content.PayloadContentHash,
            manifest.Content.PublishContentHash,
            manifest.Content.EntryAssemblySha256,
            manifest.Content.CardDatabaseContentHash,
            manifest.Content.RulesContentHash,
            manifest.Content.RulesetManifestHash,
        })
        {
            if (!Sha256Pattern.IsMatch(hash))
                throw new ReplayArtifactArchiveException($"manifest 内容哈希格式无效：{hash}");
        }
    }

    private static void ValidateReplayWorkerEntrypoint(ReplayArtifactArchiveManifest manifest)
    {
        var entrypoint = manifest.ReplayWorkerEntrypoint
            ?? throw new ReplayArtifactArchiveException("归档缺少 replay worker 入口。");
        if (!string.Equals(entrypoint.Protocol, ReplayWorkerProtocol, StringComparison.Ordinal)
            || entrypoint.Arguments is null)
            throw new ReplayArtifactArchiveException("replay worker 协议或参数列表无效。");

        if (string.Equals(manifest.ArchiveVersion, LegacyArchiveVersion, StringComparison.Ordinal))
        {
            if (entrypoint.Available
                || entrypoint.Executable is not null
                || entrypoint.WorkingDirectory is not null
                || entrypoint.Arguments.Count != 0)
                throw new ReplayArtifactArchiveException("旧版归档不得声明独立 replay worker 可用。");
            return;
        }

        if (!entrypoint.Available
            || !string.Equals(entrypoint.Executable, ReplayWorkerExecutable, StringComparison.Ordinal)
            || !string.Equals(
                entrypoint.WorkingDirectory,
                manifest.Content.PublishRoot,
                StringComparison.Ordinal)
            || !entrypoint.Arguments.SequenceEqual(ReplayWorkerArguments, StringComparer.Ordinal))
            throw new ReplayArtifactArchiveException(
                "replay worker 入口必须使用固定 dotnet、归档 payload/publish 内历史 DLL 与固定参数。");

        ValidateRelativePath(entrypoint.WorkingDirectory!, "replayWorkerEntrypoint.workingDirectory");
        if (!string.Equals(
                entrypoint.Arguments[0],
                Path.GetFileName(manifest.Content.EntryAssemblyPath),
                StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException("replay worker 入口没有指向归档历史 DLL。");
    }

    internal static ReplayArtifactDescriptor CreateTestDescriptor(
        ReplayArtifactArchiveManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var identity = manifest.RuntimeIdentity;
        return new ReplayArtifactDescriptor(
            identity.MatchLogSchema,
            identity.EventAdapterVersion,
            identity.EngineArtifactId,
            identity.EngineCommit,
            identity.BinarySha256,
            identity.RulesVersion,
            identity.RulesetManifestHash,
            identity.CardDbContentHash,
            identity.RngAlgorithmVersion,
            identity.DeterministicIdVersion,
            identity.OpeningProtocolVersion,
            identity.ReplayConfigSchema,
            $"test-archive://{identity.EngineArtifactId}/payload/publish/GrandUMIServer.dll");
    }

    private static void CopyStableTree(string source, string destination)
    {
        var before = SnapshotTree(source);
        Directory.CreateDirectory(destination);
        foreach (var directory in before.Directories)
            Directory.CreateDirectory(ResolveManifestPath(destination, directory, expectDirectory: null));
        foreach (var file in before.Files)
        {
            var sourcePath = ResolveManifestPath(source, file.RelativePath, expectDirectory: false);
            var destinationPath = ResolveManifestPath(destination, file.RelativePath, expectDirectory: null);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            if (!OperatingSystem.IsWindows() && file.Executable)
            {
                var mode = File.GetUnixFileMode(destinationPath);
                File.SetUnixFileMode(
                    destinationPath,
                    mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
            }
        }

        var after = SnapshotTree(source);
        var copied = SnapshotTree(destination);
        if (!SnapshotsEqual(before, after) || !SnapshotsEqual(before, copied))
            throw new ReplayArtifactArchiveException("归档复制期间源目录发生变化，拒绝发布非一致快照。");
    }

    private static TreeSnapshot SnapshotTree(string root)
    {
        var safeRoot = RequireExistingSafeDirectory(root, "内容目录");
        var directories = new List<string>();
        var files = new List<TreeFile>();
        var pending = new Stack<string>();
        pending.Push(safeRoot);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || HasLinkTarget(entry))
                    throw new ReplayArtifactArchiveException($"内容目录包含符号链接或重解析点：{entry}");
                var relative = ToArchiveRelativePath(safeRoot, entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(relative);
                    pending.Push(entry);
                }
                else
                {
                    var info = new FileInfo(entry);
                    files.Add(new TreeFile(
                        relative,
                        info.Length,
                        ReplayContentManifest.HashFile(entry),
                        IsExecutable(entry)));
                }
                if (directories.Count + files.Count > MaximumEntries)
                    throw new ReplayArtifactArchiveException("内容目录条目数量超过安全上限。");
            }
        }

        EnsureUniqueNormalizedPaths(directories, "目录");
        EnsureUniqueNormalizedPaths(files.Select(file => file.RelativePath), "文件");
        return new TreeSnapshot(
            directories.Order(StringComparer.Ordinal).ToArray(),
            files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray());
    }

    private static bool TreesAreByteIdentical(string leftRoot, string rightRoot)
    {
        var left = SnapshotTree(leftRoot);
        var right = SnapshotTree(rightRoot);
        if (!SnapshotsEqual(left, right)) return false;
        foreach (var file in left.Files)
        {
            var leftPath = ResolveManifestPath(leftRoot, file.RelativePath, expectDirectory: false);
            var rightPath = ResolveManifestPath(rightRoot, file.RelativePath, expectDirectory: false);
            if (!FilesAreByteIdentical(leftPath, rightPath)) return false;
        }
        return true;
    }

    private static bool SnapshotsEqual(TreeSnapshot left, TreeSnapshot right)
        => left.Directories.SequenceEqual(right.Directories, StringComparer.Ordinal)
            && left.Files.SequenceEqual(right.Files);

    private static bool FilesAreByteIdentical(string leftPath, string rightPath)
    {
        const int bufferSize = 64 * 1024;
        using var left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        if (left.Length != right.Length) return false;
        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];
        while (true)
        {
            var leftRead = left.Read(leftBuffer);
            var rightRead = right.Read(rightBuffer);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead))) return false;
        }
    }

    private static string HashCardDatabase(string cardDatabaseRoot)
    {
        var files = Directory.GetFiles(cardDatabaseRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Where(file => !Path.GetFileNameWithoutExtension(file).StartsWith("_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new ReplayArtifactArchiveException("归档卡表目录没有可参与身份哈希的 JSON 文件。");
        return ReplayContentManifest.HashFiles(cardDatabaseRoot, files);
    }

    private static void CompareHash(string field, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException(
                $"{field} 校验失败：声明 {expected}，计算 {actual}");
    }

    private static string PrepareArchiveRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        EnsureNoLinkedExistingAncestor(fullPath, "归档根目录");
        Directory.CreateDirectory(fullPath);
        return RequireExistingSafeDirectory(fullPath, "归档根目录");
    }

    private static string RequireExistingSafeDirectory(string path, string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new ReplayArtifactArchiveException($"{context}不存在：{fullPath}");
        EnsureNoLinkedExistingAncestor(fullPath, context);
        var info = new DirectoryInfo(fullPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new ReplayArtifactArchiveException($"{context}不能是符号链接或重解析点：{fullPath}");
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static string RequireExistingSafeFile(string path, string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new ReplayArtifactArchiveException($"{context}不存在：{fullPath}");
        EnsureNoLinkedExistingAncestor(Path.GetDirectoryName(fullPath)!, context);
        var info = new FileInfo(fullPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new ReplayArtifactArchiveException($"{context}不能是符号链接或重解析点：{fullPath}");
        return fullPath;
    }

    private static void EnsureNoLinkedExistingAncestor(string path, string context)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        if (!current.Exists) current = current.Parent;
        while (current is not null)
        {
            if (current.Exists
                && ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null))
                throw new ReplayArtifactArchiveException(
                    $"{context}经过符号链接或重解析点：{current.FullName}");
            current = current.Parent;
        }
    }

    private static bool HasLinkTarget(string path)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        return info.LinkTarget is not null;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return false;
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private static void EnsureDisjoint(string left, string right, string message)
    {
        if (IsInsideOrEqual(left, right) || IsInsideOrEqual(right, left))
            throw new ReplayArtifactArchiveException(message);
    }

    private static bool IsInsideOrEqual(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var candidateFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(candidateFull, rootFull, comparison)
            || candidateFull.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison);
    }

    private static string ResolveInsideRoot(string root, string relative)
    {
        ValidateRelativePath(relative, "路径");
        return ResolveManifestPath(root, relative, expectDirectory: null);
    }

    private static string ResolveManifestPath(string root, string relative, bool? expectDirectory)
    {
        ValidateRelativePath(relative, "manifest 路径");
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(rootFull, platformRelative));
        if (!IsInsideOrEqual(resolved, rootFull) || string.Equals(resolved, rootFull, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException($"manifest 路径越过归档根目录：{relative}");
        if (expectDirectory == true) RequireExistingSafeDirectory(resolved, $"manifest 目录 {relative}");
        if (expectDirectory == false) RequireExistingSafeFile(resolved, $"manifest 文件 {relative}");
        return resolved;
    }

    private static string ToArchiveRelativePath(string root, string path)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!IsInsideOrEqual(fullPath, rootFull) || string.Equals(fullPath, rootFull, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException($"内容路径越过根目录：{path}");
        var relative = Path.GetRelativePath(rootFull, fullPath)
            .Replace('\\', '/')
            .Normalize(NormalizationForm.FormC);
        ValidateRelativePath(relative, "内容相对路径");
        return relative;
    }

    private static void ValidateRelativePath(string path, string context)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > 1024
            || path.Contains('\\')
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.EndsWith("/", StringComparison.Ordinal)
            || path.Contains('\0')
            || !string.Equals(path, path.Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException($"{context}不是规范相对路径：{path}");
        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")
            || Path.IsPathRooted(path)
            || (segments[0].Length >= 2 && segments[0][1] == ':'))
            throw new ReplayArtifactArchiveException($"{context}包含路径穿越或根路径：{path}");
    }

    private static void EnsureUniqueNormalizedPaths(IEnumerable<string> paths, string context)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        foreach (var path in paths)
        {
            ValidateRelativePath(path, context);
            if (!seen.Add(path))
                throw new ReplayArtifactArchiveException($"{context}包含重复或大小写冲突路径：{path}");
        }
    }

    private sealed record TreeFile(
        string RelativePath,
        long Size,
        string Sha256,
        bool Executable);

    private sealed record TreeSnapshot(
        IReadOnlyList<string> Directories,
        IReadOnlyList<TreeFile> Files);
}
