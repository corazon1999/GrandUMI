using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public sealed class QqFeedback20260822RegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP15_026_TrashesItselfWhenActivatedMainResolves()
    {
        var state = TestScene.New().MyCharacter("OP15-026").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var target = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.DoesNotContain(source, me.Characters);
        Assert.Contains(source, me.Trash);
    }
}
