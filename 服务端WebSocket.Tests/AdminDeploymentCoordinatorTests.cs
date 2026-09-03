using GrandUMI;
using GrandUMI.Game.Hex;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class AdminDeploymentCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        OperatingSystem.IsWindows() ? @"E:\GrandUMI-Temp\Tests" : "/tmp/grandumi-tests",
        $"grandumi-admin-deploy-{Guid.NewGuid():N}");

    [Fact]
    public void 发布请求只写入白名单环境和随机请求文件()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        Directory.CreateDirectory(Path.Combine(_directory, "status"));
        var coordinator = new AdminDeploymentCoordinator(_directory);
        coordinator.Initialize();

        var status = coordinator.Queue("test");

        Assert.Equal("queued", status.State);
        var request = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "requests"), "test-*.request"));
        var content = File.ReadAllText(request);
        Assert.Contains("environment=test", content);
        Assert.Matches("nonce=[0-9a-f]{32}", content);
        Assert.Throws<ArgumentException>(() => coordinator.Queue("candidate"));
    }

    [Fact]
    public void 能读取执行器状态且不会执行状态中的文本()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        Directory.CreateDirectory(Path.Combine(_directory, "status"));
        File.WriteAllText(
            Path.Combine(_directory, "status", "production.status"),
            "state=failed\ntarget=0123456789012345678901234567890123456789\nmessage=5q2j5byP5pS+5bu65Lq65bel6L+Q6KGM5Lit44CC\nupdated=1787625600\n");
        var coordinator = new AdminDeploymentCoordinator(_directory);
        coordinator.Initialize();

        var status = coordinator.GetStatus("production");

        Assert.Equal("failed", status.State);
        Assert.Equal("0123456789012345678901234567890123456789", status.TargetCommit);
        Assert.NotEmpty(status.Message);
        Assert.NotNull(status.UpdatedAt);
    }

    [Fact]
    public void 海克斯配置严格校验完整目录摘要与当前常规池规模()
    {
        Assert.Equal(
            "sha256:f02ed93429f2b51e7c4e37abac4f996792f7716a66cddd09656295457242fcd8",
            HexCatalogConfiguration.BuiltIn.Digest);
        Assert.Equal(45, HexCatalogConfiguration.RequiredRegularHexes(HexTier.Silver));
        Assert.Equal(18, HexCatalogConfiguration.RequiredRegularHexes(HexTier.Gold));
        Assert.Equal(17, HexCatalogConfiguration.RequiredRegularHexes(HexTier.Rainbow));

        Assert.Throws<InvalidDataException>(() => HexCatalogConfiguration.Create(
            1,
            HexCatalogConfiguration.BuiltIn.Assignments.Skip(1)));
        Assert.Throws<InvalidDataException>(() => HexCatalogConfiguration.Create(
            1,
            HexCatalogConfiguration.BuiltIn.Assignments.Append(
                HexCatalogConfiguration.BuiltIn.Assignments[0])));
        Assert.Throws<InvalidDataException>(() => HexCatalogConfiguration.Create(
            1,
            HexCatalogConfiguration.BuiltIn.Assignments.Select(item =>
                new HexCatalogTierAssignment(item.Id, HexTier.Gold))));
        var unbalancedAssignments = ChangeTier(1, HexTier.Silver);
        Assert.Equal(17, unbalancedAssignments.Count(item =>
            !HexCatalog.IsAlternative(item.Id) && !HexCatalog.IsRetired(item.Id) && item.Tier == HexTier.Gold));
        Assert.Equal(46, unbalancedAssignments.Count(item =>
            !HexCatalog.IsAlternative(item.Id) && !HexCatalog.IsRetired(item.Id) && item.Tier == HexTier.Silver));
        var unbalanced = Assert.Throws<InvalidDataException>(() => HexCatalogConfiguration.Create(
            1,
            unbalancedAssignments));
        Assert.Contains("银色常规海克斯必须恰好为 45 个", unbalanced.Message);
        var draft = HexCatalogConfiguration.CreateDraft(unbalancedAssignments);
        Assert.Equal(HexTier.Silver, draft.TierOf(1));

        var emptySilver = Assert.Throws<InvalidDataException>(() => HexCatalogConfiguration.Create(
            1,
            HexCatalogConfiguration.BuiltIn.Assignments.Select(item =>
                item.Tier == HexTier.Silver ? item with { Tier = HexTier.Gold } : item)));
        Assert.Contains("银色常规海克斯必须恰好为 45 个", emptySilver.Message);

        var configured = HexCatalogConfiguration.Create(
            7,
            SwapTiers(1, 8),
            publishedAt: 1788278400000,
            publishedBy: "Admin",
            sourceDraftRevision: 3,
            sourceRequestId: "publish-1");
        var path = Path.Combine(_directory, "active.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(path, HexCatalogConfiguration.SerializeActive(configured));

        var restored = HexCatalogConfiguration.ReadActiveFile(path);
        Assert.Equal(7, restored.Revision);
        Assert.Equal(configured.Digest, restored.Digest);
        Assert.Equal(HexTier.Silver, restored.TierOf(1));
        Assert.Equal(HexTier.Gold, restored.TierOf(8));
        Assert.Equal("publish-1", restored.SourceRequestId);
    }

    [Fact]
    public void 旧版Active中退役项调过品质仍可读取但不能直接作为新版平衡配置创建()
    {
        var legacyAssignments = HexCatalogConfiguration
            .BuiltInForRulesRevision(HexRules.SevenHexReworkRulesRevision)
            .Assignments;
        var tier26 = legacyAssignments.Single(item => item.Id == 26).Tier;
        var tier27 = legacyAssignments.Single(item => item.Id == 27).Tier;
        legacyAssignments = legacyAssignments
            .Select(item => item.Id == 26
                ? item with { Tier = tier27 }
                : item.Id == 27
                    ? item with { Tier = tier26 }
                    : item)
            .ToArray();
        Assert.Throws<InvalidDataException>(() => HexCatalogConfiguration.Create(9, legacyAssignments));
        var legacyDigest = HexCatalogConfiguration.ComputeDigest(legacyAssignments);
        var path = Path.Combine(_directory, "active.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = HexCatalogConfiguration.Schema,
            revision = 9,
            digest = legacyDigest,
            sourceDraftRevision = 4,
            sourceRequestId = "legacy-active",
            publishedAt = 1788278400000L,
            publishedBy = "LegacyAdmin",
            tiers = legacyAssignments.Select(item => new { id = item.Id, tier = item.Tier.ToString() }),
        }));

        var restored = HexCatalogConfiguration.ReadActiveFile(path);

        Assert.Equal(82, restored.Assignments.Count);
        Assert.NotEqual(legacyDigest, restored.Digest);
        Assert.Equal(HexCatalogConfiguration.ComputeDigest(restored.Assignments), restored.Digest);
        Assert.Equal(HexTier.Gold, restored.TierOf(27));
        Assert.Equal(HexTier.Rainbow, restored.TierOf(26));
        Assert.Equal(HexCatalog.Get(57).Tier, restored.TierOf(57));
        Assert.Equal("legacy-active", restored.SourceRequestId);
    }

    [Fact]
    public void 管理员海克斯目录协议使用小写名称与效果字段()
    {
        var coordinator = CreateCoordinator();
        var state = coordinator.GetHexCatalogState("test");
        var method = typeof(WebSocketBridge).GetMethod(
            "ToAdminHexCatalogPayload",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var payload = method!.Invoke(null, [state]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var firstEntry = document.RootElement.GetProperty("entries")[0];
        var definition = HexCatalog.All[0];

        Assert.Equal(definition.Name, firstEntry.GetProperty("name").GetString());
        Assert.Equal(definition.Description, firstEntry.GetProperty("description").GetString());
        Assert.False(firstEntry.TryGetProperty("Name", out _));
        Assert.False(firstEntry.TryGetProperty("Description", out _));
        Assert.Equal(81, document.RootElement.GetProperty("entries").GetArrayLength());
        Assert.DoesNotContain(document.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("id").GetInt32() == 27);
    }

    [Fact]
    public void 海克斯草稿保存幂等且发布请求绑定精确基线与完整内容()
    {
        var coordinator = CreateCoordinator();
        var assignments = SwapTiers(1, 8);

        var first = coordinator.SaveHexCatalogDraft("test", 0, 0, assignments, "Admin", "save-1");
        var replay = coordinator.SaveHexCatalogDraft("test", 0, 0, assignments, "Admin", "save-1");

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(1, first.State.Draft.DraftRevision);
        Assert.Equal(first.State.Draft.Digest, replay.State.Draft.Digest);
        var conflict = Assert.Throws<InvalidOperationException>(() => coordinator.SaveHexCatalogDraft(
            "test", 0, 0, SwapTiers(2, 5), "Admin", "save-2"));
        Assert.Contains("其他管理员更新", conflict.Message);

        var queued = coordinator.QueueHexCatalog(
            "test",
            first.State.Draft.DraftRevision,
            first.State.Draft.Digest,
            "Admin",
            "publish-1");
        Assert.Equal("queued", queued.Deployment.State);
        Assert.Equal(
            $"test:draft-1:{first.State.Draft.Digest}",
            AdminDeploymentCoordinator.HexCatalogApprovalTarget("test", 1, first.State.Draft.Digest));

        var requestPath = Assert.Single(Directory.GetFiles(
            Path.Combine(_directory, "requests"),
            "hex-test-*.request"));
        using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath));
        var root = request.RootElement;
        Assert.Equal("grandumi.admin.hex-catalog-request.v1", root.GetProperty("schema").GetString());
        Assert.Equal("hex-catalog", root.GetProperty("kind").GetString());
        Assert.Equal(0, root.GetProperty("expectedActiveRevision").GetInt64());
        Assert.Equal(first.State.Draft.Digest, root.GetProperty("digest").GetString());
        Assert.Equal(82, root.GetProperty("tiers").GetArrayLength());
    }

    [Fact]
    public void 不平衡品质可以保存并恢复草稿但不能进入发布队列()
    {
        var coordinator = CreateCoordinator();
        var assignments = ChangeTier(1, HexTier.Silver);

        var saved = coordinator.SaveHexCatalogDraft(
            "test", 0, 0, assignments, "Admin", "save-unbalanced");
        var restored = coordinator.GetHexCatalogState("test").Draft;

        Assert.Equal(1, saved.State.Draft.DraftRevision);
        Assert.Equal(saved.State.Draft.Digest, restored.Digest);
        Assert.Equal(46, restored.Assignments.Count(item =>
            !HexCatalog.IsAlternative(item.Id) && !HexCatalog.IsRetired(item.Id) && item.Tier == HexTier.Silver));
        Assert.Equal(17, restored.Assignments.Count(item =>
            !HexCatalog.IsAlternative(item.Id) && !HexCatalog.IsRetired(item.Id) && item.Tier == HexTier.Gold));

        var error = Assert.Throws<InvalidDataException>(() => coordinator.QueueHexCatalog(
            "test",
            saved.State.Draft.DraftRevision,
            saved.State.Draft.Digest,
            "Admin",
            "publish-unbalanced"));

        Assert.Contains("必须恰好为 45 个", error.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "requests"), "*.request"));
    }

    [Fact]
    public void 新面板提交八十一项时沿用当前退役映射且旧面板完整提交仍兼容()
    {
        var coordinator = CreateCoordinator();
        var currentEntries = SwapTiers(1, 8).Where(item => item.Id != 27).ToArray();

        var saved = coordinator.SaveHexCatalogDraft(
            "test", 0, 0, currentEntries, "Admin", "save-without-retired");

        Assert.Equal(82, saved.State.Draft.Assignments.Count);
        Assert.Equal(HexCatalogConfiguration.BuiltIn.TierOf(27),
            saved.State.Draft.Assignments.Single(item => item.Id == 27).Tier);

        var oldClient = coordinator.SaveHexCatalogDraft(
            "production", 0, 0, SwapTiers(2, 5), "Admin", "save-old-client");
        Assert.Equal(82, oldClient.State.Draft.Assignments.Count);
    }

    [Fact]
    public async Task 相同草稿版本的并发保存只有一个成功()
    {
        var coordinatorA = CreateCoordinator();
        var coordinatorB = new AdminDeploymentCoordinator(
            _directory,
            Path.Combine(_directory, "environment-data"));
        coordinatorB.Initialize();

        async Task<string> Save(AdminDeploymentCoordinator coordinator, int firstId, int secondId, string requestId)
            => await Task.Run(() =>
            {
                try
                {
                    coordinator.SaveHexCatalogDraft("production", 0, 0, SwapTiers(firstId, secondId), "Admin", requestId);
                    return "success";
                }
                catch (InvalidOperationException)
                {
                    return "conflict";
                }
            });

        var outcomes = await Task.WhenAll(
            Save(coordinatorA, 1, 8, "save-a"),
            Save(coordinatorB, 2, 5, "save-b"));

        Assert.Single(outcomes, value => value == "success");
        Assert.Single(outcomes, value => value == "conflict");
        Assert.Equal(1, coordinatorA.GetHexCatalogState("production").Draft.DraftRevision);
    }

    [Fact]
    public void 已发布基线变化后拒绝旧草稿入队()
    {
        var coordinator = CreateCoordinator();
        var saved = coordinator.SaveHexCatalogDraft(
            "production", 0, 0, SwapTiers(1, 8), "Admin", "save-before-active-change");
        var active = HexCatalogConfiguration.Create(
            1,
            SwapTiers(2, 5),
            publishedBy: "OtherAdmin",
            sourceDraftRevision: 1,
            sourceRequestId: "other-publish");
        var activePath = Path.Combine(_directory, "environment-data", "production", "hex-catalog", "active.json");
        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        File.WriteAllBytes(activePath, HexCatalogConfiguration.SerializeActive(active));

        var error = Assert.Throws<InvalidOperationException>(() => coordinator.QueueHexCatalog(
            "production",
            saved.State.Draft.DraftRevision,
            saved.State.Draft.Digest,
            "Admin",
            "stale-publish"));

        Assert.Contains("已发布配置已变化", error.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "requests"), "*.request"));
    }

    private AdminDeploymentCoordinator CreateCoordinator()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        Directory.CreateDirectory(Path.Combine(_directory, "status"));
        var coordinator = new AdminDeploymentCoordinator(
            _directory,
            Path.Combine(_directory, "environment-data"));
        coordinator.Initialize();
        return coordinator;
    }

    private static HexCatalogTierAssignment[] ChangeTier(int id, HexTier tier)
        => HexCatalogConfiguration.BuiltIn.Assignments
            .Select(item => item.Id == id ? item with { Tier = tier } : item)
            .ToArray();

    private static HexCatalogTierAssignment[] SwapTiers(int firstId, int secondId)
    {
        var firstTier = HexCatalogConfiguration.BuiltIn.TierOf(firstId);
        var secondTier = HexCatalogConfiguration.BuiltIn.TierOf(secondId);
        if (firstTier == secondTier)
            throw new ArgumentException("测试交换项必须来自不同品质。");
        return HexCatalogConfiguration.BuiltIn.Assignments
            .Select(item => item.Id == firstId
                ? item with { Tier = secondTier }
                : item.Id == secondId
                    ? item with { Tier = firstTier }
                    : item)
            .ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
