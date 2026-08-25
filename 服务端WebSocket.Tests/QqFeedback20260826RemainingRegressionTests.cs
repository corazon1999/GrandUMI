using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class QqFeedback20260826RemainingRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task Q381_OP08_069_EmptyDiscardAfterDonChoiceLeavesAllCostsAndBenefitsUnchanged()
    {
        var scene = CreateLinlinScene();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(scene.Don.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(scene.State, 0, scene.Source, EffectTrigger.OnEnterField, prompts);

        AssertUnchanged(scene);
    }

    [Fact]
    public async Task Q381_OP08_069_InvalidOrDuplicateDonAnswerLeavesAllCostsAndBenefitsUnchanged()
    {
        var invalid = CreateLinlinScene();
        await EffectRuntime.Resolve(invalid.State, 0, invalid.Source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true).QueueChoose(Guid.NewGuid().ToString()));
        AssertUnchanged(invalid);

        var duplicate = CreateLinlinScene();
        string donId = duplicate.Don.Id.ToString();
        await EffectRuntime.Resolve(duplicate.State, 0, duplicate.Source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true).QueueChoose(donId, donId));
        AssertUnchanged(duplicate);
    }

    [Fact]
    public async Task Q381_OP08_069_CompleteCompositeCostCommitsDonThenDiscardAndAddsLife()
    {
        var scene = CreateLinlinScene();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(scene.Don.Id.ToString())
            .QueueChoose(scene.Discard.Id.ToString());

        await EffectRuntime.Resolve(scene.State, 0, scene.Source, EffectTrigger.OnEnterField, prompts);

        var me = scene.State.Players[0];
        Assert.DoesNotContain(scene.Don, me.CostArea);
        Assert.Contains(scene.Don, me.DonDeck);
        Assert.Equal(DonState.InDeck, scene.Don.State);
        Assert.DoesNotContain(scene.Discard, me.Hand);
        Assert.Contains(scene.Discard, me.Trash);
        Assert.Contains(scene.Life, me.LifeArea);
        Assert.DoesNotContain(scene.Life, me.Deck);
    }

    [Fact]
    public async Task Q381_OP08_069_RepeatedResponsesToSamePromptCommitCompositeCostOnlyOnce()
    {
        var engine = CreateEngine();
        var me = engine.State.Players[0];
        var source = Card("OP08-069");
        var discard = Card("OP15-003");
        var life = Card("OP15-004");
        var don = new DonCard { State = DonState.Active };
        me.Characters.Add(source);
        me.Hand.Add(discard);
        me.Deck.Add(life);
        me.CostArea.Add(don);

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, source, EffectTrigger.OnEnterField, engine.Prompts);

        var confirm = await WaitForPrompt(engine, "Option");
        engine.Prompts.Resolve(confirm.PromptId, new[] { "0" });
        engine.Prompts.Resolve(confirm.PromptId, new[] { "1" });

        var donPrompt = await WaitForPrompt(engine, "ReturnOwnDon");
        engine.Prompts.Resolve(donPrompt.PromptId, new[] { don.Id.ToString() });
        engine.Prompts.Resolve(donPrompt.PromptId, Array.Empty<string>());

        var discardPrompt = await WaitForPrompt(engine, "OwnHandDiscard");
        engine.Prompts.Resolve(discardPrompt.PromptId, new[] { discard.Id.ToString() });
        engine.Prompts.Resolve(discardPrompt.PromptId, Array.Empty<string>());

        await resolveTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(me.DonDeck, card => card.Id == don.Id);
        Assert.Single(me.Trash, card => card.Id == discard.Id);
        Assert.Single(me.LifeArea, card => card.Id == life.Id);
    }

    [Fact]
    public async Task G607_OP14_104_AddsChosenTrashCharacterToLifeFaceUp()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP14-104");
        var target = Card("OP14-081");
        me.Characters.Add(source);
        me.Trash.Add(target);
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueOption(0);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.DoesNotContain(target, me.Trash);
        Assert.Same(target, me.LifeArea[0]);
        Assert.True(target.IsLifeFaceUp);
    }

    [Fact]
    public async Task G616_OP06_111_CanReturnOpponentsOneCostStageToItsOwnersDeckBottom()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("OP06-111");
        var opponentStage = Card("OP06-098");
        var restTarget = Card("OP06-093");
        me.Characters.Add(source);
        opponent.StageCard = opponentStage;
        opponent.Characters.Add(restTarget);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(opponentStage.Id.ToString())
            .QueueChoose(restTarget.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Null(opponent.StageCard);
        Assert.Same(opponentStage, opponent.Deck[^1]);
        Assert.DoesNotContain(opponentStage, me.Deck);
        Assert.True(restTarget.IsTapped);
    }

    [Fact]
    public void G628_OP07_032_CanAttackCharacterButNotLeaderOnItsPlayedTurn()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var attacker = Card("OP07-032");
        attacker.TurnPlayed = state.TurnCount;
        state.Players[0].Characters.Add(attacker);
        var target = Card("OP15-003");
        target.IsTapped = true;
        state.Players[1].Characters.Add(target);

        Assert.True(ActionValidator.HasKeyword(state, attacker, "速攻：角色"));
        Assert.True(ActionValidator.CanAttack(
            state, 0, attacker.Id, targetIsLeader: false, target.Id).Ok);
        Assert.False(ActionValidator.CanAttack(
            state, 0, attacker.Id, targetIsLeader: true, targetId: null).Ok);
    }

    [Fact]
    public void G654_G655_G656_OnlyOP08_032HasTwoThousandCounter()
    {
        Assert.Equal(0, CardDatabase.Get("OP08-022")!.Counter);
        Assert.Equal(2000, CardDatabase.Get("OP08-032")!.Counter);
        Assert.Equal(0, CardDatabase.Get("OP08-034")!.Counter);
    }

    [Fact]
    public async Task G738_OP06_092_TrashesCharacterWithoutBeingBlockedByEffectKoGuard()
    {
        var state = TestScene.New().Build();
        var source = Card("OP06-092");
        var target = Card("OP02-102");
        state.Players[0].Characters.Add(source);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(
            state, 1, target, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(state.IsKoGuarded(target, "effect"));

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnEnterField,
            new MockPromptService().QueueOption(0).QueueChoose(target.Id.ToString()));

        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task G751_OP09_013_LeaderBuffLastsUntilNextOpponentEndPhase()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP09-013");
        me.Characters.Add(source);

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true));

        Assert.Equal(0, me.Leader.PowerModThisTurn);
        var modifier = Assert.Single(me.Leader.PowerModsUntilOppEnd);
        Assert.Equal(1000, modifier.Delta);
        Assert.Equal(0, modifier.AppliedBySide);

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Single(me.Leader.PowerModsUntilOppEnd);

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(me.Leader.PowerModsUntilOppEnd);
    }

    [Fact]
    public void G679_ThisTurnOriginalPowerChangesUseHighestRegardlessOfApplicationOrder()
    {
        var state = TestScene.New().Build();
        var lowerThenHigher = Card("OP15-003");
        var higherThenLower = Card("OP15-003");
        state.Players[0].Characters.Add(lowerThenHigher);
        state.Players[0].Characters.Add(higherThenLower);

        lowerThenHigher.OriginalPowerOverride = 6000;
        lowerThenHigher.OriginalPowerOverride = 8000;
        higherThenLower.OriginalPowerOverride = 8000;
        higherThenLower.OriginalPowerOverride = 6000;

        Assert.Equal(8000, state.OriginalPowerOf(0, lowerThenHigher));
        Assert.Equal(8000, state.OriginalPowerOf(0, higherThenLower));
        Assert.Equal(8000, higherThenLower.CurrentPower(0, ownerTurn: false));
    }

    [Fact]
    public void G679_DifferentDurationsUseHighestWhileOrdinaryPowerModifiersRemainAdditive()
    {
        var state = TestScene.New().Build();
        var target = Card("OP15-003");
        state.Players[0].Characters.Add(target);
        target.OriginalPowerOverride = 6000;
        AtomicOps.SetOriginalPowerUntilOppEnd(target, 8000, appliedBy: 0);
        target.PowerModThisTurn = -1000;
        AtomicOps.AddPowerUntilOppEnd(target, 2000, appliedBy: 0);

        Assert.Equal(8000, state.OriginalPowerOf(0, target));
        Assert.Equal(9000, state.CurrentPowerOf(0, target));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Null(target.OriginalPowerOverride);
        Assert.Single(target.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(10000, state.CurrentPowerOf(0, target));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(target.OriginalPowerOverridesUntilOppEnd);
        Assert.Empty(target.PowerModsUntilOppEnd);
        Assert.Equal(target.Info.Power, state.CurrentPowerOf(0, target));
    }

    [Fact]
    public void G679_UntilOpponentEndChangesRemainIndependentAcrossDifferentExpirySides()
    {
        var state = TestScene.New().Build();
        var target = Card("OP15-003");
        state.Players[0].Characters.Add(target);
        AtomicOps.SetOriginalPowerUntilOppEnd(target, 8000, appliedBy: 0);
        AtomicOps.SetOriginalPowerUntilOppEnd(target, 6000, appliedBy: 1);

        Assert.Equal(8000, state.OriginalPowerOf(0, target));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Single(target.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(6000, state.OriginalPowerOf(0, target));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(target.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(target.Info.Power, state.OriginalPowerOf(0, target));
    }

    [Fact]
    public void G679_ContinuousChangesUseHighestAndFallBackWhenHigherSourceIsNullifiedOrLeaves()
    {
        var state = TestScene.New().Build();
        var highSource = Card("OP15-092");
        var lowSource = Card("OP15-071");
        var target = Card("OP15-003");
        state.Players[0].Characters.Add(highSource);
        state.Players[0].Characters.Add(lowSource);
        state.Players[0].Characters.Add(target);
        state.ContinuousEffects.Add(OriginalPowerAura(highSource, target, 8000));
        state.ContinuousEffects.Add(OriginalPowerAura(lowSource, target, 6000));

        Assert.Equal(8000, state.OriginalPowerOf(0, target));

        highSource.IsEffectsNullified = true;
        Assert.Equal(6000, state.OriginalPowerOf(0, target));
        highSource.IsEffectsNullified = false;
        Assert.Equal(8000, state.OriginalPowerOf(0, target));

        AtomicOps.KO(state, 0, highSource);
        Assert.DoesNotContain(highSource, state.Players[0].Characters);
        Assert.Equal(6000, state.OriginalPowerOf(0, target));
    }

    [Fact]
    public async Task G679_ST26_005_UsesExactUntilOpponentEndOriginalPowerChannel()
    {
        var state = TestScene.New("OP01-003")
            .MyActiveDon(2)
            .OppActiveDon(5)
            .Build();
        var leader = state.Players[0].Leader;

        await EffectRuntime.Resolve(
            state, 0, Card("ST26-005"), EffectTrigger.OnEnterField, new MockPromptService());

        var originalPowerChange = Assert.Single(leader.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(7000, originalPowerChange.Value);
        Assert.Equal(0, originalPowerChange.AppliedBySide);
        Assert.Empty(leader.PowerModsUntilOppEnd);
        Assert.Equal(7000, state.OriginalPowerOf(0, leader));
    }

    [Fact]
    public async Task G679_DslUntilOpponentEndUsesOriginalPowerInsteadOfOrdinaryDelta()
    {
        var state = TestScene.New().Build();
        var source = Card("OP15-003");
        state.Players[0].Characters.Add(source);
        using var document = JsonDocument.Parse(
            """
            {
              "triggers": [{
                "on": "OnEnterField",
                "then": [{
                  "op": "SetOriginalPowerUntilOppEnd",
                  "target": "selfLeader",
                  "value": 7000
                }]
              }]
            }
            """);
        state.Ruleset = new CardRuleset(
            "g679-dsl-original-power",
            baseRulesetId: null,
            description: "G679 DSL 定向回归",
            new Dictionary<string, IScriptedEffect>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [source.Info.Number] = document.RootElement.Clone(),
            },
            changedCards: [source.Info.Number]);
        state.RulesetId = state.Ruleset.Id;

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Empty(state.Players[0].Leader.PowerModsUntilOppEnd);
        var change = Assert.Single(state.Players[0].Leader.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(7000, change.Value);
        Assert.Equal(7000, state.OriginalPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task G679_OP14_001_SwapReadsEachCardsEffectiveOriginalPower()
    {
        var state = TestScene.New("OP14-001").Build();
        var first = Card("OP01-019");
        var second = Card("OP01-008");
        state.Players[0].Characters.Add(first);
        state.Players[0].Characters.Add(second);
        AtomicOps.SetOriginalPowerUntilOppEnd(first, 8000, appliedBy: 0);

        await EffectRuntime.Resolve(
            state,
            0,
            state.Players[0].Leader,
            EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString()));

        Assert.Equal(8000, state.OriginalPowerOf(0, first));
        Assert.Equal(8000, state.OriginalPowerOf(0, second));
        Assert.Equal(8000, second.OriginalPowerOverride);
    }

    private static ContinuousEffect OriginalPowerAura(
        CardInstance source,
        CardInstance target,
        int value)
        => new()
        {
            SourceCardId = source.Id.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = card => card.Id == target.Id,
            },
            OriginalPowerOverride = value,
            Predicate = (_, _, _) => true,
        };

    private static LinlinScene CreateLinlinScene()
    {
        var state = TestScene.New().MyCharacter("OP08-069")
            .MyHandAdd("OP15-003").MyDeckTop("OP15-004").Build();
        var me = state.Players[0];
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        return new LinlinScene(
            state,
            me.Characters.Single(),
            don,
            me.Hand.Single(),
            me.Deck.Single());
    }

    private static void AssertUnchanged(LinlinScene scene)
    {
        var me = scene.State.Players[0];
        Assert.Contains(scene.Don, me.CostArea);
        Assert.Empty(me.DonDeck);
        Assert.Equal(DonState.Active, scene.Don.State);
        Assert.Contains(scene.Discard, me.Hand);
        Assert.DoesNotContain(scene.Discard, me.Trash);
        Assert.Contains(scene.Life, me.Deck);
        Assert.Empty(me.LifeArea);
    }

    private static async Task<PendingPrompt> WaitForPrompt(GameEngine engine, string kind)
    {
        for (int index = 0; index < 200; index++)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
            await Task.Delay(5);
        }
        throw new TimeoutException($"等待提示 {kind} 超时");
    }

    private static GameEngine CreateEngine()
    {
        _ = TestScene.New().Build();
        string deck = "OP01-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 50));
        var engine = new GameEngine(
            "q381-repeated-prompt",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 20260826);
        var me = engine.State.Players[0];
        me.Characters.Clear();
        me.Hand.Clear();
        me.Deck.Clear();
        me.LifeArea.Clear();
        me.CostArea.Clear();
        me.DonDeck.Clear();
        return engine;
    }

    private sealed record LinlinScene(
        GameState State,
        CardInstance Source,
        DonCard Don,
        CardInstance Discard,
        CardInstance Life);
}
