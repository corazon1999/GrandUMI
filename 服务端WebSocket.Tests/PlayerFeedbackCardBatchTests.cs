using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-12 玩家集中反馈的卡牌与开局流程回归测试。</summary>
public class PlayerFeedbackCardBatchTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public void PRB02_016_HasCounter2000()
    {
        _ = TestScene.New().Build();

        Assert.Equal(2000, CardDatabase.Get("PRB02-016")!.Counter);
    }

    [Fact]
    public void EB01_021_StartsWithFourLife()
    {
        _ = TestScene.New().Build();
        var deck = "EB01-021\n" + string.Join('\n', Enumerable.Repeat("EB01-022", 10));
        var engine = new GameEngine("eb01-021-life", ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);

        Assert.Equal(4, CardDatabase.Get("EB01-021")!.Cost);
        Assert.Equal(4, engine.State.Players[0].LifeArea.Count);
    }

    [Fact]
    public async Task OP12_061_UsesLifeTopToProtectLawOncePerTurn()
    {
        var state = TestScene.New("OP12-061")
            .MyCharacter("OP12-073")
            .MyCharacter("OP12-106")
            .Build();
        var me = state.Players[0];
        var firstLaw = me.Characters[0];
        var secondLaw = me.Characters[1];
        var lifeTop = Card("OP15-003");
        me.LifeArea.Add(lifeTop);

        var firstWasKOd = await BattleEngine.KOCardAsync(
            state, 0, firstLaw, new MockPromptService().QueueConfirm(true));
        var secondWasKOd = await BattleEngine.KOCardAsync(
            state, 0, secondLaw, new MockPromptService().QueueConfirm(true));

        Assert.False(firstWasKOd);
        Assert.Contains(firstLaw, me.Characters);
        Assert.Contains(lifeTop, me.Hand);
        Assert.True(secondWasKOd);
        Assert.Contains(secondLaw, me.Trash);
    }

    [Fact]
    public async Task OP11_070_CanSearchAnotherCharlottePuddingCostTwoOrMore()
    {
        var state = TestScene.New().MyDeckTop("EB03-035", "OP15-003", "OP15-004").Build();
        var source = Card("OP11-070");
        state.Players[0].Characters.Add(source);
        var pudding = state.Players[0].Deck[0];
        var prompts = new MockPromptService().QueueChoose(pudding.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var search = Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "LookTopReveal"));
        Assert.Contains(pudding.Id.ToString(), search.choices);
        Assert.Contains(pudding, state.Players[0].Hand);
    }

    [Fact]
    public async Task OP16_065_ReturnsOneDonBeforeApplyingPowerReduction()
    {
        var state = TestScene.New().MyActiveDon(1).OppCharacter("OP15-050").Build();
        var don = state.Players[0].CostArea.Single();
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(don.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP16-065"), EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.Equal(-6000, target.PowerModsUntilOppEnd.Sum(mod => mod.Delta));
    }

    [Fact]
    public async Task OP07_076_ReturnsOneDonForCounterEffect()
    {
        var state = TestScene.New().MyActiveDon(1).MyCharacter("OP15-003").OppCharacter("OP15-050").Build();
        var don = state.Players[0].CostArea.Single();
        var ally = state.Players[0].Characters.Single();
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService()
            .QueueChoose(don.Id.ToString())
            .QueueChoose(ally.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP07-076"), EffectTrigger.EventCounter, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.Equal(2000, ally.PowerModThisBattle);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task OP13_079_PlaysMaryGeoiseStageFromDeckAtGameStart()
    {
        var state = TestScene.New("OP13-079").MyDeckTop("OP13-099", "OP15-003").Build();
        var stage = state.Players[0].Deck[0];
        var prompts = new MockPromptService().QueueChoose(stage.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, prompts);

        Assert.Same(stage, state.Players[0].StageCard);
        Assert.DoesNotContain(stage, state.Players[0].Deck);
    }

    [Fact]
    public async Task OP13_079_RealOpeningPathPlaysStageBeforeOpeningHand()
    {
        _ = TestScene.New().Build();
        var imuDeck = "OP13-079\n" + string.Join('\n', Enumerable.Repeat("OP13-099", 4))
            + "\n" + string.Join('\n', Enumerable.Repeat("OP13-080", 6));
        var otherDeck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("imu-opening", ("s0", "p0", imuDeck), ("s1", "p1", otherDeck),
            firstPlayer: -1, rngSeed: 17, deferOpeningSetupUntilFirstPlayerChosen: true);
        var chooser = engine.State.StartingPlayerChooser;

        engine.HandleAction(chooser, "ChooseFirstPlayer", JsonSerializer.SerializeToElement(new { goFirst = true }));
        await engine.WaitSettledAsync();
        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Empty(engine.State.Players[0].Hand);
        Assert.Empty(engine.State.Players[0].LifeArea);

        var stageId = prompt.ValidChoices[0];
        engine.HandleAction(0, "PromptResponse",
            JsonSerializer.SerializeToElement(new { promptId = prompt.PromptId, chosen = new[] { stageId } }));
        await engine.WaitSettledAsync(resolvingPromptId: prompt.PromptId);

        Assert.Equal("OP13-099", engine.State.Players[0].StageCard?.Info.Number);
        Assert.Equal(5, engine.State.Players[0].Hand.Count);
        Assert.Equal(4, engine.State.Players[0].LifeArea.Count);
        Assert.NotNull(engine.State.MulliganDeadlineUtc);
    }

    [Fact]
    public async Task OP14_048_DiscardsAllOwnHandAfterOptionalBounce()
    {
        var state = TestScene.New()
            .MyHandAdd("OP15-003")
            .MyHandAdd("OP15-004")
            .OppCharacter("OP15-050")
            .Build();
        var ownHand = state.Players[0].Hand.ToList();
        var target = state.Players[1].Characters.Single();

        await EffectRuntime.Resolve(state, 0, Card("OP14-048"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Empty(state.Players[0].Hand);
        Assert.All(ownHand, card => Assert.Contains(card, state.Players[0].Trash));
        Assert.Contains(target, state.Players[1].Hand);
    }

    [Fact]
    public async Task OP15_096_CanGiveCounterPowerToCharacter()
    {
        var state = TestScene.New().MyHandAdd("OP15-003").MyCharacter("OP15-004").Build();
        var discard = state.Players[0].Hand.Single();
        var target = state.Players[0].Characters.Single();
        var prompts = new MockPromptService()
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-096"), EffectTrigger.EventCounter, prompts);

        Assert.Equal(3000, target.PowerModThisBattle);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
        Assert.Contains(discard, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP14_027_AttackRestCanRestOpponentCharacter()
    {
        var state = TestScene.New().MyCharacter("OP14-027").OppCharacter("OP15-050").Build();
        state.TurnCount = 3;
        var jacks = state.Players[0].Characters.Single();
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        BattleEngine.StartAttack(state, jacks.Id, targetIsLeader: true, targetId: null);
        await BattleEngine.TriggerAttackDeclareAsync(state, prompts);

        Assert.True(jacks.IsTapped);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task EB03_008_CanGrantOP11_001PermissionToAttackActiveCharacter()
    {
        var state = TestScene.New("OP11-001").OppCharacter("OP15-050").Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;
        var target = state.Players[1].Characters.Single();
        var source = Card("EB03-008");

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(leader.Id.ToString()));

        Assert.False(target.IsTapped);
        Assert.True(ActionValidator.CanAttack(state, 0, leader.Id, false, target.Id).Ok);
    }

    [Fact]
    public async Task OP16_119_NullifiesOP17_089CostEffectAndPublishesVisibleStatus()
    {
        var state = TestScene.New().OppCharacter("OP17-089").Build();
        var saul = state.Players[1].Characters.Single();
        await EffectRuntime.Resolve(state, 1, saul, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(saul.Info.Cost + 12, state.CurrentCostOf(1, saul));

        var prompts = new MockPromptService()
            .QueueChoose(saul.Id.ToString())
            .QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, Card("OP16-119"), EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.True(saul.IsEffectsNullified);
        Assert.Equal(saul.Info.Cost, state.CurrentCostOf(1, saul));
        var json = JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 0));
        Assert.Contains("\"effectsNullified\":true", json);
    }
}
