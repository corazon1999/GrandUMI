using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>费用/力量类卡效应使用“当前值”还是“原本值”的回归测试。</summary>
public class CardValueBasisRegressionTests
{
    [Fact]
    public async Task OP12_029_FirstStepUsesCurrentCost()
    {
        var state = TestScene.New()
            .OppCharacter("OP07-015")
            .OppCharacter("OP13-013")
            .Build();
        var reducedHighCost = state.Players[1].Characters[0];
        var increasedLowCost = state.Players[1].Characters[1];
        reducedHighCost.CostModThisTurn = 2 - reducedHighCost.Info.Cost;
        increasedLowCost.CostModThisTurn = 3 - increasedLowCost.Info.Cost;

        var source = new CardInstance { Info = CardDatabase.Get("OP12-029")! };
        state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService().QueueChoose(reducedHighCost.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var restPrompt = Assert.Single(prompts.ChooseHistory.Where(
            history => history.text.Contains("费用不高于 2")));
        Assert.Contains(reducedHighCost.Id.ToString(), restPrompt.choices);
        Assert.DoesNotContain(increasedLowCost.Id.ToString(), restPrompt.choices);
        Assert.True(reducedHighCost.IsTapped);
        Assert.False(increasedLowCost.IsTapped);
    }

    [Fact]
    public async Task OP12_029_ExplicitOriginalCostIgnoresCurrentCostIncrease()
    {
        var state = TestScene.New()
            .OppCharacter("OP13-013")
            .Build();
        var target = state.Players[1].Characters.Single();
        target.IsTapped = true;
        target.CostModThisTurn = 10;
        Assert.True(state.CurrentCostOf(1, target) > 1);

        var source = new CardInstance { Info = CardDatabase.Get("OP12-029")! };
        state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var koPrompt = Assert.Single(prompts.ChooseHistory.Where(
            history => history.text.Contains("原本费用不高于 1")));
        Assert.Contains(target.Id.ToString(), koPrompt.choices);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP13_013_TargetSelectionIncludesContinuousPowerEffects()
    {
        var state = TestScene.New()
            .OppCharacter("OP07-015")
            .OppCharacter("OP07-015")
            .Build();
        var zeroPowerTarget = state.Players[1].Characters[0];
        var auraProtectedTarget = state.Players[1].Characters[1];
        zeroPowerTarget.PowerModThisTurn = -zeroPowerTarget.Info.Power;
        auraProtectedTarget.PowerModThisTurn = -auraProtectedTarget.Info.Power;
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = auraProtectedTarget.Id.ToString(),
            Scope = new ContinuousScope(),
            PowerDelta = 1000,
            Predicate = (_, _, card) => card.Id == auraProtectedTarget.Id,
        });

        var source = new CardInstance { Info = CardDatabase.Get("OP13-013")! };
        state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService().QueueChoose(zeroPowerTarget.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(zeroPowerTarget.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(auraProtectedTarget.Id.ToString(), targetPrompt.choices);
        Assert.Contains(zeroPowerTarget, state.Players[1].Trash);
        Assert.Contains(auraProtectedTarget, state.Players[1].Characters);
    }

    [Fact]
    public async Task EB01_061_CopiesTargetsCurrentPowerIntoOriginalPower()
    {
        var state = TestScene.New()
            .OppCharacter("OP07-015")
            .Build();
        var target = state.Players[1].Characters.Single();
        target.PowerModThisTurn = -2000;
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = target.Id.ToString(),
            Scope = new ContinuousScope(),
            PowerDelta = 500,
            Predicate = (_, _, card) => card.Id == target.Id,
        });
        int copiedPower = state.CurrentPowerOf(1, target);

        var source = new CardInstance { Info = CardDatabase.Get("EB01-061")!, PowerModThisTurn = 1000 };
        state.Players[0].Characters.Add(source);
        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = source.Id,
        });
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(copiedPower, source.OriginalPowerOverride);
        Assert.Equal(copiedPower + 2000, state.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task P019_ExplicitOriginalPowerIgnoresCurrentPowerIncrease()
    {
        var state = TestScene.New()
            .OppCharacter("OP13-013")
            .Build();
        var target = state.Players[1].Characters.Single();
        target.PowerModThisTurn = 5000;
        Assert.True(state.CurrentPowerOf(1, target) > 3000);

        var source = new CardInstance { Info = CardDatabase.Get("P-019")! };
        state.Players[0].Characters.Add(source);
        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = source.Id,
        });
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(target.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP10_042_ContinuousCostThresholdUsesCurrentCostWithoutRecursion()
    {
        var state = TestScene.New(myLeaderNumber: "OP10-042")
            .MyCharacter("OP04-080")
            .MyCharacter("OP04-091")
            .Build();
        var raisedToTwo = state.Players[0].Characters[0];
        var stillOne = state.Players[0].Characters[1];
        Assert.Equal(1, raisedToTwo.Info.Cost);
        Assert.Equal(1, stillOne.Info.Cost);
        raisedToTwo.CostModThisTurn = 1;

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader,
            EffectTrigger.OnGameStart, new MockPromptService());

        Assert.Equal(3, state.CurrentCostOf(0, raisedToTwo));
        Assert.Equal(1, state.CurrentCostOf(0, stillOne));
    }
}
