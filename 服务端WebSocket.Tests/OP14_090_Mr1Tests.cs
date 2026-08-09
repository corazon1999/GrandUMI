using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP14_090_Mr1Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OnEnter_RestsOpponentCharacterReducedBelowZero_AndGainsRush()
    {
        var state = TestScene.New().Build();
        state.TurnCount = 3;
        var mr1 = Card("OP14-090");
        mr1.TurnPlayed = state.TurnCount;
        var reducedCharacter = Card("ST32-003");
        reducedCharacter.CostModThisTurn = -20;
        state.Players[0].Characters.Add(mr1);
        state.Players[1].Characters.Add(reducedCharacter);
        var prompts = new MockPromptService().QueueChoose(reducedCharacter.Id.ToString());

        await EffectRuntime.Resolve(state, 0, mr1, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(0, state.CurrentCostOf(1, reducedCharacter));
        Assert.True(reducedCharacter.IsTapped);
        Assert.True(ActionValidator.HasKeyword(state, mr1, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, mr1.Id, true, null).Ok);
    }

    [Fact]
    public async Task ConditionalRush_UsesBothSidesCurrentCost_AndUpdatesWithBoardState()
    {
        var state = TestScene.New().Build();
        var mr1 = Card("OP14-090");
        var ownCharacter = Card("OP14-120");
        ownCharacter.CostModThisTurn = -1;
        state.Players[0].Characters.Add(mr1);
        state.Players[0].Characters.Add(ownCharacter);

        await EffectRuntime.Resolve(state, 0, mr1, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        Assert.Equal(7, state.CurrentCostOf(0, ownCharacter));
        Assert.False(ActionValidator.HasKeyword(state, mr1, "速攻"));

        ownCharacter.CostModThisTurn = 0;
        Assert.Equal(8, state.CurrentCostOf(0, ownCharacter));
        Assert.True(ActionValidator.HasKeyword(state, mr1, "速攻"));

        state.Players[0].Characters.Remove(ownCharacter);
        Assert.False(ActionValidator.HasKeyword(state, mr1, "速攻"));

        var opponentCharacter = Card("OP14-120");
        opponentCharacter.CostModThisTurn = -1;
        state.Players[1].Characters.Add(opponentCharacter);
        Assert.Equal(7, state.CurrentCostOf(1, opponentCharacter));
        Assert.False(ActionValidator.HasKeyword(state, mr1, "速攻"));

        opponentCharacter.CostModThisTurn = 0;
        Assert.True(ActionValidator.HasKeyword(state, mr1, "速攻"));

        opponentCharacter.CostModThisTurn = -20;
        Assert.Equal(0, state.CurrentCostOf(1, opponentCharacter));
        Assert.True(ActionValidator.HasKeyword(state, mr1, "速攻"));

        state.Players[1].Characters.Remove(opponentCharacter);
        Assert.False(ActionValidator.HasKeyword(state, mr1, "速攻"));
    }
}
