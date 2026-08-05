using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class LifeRevealManagerDamageTests
{
    private static GameEngine NewEngine()
    {
        _ = TestScene.New().Build();
        string deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        return new GameEngine(
            "life-damage-test",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MultiDamage_RemovesRemainingLife_WithoutWinning(int lifeCount)
    {
        var engine = NewEngine();
        var defender = engine.State.Players[1];
        defender.LifeArea.Clear();
        for (int i = 0; i < lifeCount; i++)
            defender.LifeArea.Add(new CardInstance { Info = CardDatabase.Get("OP15-003")! });

        await LifeRevealManager.DealDamageToLeader(engine, targetPlayerIdx: 1, damage: 2);

        Assert.Empty(defender.LifeArea);
        Assert.False(engine.State.IsGameOver);
        Assert.Null(engine.State.WinnerIndex);
    }

    [Fact]
    public async Task Damage_WhenLifeWasAlreadyEmpty_WinsGame()
    {
        var engine = NewEngine();
        engine.State.Players[1].LifeArea.Clear();

        await LifeRevealManager.DealDamageToLeader(engine, targetPlayerIdx: 1, damage: 2);

        Assert.True(engine.State.IsGameOver);
        Assert.Equal(0, engine.State.WinnerIndex);
    }

    [Fact]
    public void MultiDamage_NoPromptPath_DoesNotWinAfterRemovingLastLife()
    {
        var state = TestScene.New().Build();
        var defender = state.Players[1];
        defender.LifeArea.Add(new CardInstance { Info = CardDatabase.Get("OP15-003")! });

        LifeRevealManagerSync.DealDamageToLeaderNoPrompt(state, targetPlayerIdx: 1, damage: 2);

        Assert.Empty(defender.LifeArea);
        Assert.False(state.IsGameOver);
    }
}
