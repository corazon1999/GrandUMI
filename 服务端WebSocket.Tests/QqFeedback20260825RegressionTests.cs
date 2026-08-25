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

    [Fact]
    public void Q365_OP09_078_CounterOnlyEventCannotBePlayedDuringOwnMain()
    {
        var state = TestScene.New("OP09-001")
            .MyActiveDon(2)
            .MyHandAdd("OP09-078")
            .Build();
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var me = state.Players[0];
        var eventCard = me.Hand.Single();

        var result = ActionValidator.CanPlayCard(state, 0, 0);

        Assert.False(result.Ok);
        Assert.Contains("主要", result.Reason);
        Assert.Contains(eventCard, me.Hand);
        Assert.Equal(2, me.ActiveDonCount);
        Assert.Empty(me.Trash);
    }

    [Theory]
    [InlineData("OP12-038")]
    [InlineData("OP12-037")]
    public void Q365_EventWithMainModeRemainsPlayableDuringOwnMain(string number)
    {
        var state = TestScene.New()
            .MyActiveDon(2)
            .MyHandAdd(number)
            .Build();
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var card = state.Players[0].Hand.Single();

        Assert.Contains("EventMain", card.Info.EffectTags);
        Assert.True(ActionValidator.CanPlayCard(state, 0, 0).Ok);
    }

    [Fact]
    public async Task Q406_OP06_033_CanSelectRestedOP15_030AfterPayingValidCost()
    {
        var state = TestScene.New()
            .MyHandAdd("OP06-023")
            .OppCharacter("OP15-030")
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("OP06-033");
        me.Characters.Add(source);
        var cost = me.Hand.Single();
        var target = opponent.Characters.Single();
        target.IsTapped = true;
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(cost.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var targetPrompt = Assert.Single(
            prompts.ChooseHistory,
            prompt => prompt.kind == "OpponentRestingCharacter");
        Assert.Contains(target.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(cost, me.Hand);
        Assert.Contains(cost, me.Trash);
        Assert.DoesNotContain(target, opponent.Characters);
        Assert.Contains(target, opponent.Trash);
    }

    [Fact]
    public async Task Q416_OP12_022_CanActivateWithNoSeaKingInHandAndNoTarget()
    {
        var state = TestScene.New()
            .MyCharacter("OP12-022")
            .Build();
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var me = state.Players[0];
        var inuarashi = me.Characters.Single();
        var prompts = new MockPromptService();

        Assert.Empty(me.Hand);
        Assert.True(ActionValidator.CanUseEffect(state, 0, inuarashi.Id).Ok);

        await EffectRuntime.Resolve(state, 0, inuarashi, EffectTrigger.ActivatedMain, prompts);

        Assert.True(inuarashi.IsTapped);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task Q455_Q475_SameNumberTargetsKeepDistinctInstanceIdsThroughPromptResponse()
    {
        var deck = LegalOp17Deck();
        var engine = new GameEngine(
            "qq-same-number-targets",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260826);
        var me = engine.State.Players[0];
        me.Characters.Clear();
        var first = Card("OP17-040");
        var second = Card("OP17-040");
        me.Characters.AddRange([first, second]);
        var firstId = first.Id.ToString();
        var secondId = second.Id.ToString();

        var chooseTask = engine.Prompts.ChooseCards(
            0,
            "OwnCharacter",
            "选择 1 张同名角色",
            [firstId, secondId],
            min: 1,
            max: 1);
        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);

        Assert.NotEqual(firstId, secondId);
        Assert.Equal([firstId, secondId], prompt.ValidChoices);
        using (var choiceCards = JsonDocument.Parse(JsonSerializer.Serialize(prompt.Extra["choiceCards"])))
        {
            var displayed = choiceCards.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, displayed.Length);
            Assert.All(displayed, card => Assert.Equal("OP17-040", card.GetProperty("number").GetString()));
            Assert.Equal([firstId, secondId], displayed.Select(card => card.GetProperty("id").GetString()));
        }

        Assert.True(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { secondId },
        })));
        var chosen = await chooseTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal([secondId], chosen);
        Assert.Null(engine.State.PendingPrompt);
    }

    [Fact]
    public async Task Q488_ST31_001_AttachDonImmediatelyRefreshesRushPowerAndSnapshotOnce()
    {
        const string deck = "OP02-002\nST31-001\nOP15-003";
        var engine = new GameEngine(
            "qq-st31-001-attach-don",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260826);
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        me.Hand.Clear();
        me.Deck.Clear();
        me.Characters.Clear();
        opponent.Characters.Clear();

        var sanji = Card("ST31-001");
        sanji.TurnPlayed = state.TurnCount;
        var garpTarget = Card("OP15-003");
        me.Characters.Add(sanji);
        opponent.Characters.Add(garpTarget);
        me.CostArea.Clear();
        me.CostArea.AddRange([
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
        ]);

        await EffectRuntime.Resolve(
            state, 0, sanji, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.False(ActionValidator.HasKeyword(state, sanji, "速攻"));
        Assert.False(ActionValidator.CanAttack(state, 0, sanji.Id, true, null).Ok);
        Assert.True(engine.HandleAction(0, "AttachDon", JsonSerializer.SerializeToElement(new
        {
            targetId = sanji.Id.ToString(),
            count = 2,
        })));

        for (var index = 0; index < 100 && state.PendingPrompt is null; index++)
            await Task.Delay(10);
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal([garpTarget.Id.ToString()], prompt.ValidChoices);
        Assert.True(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { garpTarget.Id.ToString() },
        })));
        await engine.WaitSettledAsync();

        Assert.Equal(2, me.AttachedDonCount(sanji.Id));
        Assert.Equal(sanji.Info.Power + 2_000, state.CurrentPowerOf(0, sanji));
        Assert.True(ActionValidator.HasKeyword(state, sanji, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, sanji.Id, true, null).Ok);
        Assert.Equal(-1, garpTarget.CostModThisTurn);
        Assert.Null(state.PendingPrompt);

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 0)));
        var sanjiSnapshot = snapshot.RootElement.GetProperty("my").GetProperty("fieldCards")
            .EnumerateArray().Single(card => card.GetProperty("id").GetString() == sanji.Id.ToString());
        Assert.Equal(sanji.Info.Power + 2_000, sanjiSnapshot.GetProperty("powerCurrent").GetInt32());
        Assert.Contains("速攻", sanjiSnapshot.GetProperty("gainedKeywords")
            .EnumerateArray().Select(keyword => keyword.GetString()));
        Assert.True(sanjiSnapshot.GetProperty("canAttack").GetBoolean());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void Q488_InvalidAttachDonCountIsRejectedWithoutPartialMutation(int count)
    {
        const string deck = "OP02-002\nST31-001";
        var engine = new GameEngine(
            $"qq-invalid-attach-don-{count}",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260826);
        var state = engine.State;
        var me = state.Players[0];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        me.CostArea.Clear();
        me.CostArea.AddRange([
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
        ]);

        var accepted = engine.HandleAction(0, "AttachDon", JsonSerializer.SerializeToElement(new
        {
            targetId = "leader",
            count,
        }));

        Assert.False(accepted);
        Assert.Equal(2, me.ActiveDonCount);
        Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
        Assert.Null(state.PendingPrompt);
    }

    private static bool LeaderOncePerTurnAvailable(GameState state)
    {
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        return snapshot.GetProperty("my").GetProperty("leaderOncePerTurnEffectAvailable").GetBoolean();
    }

    private static string LegalOp17Deck()
    {
        var leader = CardDatabase.Get("OP17-039")!;
        var pool = CardDatabase.GetBySet("OP17")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leader.Number };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
            lines.Add(card.Number);
        }

        return string.Join('\n', lines);
    }
}
