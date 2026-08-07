using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP15-021「好好看着哦!艾斯!」手牌静态减费回归测试。</summary>
public class OP15_021_JustWatchMeAceTests
{
    [Theory]
    [InlineData(0, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    public void OP15_021_HandPlayCost_DependsOnEventCountInTrash(int eventCount, int expectedCost)
    {
        var state = TestScene.New()
            .MyHandAdd("OP15-021")
            .Build();
        var me = state.Players[0];
        AddTrashCards(me, "OP15-074", eventCount);

        Assert.Equal(expectedCost, state.HandPlayCost(0, Assert.Single(me.Hand)));
    }

    [Fact]
    public void OP15_021_HandPlayCost_DoesNotCountNonEventTrashCards()
    {
        var state = TestScene.New()
            .MyHandAdd("OP15-021")
            .Build();
        var me = state.Players[0];
        AddTrashCards(me, "OP15-074", 3);
        AddTrashCards(me, "OP15-003", 5);

        Assert.Equal(4, state.HandPlayCost(0, Assert.Single(me.Hand)));
    }

    [Fact]
    public void OP15_021_Play_RestsOnlyOneDonWhenFourEventsAreInTrash()
    {
        var state = TestScene.New()
            .MyActiveDon(4)
            .MyHandAdd("OP15-021")
            .Build();
        var me = state.Players[0];
        AddTrashCards(me, "OP15-074", 4);

        var result = CardPlayer.Play(state, 0, 0);

        Assert.Equal(PlayKind.Event, result.Kind);
        Assert.Equal(3, me.ActiveDonCount);
        Assert.Equal(1, me.CostArea.Count(d => d.State == DonState.Rest));
        Assert.Contains(result.Card, me.Trash);
    }

    private static void AddTrashCards(PlayerState player, string number, int count)
    {
        var info = CardDatabase.Get(number)!;
        for (int i = 0; i < count; i++)
            player.Trash.Add(new CardInstance { Info = info });
    }
}
