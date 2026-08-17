using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST10_015_GumGumGiantSumoSlapTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task EventCounter_AddsTwoThousandAndKOsLowPowerCharacter()
    {
        var state = TestScene.New().MyCharacter("OP15-003").OppCharacter("OP15-050").Build();
        var ally = state.Players[0].Characters.Single();
        var target = state.Players[1].Characters.Single();
        target.PowerModThisTurn = -4000;
        var prompts = new MockPromptService()
            .QueueChoose(ally.Id.ToString())
            .QueueChoose(target.Id.ToString());

        var source = Card("ST10-015");
        Assert.Contains(nameof(EffectTrigger.EventCounter), source.Info.EffectTags);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Equal(2000, ally.PowerModThisBattle);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }
}
