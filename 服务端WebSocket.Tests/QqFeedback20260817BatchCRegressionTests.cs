using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-17 QQ 群卡效反馈 UF-010～UF-054 定向回归。</summary>
public class QqFeedback20260817BatchCRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task UF010_OP15_080_CountsTenThousandPowerMoriaLeader()
    {
        var state = TestScene.New("OP06-080")
            .AttachDonToMyLeader(5)
            .MyCharacter("OP15-080")
            .Build();
        var oars = Assert.Single(state.Players[0].Characters);

        await EffectRuntime.Resolve(state, 0, oars, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(7000, state.CurrentPowerOf(0, oars));
    }

    [Fact]
    public async Task UF011_OP12_108_SearchesNormallyForSecondPlayer()
    {
        var state = TestScene.New().MyDeckTop("OP12-073", "OP15-003", "OP15-004").Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 2;
        var law = state.Players[0].Deck[0];
        var prompts = new MockPromptService().QueueChoose(law.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP12-108"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(law, state.Players[0].Hand);
        Assert.DoesNotContain(law, state.Players[0].Deck);
    }

    [Fact]
    public async Task UF016_OP06_043_CannotPayCostWithoutEligibleCharacter()
    {
        var state = TestScene.New().MyCharacter("OP06-043").MyHandAdd("OP15-003").Build();
        var aramaki = Assert.Single(state.Players[0].Characters);
        var hand = Assert.Single(state.Players[0].Hand);

        await EffectRuntime.Resolve(state, 0, aramaki, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Contains(hand, state.Players[0].Hand);
        Assert.Empty(state.Players[0].Trash);
        Assert.Equal(0, aramaki.PowerModThisTurn);
    }

    [Fact]
    public async Task UF016_OP06_043_ReturnsEitherPlayersEligibleCharacterThenGainsPower()
    {
        var state = TestScene.New().MyCharacter("OP06-043").MyHandAdd("OP15-003")
            .OppCharacter("OP06-052").Build();
        var aramaki = Assert.Single(state.Players[0].Characters);
        var discard = Assert.Single(state.Players[0].Hand);
        var target = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, aramaki, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(discard, state.Players[0].Trash);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Equal(target, state.Players[1].Deck[^1]);
        Assert.Equal(3000, aramaki.PowerModThisTurn);
    }

    [Fact]
    public async Task UF024_OP12_058_TriggersOnPlayEffectOfCharacterPlayedFromDeck()
    {
        var state = TestScene.New("OP02-001").MyDeckTop("ST22-011").Build();
        var firstReveal = Card("OP01-023");
        var secondReveal = Card("OP01-033");
        state.Players[0].Hand.AddRange([firstReveal, secondReveal]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(firstReveal.Id.ToString(), secondReveal.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP12-058"), EffectTrigger.EventMain, prompts);

        Assert.Contains(state.Players[0].Characters, card => card.Info.Number == "ST22-011");
        Assert.Equal(2000, state.Players[0].Leader.PowerModThisTurn);
    }

    [Fact]
    public async Task UF025_OP14_044_DrawsTwoThenDiscardsOneWhenRevealMatches()
    {
        var state = TestScene.New().MyDeckTop("OP01-023", "OP15-003").Build();
        var discard = state.Players[0].Deck[1];
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-044"), EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[0].Deck);
        Assert.Single(state.Players[0].Hand);
        Assert.Contains(discard, state.Players[0].Trash);
    }

    [Fact]
    public async Task UF030_ST22_001_AcceptsFormerWhitebeardPiratesTrait()
    {
        var state = TestScene.New("ST22-001").MyHandAdd("OP01-023").MyDeckTop("OP15-003").Build();
        var formerWhitebeard = Assert.Single(state.Players[0].Hand);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(formerWhitebeard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(formerWhitebeard, state.Players[0].Deck[0]);
        Assert.Single(state.Players[0].Hand);
    }

    [Fact]
    public async Task UF054_OP13_064_BonusesExpireAfterNextOpponentTurn()
    {
        var state = TestScene.New().MyCharacter("OP13-064").MyActiveDon(3)
            .OppCharacter("OP15-003").Build();
        state.TurnCount = 10;
        var roger = Assert.Single(state.Players[0].Characters);
        var opponent = Assert.Single(state.Players[1].Characters);

        await EffectRuntime.Resolve(state, 0, roger, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(2000, Assert.Single(state.Players[0].Leader.PowerModsUntilOppEnd).Delta);
        Assert.Equal(-2000, Assert.Single(opponent.PowerModsUntilOppEnd).Delta);

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Single(state.Players[0].Leader.PowerModsUntilOppEnd);
        Assert.Single(opponent.PowerModsUntilOppEnd);

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(state.Players[0].Leader.PowerModsUntilOppEnd);
        Assert.Empty(opponent.PowerModsUntilOppEnd);
    }
}
