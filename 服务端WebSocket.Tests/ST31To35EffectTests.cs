using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class ST31To35EffectTests
{
    [Fact]
    public void AllTwentyFiveCardsHaveRegisteredImplementations()
    {
        for (int set = 31; set <= 35; set++)
        for (int index = 1; index <= 5; index++)
        {
            string number = $"ST{set}-{index:000}";
            Assert.NotNull(CardDatabase.Get(number));
            Assert.NotNull(ScriptedEffectRegistry.TryGet(number));
        }
    }

    [Fact]
    public async Task ST31_003_BlockerAndPowerAreConditionalDuringOpponentTurn()
    {
        var s = TestScene.New().Build();
        s.TurnCount = 2;
        var brook = new CardInstance { Info = CardDatabase.Get("ST31-003")!, TurnPlayed = 1 };
        s.Players[0].Characters.Add(brook);

        await EffectRuntime.Resolve(s, 0, brook, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(ActionValidator.HasKeyword(s, brook, "阻挡者"));

        for (int i = 0; i < 3; i++)
            s.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = s.Players[0].Leader.Id });
        s.CurrentTurnPlayer = 1;

        Assert.True(ActionValidator.HasKeyword(s, brook, "阻挡者"));
        Assert.Equal(brook.Info.Power + 3000, s.CurrentPowerOf(0, brook));
    }

    [Fact]
    public async Task ST32_004_RushCharacterOnlyWorksAgainstCharactersOnPlayTurn()
    {
        var s = TestScene.New(myLeaderNumber: "OP01-001")
            .OppCharacter("ST31-003")
            .Build();
        s.TurnCount = 2;
        var rayleigh = new CardInstance { Info = CardDatabase.Get("ST32-004")!, TurnPlayed = 2 };
        s.Players[0].Characters.Add(rayleigh);
        s.Players[1].Characters[0].IsTapped = true;

        await EffectRuntime.Resolve(s, 0, rayleigh, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        Assert.True(ActionValidator.CanAttack(s, 0, rayleigh.Id, false, s.Players[1].Characters[0].Id).Ok);
        Assert.False(ActionValidator.CanAttack(s, 0, rayleigh.Id, true, null).Ok);
    }

    [Fact]
    public async Task ST32_002_DrawsBeforeApplyingTheRestRestriction()
    {
        var s = TestScene.New()
            .MyDeckTop("ST31-003")
            .OppCharacter("ST31-003")
            .Build();
        var oden = new CardInstance { Info = CardDatabase.Get("ST32-002")! };
        s.Players[0].Characters.Add(oden);
        int handBefore = s.Players[0].Hand.Count;

        await EffectRuntime.Resolve(s, 0, oden, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        Assert.Equal(handBefore + 1, s.Players[0].Hand.Count);
        Assert.Empty(s.Players[1].Characters[0].Restrictions);
    }

    [Fact]
    public async Task ST33_004_DiscountTracksEffectDiscardButNotCostsAndClearsAtEndPhase()
    {
        var s = TestScene.New()
            .MyHandAdd("ST31-003")
            .Build();
        var koby = new CardInstance { Info = CardDatabase.Get("ST33-001")! };
        s.Players[0].Characters.Add(koby);
        await EffectRuntime.Resolve(s, 0, koby, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(s.Players[0].HandDiscardedByEffectThisTurn);

        s.Players[0].Hand.Add(new CardInstance { Info = CardDatabase.Get("ST31-003")! });
        s.Players[0].Deck.Add(new CardInstance { Info = CardDatabase.Get("ST31-003")! });
        s.Players[0].Deck.Add(new CardInstance { Info = CardDatabase.Get("ST31-003")! });
        var don = new DonCard { State = DonState.Active };
        s.Players[0].CostArea.Add(don);
        var kinemon = new CardInstance { Info = CardDatabase.Get("ST32-001")! };
        s.Players[0].Characters.Add(kinemon);

        await EffectRuntime.Resolve(s, 0, kinemon, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(don.Id.ToString()));
        Assert.True(s.Players[0].HandDiscardedByEffectThisTurn);

        var borsalino = new CardInstance { Info = CardDatabase.Get("ST33-004")! };
        s.Players[0].Hand.Add(borsalino);
        Assert.Equal(3, s.HandPlayCost(0, borsalino));

        TurnEngine.EnterEndPhase(s);
        Assert.False(s.Players[0].HandDiscardedByEffectThisTurn);
        Assert.Equal(6, s.HandPlayCost(0, borsalino));
    }

    [Fact]
    public async Task ST34_001_RefreshesAtMostTwoDonOncePerTurn()
    {
        var s = TestScene.New(myLeaderNumber: "ST07-001").Build();
        s.TurnCount = 2;
        var katakuri = new CardInstance { Info = CardDatabase.Get("ST34-001")! };
        s.Players[0].Characters.Add(katakuri);
        for (int i = 0; i < 4; i++) s.Players[0].DonDeck.Add(new DonCard());
        var payload = new Dictionary<string, object?> { ["owner"] = 0, ["count"] = 1 };

        await EffectRuntime.Resolve(s, 0, katakuri, EffectTrigger.OnDonReturnedToDeck,
            new MockPromptService(), payload);
        Assert.Equal(2, s.Players[0].RestDonCount);

        await EffectRuntime.Resolve(s, 0, katakuri, EffectTrigger.OnDonReturnedToDeck,
            new MockPromptService(), payload);
        Assert.Equal(2, s.Players[0].RestDonCount);
    }

    [Fact]
    public async Task ST35_005_AddsPermanentCostAndPlaysRevolutionaryFromTrash()
    {
        var s = TestScene.New().Build();
        s.TurnCount = 2;
        var kuma = new CardInstance { Info = CardDatabase.Get("ST35-005")! };
        s.Players[0].Characters.Add(kuma);
        var haku = new CardInstance { Info = CardDatabase.Get("ST35-001")! };
        s.Players[0].Trash.Add(haku);
        var restDon = new DonCard { State = DonState.Rest };
        s.Players[0].CostArea.Add(restDon);

        await EffectRuntime.Resolve(s, 0, kuma, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(restDon.Id.ToString()).QueueChoose(haku.Id.ToString()));

        Assert.Equal(8, s.CurrentCostOf(0, kuma));
        Assert.Contains(haku, s.Players[0].Characters);
        Assert.DoesNotContain(haku, s.Players[0].Trash);
        Assert.Equal(1, s.Players[0].AttachedDonCount(s.Players[0].Leader.Id));
    }
}
