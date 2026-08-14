using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-13 玩家集中反馈的规则回归测试。</summary>
public class August2026PlayerBugRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ST36_005_OpponentAttack_CanRedirectToBasePowerFiveThousandKid()
    {
        var state = TestScene.New().Build();
        var kid = Card("ST36-005");
        state.Players[0].Characters.Add(kid);
        state.Players[0].LifeArea.Add(Card("OP15-003"));
        state.Players[0].LifeArea[0].IsLifeFaceUp = true;
        state.CurrentTurnPlayer = 1;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = true,
        };
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(kid.Id.ToString());

        await EffectRuntime.Resolve(state, 0, kid, EffectTrigger.OnOppAttackDeclare, prompts);

        Assert.False(state.CurrentBattle.TargetIsLeader);
        Assert.Equal(kid.Id, state.CurrentBattle.TargetCardId);
        Assert.False(state.Players[0].LifeArea[0].IsLifeFaceUp);
    }

    [Fact]
    public async Task OP01_055_RestCost_OnlyOffersOwnActiveCharacters()
    {
        var state = TestScene.New().MyCharacter("OP15-003").MyCharacter("OP15-004")
            .MyDeckTop("OP15-005", "OP15-006").Build();
        var me = state.Players[0];
        me.StageCard = Card("OP09-099");
        me.CostArea.Add(new DonCard { State = DonState.Active });
        var ids = me.Characters.Select(card => card.Id.ToString()).ToArray();
        var prompts = new MockPromptService().QueueChoose(ids);

        await EffectRuntime.Resolve(state, 0, Card("OP01-055"), EffectTrigger.EventMain, prompts);

        var costPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnActiveCharacter", costPrompt.kind);
        Assert.Equal(ids.Order(), costPrompt.choices.Order());
        Assert.All(me.Characters, card => Assert.True(card.IsTapped));
        Assert.Equal(2, me.Hand.Count);
    }

    [Fact]
    public void OP13_003_DoesNotAttachDonAtZeroOrTenDon()
    {
        var firstTurn = TestScene.New("OP13-003").Build();
        firstTurn.FirstPlayer = 0;
        firstTurn.CurrentTurnPlayer = 0;
        firstTurn.TurnCount = 1;
        for (int i = 0; i < 10; i++) firstTurn.Players[0].DonDeck.Add(new DonCard());
        TurnEngine.EnterDonPhase(firstTurn);
        Assert.Single(firstTurn.Players[0].CostArea);
        Assert.Equal(0, firstTurn.Players[0].AttachedDonCount(firstTurn.Players[0].Leader.Id));

        var fullDon = TestScene.New("OP13-003").Build();
        fullDon.CurrentTurnPlayer = 0;
        fullDon.TurnCount = 3;
        for (int i = 0; i < 10; i++) fullDon.Players[0].CostArea.Add(new DonCard { State = DonState.Active });
        fullDon.Players[0].DonDeck.Add(new DonCard());
        TurnEngine.EnterDonPhase(fullDon);
        Assert.Equal(10, fullDon.Players[0].CostArea.Count);
        Assert.Equal(0, fullDon.Players[0].AttachedDonCount(fullDon.Players[0].Leader.Id));
    }

    [Fact]
    public async Task OP05_098_LastLifeBanished_StillRestoresLife()
    {
        var engine = EnelEngine("enel-banish");
        var state = engine.State;
        var me = state.Players[0];
        var attacker = Card("OP04-014");
        state.Players[1].Characters.Add(attacker);
        state.CurrentTurnPlayer = 1;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = attacker.Id,
            TargetIsLeader = true,
        };

        await LifeRevealManager.DealDamageToLeader(engine, 0, 1);

        Assert.Single(me.LifeArea);
        Assert.Contains(me.Trash, card => card.Info.Number == "OP15-003");
    }

    [Fact]
    public async Task OP05_098_DoubleDamage_StopsAfterLifeZeroTriggerRestoresLife()
    {
        var engine = EnelEngine("enel-double");
        var me = engine.State.Players[0];

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 2);
        for (int i = 0; i < 100 && !damage.IsCompleted; i++)
        {
            if (engine.State.PendingPrompt is { } prompt)
                engine.Prompts.Resolve(prompt.PromptId, prompt.ValidChoices.Take(1).ToArray());
            await Task.Delay(5);
        }
        await damage;

        Assert.Single(me.LifeArea);
        Assert.Equal("OP15-004", me.LifeArea[0].Info.Number);
        Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task ST23_002_BonusExpiresAtOpponentEndPhase()
    {
        var state = TestScene.New("ST23-001").Build();
        var leader = state.Players[0].Leader;

        await EffectRuntime.Resolve(state, 0, Card("ST23-002"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(leader.Info.Power + 2000, state.CurrentPowerOf(0, leader));
        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(leader.Info.Power + 2000, state.CurrentPowerOf(0, leader));
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(leader.Info.Power, state.CurrentPowerOf(0, leader));
    }

    [Fact]
    public async Task ST26_005_LeaderPowerStaysSevenThousandDuringOpponentTurn()
    {
        var state = TestScene.New("OP01-003")
            .MyActiveDon(2)
            .OppActiveDon(5)
            .Build();
        var leader = state.Players[0].Leader;

        await EffectRuntime.Resolve(
            state, 0, Card("ST26-005"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(7000, state.CurrentPowerOf(0, leader));
        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(7000, state.CurrentPowerOf(0, leader));
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterResetPhase(state);
        Assert.Equal(7000, state.CurrentPowerOf(0, leader));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(leader.Info.Power, state.CurrentPowerOf(0, leader));
    }

    [Fact]
    public async Task OP07_059_CostCanBePaidBeforeThreeFoxyCharactersCondition()
    {
        var state = TestScene.New("OP07-059").MyActiveDon(3).Build();
        var ids = state.Players[0].CostArea.Select(don => don.Id.ToString()).ToArray();
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(ids);

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Equal(3, state.Players[0].DonDeck.Count);
    }

    [Fact]
    public async Task OP07_059_AutoLocksRestedLeaderAndCanLockOneCharacter()
    {
        var state = TestScene.New("OP07-059").MyActiveDon(3)
            .MyCharacter("EB04-037").MyCharacter("EB04-037").MyCharacter("EB04-037")
            .OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var opp = state.Players[1];
        opp.Leader.IsTapped = true;
        opp.Characters[0].IsTapped = true;
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(me.CostArea.Select(don => don.Id.ToString()).ToArray())
            .QueueChoose(opp.Characters[0].Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.True(opp.Leader.CannotActivateNextReset);
        Assert.True(opp.Characters[0].CannotActivateNextReset);
        Assert.DoesNotContain(prompts.ChooseHistory, prompt => prompt.kind == "OpponentLeader");
    }

    [Fact]
    public async Task OP14_108_UsesBasePowerEvenWhenTargetPowerIsBuffed()
    {
        var state = TestScene.New("OP15-001").OppCharacter("EB01-012").Build();
        var target = state.Players[1].Characters.Single();
        target.PowerModThisTurn = 12000;
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-108"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP09_099_CanSearchOP09_089()
    {
        var state = TestScene.New().MyHandAdd("OP15-003")
            .MyDeckTop("OP09-089", "OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var stage = Card("OP09-099");
        me.StageCard = stage;
        var target = me.Deck[0];
        var prompts = new MockPromptService()
            .QueueChoose(me.Hand[0].Id.ToString())
            .QueueChoose(target.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(me.Hand, card => card.Info.Number == "OP09-089");
        Assert.Contains(target.Id.ToString(), prompts.ChooseHistory.Single(p => p.kind == "LookTopReveal").choices);
    }

    [Fact]
    public async Task OP09_086_UsesLiveTrashCount_AndOpponentSnapshotShowsTrash()
    {
        var state = TestScene.New("OP09-081").Build();
        var burgess = Card("OP09-086");
        state.Players[0].Characters.Add(burgess);
        for (int i = 0; i < 4; i++) state.Players[0].Trash.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(state, 0, burgess, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(6000, state.CurrentPowerOf(0, burgess));
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 1));
        Assert.Equal(4, snapshot.GetProperty("opponent").GetProperty("trashNumbers").GetArrayLength());
    }

    [Fact]
    public async Task EB01_009_CounterPlaysAnimalFromDeckInsteadOfAddingToHand()
    {
        var state = TestScene.New().MyDeckTop("OP09-089", "OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var animal = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(animal.Id.ToString()).QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, Card("EB01-009"), EffectTrigger.EventCounter, prompts);

        Assert.Contains(animal, me.Characters);
        Assert.DoesNotContain(animal, me.Hand);
    }

    [Fact]
    public async Task ST30_014_SingleTargetReceivesAtMostTwoRestDon()
    {
        var state = TestScene.New().MyCharacter("ST30-014").MyCharacter("EB01-012").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var target = me.Characters[1];
        for (int i = 0; i < 4; i++) me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(2, me.AttachedDonCount(target.Id));
        Assert.Equal(2, me.RestDonCount);
        Assert.Single(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP07_072_SearchIncludesItselfAndEB04_037()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var me = state.Players[0];
        var source = Card("OP07-072");
        me.Characters.Add(source);
        me.Deck.Add(Card("OP07-072"));
        me.Deck.Add(Card("EB04-037"));
        me.Deck.Add(Card("OP15-003"));
        var searchedSelf = me.Deck[0];
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(me.CostArea[0].Id.ToString())
            .QueueChoose(searchedSelf.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var search = prompts.ChooseHistory.Single(prompt => prompt.kind == "LookTopReveal");
        Assert.Contains(searchedSelf.Id.ToString(), search.choices);
        Assert.Contains(me.Deck.Concat(me.Hand).Single(card => card.Info.Number == "EB04-037").Id.ToString(), search.choices);
    }

    [Fact]
    public async Task EB03_041_BuffsBladeCharactersButNotLeader()
    {
        var state = TestScene.New("OP11-001").Build();
        var peacock = Card("EB03-041");
        state.Players[0].Characters.Add(peacock);
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(state, 0, peacock, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(peacock.Info.Power + 2000, state.CurrentPowerOf(0, peacock));
        Assert.Equal(state.Players[0].Leader.Info.Power, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task EB04_046_CostAura_OnlyAffectsOwnFieldCharacters()
    {
        var state = TestScene.New("OP02-093")
            .MyCharacter("OP02-114").OppCharacter("OP02-114").MyHandAdd("OP02-114").Build();
        var dol = Card("EB04-046");
        state.Players[0].Characters.Add(dol);
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(state, 0, dol, EffectTrigger.OnEnterField, new MockPromptService());

        var ownNavy = state.Players[0].Characters.First(card => card.Info.Number == "OP02-114");
        var opponentNavy = state.Players[1].Characters.Single();
        var handNavy = state.Players[0].Hand.Single();
        Assert.Equal(ownNavy.Info.Cost + 2, state.CurrentCostOf(0, ownNavy));
        Assert.Equal(opponentNavy.Info.Cost, state.CurrentCostOf(1, opponentNavy));
        Assert.Equal(handNavy.Info.Cost, state.HandPlayCost(0, handNavy));
    }

    [Fact]
    public async Task OP15_114_FlipsFaceDownTopLifeAndCannotPayWithFaceUpLife()
    {
        var state = TestScene.New().MyDeckTop("OP15-003").OppCharacter("OP13-082").Build();
        var me = state.Players[0];
        me.LifeArea.Add(Card("OP15-003"));
        var source = Card("OP15-114");
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.True(me.LifeArea[0].IsLifeFaceUp);
        Assert.Equal(-2000, state.Players[1].Characters.Single().PowerModThisTurn);

        var faceUpState = TestScene.New().OppCharacter("OP13-082").Build();
        faceUpState.Players[0].LifeArea.Add(Card("OP15-003"));
        faceUpState.Players[0].LifeArea[0].IsLifeFaceUp = true;
        var unavailable = new MockPromptService().QueueConfirm(true);
        await EffectRuntime.Resolve(faceUpState, 0, Card("OP15-114"), EffectTrigger.OnEnterField, unavailable);
        Assert.Empty(unavailable.ConfirmHistory);
        Assert.Equal(0, faceUpState.Players[1].Characters.Single().PowerModThisTurn);
    }

    [Fact]
    public async Task OP13_099_PassiveRegistersOnEnterAndActiveMainSearchesBlackFiveElders()
    {
        var state = TestScene.New().MyActiveDon(7).MyHandAdd("OP13-083").Build();
        var me = state.Players[0];
        for (int index = 0; index < 19; index++) me.Trash.Add(Card("OP15-003"));
        var stage = Card("OP13-099");
        me.StageCard = stage;
        state.CurrentTurnPlayer = 0;

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(me.Leader.Info.Power + 1000, state.CurrentPowerOf(0, me.Leader));

        var candidate = me.Hand.Single();
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(candidate.Id.ToString());
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);
        Assert.Contains(candidate.Id.ToString(), prompts.ChooseHistory.Single().choices);
        Assert.Contains(candidate, me.Characters);
        Assert.True(stage.IsTapped);
    }

    [Fact]
    public void OP08_044_CostIsFour_AndST22_009HasBlocker()
    {
        Assert.Equal(4, CardDatabase.Get("OP08-044")!.Cost);
        Assert.Contains("阻挡者", CardDatabase.Get("ST22-009")!.Abilities);
    }

    [Fact]
    public async Task OP09_077_UsesCurrentPowerForKO()
    {
        var state = TestScene.New().MyActiveDon(2).OppCharacter("OP10-030").Build();
        var target = state.Players[1].Characters.Single();
        target.PowerModThisTurn = 6000 - target.Info.Power;
        var prompts = new MockPromptService()
            .QueueChoose(state.Players[0].CostArea.Select(don => don.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP09-077"), EffectTrigger.EventMain, prompts);

        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP15_077_UsesCurrentPowerForResetLock()
    {
        var state = TestScene.New().MyActiveDon(1).MyDeckTop("OP15-003").OppCharacter("OP10-030").Build();
        var target = state.Players[1].Characters.Single();
        target.IsTapped = true;
        target.PowerModThisTurn = 6000 - target.Info.Power;
        var prompts = new MockPromptService()
            .QueueChoose(state.Players[0].CostArea[0].Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-077"), EffectTrigger.EventMain, prompts);

        Assert.True(target.CannotActivateNextReset);
    }

    [Fact]
    public async Task OP15_001_AppliesMinusTwoThousandDuringOpponentTurn()
    {
        var state = TestScene.New("OP15-001").MyCharacter("EB02-018").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var target = state.Players[1].Characters.Single();
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        state.CurrentTurnPlayer = 1;

        Assert.Equal(target.Info.Power - 2000, state.CurrentPowerOf(1, target));
    }

    [Fact]
    public async Task OP17_103_LifeAdditionIsOptional()
    {
        var state = TestScene.New("OP03-077").MyDeckTop("OP15-003").OppCharacter("OP15-004").Build();
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService().QueueConfirm(false).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP17-103"), EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[0].LifeArea);
        Assert.Equal(-3000, target.PowerModThisTurn);
    }

    [Fact]
    public async Task EB04_007_RushCharacter_CanAttackCharacterButNotLeaderOnPlayTurn()
    {
        var state = TestScene.New().OppCharacter("OP17-065").Build();
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var zoro = Card("EB04-007");
        zoro.TurnPlayed = state.TurnCount;
        state.Players[0].Characters.Add(zoro);
        var target = state.Players[1].Characters.Single();
        target.IsTapped = true;

        await EffectRuntime.Resolve(state, 0, zoro, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.False(ActionValidator.CanAttack(state, 0, zoro.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(state, 0, zoro.Id, false, target.Id).Ok);
    }

    [Fact]
    public async Task DeckZero_ImmediatelyEndsGameExceptNamiAndBrookRules()
    {
        var normal = TestScene.New().MyDeckTop("OP15-003").Build();
        DeckOutRules.Arm(normal);
        TurnEngine.DrawCard(normal, 0, 1);
        Assert.True(normal.IsGameOver);
        Assert.Equal(1, normal.WinnerIndex);

        var nami = TestScene.New("OP03-040").MyDeckTop("OP15-003").Build();
        await EffectRuntime.Resolve(nami, 0, nami.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        DeckOutRules.Arm(nami);
        TurnEngine.DrawCard(nami, 0, 1);
        Assert.True(nami.IsGameOver);
        Assert.Equal(0, nami.WinnerIndex);

        var brook = TestScene.New("OP15-022").Build();
        brook.Players[1].Deck.Add(Card("OP15-003"));
        DeckOutRules.Arm(brook);
        brook.EvaluateDeckOut();
        Assert.False(brook.IsGameOver);
        brook.EvaluateDeckOut(endOfTurn: true);
        Assert.True(brook.IsGameOver);
        Assert.Equal(1, brook.WinnerIndex);
    }

    [Fact]
    public async Task ReorderPrompt_AllowsEmptyResponseToKeepDefaultOrder()
    {
        var state = TestScene.New().MyHandAdd("OP15-006")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var original = state.Players[0].Deck.Select(card => card.Id).ToArray();
        var stage = Card("OP09-099");
        state.Players[0].StageCard = stage;
        var prompts = new MockPromptService()
            .QueueChoose(state.Players[0].Hand[0].Id.ToString())
            .QueueChooseEmpty()
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);

        var reorder = Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "ReorderToDeckBottom"));
        Assert.Equal(0, reorder.min);
        Assert.True(Assert.IsType<bool>(reorder.extra!["allowDefaultOrder"]));
        Assert.Equal(original, state.Players[0].Deck.Select(card => card.Id));
    }

    private static GameEngine EnelEngine(string roomId)
    {
        _ = TestScene.New().Build();
        string deck = "OP05-098\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine(roomId, ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);
        var me = engine.State.Players[0];
        me.Hand.Clear();
        me.Deck.Clear();
        me.LifeArea.Clear();
        me.LifeArea.Add(Card("OP15-003"));
        me.Deck.Add(Card("OP15-004"));
        me.Deck.Add(Card("OP15-005"));
        engine.State.CurrentTurnPlayer = 1;
        return engine;
    }
}
