using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-02 QQ 机器人与游戏内 F 反馈的定向回归。</summary>
public sealed class LatestFeedback20260902Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP15_010_OnlyAttachesRestedDonFromTargetsOwner()
    {
        var state = TestScene.New().MyCharacter("OP15-010").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var active = new DonCard { State = DonState.Active };
        var rested = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([active, rested]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()));

        Assert.Equal(DonState.Active, active.State);
        Assert.Equal(DonState.Attached, rested.State);
        Assert.Equal(me.Leader.Id, rested.AttachedToCardId);
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task OP15_010_DoesNotConsumeActiveDonWhenNoRestedDonExists()
    {
        var state = TestScene.New().MyCharacter("OP15-010").MyActiveDon(1).Build();
        var me = state.Players[0];

        await EffectRuntime.Resolve(state, 0, Assert.Single(me.Characters), EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()));

        Assert.Equal(DonState.Active, Assert.Single(me.CostArea).State);
        Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task OP15_023_CanAttachRestedDonFromSelectedTargetsOwner()
    {
        var state = TestScene.New().MyCharacter("OP15-023").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Assert.Single(me.Characters);
        var costTarget = Assert.Single(opponent.Characters);
        var active = new DonCard { State = DonState.Active };
        var rested = new DonCard { State = DonState.Rest };
        var opponentCost = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([active, rested]);
        opponent.CostArea.Add(opponentCost);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(costTarget.Id.ToString())
            .QueueChoose(me.Leader.Id.ToString())
            .QueueChoose(rested.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(DonState.Attached, opponentCost.State);
        Assert.Equal(costTarget.Id, opponentCost.AttachedToCardId);
        Assert.Equal(DonState.Active, active.State);
        Assert.Equal(DonState.Attached, rested.State);
        Assert.Equal(me.Leader.Id, rested.AttachedToCardId);
        Assert.Contains(me.TurnOnceUsed, key => key == $"OP15-023-act:{source.Id}");
    }

    [Fact]
    public async Task OP15_063_ReturnsChosenDonBeforeDrawing()
    {
        var state = TestScene.New().MyCharacter("OP15-063").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var active = new DonCard { State = DonState.Active };
        var rested = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([active, rested]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(rested.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(DonState.Active, active.State);
        Assert.DoesNotContain(rested, me.CostArea);
        Assert.Contains(rested, me.DonDeck);
        Assert.Single(me.Hand);
        Assert.Empty(me.Deck);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "ReturnOwnDon");
    }

    [Fact]
    public async Task OP15_063_DecliningCostDoesNotDraw()
    {
        var state = TestScene.New().MyCharacter("OP15-063").MyActiveDon(1)
            .MyDeckTop("OP15-003").Build();
        var me = state.Players[0];

        await EffectRuntime.Resolve(state, 0, Assert.Single(me.Characters), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(false));

        Assert.Single(me.CostArea);
        Assert.Empty(me.Hand);
        Assert.Single(me.Deck);
    }

    [Fact]
    public async Task OP14_052_DiscardsExactlyThreeBeforePlayingImpelDownCharacter()
    {
        var state = TestScene.New().MyCharacter("OP14-052")
            .MyHandAdd("OP15-003").MyHandAdd("OP15-004").MyHandAdd("OP15-005")
            .MyHandAdd("OP11-064").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var discards = me.Hand.Take(3).ToArray();
        var target = me.Hand[3];
        var prompts = new MockPromptService()
            .QueueChoose(discards.Select(card => card.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.All(discards, card => Assert.Contains(card, me.Trash));
        Assert.Contains(target, me.Characters);
        Assert.Empty(me.Hand);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "DiscardOwnChosen" && prompt.min == 0 && prompt.max == 3);
    }

    [Fact]
    public async Task OP14_052_CanDeclineWithoutDiscardingOrPlaying()
    {
        var state = TestScene.New().MyCharacter("OP14-052")
            .MyHandAdd("OP15-003").MyHandAdd("OP15-004").MyHandAdd("OP15-005")
            .MyHandAdd("OP11-064").Build();
        var me = state.Players[0];

        await EffectRuntime.Resolve(state, 0, Assert.Single(me.Characters), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        Assert.Equal(4, me.Hand.Count);
        Assert.Empty(me.Trash);
        Assert.Single(me.Characters);
    }

    [Fact]
    public async Task OP14_072_OptionalDonCostAddsDeckTopToLife()
    {
        var state = TestScene.New().MyCharacter("OP14-072").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var active = new DonCard { State = DonState.Active };
        var rested = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([active, rested]);
        var lifeCard = Assert.Single(me.Deck);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(rested.Id.ToString());

        Assert.True(await BattleEngine.KOCardAsync(state, 0, source, prompts));

        Assert.Contains(source, me.Trash);
        Assert.Equal(DonState.Active, active.State);
        Assert.Contains(rested, me.DonDeck);
        Assert.Same(lifeCard, Assert.Single(me.LifeArea));
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task OP14_072_DecliningCostStillCompletesKoWithoutLifeGain()
    {
        var state = TestScene.New().MyCharacter("OP14-072").MyActiveDon(1)
            .MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);

        Assert.True(await BattleEngine.KOCardAsync(state, 0, source,
            new MockPromptService().QueueConfirm(false)));

        Assert.Contains(source, me.Trash);
        Assert.Single(me.CostArea);
        Assert.Empty(me.LifeArea);
        Assert.Single(me.Deck);
    }

    [Fact]
    public async Task EB03_013_PlaysZouStageFromHandAfterKoStep()
    {
        var state = TestScene.New().MyCharacter("EB03-013").MyHandAdd("OP08-039").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        source.TurnPlayed = state.TurnCount;
        var zou = Assert.Single(me.Hand);
        var prompts = new MockPromptService().QueueChoose(zou.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Same(zou, me.StageCard);
        Assert.Empty(me.Hand);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "OwnHandStage");
        Assert.Contains(me.TurnOnceUsed, key => key == $"EB03-013-act:{source.Id}");
    }

    [Fact]
    public async Task OP17_112_StaticAuraSurvivesOpponentsOnEnterNullification()
    {
        var state = TestScene.New("OP09-081").MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var bigMom = Card("OP17-112");
        var triggerCharacter = Card("OP17-102");
        opponent.Characters.AddRange([bigMom, triggerCharacter]);
        opponent.Deck.Add(Card("OP15-004"));
        state.CurrentTurnPlayer = 1;
        var discarded = Assert.Single(me.Hand);

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discarded.Id.ToString()));
        await EffectRuntime.Resolve(state, 1, bigMom, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8000, state.CurrentPowerOf(1, triggerCharacter));
        Assert.Empty(opponent.Hand);
        Assert.Empty(opponent.LifeArea);
        Assert.Single(opponent.Deck);
    }
}
