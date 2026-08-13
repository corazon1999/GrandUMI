using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP13_099_EmptyThroneRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ImuGameStart_PlayedStageRegistersTrashPowerPassive()
    {
        var state = TestScene.New("OP13-079").MyDeckTop("OP13-099", "OP15-003").Build();
        var stage = state.Players[0].Deck[0];

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart,
            new MockPromptService().QueueChoose(stage.Id.ToString()));

        Assert.Same(stage, state.Players[0].StageCard);
        Assert.Contains(state.ContinuousEffects, effect => effect.SourceCardId == stage.Id.ToString());

        for (int index = 0; index < 19; index++) state.Players[0].Trash.Add(Card("OP15-003"));
        Assert.Equal(state.Players[0].Leader.Info.Power + 1000,
            state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task ActivatedMain_PaysCostBeforePlayingEligibleFiveElder()
    {
        var state = TestScene.New().MyActiveDon(7).MyHandAdd("OP13-084").Build();
        var me = state.Players[0];
        var stage = Card("OP13-099");
        var character = me.Hand.Single();
        me.StageCard = stage;
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(character.Id.ToString());

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);

        Assert.True(stage.IsTapped);
        Assert.Equal(4, me.ActiveDonCount);
        Assert.Equal(3, me.RestDonCount);
        Assert.Contains(character, me.Characters);
        Assert.DoesNotContain(character, me.Hand);
    }

    [Fact]
    public async Task ActivatedMain_CanPayOptionalCostWithoutEligibleTarget()
    {
        var state = TestScene.New().MyActiveDon(3).MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var stage = Card("OP13-099");
        me.StageCard = stage;

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true));

        Assert.True(stage.IsTapped);
        Assert.Equal(0, me.ActiveDonCount);
        Assert.Equal(3, me.RestDonCount);
        Assert.Single(me.Hand);
        Assert.Empty(me.Characters);
    }

    [Fact]
    public async Task ActivatedMain_DeclinedCostDoesNotRestCards()
    {
        var state = TestScene.New().MyActiveDon(3).MyHandAdd("OP13-083").Build();
        var me = state.Players[0];
        var stage = Card("OP13-099");
        me.StageCard = stage;

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(false));

        Assert.False(stage.IsTapped);
        Assert.Equal(3, me.ActiveDonCount);
        Assert.Equal(0, me.RestDonCount);
        Assert.Empty(me.Characters);
    }

    [Fact]
    public async Task UseEffectAction_OpensCostPromptThenPlaysChosenCharacter()
    {
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("op13-099-action", ("s0", "p0", deck), ("s1", "p1", deck),
            firstPlayer: 0, rngSeed: 9);
        var state = engine.State;
        var me = state.Players[0];
        var stage = Card("OP13-099");
        var character = Card("OP13-083");
        me.StageCard = stage;
        me.Hand.Clear();
        me.Hand.Add(character);
        me.CostArea.Clear();
        for (int index = 0; index < 7; index++) me.CostArea.Add(new DonCard { State = DonState.Active });
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;

        Assert.True(engine.HandleAction(0, "UseEffect",
            JsonSerializer.SerializeToElement(new { sourceId = stage.Id.ToString(), effectKey = "main" })));
        await engine.WaitSettledAsync();

        var costPrompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("Option", costPrompt.Kind);
        engine.HandleAction(0, "PromptResponse",
            JsonSerializer.SerializeToElement(new { promptId = costPrompt.PromptId, chosen = new[] { "0" } }));
        await engine.WaitSettledAsync(resolvingPromptId: costPrompt.PromptId);

        var characterPrompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("OwnHandCharacter", characterPrompt.Kind);
        Assert.Contains(character.Id.ToString(), characterPrompt.ValidChoices);
        engine.HandleAction(0, "PromptResponse",
            JsonSerializer.SerializeToElement(new
            {
                promptId = characterPrompt.PromptId,
                chosen = new[] { character.Id.ToString() },
            }));
        await engine.WaitSettledAsync(resolvingPromptId: characterPrompt.PromptId);

        // OP13-083 已成功登场，并继续进入它自身的【登场时】检索效果。
        var enterPrompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("LookTopReveal", enterPrompt.Kind);
        engine.HandleAction(0, "PromptResponse",
            JsonSerializer.SerializeToElement(new { promptId = enterPrompt.PromptId, chosen = Array.Empty<string>() }));
        await engine.WaitSettledAsync(resolvingPromptId: enterPrompt.PromptId);

        Assert.Null(state.PendingPrompt);
        Assert.True(stage.IsTapped);
        Assert.Equal(4, me.ActiveDonCount);
        Assert.Contains(character, me.Characters);
    }
}
