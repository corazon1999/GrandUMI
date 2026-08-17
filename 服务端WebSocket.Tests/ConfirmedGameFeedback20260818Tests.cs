using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-18 汇总的 13 项确认未修复游戏内反馈。</summary>
public sealed class ConfirmedGameFeedback20260818Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP14_031_RefreshesUpToFiveDonAtEndOfEntryTurn()
    {
        var state = TestScene.New().OppCharacter("OP15-003").OppCharacter("OP15-004").Build();
        var me = state.Players[0];
        for (int i = 0; i < 6; i++) me.CostArea.Add(new DonCard { State = DonState.Rest });
        var targets = state.Players[1].Characters.ToArray();
        var source = Card("OP14-031");
        me.Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(targets.Select(card => card.Id.ToString()).ToArray()));

        Assert.All(targets, target => Assert.True(target.IsTapped));
        Assert.Contains(state.EndOfTurnTasks, task => task.Kind == "RefreshOwnDon" && task.Count == 5);
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5, me.ActiveDonCount);
        Assert.Equal(1, me.CostArea.Count(don => don.State == DonState.Rest));
    }

    [Fact]
    public async Task OP09_086_StaticPowerWorksUnderOP09_081()
    {
        var state = TestScene.New("OP09-081").Build();
        var me = state.Players[0];
        var burgess = Card("OP09-086");
        me.Characters.Add(burgess);
        for (int i = 0; i < 4; i++) me.Trash.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, burgess, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.DoesNotContain("OnEnterField", burgess.Info.EffectTags);
        Assert.Equal(6000, state.CurrentPowerOf(0, burgess));
    }

    [Fact]
    public async Task OP11_030_SearchCanFindAnotherShirahoshi()
    {
        var state = TestScene.New().MyActiveDon(1)
            .MyDeckTop("OP11-030", "OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = Card("OP11-030");
        me.Characters.Add(source);
        var target = me.Deck[0];
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(target, me.Hand);
        Assert.True(source.IsTapped);
        Assert.Equal(DonState.Rest, Assert.Single(me.CostArea).State);
    }

    [Fact]
    public async Task ST17_005_ReturnsHandToDeckTopInsteadOfTrash()
    {
        var state = TestScene.New().MyCharacter("ST17-005").MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var source = Assert.Single(me.Characters);
        var returned = Assert.Single(me.Hand);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(returned.Id.ToString())
            .QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Same(returned, Assert.Single(me.Deck));
        Assert.Empty(me.Hand);
        Assert.DoesNotContain(returned, me.Trash);
        Assert.Equal(2, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task OP14_038_CostCanRestLeaderAndCharacter()
    {
        var state = TestScene.New().MyCharacter("OP15-003").MyActiveDon(2)
            .MyDeckTop("OP15-004").Build();
        var me = state.Players[0];
        var character = Assert.Single(me.Characters);
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString(), character.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-038"), EffectTrigger.EventMain, prompts);

        Assert.True(me.Leader.IsTapped);
        Assert.True(character.IsTapped);
        Assert.Equal(2, me.ActiveDonCount);
        Assert.Single(me.Hand);
    }

    [Fact]
    public async Task OP10_003_AddsActiveDonWhenOwnerActivatesEventDuringOpponentTurn()
    {
        var state = TestScene.New("OP10-003").Build();
        state.CurrentTurnPlayer = 1;
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnOppEventPlayed, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });

        Assert.Empty(state.Players[0].DonDeck);
        Assert.Equal(DonState.Active, Assert.Single(state.Players[0].CostArea).State);
        Assert.Contains(state.Players[0].TurnOnceUsed, key => key.StartsWith("OP10-003-event:"));
    }

    [Fact]
    public async Task PRB01_001_CanGrantRushToStaticOnlyBurgess()
    {
        var state = TestScene.New("PRB01-001").MyCharacter("OP09-086").Build();
        var burgess = Assert.Single(state.Players[0].Characters);

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(burgess.Id.ToString()));

        Assert.True(ActionValidator.HasKeyword(state, burgess, "速攻"));
    }

    [Fact]
    public async Task OP17_089_StaticCostSurvivesOP09_081OnPlayNullification()
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP17-089")
            .MyDeckTop("OP17-080", "OP17-081", "OP17-082").Build();
        var me = state.Players[0];
        var saul = Assert.Single(me.Characters);

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, saul, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(saul.Info.Cost + 12, state.CurrentCostOf(0, saul));
        Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task OP15_025_AttachesOnlyRestedOpponentDon()
    {
        var state = TestScene.New().OppCharacter("OP15-003").Build();
        var opponent = state.Players[1];
        var active = new DonCard { State = DonState.Active };
        var rested = new DonCard { State = DonState.Rest };
        opponent.CostArea.AddRange([active, rested]);
        var target = Assert.Single(opponent.Characters);

        await EffectRuntime.Resolve(state, 0, Card("OP15-025"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Equal(1, opponent.AttachedDonCount(target.Id));
        Assert.Equal(DonState.Active, active.State);
        Assert.Equal(DonState.Attached, rested.State);
    }

    [Fact]
    public async Task WhitebeardContainsMatchingCoversAlliesKeyword()
    {
        var searchState = TestScene.New().MyDeckTop("OP16-017", "OP15-003").Build();
        var searched = searchState.Players[0].Deck[0];
        await EffectRuntime.Resolve(searchState, 0, Card("OP17-019"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(searched.Id.ToString()));
        Assert.Contains(searched, searchState.Players[0].Hand);

        var powerState = TestScene.New().MyCharacter("OP16-017").Build();
        var oz = Assert.Single(powerState.Players[0].Characters);
        oz.CostModThisTurn = 4;
        await EffectRuntime.Resolve(powerState, 0, oz, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(8000, powerState.CurrentPowerOf(0, oz));
    }

    [Fact]
    public async Task OP17_109_TriggerReordersRemainingCardsToDeckBottom()
    {
        var state = TestScene.New().MyDeckTop("OP17-110", "OP17-101", "OP17-102", "OP17-103", "OP17-104", "OP15-003").Build();
        var me = state.Players[0];
        var selected = me.Deck[0];
        var remainder = me.Deck.Skip(1).Take(4).ToArray();
        var tail = me.Deck[5];
        var desired = remainder.Reverse().ToArray();
        var prompts = new MockPromptService()
            .QueueChoose(selected.Id.ToString())
            .QueueChoose(desired.Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, Card("OP17-109"), EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Contains(selected, me.Hand);
        Assert.Equal(new[] { tail.Id }.Concat(desired.Select(card => card.Id)), me.Deck.Select(card => card.Id));
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "ReorderToDeckBottom");
    }

    [Fact]
    public async Task OP16_119_ReordersRemainingCardsToDeckBottom()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-006").Build();
        var me = state.Players[0];
        var selected = me.Deck[0];
        var second = me.Deck[1];
        var third = me.Deck[2];
        var tail = me.Deck[3];
        var prompts = new MockPromptService()
            .QueueChoose(selected.Id.ToString())
            .QueueChoose(third.Id.ToString(), second.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP16-119"), EffectTrigger.OnEnterField, prompts);

        Assert.Same(selected, Assert.Single(me.LifeArea));
        Assert.Equal(new[] { tail.Id, third.Id, second.Id }, me.Deck.Select(card => card.Id));
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "ReorderToDeckBottom");
    }
}
