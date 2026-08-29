using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ReplayArtifactArchiveTests
{
    [Fact]
    public void 完整归档_逐层自校验并绑定运行身份()
    {
        using var fixture = new ReplayArtifactTestWorkspace();

        var result = fixture.Capture();
        var verified = ReplayArtifactArchive.Verify(result.ManifestPath);

        Assert.Equal(ReplayArtifactCaptureDisposition.Created, result.Disposition);
        Assert.Equal(fixture.Identity, verified.Manifest.RuntimeIdentity);
        Assert.Equal(fixture.Identity.BinarySha256, verified.Manifest.Content.EntryAssemblySha256);
        Assert.Equal(fixture.Identity.CardDbContentHash, verified.Manifest.Content.CardDatabaseContentHash);
        Assert.Equal(fixture.Identity.RulesetManifestHash, verified.Manifest.Content.RulesetManifestHash);
        Assert.False(verified.Manifest.ReplayWorkerEntrypoint.Available);
        Assert.False(verified.Manifest.CandidateStatus.ProductionRegistryEligible);
        Assert.Contains(
            verified.Manifest.Content.Files,
            file => file.Path == "payload/publish/GrandUMIServer.dll");
        Assert.Contains(
            verified.Manifest.Content.Files,
            file => file.Path == "payload/publish/卡牌数据/cards.json");

        ReplayArtifactArchive.VerifyCurrentRuntimeBinding(
            result.ManifestPath,
            fixture.Identity,
            fixture.PublishRoot,
            fixture.RulesRoot);
    }

    [Fact]
    public void Manifest篡改_自哈希立即失败()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var result = fixture.Capture();
        var root = JsonNode.Parse(File.ReadAllText(result.ManifestPath))!.AsObject();
        root["archiveVersion"] = "tampered";
        var canonical = CanonicalJson.Encode(JsonSerializer.SerializeToElement(root));
        File.WriteAllBytes(result.ManifestPath, [.. canonical, (byte)'\n']);

        var error = Assert.Throws<ReplayArtifactArchiveException>(
            () => ReplayArtifactArchive.Verify(result.ManifestPath));

        Assert.Contains("自校验", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest只改空白_也因非唯一规范字节失败()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var result = fixture.Capture();
        var original = File.ReadAllText(result.ManifestPath);
        File.WriteAllText(result.ManifestPath, " \n" + original);

        var error = Assert.Throws<ReplayArtifactArchiveException>(
            () => ReplayArtifactArchive.Verify(result.ManifestPath));

        Assert.Contains("唯一规范", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest路径穿越_即使重算自哈希仍失败()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var result = fixture.Capture();
        fixture.RewriteManifest(root =>
        {
            var files = root["content"]!["files"]!.AsArray();
            files[0]!["path"] = "../escape";
        });

        var error = Assert.Throws<ReplayArtifactArchiveException>(
            () => ReplayArtifactArchive.Verify(result.ManifestPath));

        Assert.Contains("路径穿越", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 文件篡改_逐文件哈希失败()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var result = fixture.Capture();
        File.AppendAllText(
            Path.Combine(result.ArchiveDirectory, "payload", "publish", "server.deps.json"),
            "tampered");

        var error = Assert.Throws<ReplayArtifactArchiveException>(
            () => ReplayArtifactArchive.Verify(result.ArchiveDirectory));

        Assert.Contains("文件校验失败", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 缺文件或多文件_文件集合失败(bool removeFile)
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var result = fixture.Capture();
        var publish = Path.Combine(result.ArchiveDirectory, "payload", "publish");
        if (removeFile)
            File.Delete(Path.Combine(publish, "server.deps.json"));
        else
            File.WriteAllText(Path.Combine(publish, "unexpected.txt"), "extra");

        var error = Assert.Throws<ReplayArtifactArchiveException>(
            () => ReplayArtifactArchive.Verify(result.ArchiveDirectory));

        Assert.Contains("文件集合", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 符号链接源_拒绝跟随到归档根外()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var outside = Path.Combine(fixture.Root, "outside.txt");
        File.WriteAllText(outside, "secret");
        var link = Path.Combine(fixture.PublishRoot, "escape.link");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            // 受限 Windows token 不能创建新符号链接；仓库卡图 junction 是现成真实重解析点。
            var junction = RepoPath("opcgpro-web", "public", "cards");
            var junctionError = Assert.Throws<ReplayArtifactArchiveException>(() =>
                ReplayArtifactArchive.Capture(
                    new ReplayArtifactCaptureOptions(
                        junction,
                        fixture.RulesRoot,
                        fixture.ArchiveRoot,
                        fixture.Identity.EngineCommit),
                    _ => fixture.Identity));
            Assert.Contains("符号链接", junctionError.Message, StringComparison.Ordinal);
            return;
        }

        var error = Assert.Throws<ReplayArtifactArchiveException>(() => fixture.Capture());

        Assert.Contains("符号链接", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(fixture.ArchiveRoot, "grandumi-runtime-*"));
    }

    [Fact]
    public void 重复归档字节完全相同_幂等复用同一目录()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var first = fixture.Capture();
        var second = fixture.Capture();

        Assert.Equal(ReplayArtifactCaptureDisposition.Created, first.Disposition);
        Assert.Equal(ReplayArtifactCaptureDisposition.Idempotent, second.Disposition);
        Assert.Equal(first.ManifestPath, second.ManifestPath);
        Assert.Equal(first.Manifest.ManifestHash, second.Manifest.ManifestHash);
    }

    [Fact]
    public void 同ArtifactId内容冲突_拒绝覆盖既有有效归档()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var first = fixture.Capture();
        var originalManifest = File.ReadAllBytes(first.ManifestPath);
        File.WriteAllText(Path.Combine(fixture.PublishRoot, "new-unbound-file.txt"), "conflict");

        var error = Assert.Throws<ReplayArtifactArchiveException>(() => fixture.Capture());

        Assert.Contains("字节不一致", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalManifest, File.ReadAllBytes(first.ManifestPath));
        ReplayArtifactArchive.Verify(first.ManifestPath);
    }

    [Fact]
    public async Task 并发归档同一Artifact_仅一个创建其余全部幂等()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        using var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                gate.Wait();
                return fixture.Capture();
            }))
            .ToArray();
        gate.Set();

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, result => result.Disposition == ReplayArtifactCaptureDisposition.Created);
        Assert.Equal(7, results.Count(result => result.Disposition == ReplayArtifactCaptureDisposition.Idempotent));
        Assert.Single(results.Select(result => result.ManifestPath).Distinct(StringComparer.Ordinal));
        ReplayArtifactArchive.Verify(results[0].ManifestPath);
    }

    [Fact]
    public void Staging中断残留_不会被Catalog取回也不阻塞新归档()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var abandoned = Path.Combine(fixture.ArchiveRoot, ".staging", "abandoned", "payload");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "partial.bin"), "partial");

        var result = fixture.Capture();
        var catalog = ReplayArtifactArchiveCatalog.Load(fixture.ArchiveRoot);

        Assert.True(Directory.Exists(abandoned));
        Assert.Single(catalog.Archives);
        Assert.Equal(result.ArtifactId, catalog.Archives[0].Manifest.ArtifactId);
    }

    [Fact]
    public void 内容清单算法_枚举乱序仍匹配冻结Golden哈希()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var hash = ReplayContentManifest.HashFiles(
            fixture.HashGoldenRoot,
            new[]
            {
                Path.Combine(fixture.HashGoldenRoot, "z.bin"),
                Path.Combine(fixture.HashGoldenRoot, "a.txt"),
            });
        var reverse = ReplayContentManifest.HashFiles(
            fixture.HashGoldenRoot,
            new[]
            {
                Path.Combine(fixture.HashGoldenRoot, "a.txt"),
                Path.Combine(fixture.HashGoldenRoot, "z.bin"),
            });

        Assert.Equal("sha256:6434c4de523b83558627d7bf89bc3d8191db7b514b9d46f452e73f44548422ba", hash);
        Assert.Equal(hash, reverse);
    }

    [Fact]
    public void 当前Publish或规则包变化_运行时绑定FailClosed()
    {
        using var fixture = new ReplayArtifactTestWorkspace();
        var result = fixture.Capture();
        File.WriteAllText(Path.Combine(fixture.RulesRoot, "late-change.txt"), "changed");

        var error = Assert.Throws<ReplayArtifactArchiveException>(() =>
            ReplayArtifactArchive.VerifyCurrentRuntimeBinding(
                result.ManifestPath,
                fixture.Identity,
                fixture.PublishRoot,
                fixture.RulesRoot));

        Assert.Contains("规则包内容", error.Message, StringComparison.Ordinal);
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
}

