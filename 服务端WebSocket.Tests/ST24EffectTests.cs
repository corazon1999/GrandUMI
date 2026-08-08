using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class ST24EffectTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ST24_004_RestingSecondOpponentCharacter_GivesLeader2000AndLocksTarget()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .OppCharacter("OP15-051")
            .Build();
        var opponentCharacters = state.Players[1].Characters;
        opponentCharacters[1].IsTapped = true;
        var target = opponentCharacters[0];
        var bepo = Card("ST24-004");
        state.Players[0].Characters.Add(bepo);
        int leaderPowerBefore = state.CurrentPowerOf(0, state.Players[0].Leader);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, bepo, EffectTrigger.OnEnterField, prompts);

        Assert.True(target.IsTapped);
        Assert.True(target.CannotActivateNextReset);
        Assert.Equal(leaderPowerBefore + 2000, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task ST24_004_WithOnlyOneRestingOpponentCharacter_DoesNotGiveLeaderPower()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .Build();
        state.Players[1].Characters[0].IsTapped = true;
        var bepo = Card("ST24-004");
        state.Players[0].Characters.Add(bepo);
        int leaderPowerBefore = state.CurrentPowerOf(0, state.Players[0].Leader);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, bepo, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(leaderPowerBefore, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task ST24_004_WithTwoAlreadyRestingOpponentCharacters_GivesLeader2000WithoutChoosingTarget()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .OppCharacter("OP15-051")
            .Build();
        foreach (var character in state.Players[1].Characters)
            character.IsTapped = true;
        var bepo = Card("ST24-004");
        state.Players[0].Characters.Add(bepo);
        int leaderPowerBefore = state.CurrentPowerOf(0, state.Players[0].Leader);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, bepo, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(leaderPowerBefore + 2000, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task ST24_004_LeaderPowerBonus_LastsUntilOpponentTurnEnds()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .OppCharacter("OP15-051")
            .Build();
        foreach (var character in state.Players[1].Characters)
            character.IsTapped = true;
        var bepo = Card("ST24-004");
        state.Players[0].Characters.Add(bepo);
        var leader = state.Players[0].Leader;
        int leaderPowerBefore = state.CurrentPowerOf(0, leader);

        await EffectRuntime.Resolve(
            state,
            0,
            bepo,
            EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        Assert.Equal(leaderPowerBefore + 2000, state.CurrentPowerOf(0, leader));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(leaderPowerBefore + 2000, state.CurrentPowerOf(0, leader));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(leaderPowerBefore, state.CurrentPowerOf(0, leader));
    }
}
