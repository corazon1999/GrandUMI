using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-31 已确认反馈 #7、#14、#25 的服务端回归。</summary>
public sealed class ConfirmedFeedback20260831DonRulesTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task UpToDonCount_AppliesExactlySelectedCountAndAllowsZero()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        me.CostArea.AddRange([
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);
        var prompts = new MockPromptService().QueueOption(2);

        int applied = await AtomicOps.PromptChooseAndApplyDonCount(
            state,
            prompts,
            0,
            3,
            "测试最多数量",
            don => don.State == DonState.Rest,
            don => don.State = DonState.Active);

        Assert.Equal(2, applied);
        Assert.Equal(2, me.ActiveDonCount);
        Assert.Equal(["0 张", "1 张", "2 张", "3 张"], prompts.OptionHistory.Single().options);

        int zeroApplied = await AtomicOps.PromptChooseAndApplyDonCount(
            state,
            new MockPromptService().QueueOption(0),
            0,
            3,
            "测试选择零张",
            don => don.State == DonState.Rest,
            don => don.State = DonState.Active);
        Assert.Equal(0, zeroApplied);
        Assert.Equal(2, me.ActiveDonCount);
    }

    [Fact]
    public async Task UpToDonCount_StaleAvailabilityDoesNotPartiallyApply()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var first = new DonCard { State = DonState.Rest };
        var second = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([first, second]);
        var prompts = new MockPromptService().QueueOption(2);
        prompts.OnOptionResponse = () => first.State = DonState.Active;

        int applied = await AtomicOps.PromptChooseAndApplyDonCount(
            state,
            prompts,
            0,
            2,
            "测试响应后状态变化",
            don => don.State == DonState.Rest,
            don => don.State = DonState.Active);

        Assert.Equal(0, applied);
        Assert.Equal(DonState.Rest, second.State);
    }

    [Fact]
    public async Task OP13_001_RestsOnlyChosenDonAndGrantsMatchingPowerInstances()
    {
        var state = TestScene.New("OP13-001").Build();
        var me = state.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        me.CostArea.AddRange([
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
        ]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueOption(2)
            .QueueChoose(me.Leader.Id.ToString())
            .QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnOppAttackDeclare, prompts);

        Assert.Equal(1, me.ActiveDonCount);
        Assert.Equal(2, me.RestDonCount);
        Assert.Equal(4000, me.Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP13_027_And_OP14_022_PromptForQuantityWithoutChangingForcedDslEffects()
    {
        var sanjiState = TestScene.New().Build();
        var sanji = Card("OP13-027");
        sanjiState.Players[0].Characters.Add(sanji);
        sanjiState.Players[0].CostArea.AddRange([
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);
        await EffectRuntime.Resolve(sanjiState, 0, sanji, EffectTrigger.OnEnterField,
            new MockPromptService().QueueOption(1));
        Assert.Equal(1, sanjiState.Players[0].ActiveDonCount);

        var optionalDslState = TestScene.New().Build();
        var optionalDslSource = Card("OP14-022");
        optionalDslState.Players[0].Characters.Add(optionalDslSource);
        optionalDslState.Players[0].CostArea.AddRange([
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);
        await EffectRuntime.Resolve(optionalDslState, 0, optionalDslSource, EffectTrigger.OnMyTurnEnd,
            new MockPromptService().QueueOption(1));
        Assert.Equal(1, optionalDslState.Players[0].ActiveDonCount);

        var forcedDslState = TestScene.New().Build();
        var forcedDslSource = Card("OP14-024");
        forcedDslState.Players[0].Characters.Add(forcedDslSource);
        forcedDslState.Players[0].CostArea.AddRange([
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);
        var forcedPrompts = new MockPromptService();
        await EffectRuntime.Resolve(forcedDslState, 0, forcedDslSource, EffectTrigger.OnEnterField, forcedPrompts);
        Assert.Equal(3, forcedDslState.Players[0].ActiveDonCount);
        Assert.Empty(forcedPrompts.OptionHistory);
    }

    [Fact]
    public async Task OP14_031_UsesPromptedTaskWhileLegacyRefreshTaskRemainsCompatible()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP14-031");
        me.Characters.Add(source);
        for (int i = 0; i < 6; i++) me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService().QueueOption(3);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, prompts);

        Assert.Equal(3, me.ActiveDonCount);
        Assert.DoesNotContain(state.EndOfTurnTasks, task => task.Kind == "ChooseRefreshOwnDonUpTo");

        state.EndOfTurnTasks.Add(new EndTurnTask { Kind = "RefreshOwnDon", Owner = 0, Count = 2 });
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5, me.ActiveDonCount);
    }

    [Fact]
    public async Task OP15_023_CancelledOrStaleBenefitSelectionDoesNotAttachDon()
    {
        var cancelledState = TestScene.New().MyCharacter("OP15-023").OppCharacter("OP15-003").Build();
        var cancelledMe = cancelledState.Players[0];
        var cancelledOpp = cancelledState.Players[1];
        var cancelledDon = new DonCard { State = DonState.Active };
        cancelledMe.CostArea.Add(cancelledDon);
        cancelledOpp.CostArea.Add(new DonCard { State = DonState.Rest });
        await EffectRuntime.Resolve(
            cancelledState,
            0,
            cancelledMe.Characters.Single(),
            EffectTrigger.ActivatedMain,
            new MockPromptService()
                .QueueChoose(cancelledOpp.Characters.Single().Id.ToString())
                .QueueChoose(cancelledMe.Leader.Id.ToString())
                .QueueChooseEmpty());
        Assert.Equal(DonState.Active, cancelledDon.State);

        var staleState = TestScene.New().MyCharacter("OP15-023").MyCharacter("OP15-003").OppCharacter("OP15-004").Build();
        var staleMe = staleState.Players[0];
        var staleOpp = staleState.Players[1];
        var source = staleMe.Characters.Single(card => card.Info.Number == "OP15-023");
        var benefitTarget = staleMe.Characters.Single(card => card.Info.Number == "OP15-003");
        var staleDon = new DonCard { State = DonState.Active };
        staleMe.CostArea.Add(staleDon);
        staleOpp.CostArea.Add(new DonCard { State = DonState.Rest });
        var stalePrompts = new MockPromptService()
            .QueueChoose(staleOpp.Characters.Single().Id.ToString())
            .QueueChoose(benefitTarget.Id.ToString())
            .QueueChoose(staleDon.Id.ToString());
        stalePrompts.OnChooseResponse = kind =>
        {
            if (kind == "HolderActiveDon") staleMe.Characters.Remove(benefitTarget);
        };

        await EffectRuntime.Resolve(staleState, 0, source, EffectTrigger.ActivatedMain, stalePrompts);

        Assert.Equal(DonState.Active, staleDon.State);
        Assert.Null(staleDon.AttachedToCardId);
    }

    [Fact]
    public async Task OP15_023_StaleCostTargetDoesNotPayOrConsumeOncePerTurn()
    {
        var state = TestScene.New().MyCharacter("OP15-023").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = me.Characters.Single();
        var target = opponent.Characters.Single();
        var costDon = new DonCard { State = DonState.Rest };
        opponent.CostArea.Add(costDon);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        prompts.OnChooseResponse = kind =>
        {
            if (kind == "OpponentCharacter") opponent.Characters.Remove(target);
        };

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(DonState.Rest, costDon.State);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task OP13_082_ZeroOwnCharactersStillPaysCostAndDoesNotTouchOpponent()
    {
        var state = TestScene.New("OP13-079").MyActiveDon(1).MyHandAdd("OP15-003").OppCharacter("OP15-004").Build();
        var me = state.Players[0];
        var opponentCharacter = state.Players[1].Characters.Single();
        var discard = me.Hand.Single();

        await EffectRuntime.Resolve(
            state,
            0,
            Card("OP13-082"),
            EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(discard.Id.ToString()).QueueChooseEmpty());

        Assert.Empty(me.Characters);
        Assert.Contains(discard, me.Trash);
        Assert.Equal(DonState.Rest, me.CostArea.Single().State);
        Assert.Contains(opponentCharacter, state.Players[1].Characters);
    }

    [Fact]
    public async Task OP13_082_TrashesAllSnapshotCharactersDespiteOpponentEffectOnlyLeaveGuard()
    {
        var state = TestScene.New("OP13-079").MyActiveDon(1).MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP13-082");
        var guarded = Card("OP13-084");
        var ordinary = Card("OP15-004");
        me.Characters.AddRange([source, guarded, ordinary]);
        for (int i = 0; i < 7; i++) me.Trash.Add(Card("OP15-003"));
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = guarded.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            LeaveGuard = "effect",
            Predicate = (_, side, card) => side == 0 && ReferenceEquals(card, guarded),
        });
        var discard = me.Hand.Single();

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(discard.Id.ToString()).QueueChooseEmpty());

        Assert.Empty(me.Characters);
        Assert.Contains(source, me.Trash);
        Assert.Contains(guarded, me.Trash);
        Assert.Contains(ordinary, me.Trash);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OP13_082_StaleCostResponseDoesNotPartiallyPay(bool removeHand)
    {
        var state = TestScene.New("OP13-079").MyActiveDon(1).MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP13-082");
        me.Characters.Add(source);
        var discard = me.Hand.Single();
        var don = me.CostArea.Single();
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString());
        prompts.OnChooseResponse = kind =>
        {
            if (kind != "OwnHand") return;
            if (removeHand) me.Hand.Remove(discard);
            else don.State = DonState.Rest;
        };

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(source, me.Characters);
        if (removeHand) Assert.Equal(DonState.Active, don.State);
        else Assert.Contains(discard, me.Hand);
    }

    [Fact]
    public async Task OP13_082_UnpayableStateDoesNotOpenConfirmation()
    {
        var state = TestScene.New("OP13-079").MyHandAdd("OP15-003").Build();
        var source = Card("OP13-082");
        state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(prompts.ConfirmHistory);
        Assert.Contains(source, state.Players[0].Characters);
    }
}
