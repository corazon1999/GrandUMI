using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP11_067_CharlotteKatakuriTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task MyTurnEnd_ActivatesBothSelectedBigMomPiratesCharacters()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var katakuri = Card("OP11-067");
        var firstTarget = Card("OP11-067");
        var secondTarget = Card("OP11-067");
        firstTarget.IsTapped = true;
        secondTarget.IsTapped = true;
        me.Characters.AddRange([katakuri, firstTarget, secondTarget]);

        var prompts = new MockPromptService()
            .QueueChoose(firstTarget.Id.ToString(), secondTarget.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, katakuri, EffectTrigger.OnMyTurnEnd, prompts);

        Assert.False(firstTarget.IsTapped);
        Assert.False(secondTarget.IsTapped);
        var selection = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnCharacter", selection.kind);
        Assert.Equal(2, selection.max);
    }
}
