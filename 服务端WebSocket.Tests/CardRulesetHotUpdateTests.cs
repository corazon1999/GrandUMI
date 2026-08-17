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
