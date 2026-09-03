using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Dsl;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public sealed class CardContentPipelineTests : IDisposable
{
    private readonly string _tempRoot;

    public CardContentPipelineTests()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(configuredRoot))
            throw new InvalidOperationException(
                "卡牌内容流水线测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _tempRoot = Path.Combine(Path.GetFullPath(configuredRoot), "card-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void RepositoryManifest_ValidatesAllCanonicalCardSets()
    {
        var manifest = CardContentManifest.Validate(RepoPath("卡牌数据"));

        Assert.Equal(62, manifest.Files.Count);
        Assert.Equal(2840, manifest.TotalCards);
        Assert.Matches("^[0-9a-f]{64}$", manifest.ContentSha256);
    }

    [Fact]
    public void Manifest_TamperedOrUnexpectedSet_FailsClosed()
    {
        var tampered = CreateMinimalContentRoot("tampered");
        File.AppendAllText(Path.Combine(tampered, "TS01.json"), " ", Encoding.UTF8);
        Assert.Throws<InvalidDataException>(() => CardContentManifest.Validate(tampered));

        var unexpected = CreateMinimalContentRoot("unexpected");
        File.WriteAllText(Path.Combine(unexpected, "TS02.json"), "[]", new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() => CardContentManifest.Validate(unexpected));
    }

    [Fact]
    public void DslMalformedFile_FailClosedModeRejectsPartialDirectory()
    {
        var directory = Path.Combine(_tempRoot, "dsl-invalid");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "valid.json"), "{\"TS01-001\":{\"triggers\":[]}}", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{", new UTF8Encoding(false));

        Assert.Throws<InvalidOperationException>(
            () => DslInterpreter.ReadDefinitionsDirectory(directory, failClosed: true));
    }

    [Fact]
    public async Task OP07_032_OnEnter_RestsOnlyEligibleCharacter()
    {
        var state = TestScene.New("OP03-022").Build();
        var source = Card("OP07-032");
        var eligible = Card("OP15-007");
        var tooExpensive = Card("OP15-025");
        state.Players[0].Characters.Add(source);
        state.Players[1].Characters.Add(eligible);
        state.Players[1].Characters.Add(tooExpensive);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(eligible.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.True(eligible.IsTapped);
        Assert.False(tooExpensive.IsTapped);
        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(eligible.Id.ToString(), choice.choices);
        Assert.DoesNotContain(tooExpensive.Id.ToString(), choice.choices);
    }

    [Fact]
    public async Task OP07_032_OnEnter_RequiresLeaderTrait()
    {
        var state = TestScene.New("OP01-001").Build();
        var source = Card("OP07-032");
        var target = Card("OP15-007");
        state.Players[0].Characters.Add(source);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.False(target.IsTapped);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number) ?? throw new InvalidOperationException($"测试卡不存在：{number}") };

    private string CreateMinimalContentRoot(string name)
    {
        var root = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(root);
        var utf8 = new UTF8Encoding(false);
        const string schemaName = "_schema.v1.json";
        const string setName = "TS01.json";
        File.WriteAllText(Path.Combine(root, schemaName), "{}", utf8);
        File.WriteAllText(Path.Combine(root, setName), "[]", utf8);
        var schemaHash = HashFile(Path.Combine(root, schemaName));
        var setHash = HashFile(Path.Combine(root, setName));
        var contentHash = HashBytes(Encoding.UTF8.GetBytes($"{setName}\0{setHash}\0{0}\n"));
        var manifest = new
        {
            schemaVersion = CardContentManifest.SchemaVersion,
            schema = new { path = schemaName, sha256 = schemaHash },
            totalCards = 0,
            contentSha256 = contentHash,
            files = new[] { new { path = setName, sha256 = setHash, cardCount = 0 } },
        };
        File.WriteAllText(
            Path.Combine(root, CardContentManifest.ManifestFileName),
            JsonSerializer.Serialize(manifest),
            utf8);
        Assert.Equal(0, CardContentManifest.Validate(root).TotalCards);
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string RepoPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "卡牌数据")))
                return Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("未找到仓库根目录。");
    }
}
