using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class ActionValidatorAttackTests
{
    [Fact]
    public void 双方各自首回合不能攻击_之后可以攻击()
    {
        var state = TestScene.New().Build();

        state.TurnCount = 1;
        state.CurrentTurnPlayer = state.FirstPlayer;
        var firstPlayerLeader = state.Players[state.FirstPlayer].Leader;
        Assert.False(ActionValidator.CanAttack(
            state, state.FirstPlayer, firstPlayerLeader.Id, true, null).Ok);

        int secondPlayer = 1 - state.FirstPlayer;
        state.TurnCount = 2;
        state.CurrentTurnPlayer = secondPlayer;
        var secondPlayerLeader = state.Players[secondPlayer].Leader;
        Assert.False(ActionValidator.CanAttack(
            state, secondPlayer, secondPlayerLeader.Id, true, null).Ok);

        state.TurnCount = 3;
        state.CurrentTurnPlayer = state.FirstPlayer;
        Assert.True(ActionValidator.CanAttack(
            state, state.FirstPlayer, firstPlayerLeader.Id, true, null).Ok);
    }
}
