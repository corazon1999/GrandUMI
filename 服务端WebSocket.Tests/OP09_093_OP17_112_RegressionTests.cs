using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP09_093_OP17_112_RegressionTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Fact]
    public async Task OP17_112_TwoCopies_KeepEligibleCharacterAt8000Power()
    {
        var state = TestScene.New().Build();
        var firstBigMom = Card("OP17-112");
        var secondBigMom = Card("OP17-112");
        var triggerCharacter = Card("OP17-102");
        state.Players[0].Characters.Add(firstBigMom);
        state.Players[0].Characters.Add(secondBigMom);
        state.Players[0].Characters.Add(triggerCharacter);
        state.CurrentTurnPlayer = 0;

        await EffectRuntime.Resolve(
            state, 0, firstBigMom, EffectTrigger.OnEnterField, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, secondBigMom, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8000, state.CurrentPowerOf(0, triggerCharacter));
    }

    [Fact]
    public async Task OP09_093_Nullifies_OP17_112_ContinuousPowerBonus()
    {
        var state = TestScene.New("OP16-080").Build();
        var bigMom = Card("OP17-112");
        var triggerCharacter = Card("OP17-102");
        state.Players[1].Characters.Add(bigMom);
        state.Players[1].Characters.Add(triggerCharacter);

        state.CurrentTurnPlayer = 1;
        await EffectRuntime.Resolve(
            state, 1, bigMom, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(8000, state.CurrentPowerOf(1, triggerCharacter));

        state.CurrentTurnPlayer = 0;
        var teach = Card("OP09-093", state.TurnCount);
        state.Players[0].Characters.Add(teach);
        await EffectRuntime.Resolve(
            state, 0, teach, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(bigMom.Id.ToString()));

        state.CurrentTurnPlayer = 1;
        Assert.True(state.IsContinuouslyNullified(bigMom));
        Assert.Equal(4000, state.CurrentPowerOf(1, triggerCharacter));
    }
}
