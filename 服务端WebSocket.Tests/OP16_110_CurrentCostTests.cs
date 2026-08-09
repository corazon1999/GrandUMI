using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP16_110_CurrentCostTests
{
    [Theory]
    [InlineData(EffectTrigger.OnKO)]
    [InlineData(EffectTrigger.OnLifeRevealTrigger)]
    public async Task CostBuffed_OP17_089_IsNotEligibleToBeRested(EffectTrigger trigger)
    {
        var state = TestScene.New()
            .OppCharacter("OP17-089")
            .MyDeckTop("OP16-110")
            .Build();
        var target = state.Players[1].Characters.Single();

        await EffectRuntime.Resolve(state, 1, target, EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.True(state.CurrentCostOf(1, target) > 6);

        var source = new CardInstance { Info = CardDatabase.Get("OP16-110")! };
        state.Players[0].Trash.Add(source);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, trigger, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory.Where(
            history => history.kind == "OpponentCharacterCostLe6"));
        Assert.DoesNotContain(target.Id.ToString(), targetPrompt.choices);
        Assert.False(target.IsTapped);
    }

    [Fact]
    public async Task CharacterWhoseCurrentCostIsSix_RemainsEligibleToBeRested()
    {
        var state = TestScene.New()
            .OppCharacter("OP17-089")
            .MyDeckTop("OP16-110")
            .Build();
        var target = state.Players[1].Characters.Single();
        target.CostModThisTurn = 2;
        Assert.Equal(6, state.CurrentCostOf(1, target));

        var source = new CardInstance { Info = CardDatabase.Get("OP16-110")! };
        state.Players[0].Trash.Add(source);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory.Where(
            history => history.kind == "OpponentCharacterCostLe6"));
        Assert.Contains(target.Id.ToString(), targetPrompt.choices);
        Assert.True(target.IsTapped);
    }
}
