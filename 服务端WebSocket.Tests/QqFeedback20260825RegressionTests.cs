using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class QqFeedback20260825RegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP17_017_CounterAcceptsWhitebeardAffiliatedTrait()
    {
        var state = TestScene.New().Build();
        var affiliated = Card("OP16-017");
        var unrelated = Card("OP14-029");
        state.Players[0].Characters.AddRange([affiliated, unrelated]);
        var prompts = new MockPromptService()
            .QueueChoose(affiliated.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, Card("OP17-017"), EffectTrigger.EventCounter, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "OwnLeaderOrCharacter"));
        Assert.Contains(affiliated.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(unrelated.Id.ToString(), targetPrompt.choices);
        Assert.Equal(2000, affiliated.PowerModThisBattle);
    }

    [Fact]
    public async Task ST30_014_TwoRestDonCanBeSplitAcrossTwoCharacters()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("ST30-014");
        var first = Card("ST30-006");
        var second = Card("ST30-007");
        me.Characters.AddRange([source, first, second]);
        me.CostArea.AddRange([
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);
        var prompts = new MockPromptService()
            .QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueOption(1)
            .QueueOption(1);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(1, me.AttachedDonCount(first.Id));
        Assert.Equal(1, me.AttachedDonCount(second.Id));
        Assert.Equal(0, me.RestDonCount);
    }

    [Fact]
    public async Task OP16_048_SkippingFirstAttackDoesNotConsumeOncePerTurn()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var buggy = Card("OP16-048");
        var prisoner = Card("OP16-042");
        me.Characters.AddRange([buggy, prisoner]);
        string key = $"{buggy.Id}-Trigger-{EffectTrigger.OnOppAttackDeclare}";

        await EffectRuntime.Resolve(state, 0, buggy, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChooseEmpty());
        Assert.DoesNotContain(key, me.TurnOnceUsed);

        await EffectRuntime.Resolve(state, 0, buggy, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(prisoner.Id.ToString()));
        Assert.Contains(key, me.TurnOnceUsed);
        Assert.True(ActionValidator.HasKeyword(state, prisoner, "阻挡者"));
    }

    [Fact]
    public async Task OP14_117_CounterLetsPlayerBuffThrillerBarkCharacter()
    {
        var state = TestScene.New().Build();
        var target = Card("OP14-033");
        state.Players[0].Characters.Add(target);

        await EffectRuntime.Resolve(state, 0, Card("OP14-117"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Equal(3000, target.PowerModThisBattle);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP14_058_MainStillBouncesPower6000CharacterWhenNoCardIsPlayed()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var me = state.Players[0];
        var target = Card("OP14-055");
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService()
            .QueueChoose(me.CostArea.Select(don => don.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-058"), EffectTrigger.EventMain, prompts);

        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Hand);
        Assert.Equal(3, me.RestDonCount);
    }

    [Fact]
    public async Task OP07_079_CanPayAttackCostWithoutOpponentCharacter()
    {
        var state = TestScene.New().MyDeckTop("OP14-042", "OP14-045", "OP14-050").Build();
        var me = state.Players[0];
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, Card("OP07-079"), EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(2, me.Trash.Count);
        Assert.Single(me.Deck);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public void OP15_058_IsUnavailableUntilControllersSecondTurn()
    {
        var state = TestScene.New("OP15-058").Build();
        var leader = state.Players[0].Leader;
        state.FirstPlayer = 0;
        state.CurrentTurnPlayer = 0;
        state.Phase = Phase.Main;

        state.TurnCount = 1;
        Assert.False(ActionValidator.CanUseEffect(state, 0, leader.Id).Ok);
        Assert.False(LeaderOncePerTurnAvailable(state));

        state.TurnCount = 3;
        Assert.True(ActionValidator.CanUseEffect(state, 0, leader.Id).Ok);
        Assert.True(LeaderOncePerTurnAvailable(state));
    }

    [Fact]
    public async Task SimultaneousEnterEffects_AreResolvedInPlayersChosenOrder()
    {
        var state = TestScene.New("OP14-041")
            .MyDeckTop("OP14-042")
            .Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        me.LifeArea.Add(Card("OP14-050"));
        var entering = Card("OP14-103");
        me.Hand.Add(entering);
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString())
            .QueueConfirm(false);

        await AtomicOps.PlayFromHandFree(state, 0, entering);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        var orderPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("EffectOrder", orderPrompt.kind);
        Assert.Contains(me.Leader.Id.ToString(), orderPrompt.choices);
        Assert.Contains(entering.Id.ToString(), orderPrompt.choices);
        Assert.Contains(me.Hand, card => card.Info.Number == "OP14-042");
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task OP12_101_PowerBoostLastsUntilNextOpponentEndPhase()
    {
        var state = TestScene.New("OP14-001").Build();
        var me = state.Players[0];
        var bonney = Card("OP12-101");
        me.Characters.Add(bonney);

        await EffectRuntime.Resolve(state, 0, bonney, EffectTrigger.ActivatedMain, new MockPromptService());

        var modifier = Assert.Single(me.Leader.PowerModsUntilOppEnd);
        Assert.Equal(1000, modifier.Delta);
        Assert.Equal(0, modifier.AppliedBySide);
        Assert.Equal(0, me.Leader.PowerModThisTurn);

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Single(me.Leader.PowerModsUntilOppEnd);

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(me.Leader.PowerModsUntilOppEnd);
    }

    [Fact]
    public async Task OP17_109_OnEnterEffectCanBeDeclined()
    {
        var state = TestScene.New()
            .MyHandAdd("OP17-104")
            .MyDeckTop("OP17-101", "OP17-102", "OP17-103")
            .Build();
        var me = state.Players[0];
        var originalHand = Assert.Single(me.Hand);

        await EffectRuntime.Resolve(state, 0, Card("OP17-109"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(false));

        Assert.Equal(new[] { originalHand.Id }, me.Hand.Select(card => card.Id));
        Assert.Equal(3, me.Deck.Count);
        Assert.Empty(me.Trash);
    }

    [Fact]
    public async Task OP17_109_AcceptedEffectDiscardsSelectedTriggerCardThenDrawsThree()
    {
        var state = TestScene.New()
            .MyHandAdd("OP17-104")
            .MyHandAdd("OP17-105")
            .MyDeckTop("OP17-101", "OP17-102", "OP17-103")
            .Build();
        var me = state.Players[0];
        var discard = me.Hand.Single(card => card.Info.Number == "OP17-104");
        var keep = me.Hand.Single(card => card.Info.Number == "OP17-105");
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP17-109"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(discard, me.Trash);
        Assert.Contains(keep, me.Hand);
        Assert.Equal(4, me.Hand.Count);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task OP15_045_OnEnterEffectLetsPlayerChooseEventCostBeforeDrawingTwo()
    {
        var state = TestScene.New()
            .MyHandAdd("OP14-117")
            .MyHandAdd("OP14-116")
            .MyDeckTop("OP15-040", "OP15-041")
            .Build();
        var me = state.Players[0];
        var discard = me.Hand.Single(card => card.Info.Number == "OP14-117");
        var keep = me.Hand.Single(card => card.Info.Number == "OP14-116");
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-045"), EffectTrigger.OnEnterField, prompts);

        var costPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OP15_045_DiscardEvent", costPrompt.kind);
        Assert.Contains(discard.Id.ToString(), costPrompt.choices);
        Assert.Contains(keep.Id.ToString(), costPrompt.choices);
        Assert.Contains(discard, me.Trash);
        Assert.Contains(keep, me.Hand);
        Assert.Equal(3, me.Hand.Count);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task OP15_045_OnEnterEffectCanBeDeclinedWithoutDiscardOrDraw()
    {
        var state = TestScene.New()
            .MyHandAdd("OP14-117")
            .MyDeckTop("OP15-040", "OP15-041")
            .Build();
        var me = state.Players[0];

        await EffectRuntime.Resolve(state, 0, Card("OP15-045"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(false));

        Assert.Single(me.Hand);
        Assert.Equal(2, me.Deck.Count);
        Assert.Empty(me.Trash);
    }

    private static bool LeaderOncePerTurnAvailable(GameState state)
    {
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        return snapshot.GetProperty("my").GetProperty("leaderOncePerTurnEffectAvailable").GetBoolean();
    }
}
