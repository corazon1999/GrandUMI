using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP13EffectTests
{
    [Fact]
    public async Task OP13_113_CanSearchBurningSwordWithPrintedLifeTrigger()
    {
        var state = TestScene.New()
            .MyDeckTop("OP08-117", "OP15-003")
            .Build();
        var me = state.Players[0];
        var source = new CardInstance { Info = CardDatabase.Get("OP13-113")! };
        var burningSword = me.Deck[0];
        me.Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(burningSword.Id.ToString());

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnEnterField,
            prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LilithReveal", prompt.kind);
        Assert.Contains(burningSword.Id.ToString(), prompt.choices);
        Assert.False(string.IsNullOrWhiteSpace(burningSword.Info.Trigger));
        Assert.Contains(burningSword, me.Hand);
        Assert.DoesNotContain(burningSword, me.Deck);
    }

    [Fact]
    public async Task OP13_004_CurrentCostAtLeast8_BuffsLeaderAndAllOwnCharacters()
    {
        var state = TestScene.New("OP13-004")
            .MyCharacter("OP02-013")
            .MyCharacter("OP15-003")
            .OppCharacter("OP15-003")
            .AttachDonToMyLeader(1)
            .Build();
        var me = state.Players[0];
        var currentCost8 = me.Characters[0];
        currentCost8.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, currentCost8));
        Assert.Equal(1000, state.ContinuousPowerBonus(0, me.Leader));
        Assert.All(me.Characters, card =>
            Assert.Equal(1000, state.ContinuousPowerBonus(0, card)));
        Assert.Equal(0, state.ContinuousPowerBonus(1, state.Players[1].Characters[0]));
    }

    [Fact]
    public async Task OP13_004_RequiresAttachedDonAndCurrentCostAtLeast8()
    {
        var state = TestScene.New("OP13-004")
            .MyCharacter("OP02-013")
            .Build();
        var me = state.Players[0];
        var character = me.Characters[0];
        character.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(0, me.Leader));

        me.CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = me.Leader.Id,
        });
        Assert.Equal(1000, state.ContinuousPowerBonus(0, me.Leader));

        character.CostModThisTurn = 0;
        Assert.Equal(7, state.CurrentCostOf(0, character));
        Assert.Equal(0, state.ContinuousPowerBonus(0, me.Leader));
    }
}
