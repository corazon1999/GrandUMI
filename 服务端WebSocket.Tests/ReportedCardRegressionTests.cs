using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ReportedCardRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP13_117_UsesOriginalCostInsteadOfCostBuffs()
    {
        var state = TestScene.New().OppCharacter("OP07-015").Build();
        var target = state.Players[1].Characters.Single();
        target.CostModThisTurn = 6 - target.Info.Cost;
        Assert.Equal(6, state.CurrentCostOf(1, target));

        await EffectRuntime.Resolve(state, 0, new CardInstance { Info = CardDatabase.Get("OP13-117")! },
            EffectTrigger.EventMain, new MockPromptService());

        Assert.Empty(state.Players[1].Trash);
    }

    [Fact]
    public async Task OP09_081_ActivatedEffect_NullifiesOpponentEnterEffects()
    {
        var state = TestScene.New("OP09-081")
            .MyHandAdd("OP15-003")
            .OppCharacter("OP15-003")
            .Build();
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(state.Players[0].Hand.Single().Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, prompts);
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.True(state.IsTriggerNullified(target, EffectTrigger.OnEnterField));
    }

    [Fact]
    public async Task ST17_002_ReturnsOwnCharacterAsCost_ThenAnyCurrentCostFourCharacter()
    {
        var state = TestScene.New("ST17-001")
            .MyCharacter("ST17-002")
            .MyCharacter("ST17-001")
            .OppCharacter("OP03-004")
            .Build();
        var me = state.Players[0];
        var law = me.Characters.Single(card => card.Info.Number == "ST17-002");
        var cost = me.Characters.Single(card => card.Info.Number == "ST17-001");
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(cost.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, law, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(cost, me.Hand);
        Assert.Contains(target, state.Players[1].Hand);
        Assert.DoesNotContain(target, state.Players[1].Characters);
    }

    [Fact]
    public async Task NullifiedCharacter_LosesItsContinuousCostAndRushEffects()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-067")
            .OppActiveDon(6)
            .Build();
        var target = state.Players[1].Characters.Single();
        target.TurnPlayed = state.TurnCount;

        await EffectRuntime.Resolve(state, 1, target, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(target.IsEffectsNullified);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = target.Id.ToString(),
            Scope = new ContinuousScope(),
            CostDelta = 12,
            Predicate = (_, _, card) => card.Id == target.Id,
        });

        Assert.Equal(target.Info.Cost + 12, state.CurrentCostOf(1, target));
        Assert.True(GrandUMI.Game.Validation.ActionValidator.HasKeyword(state, target, "速攻"));

        AtomicOps.NullifyEffects(target, KeywordDuration.ThisTurn);

        Assert.Equal(target.Info.Cost, state.CurrentCostOf(1, target));
        Assert.False(GrandUMI.Game.Validation.ActionValidator.HasKeyword(state, target, "速攻"));
    }

    [Fact]
    public async Task OP16_119_NullifiesCostEffectBeforeChoosingFiveCostKoTarget()
    {
        var state = TestScene.New("OP16-080")
            .OppCharacter("OP03-004")
            .Build();
        var target = state.Players[1].Characters.Single();
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = target.Id.ToString(),
            Scope = new ContinuousScope(),
            CostDelta = 12,
            Predicate = (_, _, card) => card.Id == target.Id,
        });
        Assert.Equal(target.Info.Cost + 12, state.CurrentCostOf(1, target));
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, new CardInstance { Info = CardDatabase.Get("OP16-119")! },
            EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Equal(target.Info.Cost, state.CurrentCostOf(target));
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP05_098_StillTriggersWhenLastLifeIsOP06_115()
    {
        _ = TestScene.New().Build();
        string deck = "OP05-098\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("enel-last-life", ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);
        var state = engine.State;
        var me = state.Players[0];
        me.Hand.Clear();
        me.Deck.Clear();
        me.LifeArea.Clear();
        me.LifeArea.Add(Card("OP06-115"));
        me.Deck.Add(Card("OP15-003"));
        me.Deck.Add(Card("OP15-004"));
        me.Hand.Add(Card("OP15-003"));
        me.Hand.Add(Card("OP15-004"));
        state.CurrentTurnPlayer = 1;

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        for (int i = 0; i < 100 && !damage.IsCompleted; i++)
        {
            if (state.PendingPrompt is { } prompt)
            {
                var choice = prompt.Kind == "LifeTrigger"
                    ? new[] { "trigger" }
                    : prompt.ValidChoices.Take(1).ToArray();
                engine.Prompts.Resolve(prompt.PromptId, choice);
            }
            await Task.Delay(10);
        }
        await damage;

        Assert.Equal(2, me.LifeArea.Count);
        Assert.Empty(me.Hand);
    }
}
