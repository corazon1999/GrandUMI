using System.Text.Json;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public sealed class CardRulesetHotUpdateTests
{
    private static readonly string Deck = string.Join(
        '\n',
        new[] { "OP16-080" }.Concat(Enumerable.Repeat("OP16-103", 50)));

    [Fact]
    public async Task 不同对局可同时使用同一卡牌的不同效果实现()
    {
        var oldState = TestScene.New().MyCharacter("OP15-003").Build();
        var newState = TestScene.New().MyCharacter("OP15-003").Build();
        var oldRuleset = CreateRuleset("rules-old", new MarkerEffect(101));
        var newRuleset = CreateRuleset("rules-new", new MarkerEffect(202));
        oldState.Ruleset = oldRuleset;
        oldState.RulesetId = oldRuleset.Id;
        newState.Ruleset = newRuleset;
        newState.RulesetId = newRuleset.Id;

        await EffectRuntime.Resolve(
            oldState,
            0,
            oldState.Players[0].Characters[0],
            EffectTrigger.OnEnterField,
            new MockPromptService());
        await EffectRuntime.Resolve(
            newState,
            0,
            newState.Players[0].Characters[0],
            EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.Equal(101, oldState.TurnCount);
        Assert.Equal(202, newState.TurnCount);
        Assert.Same(oldRuleset, CardRulesetManager.For(oldState));
        Assert.Same(newRuleset, CardRulesetManager.For(newState));
    }

    [Fact]
    public async Task 新建与重放引擎都锁定显式规则版本()
    {
        _ = TestScene.New().Build();
        var oldRuleset = CreateRuleset("rules-pinned-old", new MarkerEffect(1));
        var newRuleset = CreateRuleset("rules-pinned-new", new MarkerEffect(2));

        var oldEngine = new GameEngine(
            "old-room",
            ("old-0", "alice", Deck),
            ("old-1", "bob", Deck),
            firstPlayer: 0,
            rngSeed: 123,
            ruleset: oldRuleset);
        var newEngine = new GameEngine(
            "new-room",
            ("new-0", "alice", Deck),
            ("new-1", "bob", Deck),
            firstPlayer: 0,
            rngSeed: 456,
            ruleset: newRuleset);
        var rebuilt = await MatchReplay.RebuildAsync(
            "old-room",
            123,
            0,
            ("alice", Deck),
            ("bob", Deck),
            [],
            ruleset: oldRuleset);

        Assert.Equal("rules-pinned-old", oldEngine.State.RulesetId);
        Assert.Equal("rules-pinned-new", newEngine.State.RulesetId);
        Assert.Equal("rules-pinned-old", rebuilt.State.RulesetId);
        Assert.Contains(
            "\"rulesetId\":\"rules-pinned-old\"",
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(rebuilt.State)));
    }

    [Fact]
    public void 发布绑定的旧内置规则别名_仅供恢复且清单不完整时失败关闭()
    {
        _ = TestScene.New().Build();
        var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("规则集恢复别名测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");
        var directory = Path.Combine(root, "ruleset-recovery-alias", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var alias = "builtin-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var invalidAlias = "builtin-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        try
        {
            var invalidPath = Path.Combine(directory, "invalid.json");
            File.WriteAllText(invalidPath, JsonSerializer.Serialize(new
            {
                schema = "grandumi.builtin-ruleset-recovery-aliases.v1",
                targetRulesetId = "builtin-cccccccccccccccccccccccccccccccccccccccc",
                aliases = new[] { invalidAlias },
            }));
            Assert.Throws<InvalidDataException>(
                () => CardRulesetManager.InitializeBuiltInRecoveryAliases(invalidPath));
            Assert.Throws<InvalidOperationException>(() => CardRulesetManager.GetRequired(invalidAlias));

            var validPath = Path.Combine(directory, CardRulesetManager.BuiltInRecoveryAliasesFileName);
            File.WriteAllText(validPath, JsonSerializer.Serialize(new
            {
                schema = "grandumi.builtin-ruleset-recovery-aliases.v1",
                targetRulesetId = CardRulesetManager.BuiltIn.Id,
                aliases = new[] { alias },
            }));
            CardRulesetManager.InitializeBuiltInRecoveryAliases(validPath);

            var recovered = CardRulesetManager.GetRequired(alias);
            Assert.Equal(alias, recovered.Id);
            Assert.Equal(
                CardRulesetManager.BuiltIn.TryGetScriptedEffect("OP15-003")?.GetType(),
                recovered.TryGetScriptedEffect("OP15-003")?.GetType());
            Assert.DoesNotContain(alias, JsonSerializer.Serialize(CardRulesetManager.Snapshot()), StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => CardRulesetManager.Activate(alias));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static CardRuleset CreateRuleset(string id, IScriptedEffect effect)
        => new(
            id,
            baseRulesetId: null,
            description: id,
            new Dictionary<string, IScriptedEffect>(StringComparer.OrdinalIgnoreCase)
            {
                [effect.CardNumber] = effect,
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            changedCards: [effect.CardNumber]);

    private sealed class MarkerEffect(int marker) : IScriptedEffect
    {
        public string CardNumber => "OP15-003";
        public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

        public Task Resolve(EffectContext ctx)
        {
            ctx.State.TurnCount = marker;
            return Task.CompletedTask;
        }
    }
}
