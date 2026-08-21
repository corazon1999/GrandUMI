using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public sealed class QqFeedback20260822RegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP15_026_TrashesItselfWhenActivatedMainResolves()
    {
        var state = TestScene.New().MyCharacter("OP15-026").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var target = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.DoesNotContain(source, me.Characters);
        Assert.Contains(source, me.Trash);
    }

    [Fact]
    public async Task OP15_025_WaitsUntilEndOfTurnBeforeChoosingCharacterToLock()
    {
        var state = TestScene.New().OppCharacter("OP15-003").Build();
        var opponent = state.Players[1];
        opponent.CostArea.AddRange(
        [
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);
        var target = Assert.Single(opponent.Characters);
        target.IsTapped = true;
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-025"), EffectTrigger.OnEnterField, prompts);

        Assert.False(target.CannotActivateNextReset);
        Assert.Equal(2, opponent.AttachedDonCount(target.Id));
        AtomicOps.AttachDonFromCost(opponent, target.Id, 1, DonState.Rest);

        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, prompts);

        Assert.True(target.CannotActivateNextReset);
        Assert.DoesNotContain(state.EndOfTurnTasks,
            task => task.Kind == "PreventOpponentDonCharacterReset");
    }

    [Fact]
    public async Task OP16_014_PromptsOnlyOneMarcoForOneBatchLeaveReplacement()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var firstMarco = Card("OP16-014");
        var secondMarco = Card("OP16-014");
        me.Characters.AddRange([firstMarco, secondMarco]);
        var gravityBlade = Card("OP06-058");
        var prompts = new MockPromptService()
            .QueueChoose(firstMarco.Id.ToString(), secondMarco.Id.ToString())
            .QueueConfirm(true);

        await EffectRuntime.Resolve(state, 1, gravityBlade, EffectTrigger.EventMain, prompts);

        Assert.Single(prompts.ConfirmHistory);
        Assert.Single(me.Characters);
        Assert.Contains(me.Characters.Single(), new[] { firstMarco, secondMarco });
        Assert.Single(me.Trash);
        Assert.Contains(me.Trash.Single(), new[] { firstMarco, secondMarco });
        Assert.Empty(me.Deck);
    }
}
