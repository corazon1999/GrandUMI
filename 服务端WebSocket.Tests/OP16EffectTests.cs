using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP16EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public void OP16_118_Changes8000PowerCharactersInHandToCounter2000()
    {
        var state = TestScene.New("OP16-001").Build();
        var ace = Card("OP16-118");
        var counter1000 = Card("OP16-017");
        var counter0 = Card("OP16-011");
        var non8000 = Card("OP16-009");

        state.Players[0].Characters.Add(ace);
        state.Players[0].Hand.AddRange([counter1000, counter0, non8000]);

        Assert.Equal(2000, HandStaticCounter.Value(state, 0, counter1000));
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, counter0));
        Assert.Equal(non8000.Info.Counter, HandStaticCounter.Value(state, 0, non8000));
    }

    [Fact]
    public void OP16_118_StopsChangingCountersAfterLeavingFieldOrBeingNullified()
    {
        var state = TestScene.New("OP16-001").Build();
        var ace = Card("OP16-118");
        var target = Card("OP16-017");

        state.Players[0].Characters.Add(ace);
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, target));

        ace.IsEffectsNullified = true;
        Assert.Equal(target.Info.Counter, HandStaticCounter.Value(state, 0, target));

        ace.IsEffectsNullified = false;
        state.Players[0].Characters.Remove(ace);
        Assert.Equal(target.Info.Counter, HandStaticCounter.Value(state, 0, target));
    }
}
