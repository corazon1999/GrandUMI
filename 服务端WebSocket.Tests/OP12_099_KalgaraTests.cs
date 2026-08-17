using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP12_099_KalgaraTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 我方回合中_对方生命因效果离场时抽一张牌()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003")
            .MyCharacter("OP12-099")
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var lifeCard = Card("OP15-004");
        opponent.LifeArea.Add(lifeCard);
        for (var i = 0; i < 7; i++) opponent.Hand.Add(Card("OP15-005"));

        var shanks = Card("ST13-009");
        me.Characters.Add(shanks);
        await EffectRuntime.Resolve(state, 0, shanks, EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true));

        Assert.Empty(opponent.LifeArea);
        Assert.Contains(lifeCard, opponent.Trash);
        Assert.Single(me.Hand);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task 对方回合中_对方生命离场时不抽牌()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003")
            .MyCharacter("OP12-099")
            .Build();
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnLifeLeaveField,
            new MockPromptService(), new Dictionary<string, object?> { ["owner"] = 1 });

        Assert.Empty(state.Players[0].Hand);
        Assert.Single(state.Players[0].Deck);
    }
}