internal sealed class ReplayArtifactTestWorkspace : IDisposable
{
    private const string Commit = "3333333333333333333333333333333333333333";

    public ReplayArtifactTestWorkspace()
    {
        TestScene.New();
        Root = Path.Combine(
            Path.GetTempPath(),
            "GrandUMI-ReplayArtifactTests",
            Guid.NewGuid().ToString("N"));
        PublishRoot = Path.Combine(Root, "publish-source");
        RulesRoot = Path.Combine(Root, "rules-source");
        ArchiveRoot = Path.Combine(Root, "archives");
        HashGoldenRoot = Path.Combine(Root, "hash-golden");
        Directory.CreateDirectory(Path.Combine(PublishRoot, "卡牌数据"));
        Directory.CreateDirectory(Path.Combine(PublishRoot, "Effects", "Definitions"));
        Directory.CreateDirectory(RulesRoot);
        Directory.CreateDirectory(HashGoldenRoot);
        File.WriteAllBytes(
            Path.Combine(PublishRoot, "GrandUMIServer.dll"),
            Encoding.UTF8.GetBytes("fixture-server-binary-v1"));
        File.WriteAllText(Path.Combine(PublishRoot, "server.deps.json"), "{\"fixture\":true}");
        File.WriteAllText(Path.Combine(PublishRoot, "卡牌数据", "cards.json"), "[]");
        File.WriteAllText(Path.Combine(PublishRoot, "卡牌数据", "_metadata.json"), "{\"ignored\":true}");
        File.WriteAllText(Path.Combine(PublishRoot, "Effects", "Definitions", "base.json"), "{}");
        File.WriteAllText(Path.Combine(RulesRoot, "active-ruleset"), "builtin-fixture\n");
        File.WriteAllText(Path.Combine(HashGoldenRoot, "a.txt"), "A\n", new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(HashGoldenRoot, "z.bin"), new byte[] { 0, 1, 2, 255 });

        var binaryHash = ReplayContentManifest.HashFile(Path.Combine(PublishRoot, "GrandUMIServer.dll"));
        var cardFiles = new[] { Path.Combine(PublishRoot, "卡牌数据", "cards.json") };
        var cardHash = ReplayContentManifest.HashFiles(Path.Combine(PublishRoot, "卡牌数据"), cardFiles);
        Identity = ReplayRuntimeIdentityFactory.Create(
            new ReplayRuntimeBuildIdentity(Commit, binaryHash, cardHash),
            GrandUMI.Effects.Rules.CardRulesetManager.Current,
            new Version(10, 0, 7));
    }

    public string Root { get; }
    public string PublishRoot { get; }
    public string RulesRoot { get; }
    public string ArchiveRoot { get; }
    public string HashGoldenRoot { get; }
    public ReplayRuntimeIdentity Identity { get; }
    public ReplayArtifactCaptureResult? Captured { get; private set; }

    public ReplayArtifactCaptureResult Capture()
    {
        Captured = ReplayArtifactArchive.Capture(
            new ReplayArtifactCaptureOptions(PublishRoot, RulesRoot, ArchiveRoot, Commit),
            _ => Identity);
        return Captured;
    }

    public void RewriteManifest(Action<JsonObject> mutate)
    {
        var path = Captured?.ManifestPath
            ?? throw new InvalidOperationException("必须先创建归档。");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        mutate(root);
        root["manifestHash"] = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(root),
            "manifestHash");
        var canonical = CanonicalJson.Encode(JsonSerializer.SerializeToElement(root));
        File.WriteAllBytes(path, [.. canonical, (byte)'\n']);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
