using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP14_033_PeronaTests
{
    [Fact]
    public async Task OnKO_CanRestAnyActiveOwnCardAsCost()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = new CardInstance { Info = CardDatabase.Get("OP14-033")! };
        var otherCharacter = new CardInstance { Info = CardDatabase.Get("OP14-034")! };
        var stage = new CardInstance { Info = CardDatabase.Get("OP14-020")! };
        var don = new DonCard { State = DonState.Active };
        var playable = new CardInstance { Info = CardDatabase.Get("OP14-034")! };
        me.Characters.Add(otherCharacter);
        me.StageCard = stage;
        me.CostArea.Add(don);
        me.Hand.Add(playable);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(stage.Id.ToString())
            .QueueChoose(playable.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        var costPrompt = prompts.ChooseHistory[0];
        Assert.Equal("RestOwnCardsOrDon", costPrompt.kind);
        Assert.Contains(me.Leader.Id.ToString(), costPrompt.choices);
        Assert.Contains(otherCharacter.Id.ToString(), costPrompt.choices);
        Assert.Contains(stage.Id.ToString(), costPrompt.choices);
        Assert.Contains(don.Id.ToString(), costPrompt.choices);
        Assert.True(stage.IsTapped);
        Assert.Contains(playable, me.Characters);
    }
}
