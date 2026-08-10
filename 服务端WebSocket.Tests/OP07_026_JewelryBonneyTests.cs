using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class OP07_026_JewelryBonneyTests
{
    [Fact]
    public async Task OnEnter_CanChooseRestDon_AndItSkipsTheNextReset()
    {
        var state = TestScene.New().Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP07-026")! };
        var restDon = new DonCard { State = DonState.Rest };
        state.Players[1].CostArea.Add(restDon);
        var prompts = new MockPromptService().QueueChoose(restDon.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OpponentRestingCharacterOrDon", prompt.kind);
        Assert.Contains(restDon.Id.ToString(), prompt.choices);
        Assert.NotNull(prompt.extra);
        var donChoicesJson = JsonSerializer.Serialize(prompt.extra!["donChoices"]);
        Assert.Contains(restDon.Id.ToString(), donChoicesJson);
        Assert.Contains("Rest", donChoicesJson);
        Assert.True(restDon.CannotActivateNextReset);

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterResetPhase(state);

        Assert.Equal(DonState.Rest, restDon.State);
        Assert.False(restDon.CannotActivateNextReset);

        TurnEngine.EnterResetPhase(state);
        Assert.Equal(DonState.Active, restDon.State);
    }

    [Fact]
    public async Task OnEnter_MixedTargets_StillAllowsChoosingRestCharacter()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP07-026")! };
        var character = Assert.Single(state.Players[1].Characters);
        character.IsTapped = true;
        var restDon = new DonCard { State = DonState.Rest };
        state.Players[1].CostArea.Add(restDon);
        var prompts = new MockPromptService().QueueChoose(character.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(character.Id.ToString(), prompt.choices);
        Assert.Contains(restDon.Id.ToString(), prompt.choices);
        Assert.True(character.CannotActivateNextReset);
        Assert.False(restDon.CannotActivateNextReset);
    }
}
