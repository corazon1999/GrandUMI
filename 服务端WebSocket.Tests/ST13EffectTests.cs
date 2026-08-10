using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class ST13EffectTests
{
    [Theory]
    [InlineData("ST13-007", "ST13-008")]
    [InlineData("ST13-010", "ST13-011")]
    [InlineData("ST13-014", "ST13-015")]
    public async Task 生命区角色登场后领袖加成持续到下个对方回合结束(
        string sourceNumber,
        string lifeTargetNumber)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card(sourceNumber);
        var lifeTarget = Card(lifeTargetNumber);
        me.Characters.Add(source);
        me.LifeArea.Add(lifeTarget);

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true));

        Assert.Contains(source, me.Trash);
        Assert.Contains(lifeTarget, me.Characters);
        Assert.Equal(0, me.Leader.PowerModPersistent);
        var modifier = Assert.Single(me.Leader.PowerModsUntilOppEnd);
        Assert.Equal(2000, modifier.Delta);
        Assert.Equal(0, modifier.AppliedBySide);

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Single(me.Leader.PowerModsUntilOppEnd);

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(me.Leader.PowerModsUntilOppEnd);
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };
}
