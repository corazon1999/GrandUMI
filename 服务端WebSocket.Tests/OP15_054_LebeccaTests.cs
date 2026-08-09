using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP15_054_LebeccaTests
{
    [Fact]
    public async Task DrawDiscardOption_PromptsForEligibleDressrosaCharacterAndPlaysIt()
    {
        var state = TestScene.New("OP15-002")
            .MyHandAdd("OP15-006") // 4 费《德莱斯罗兹》
            .MyHandAdd("OP15-045") // 5 费《德莱斯罗兹》
            .MyHandAdd("OP15-004") // 1 费非《德莱斯罗兹》
            .MyDeckTop("OP15-040", "OP15-046") // 1 费与 7 费《德莱斯罗兹》
            .Build();
        var me = state.Players[0];
        var discard = me.Hand.Single(c => c.Info.Number == "OP15-004");
        var handEligible = me.Hand.Single(c => c.Info.Number == "OP15-006");
        var drawnEligible = me.Deck[0];
        var source = new CardInstance { Info = CardDatabase.Get("OP15-054")! };
        var prompts = new MockPromptService()
            .QueueOption(0)
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(drawnEligible.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, prompts);

        Assert.Contains(discard, me.Trash);
        Assert.Contains(drawnEligible, me.Characters);
        Assert.DoesNotContain(drawnEligible, me.Hand);

        Assert.Equal(2, prompts.ChooseHistory.Count);
        var playPrompt = prompts.ChooseHistory[1];
        Assert.Equal("OwnHandDressrosaCostLe4", playPrompt.kind);
        Assert.Equal(0, playPrompt.min);
        Assert.Equal(1, playPrompt.max);
        Assert.Contains(handEligible.Id.ToString(), playPrompt.choices);
        Assert.Contains(drawnEligible.Id.ToString(), playPrompt.choices);
        Assert.DoesNotContain(me.Hand.Single(c => c.Info.Number == "OP15-045").Id.ToString(), playPrompt.choices);
        Assert.DoesNotContain(me.Hand.Single(c => c.Info.Number == "OP15-046").Id.ToString(), playPrompt.choices);
    }
}
