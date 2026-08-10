using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP15_096_FlyingSwallowTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task EventCounter_WithoutDiscardingHand_DoesNotGrantPower()
    {
        var state = TestScene.New().Build();
        var source = Card("OP15-096");
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
        Assert.Empty(state.Players[0].Trash);
    }

    [Fact]
    public async Task EventCounter_AfterDiscardingHand_GrantsPower()
    {
        var state = TestScene.New().Build();
        var source = Card("OP15-096");
        var discard = Card("OP15-095");
        state.Players[0].Hand.Add(discard);
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Equal(3000, state.Players[0].Leader.PowerModThisBattle);
        Assert.Contains(discard, state.Players[0].Trash);
    }
}
