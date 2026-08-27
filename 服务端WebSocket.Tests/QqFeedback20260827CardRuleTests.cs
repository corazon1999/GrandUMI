using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class QqFeedback20260827CardRuleTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP18_078_ActivatedMainOnlyAttachesRestedDon()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var stage = Card("OP18-078");
        var character = Card("OP18-031");
        me.StageCard = stage;
        me.Characters.Add(character);
        var restedDon = new DonCard { State = DonState.Rest };
        var activeDon = new DonCard { State = DonState.Active };
        me.CostArea.AddRange([restedDon, activeDon]);
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString(), character.Id.ToString());

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(1, targetPrompt.max);
        Assert.True(stage.IsTapped);
        Assert.Equal(DonState.Attached, restedDon.State);
        Assert.Equal(me.Leader.Id, restedDon.AttachedToCardId);
        Assert.Equal(DonState.Active, activeDon.State);
        Assert.Null(activeDon.AttachedToCardId);
    }

    [Fact]
    public async Task OP11_104_SearchTreatsSharlyAsFishmanIslandCard()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var life = Card("OP11-036");
        life.IsLifeFaceUp = true;
        me.LifeArea.Add(life);
        var sharly = Card("OP11-104");
        me.Deck.Add(sharly);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, Card("OP11-104"), EffectTrigger.OnEnterField, prompts);

        var search = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LookTopReveal", search.kind);
        Assert.Contains(sharly.Id.ToString(), search.choices);
        Assert.False(life.IsLifeFaceUp);
    }

    [Fact]
    public void OP17_109_UsesKnowledgeProperty()
    {
        _ = TestScene.New().Build();

        Assert.Equal("知", CardDatabase.Get("OP17-109")!.Property);
    }

    [Fact]
    public async Task OP14_033_ActualKoFlowCanRestDonAndPlaySelectedCharacter()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var perona = Card("OP14-033");
        var playable = Card("OP14-034");
        var don = new DonCard { State = DonState.Active };
        me.Characters.Add(perona);
        me.Hand.Add(playable);
        me.CostArea.Add(don);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(don.Id.ToString())
            .QueueChoose(playable.Id.ToString());

        var wasKOd = await BattleEngine.KOCardAsync(state, 0, perona, prompts);

        Assert.True(wasKOd);
        Assert.Contains(perona, me.Trash);
        Assert.Equal(DonState.Rest, don.State);
        Assert.DoesNotContain(playable, me.Hand);
        Assert.Contains(playable, me.Characters);
    }

    [Fact]
    public async Task ReturningRestrictedCharacterToHandClearsRestrictionBeforeReplay()
    {
        var state = TestScene.New().Build();
        var targetOwner = state.Players[1];
        var oden = Card("ST32-002");
        var target = Card("OP11-036");
        state.Players[0].Characters.Add(oden);
        targetOwner.Characters.Add(target);

        await EffectRuntime.Resolve(state, 0, oden, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));

        AtomicOps.BounceToHand(state, 1, target);
        Assert.Empty(target.Restrictions);
        Assert.Contains(target, targetOwner.Hand);

        AtomicOps.BounceToHand(state, 1, target);
        Assert.Equal(1, targetOwner.Hand.Count(card => ReferenceEquals(card, target)));

        await AtomicOps.PlayFromHandFree(state, 1, target);
        AtomicOps.RestCard(target);

        Assert.Contains(target, targetOwner.Characters);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task OP11_036_SearchTreatsSpottedFishAsSeaKingCard()
    {
        var state = TestScene.New("OP11-022").Build();
        var spottedFish = Card("OP11-036");
        state.Players[0].Deck.Add(spottedFish);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, Card("OP11-036"), EffectTrigger.OnEnterField, prompts);

        var search = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LookTopReveal", search.kind);
        Assert.Contains(spottedFish.Id.ToString(), search.choices);
    }

    [Fact]
    public async Task OP11_104_CanPutRemainingCardsOnTopInChosenOrder()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var life = Card("OP11-036");
        life.IsLifeFaceUp = true;
        me.LifeArea.Add(life);
        var picked = Card("OP11-104");
        var firstRemaining = Card("OP11-036");
        var secondRemaining = Card("OP11-037");
        var untouched = Card("OP11-038");
        me.Deck.AddRange([picked, firstRemaining, secondRemaining, untouched]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(picked.Id.ToString())
            .QueueChoose(secondRemaining.Id.ToString(), firstRemaining.Id.ToString())
            .QueueOption(0);

        await EffectRuntime.Resolve(state, 0, Card("OP11-104"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(picked, me.Hand);
        Assert.Equal(
            new[] { secondRemaining.Id, firstRemaining.Id, untouched.Id },
            me.Deck.Select(card => card.Id).ToArray());
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "OrderDeckCards");
    }

    [Fact]
    public async Task OP11_104_IncompleteOrderResponseKeepsEveryRemainingCard()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var life = Card("OP11-036");
        life.IsLifeFaceUp = true;
        me.LifeArea.Add(life);
        var first = Card("OP11-036");
        var second = Card("OP11-037");
        var third = Card("OP11-038");
        me.Deck.AddRange([first, second, third]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChooseEmpty()
            .QueueChoose(second.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP11-104"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(3, me.Deck.Count);
        Assert.Equal(
            new[] { second.Id, first.Id, third.Id },
            me.Deck.Select(card => card.Id).ToArray());
    }

    [Fact]
    public async Task OP10_037_CanReplaceKoCausedByAttackTriggeredOpponentEffect()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var lim = Card("OP10-037");
        var restCost = Card("OP10-033");
        var vista = Card("OP16-011");
        me.Characters.AddRange([lim, restCost]);
        opponent.Characters.Add(vista);
        opponent.CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = vista.Id,
        });
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            AttackerCardId = vista.Id,
            DefenderPlayerIndex = 0,
            TargetIsLeader = true,
        };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(lim.Id.ToString())
            .QueueChoose(restCost.Id.ToString());

        await EffectRuntime.Resolve(state, 1, vista, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Contains(lim, me.Characters);
        Assert.DoesNotContain(lim, me.Trash);
        Assert.True(restCost.IsTapped);
        Assert.Contains($"OP10-037-prevent:{lim.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task OP10_037_DoesNotReplaceBattleKo()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var lim = Card("OP10-037");
        var restCost = Card("OP10-033");
        me.Characters.AddRange([lim, restCost]);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            AttackerCardId = state.Players[1].Leader.Id,
            DefenderPlayerIndex = 0,
            TargetCardId = lim.Id,
            TargetIsLeader = false,
        };

        var wasKOd = await BattleEngine.KOCardAsync(state, 0, lim, new MockPromptService());

        Assert.True(wasKOd);
        Assert.Contains(lim, me.Trash);
        Assert.False(restCost.IsTapped);
    }
}
