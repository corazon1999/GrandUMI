using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Collections.Concurrent;
using System.Text.Json;
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
    public async Task ST31_005_Search_RevealsOnlyAddedCardToOpponent()
    {
        const string deck = "OP01-001\nST31-005";
        var engine = new GameEngine("st31-005-search-reveal",
            ("s0", "alice", deck), ("s1", "bob", deck), 0, 31);
        var opponentMessages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 1) opponentMessages.Enqueue(JsonSerializer.Serialize(payload));
        };

        var selected = new CardInstance { Info = CardDatabase.Get("ST31-003")! };
        var ineligible = new CardInstance { Info = CardDatabase.Get("ST35-005")! };
        Assert.True(selected.Info.HasKeyword("草帽一伙"));
        Assert.False(ineligible.Info.HasKeyword("草帽一伙"));

        var player = engine.State.Players[0];
        player.Deck.Clear();
        player.Deck.AddRange(new[] { selected, ineligible });
        var source = new CardInstance { Info = CardDatabase.Get("ST31-005")! };

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, source, EffectTrigger.OnEnterField, engine.Prompts);

        for (int i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal(new[] { selected.Id.ToString() }, prompt.ValidChoices);
        using (var choiceCards = JsonDocument.Parse(JsonSerializer.Serialize(prompt.Extra["choiceCards"])))
            Assert.Equal(2, choiceCards.RootElement.GetArrayLength());

        engine.Prompts.Resolve(prompt.PromptId, new[] { selected.Id.ToString() });
        await resolveTask;

        Assert.Contains(selected, player.Hand);
        var revealMessages = opponentMessages
            .Select(message => JsonDocument.Parse(message))
            .Where(document => document.RootElement.GetProperty("lastAction").GetString() == "RevealCards")
            .ToList();
        var revealMessage = Assert.Single(revealMessages);
        var revealedNumbers = revealMessage.RootElement
            .GetProperty("reveal")
            .GetProperty("cardNumbers")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToList();
        Assert.Equal(new[] { selected.Info.Number }, revealedNumbers);

        foreach (var document in revealMessages) document.Dispose();
    }

    [Fact]
    public async Task ST31_005_ActivatedMain_DoesNotTreatLuffyAceLeaderAsLuffy()
    {
        var s = TestScene.New(myLeaderNumber: "ST30-001")
            .MyCharacter("OP01-024")
            .Build();
        var me = s.Players[0];
        var luffy = Assert.Single(me.Characters);
        var source = new CardInstance { Info = CardDatabase.Get("ST31-005")! };
        me.StageCard = source;
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService().QueueChoose(luffy.Id.ToString());

        await EffectRuntime.Resolve(s, 0, source, EffectTrigger.ActivatedMain, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnLeaderOrCharacter", prompt.kind);
        Assert.Equal(new[] { luffy.Id.ToString() }, prompt.choices);
        Assert.DoesNotContain(me.Leader.Id.ToString(), prompt.choices);
        Assert.True(source.IsTapped);
        Assert.Equal(1, me.AttachedDonCount(luffy.Id));
        Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
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
        s.TurnCount = 3;
        var rayleigh = new CardInstance { Info = CardDatabase.Get("ST32-004")!, TurnPlayed = 3 };
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
    public async Task ST32_002_SelectedCharacterCannotAttackBlockOrBeRestedByEffect()
    {
        var s = TestScene.New()
            .MyDeckTop("ST31-003")
            .OppCharacter("ST31-003")
            .Build();
        var oden = new CardInstance { Info = CardDatabase.Get("ST32-002")! };
        var target = s.Players[1].Characters[0];
        s.Players[0].Characters.Add(oden);

        await EffectRuntime.Resolve(s, 0, oden, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));

        AtomicOps.RestCard(target);
        Assert.False(target.IsTapped);

        target.GainedKeywords.Add(new TemporaryKeyword
        {
            Keyword = "阻挡者",
            Duration = KeywordDuration.ThisTurn,
        });
        s.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = oden.Id,
            DefenderPlayerIndex = 1,
            TargetIsLeader = true,
        };
        s.Phase = Phase.BattleBlock;
        Assert.False(ActionValidator.CanDeclareBlocker(s, 1, target.Id).Ok);

        s.CurrentBattle = null;
        s.CurrentTurnPlayer = 1;
        s.TurnCount = 3;
        s.Phase = Phase.Main;
        Assert.False(ActionValidator.CanAttack(s, 1, target.Id, true, null).Ok);
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
    public async Task ST32_001_DonChoicesCarryDonDisplayMetadata()
    {
        var s = TestScene.New().Build();
        var don = new DonCard { State = DonState.Active };
        s.Players[0].CostArea.Add(don);
        var kinemon = new CardInstance { Info = CardDatabase.Get("ST32-001")! };
        s.Players[0].Characters.Add(kinemon);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(s, 0, kinemon, EffectTrigger.OnEnterField, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnLeaderOrDon", prompt.kind);
        var donChoices = Assert.IsAssignableFrom<IEnumerable<object>>(prompt.extra!["donChoices"]);
        var choice = Assert.Single(donChoices);
        Assert.Equal(don.Id.ToString(), choice.GetType().GetProperty("id")!.GetValue(choice));
        Assert.Equal("Active", choice.GetType().GetProperty("state")!.GetValue(choice));
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
