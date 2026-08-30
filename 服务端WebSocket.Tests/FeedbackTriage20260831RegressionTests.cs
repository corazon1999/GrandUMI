using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-31 QQ 群与游戏内反馈首批回归。</summary>
public sealed class FeedbackTriage20260831RegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP15_098_ProtectsEligibleSkyIslandCharacterFromOpponentBattleKO()
    {
        var state = TestScene.New("OP15-098").Build();
        var defender = state.Players[0];
        var attackerSide = state.Players[1];
        var victim = Card("OP15-099");
        var attacker = Card("OP16-003");
        var life = Card("OP15-003");
        defender.Characters.Add(victim);
        defender.LifeArea.Add(life);
        attackerSide.Characters.Add(attacker);
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        var prompts = new MockPromptService().QueueConfirm(true);

        BattleEngine.StartAttack(state, attacker.Id, targetIsLeader: false, victim.Id);
        await BattleEngine.ResolveDamageAsync(state, prompts);

        Assert.Contains(victim, defender.Characters);
        Assert.DoesNotContain(victim, defender.Trash);
        Assert.Empty(defender.LifeArea);
        Assert.Contains(life, defender.Hand);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP15_098_DecliningOpponentEffectKO_IsAskedOnlyOnceAndDoesNotPay()
    {
        var state = TestScene.New("OP15-098").Build();
        var defender = state.Players[0];
        var victim = Card("OP15-100");
        var life = Card("OP15-003");
        defender.Characters.Add(victim);
        defender.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(false);

        bool wasKOd = await AtomicOps.KOByEffectAsync(
            state, 0, victim, prompts, actingSide: 1);

        Assert.True(wasKOd);
        Assert.DoesNotContain(victim, defender.Characters);
        Assert.Contains(victim, defender.Trash);
        Assert.Equal([life], defender.LifeArea);
        Assert.Empty(defender.Hand);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP15_098_OnePaymentProtectsAllEligibleVictimsInSimultaneousEffectKO()
    {
        var state = TestScene.New("OP15-098").Build();
        var defender = state.Players[0];
        var first = Card("OP15-099");
        var second = Card("OP15-100");
        var life = Card("OP15-003");
        defender.Characters.AddRange([first, second]);
        defender.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);

        int koCount = await AtomicOps.KOCardsByEffectAsync(
            state, 0, [first, second], prompts, actingSide: 1);

        Assert.Equal(0, koCount);
        Assert.Contains(first, defender.Characters);
        Assert.Contains(second, defender.Characters);
        Assert.Empty(defender.LifeArea);
        Assert.Contains(life, defender.Hand);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP15_098_DoesNotProtectAgainstOwnersOwnEffect()
    {
        var state = TestScene.New("OP15-098").Build();
        var defender = state.Players[0];
        var victim = Card("OP15-100");
        var life = Card("OP15-003");
        defender.Characters.Add(victim);
        defender.LifeArea.Add(life);
        var prompts = new MockPromptService();

        bool wasKOd = await AtomicOps.KOByEffectAsync(
            state, 0, victim, prompts, actingSide: 0);

        Assert.True(wasKOd);
        Assert.Contains(victim, defender.Trash);
        Assert.Equal([life], defender.LifeArea);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP15_098_CannotProtectBattleKOWhenLifeCostIsUnavailable()
    {
        var state = TestScene.New("OP15-098").Build();
        var defender = state.Players[0];
        var attackerSide = state.Players[1];
        var victim = Card("OP15-100");
        var attacker = Card("OP16-003");
        defender.Characters.Add(victim);
        attackerSide.Characters.Add(attacker);
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        var prompts = new MockPromptService();

        BattleEngine.StartAttack(state, attacker.Id, targetIsLeader: false, victim.Id);
        await BattleEngine.ResolveDamageAsync(state, prompts);

        Assert.DoesNotContain(victim, defender.Characters);
        Assert.Contains(victim, defender.Trash);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP16_117_MainDiscardsTriggerCardBeforeNullifyingTarget()
    {
        var state = TestScene.New().OppCharacter("OP15-050").Build();
        var me = state.Players[0];
        var target = Assert.Single(state.Players[1].Characters);
        var discard = Card("OP16-117");
        me.Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP16-117"), EffectTrigger.EventMain, prompts);

        Assert.DoesNotContain(discard, me.Hand);
        Assert.Contains(discard, me.Trash);
        Assert.True(target.IsEffectsNullified);
        Assert.Equal(["DiscardOwnChosen", "OpponentCharacterCostLe8"],
            prompts.ChooseHistory.Select(prompt => prompt.kind));
    }

    [Fact]
    public async Task OP16_117_MainCanDeclineCostWithoutNullifyingTarget()
    {
        var state = TestScene.New().OppCharacter("OP15-050").Build();
        var me = state.Players[0];
        var target = Assert.Single(state.Players[1].Characters);
        var discard = Card("OP16-117");
        me.Hand.Add(discard);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(
            state, 0, Card("OP16-117"), EffectTrigger.EventMain, prompts);

        Assert.Contains(discard, me.Hand);
        Assert.Empty(me.Trash);
        Assert.False(target.IsEffectsNullified);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("DiscardOwnChosen", prompt.kind);
        Assert.Equal(0, prompt.min);
        Assert.Equal(1, prompt.max);
    }

    [Fact]
    public async Task OP16_117_MainWithoutTriggerCardCannotPayOrChooseTarget()
    {
        var state = TestScene.New().OppCharacter("OP15-050").Build();
        var target = Assert.Single(state.Players[1].Characters);
        state.Players[0].Hand.Add(Card("OP17-012"));
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(
            state, 0, Card("OP16-117"), EffectTrigger.EventMain, prompts);

        Assert.False(target.IsEffectsNullified);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public void ST12_014_IsPlayableAsTwoCostCharacter()
    {
        var state = TestScene.New().MyActiveDon(2).MyHandAdd("ST12-014").Build();
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var card = Assert.Single(state.Players[0].Hand);

        Assert.Equal(CardKind.Character, card.Info.Kind);
        Assert.Contains("阻挡者", card.Info.Abilities);
        Assert.True(ActionValidator.CanPlayCard(state, 0, 0).Ok);
    }

    [Fact]
    public void OP17_021_HasPrintedZeroPower()
    {
        var state = TestScene.New().Build();
        var card = Card("OP17-021");
        state.Players[0].Characters.Add(card);

        Assert.Equal(0, card.Info.Power);
        Assert.Equal(0, state.CurrentPowerOf(0, card));
    }

    [Fact]
    public void OP08_034_HasPrintedOneThousandCounter()
        => Assert.Equal(1000, CardDatabase.Get("OP08-034")!.Counter);

    [Fact]
    public async Task ST29_015_AtThreeLifeReducesOpponentForWholeTurn()
    {
        var state = TestScene.New().OppCharacter("OP15-050").Build();
        var me = state.Players[0];
        me.LifeArea.AddRange([Card("OP15-003"), Card("OP15-004"), Card("OP15-005")]);
        var ownLeader = me.Leader;
        var opponent = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueChoose(ownLeader.Id.ToString())
            .QueueChoose(opponent.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("ST29-015"), EffectTrigger.EventCounter, prompts);

        Assert.Equal(2000, ownLeader.PowerModThisBattle);
        Assert.Equal(-2000, opponent.PowerModThisTurn);
        Assert.Equal(0, opponent.PowerModThisBattle);
        Assert.Equal(2, prompts.ChooseHistory.Count);
    }

}
