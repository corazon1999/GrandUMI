using System.Text.Json;
using GrandUMI.Effects.Rules;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public class ReplayRuntimeIdentityTests
{
    private static readonly ReplayRuntimeBuildIdentity Build = new(
        "2222222222222222222222222222222222222222",
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
        "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");

    [Fact]
    public void 相同发布输入_生成逐字段相同且不含玩家隐私的不可变身份()
    {
        TestScene.New();
        var ruleset = CardRulesetManager.Current;

        var first = ReplayRuntimeIdentityFactory.Create(Build, ruleset, new Version(10, 0, 7));
        var second = ReplayRuntimeIdentityFactory.Create(Build, ruleset, new Version(10, 0, 7));

        Assert.Equal(first, second);
        Assert.StartsWith("grandumi-runtime-", first.EngineArtifactId, StringComparison.Ordinal);
        Assert.Equal("grandumi-runtime-".Length + 64, first.EngineArtifactId.Length);
        Assert.Equal(Build.BinarySha256, first.BinarySha256);
        Assert.Equal(Build.CardDbContentHash, first.CardDbContentHash);
        Assert.Equal(ruleset.ManifestHash, first.RulesetManifestHash);
        Assert.Equal(MatchLogEventAdapter.CurrentAdapterVersion, first.EventAdapterVersion);
        Assert.Equal("dotnet-system-random-seeded-10.0.7.v1", first.RngAlgorithmVersion);

        var json = JsonSerializer.Serialize(first);
        Assert.DoesNotContain("account", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deckRaw", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 提交或哈希不精确_身份生成FailClosed()
    {
        TestScene.New();
        var ruleset = CardRulesetManager.Current;

        var commit = Assert.Throws<InvalidOperationException>(() =>
            ReplayRuntimeIdentityFactory.Create(Build with { EngineCommit = "short" }, ruleset));
        var binary = Assert.Throws<InvalidOperationException>(() =>
            ReplayRuntimeIdentityFactory.Create(Build with { BinarySha256 = "unknown" }, ruleset));

        Assert.Contains("engineCommit", commit.Message, StringComparison.Ordinal);
        Assert.Contains("BinarySha256", binary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchStart工厂_一次性补齐精确身份与完整重放配置()
    {
        TestScene.New();
        var identity = ReplayRuntimeIdentityFactory.Create(Build, CardRulesetManager.Current, new Version(10, 0, 7));
        var payload = ReplayRuntimeIdentityFactory.CreateMatchStartPayload(
            identity,
            [
                new ReplayMatchStartPlayer(0, "fixture-a", "L0\nC0", false),
                new ReplayMatchStartPlayer(1, "fixture-b", "L1\nC1", true),
            ],
            firstPlayer: -1,
            startingPlayerChooser: 0,
            startingDiceRolls: [new { player0 = 6, player1 = 2 }],
            rngSeed: 123,
            openingSetupAfterFirstPlayerChoice: true,
            matchKind: "Friendly",
            leaderKeywordWildcard: false);
        var element = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Equal(identity.EngineArtifactId, element.GetProperty("engineArtifactId").GetString());
        Assert.Equal(identity.EngineCommit, element.GetProperty("engineCommit").GetString());
        Assert.Equal(identity.BinarySha256, element.GetProperty("binarySha256").GetString());
        Assert.Equal(identity.RulesetManifestHash, element.GetProperty("rulesetManifestHash").GetString());
        Assert.Equal(identity.CardDbContentHash, element.GetProperty("cardDbContentHash").GetString());
        Assert.Equal(identity.EventAdapterVersion, element.GetProperty("eventAdapterVersion").GetString());
        Assert.Equal(identity.ManifestHash, element.GetProperty("replayRuntimeManifestHash").GetString());
        Assert.True(element.GetProperty("players")[1].GetProperty("alwaysPromptOnLifeReveal").GetBoolean());
        Assert.False(element.GetProperty("replayConfig").GetProperty("leaderKeywordWildcard").GetBoolean());
        Assert.True(element.GetProperty("openingSetupAfterFirstPlayerChoice").GetBoolean());
    }

    [Fact]
    public void 进程身份按规则Manifest缓存_逐局不重复构建或遍历磁盘()
    {
        TestScene.New();
        var ruleset = CardRulesetManager.Current;

        var first = ReplayRuntimeIdentityProvider.For(ruleset);
        var second = ReplayRuntimeIdentityProvider.For(ruleset);

        Assert.Same(first, second);
        Assert.Matches("^sha256:[0-9a-f]{64}$", ruleset.ManifestHash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", GrandUMI.Cards.CardDatabase.ContentHash);
    }

    [Fact]
    public void 卡表内容清单_文件枚举乱序仍得到启动缓存的同一哈希()
    {
        TestScene.New();
        var root = RepoPath("卡牌数据");
        var files = Directory.GetFiles(root, "*.json")
            .Where(file => !Path.GetFileNameWithoutExtension(file).StartsWith("_", StringComparison.Ordinal))
            .ToArray();

        var forward = ReplayContentManifest.HashFiles(root, files);
        var reversed = ReplayContentManifest.HashFiles(root, files.Reverse());

        Assert.Equal(forward, reversed);
        Assert.Equal(GrandUMI.Cards.CardDatabase.ContentHash, forward);
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
