using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-16 QQ 群与游戏内反馈的卡效、开局流程回归。</summary>
public class QqFeedback20260816RegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ST29_015_CounterBoostsOwnCardAndReducesOpponentAtOneLifeOrLess()
    {
        var state = TestScene.New().OppCharacter("OP15-050").Build();
        var ownLeader = state.Players[0].Leader;
        var opponent = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueChoose(ownLeader.Id.ToString())
            .QueueChoose(opponent.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("ST29-015"), EffectTrigger.EventCounter, prompts);

        Assert.Equal(2000, ownLeader.PowerModThisBattle);
        Assert.Equal(-2000, opponent.PowerModThisBattle);
        Assert.Equal(2, prompts.ChooseHistory.Count);
    }

    [Fact]
    public async Task EB04_040_CounterReturnsOneDonBeforeBoostingLeader()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var don = Assert.Single(state.Players[0].CostArea);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("EB04-040"), EffectTrigger.EventCounter, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.Equal(4000, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task EB04_053_DrawsOnlyWhenSentomaruItselfBlocks()
    {
        var state = TestScene.New().MyCharacter("EB04-053").MyCharacter("OP15-003").Build();
        var sentomaru = state.Players[0].Characters[0];
        var otherBlocker = state.Players[0].Characters[1];
        state.Players[0].Deck.Add(Card("OP15-004"));
        state.CurrentBattle = BattleWithBlocker(state, otherBlocker);

        await BattleEngine.TriggerBlockDeclareAsync(state, new MockPromptService());

        Assert.Empty(state.Players[0].Hand);

        state.CurrentBattle = BattleWithBlocker(state, sentomaru);
        await BattleEngine.TriggerBlockDeclareAsync(state, new MockPromptService());

        Assert.Single(state.Players[0].Hand);
    }

    [Fact]
    public async Task P_107_LeaderBoostRemainsAfterRogerLeavesField()
    {
        var state = TestScene.New().MyActiveDon(10).Build();
        var roger = Card("P-107");
        state.Players[0].Characters.Add(roger);

        await EffectRuntime.Resolve(state, 0, roger, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(2000, Assert.Single(state.Players[0].Leader.PowerModsUntilOppEnd).Delta);

        await BattleEngine.KOCardAsync(state, 0, roger, new MockPromptService());

        Assert.DoesNotContain(roger, state.Players[0].Characters);
        Assert.Equal(2000, Assert.Single(state.Players[0].Leader.PowerModsUntilOppEnd).Delta);
    }

    [Fact]
    public async Task OP17_063_CannotActivateAfterItsEntryTurn()
    {
        var state = TestScene.New("OP17-039").MyActiveDon(1).OppCharacter("OP17-011").Build();
        state.TurnCount = 4;
        var ged = Card("OP17-063");
        ged.TurnPlayed = 2;
        state.Players[0].Characters.Add(ged);
        var don = Assert.Single(state.Players[0].CostArea);
        var victim = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueChoose(don.Id.ToString())
            .QueueChoose(victim.Id.ToString());

        await EffectRuntime.Resolve(state, 0, ged, EffectTrigger.ActivatedMain, prompts);

        Assert.False(victim.IsEffectsNullified);
        Assert.Contains(victim, state.Players[1].Characters);
        Assert.Contains(don, state.Players[0].CostArea);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP17_099_FirstOptionRequiresSecondDiscardBeforeAddingLife()
    {
        var state = TestScene.New("OP17-099").MyDeckTop("OP17-100").Build();
        var activationDiscard = Card("OP17-101");
        var kept = Card("OP17-102");
        state.Players[0].Hand.AddRange([activationDiscard, kept]);
        var lifeCard = Assert.Single(state.Players[0].Deck);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(false)
            .QueueConfirm(true)
            .QueueChoose(activationDiscard.Id.ToString())
            .QueueOption(0);

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader,
            EffectTrigger.OnAttackDeclare, prompts);

        Assert.Contains(kept, state.Players[0].Hand);
        Assert.Contains(activationDiscard, state.Players[0].Trash);
        Assert.DoesNotContain(lifeCard, state.Players[0].LifeArea);
        Assert.Contains(lifeCard, state.Players[0].Deck);
    }

    [Fact]
    public async Task OP17_113_AllowsReorderingUnselectedCardsToDeckBottom()
    {
        var state = TestScene.New().MyDeckTop("OP17-114", "OP17-115", "OP17-116").Build();
        var selected = state.Players[0].Deck[0];
        var middle = state.Players[0].Deck[1];
        var last = state.Players[0].Deck[2];
        var prompts = new MockPromptService()
            .QueueChoose(selected.Id.ToString())
            .QueueChoose(last.Id.ToString(), middle.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP17-113"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(selected, state.Players[0].Hand);
        Assert.Equal(new[] { last, middle }, state.Players[0].Deck);
        var reorder = Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "ReorderToDeckBottom"));
        Assert.Equal(0, reorder.min);
        Assert.True(Assert.IsType<bool>(reorder.extra!["allowDefaultOrder"]));
    }

    [Fact]
    public async Task OP09_004_ReducesOnlyOpponentCharacters()
    {
        var state = TestScene.New().OppCharacter("OP15-050").Build();
        var shanks = Card("OP09-004");
        state.Players[0].Characters.Add(shanks);

        await EffectRuntime.Resolve(state, 0, shanks, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(0, state.Players[0].Leader));
        Assert.Equal(0, state.ContinuousPowerBonus(0, shanks));
        Assert.Equal(0, state.ContinuousPowerBonus(1, state.Players[1].Leader));
        Assert.Equal(-1000, state.ContinuousPowerBonus(1, state.Players[1].Characters[0]));
    }

    [Fact]
    public async Task OP15_119_AlsoRespondsWhenOpponentPlaysCounterEvent()
    {
        _ = TestScene.New().Build();
        string deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("counter-event-watcher", ("s0", "p0", deck), ("s1", "p1", deck), 0, 7);
        var state = engine.State;
        var luffy = Card("OP15-119");
        state.Players[0].Characters.Clear();
        state.Players[0].Characters.Add(luffy);
        state.Players[0].LifeArea.Clear();
        state.Players[0].LifeArea.Add(Card("OP17-100"));
        state.Players[1].Hand.Clear();
        state.Players[1].Hand.Add(Card("OP17-018"));
        state.Players[1].CostArea.Clear();
        state.Players[1].CostArea.Add(new DonCard { State = DonState.Active });
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;

        BattleEngine.StartAttack(state, state.Players[0].Leader.Id, targetIsLeader: true, targetId: null);
        await BattleEngine.TriggerAttackDeclareAsync(state, new MockPromptService());
        BattleEngine.PassBlock(state);
        Assert.True(engine.HandleAction(1, "PlayCounter", JsonSerializer.SerializeToElement(new
        {
            handIndex = 0,
            useCounterIcon = false,
        })));
        await engine.WaitSettledAsync();

        Assert.Equal(7000, luffy.PowerModThisTurn);
    }

    [Fact]
    public async Task OP13_079_OnlineOpeningPromptsBeforeDiceAndOpeningHand()
    {
        _ = TestScene.New().Build();
        string imuDeck = "OP13-079\n" + string.Join('\n', Enumerable.Repeat("OP13-099", 4))
            + "\n" + string.Join('\n', Enumerable.Repeat("OP13-080", 6));
        string otherDeck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("deferred-online-opening", ("s0", "p0", imuDeck), ("s1", "p1", otherDeck),
            firstPlayer: -1, rngSeed: 17, deferOpeningSetupUntilFirstPlayerChosen: true,
            deferInitialSetupUntilStart: true);

        engine.BroadcastInitialState();

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal(0, prompt.PlayerIndex);
        Assert.Empty(engine.State.StartingDiceRounds);
        Assert.Empty(engine.State.Players[0].Hand);
        Assert.Empty(engine.State.Players[0].LifeArea);

        Assert.True(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { prompt.ValidChoices[0] },
        })));
        await engine.WaitSettledAsync(resolvingPromptId: prompt.PromptId);

        Assert.Equal("OP13-099", engine.State.Players[0].StageCard?.Info.Number);
        Assert.NotEmpty(engine.State.StartingDiceRounds);
        Assert.False(engine.State.StartingPlayerChosen);
        Assert.Empty(engine.State.Players[0].Hand);
        Assert.Empty(engine.State.Players[0].LifeArea);
    }

    private static BattleContext BattleWithBlocker(GameState state, CardInstance blocker)
        => new()
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = false,
            TargetCardId = blocker.Id,
            ReplacedByBlockerCardId = blocker.Id,
            BlockerDeclared = true,
        };
}
