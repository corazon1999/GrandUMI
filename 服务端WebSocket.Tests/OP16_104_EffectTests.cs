using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class OP16_104_EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task AttackCopiesSelectedOpponentCharactersCurrentPower()
    {
        var state = TestScene.New("OP16-080").Build();
        var devon = Card("OP16-104");
        var target = Card("OP16-119");
        target.PowerModThisTurn = -2000;
        state.Players[0].Characters.Add(devon);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(
            state,
            0,
            devon,
            EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Equal(8000, devon.OriginalPowerOverride);
        Assert.Equal(8000, state.CurrentPowerOf(0, devon));
    }

    [Fact]
    public async Task AttackCanChooseNoCharacterAndLeavePowerUnchanged()
    {
        var state = TestScene.New("OP16-080").Build();
        var devon = Card("OP16-104");
        var target = Card("OP16-119");
        state.Players[0].Characters.Add(devon);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(
            state,
            0,
            devon,
            EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChooseEmpty());

        Assert.Null(devon.OriginalPowerOverride);
        Assert.Equal(3000, state.CurrentPowerOf(0, devon));
    }

    [Fact]
    public async Task CopiedOriginalPowerExpiresAtEndOfTurn()
    {
        var state = TestScene.New("OP16-080").Build();
        var devon = Card("OP16-104");
        var target = Card("OP16-119");
        state.Players[0].Characters.Add(devon);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(
            state,
            0,
            devon,
            EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Equal(10000, state.CurrentPowerOf(0, devon));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);

        Assert.Null(devon.OriginalPowerOverride);
        Assert.Equal(3000, state.CurrentPowerOf(0, devon));
    }
}
