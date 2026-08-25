using System.Text.Json;
using GrandUMI;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>QQ群 2026-08-15 未处理反馈的定向回归。</summary>
public class QqFeedback20260815RegressionTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Theory]
    [InlineData("OP17-003")]
    [InlineData("OP17-027")]
    [InlineData("OP17-048")]
    public void RushCharacter_SnapshotKeepsAttackButtonWhenRestedCharacterIsLegal(string number)
    {
        var state = TestScene.New().OppCharacter("OP17-065").Build();
        state.TurnCount = 3;
        var attacker = Card(number, state.TurnCount);
        var target = state.Players[1].Characters.Single();
        target.IsTapped = true;
        state.Players[0].Characters.Add(attacker);

        Assert.False(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(state, 0, attacker.Id, false, target.Id).Ok);

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 0)));
        var attackerSnapshot = snapshot.RootElement.GetProperty("my").GetProperty("fieldCards")
            .EnumerateArray().Single(card => card.GetProperty("id").GetString() == attacker.Id.ToString());
        Assert.True(attackerSnapshot.GetProperty("canAttack").GetBoolean());
    }

    [Fact]
    public void OP17_044_SnapshotKeepsAttackButtonWhenJohnIsTheOnlyLegalTarget()
    {
        var state = TestScene.New("OP17-039").Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 3;
        var john = Card("OP17-044");
        john.IsTapped = true;
        state.Players[0].Characters.Add(john);
        var attacker = state.Players[1].Leader;

        Assert.False(ActionValidator.CanAttack(state, 1, attacker.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(state, 1, attacker.Id, false, john.Id).Ok);

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 1)));
        Assert.True(snapshot.RootElement.GetProperty("my").GetProperty("leaderCanAttack").GetBoolean());
    }

    [Fact]
    public async Task ST32_003_AcceptsDualPropertySlashLeader()
    {
        var state = TestScene.New("ST12-001").MyHandAdd("ST32-004").Build();
        var source = Card("ST32-003");
        var candidate = state.Players[0].Hand.Single();
        state.Players[0].Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(candidate.Id.ToString()));

        Assert.DoesNotContain(candidate, state.Players[0].Hand);
        Assert.Contains(candidate, state.Players[0].Characters);
    }

    [Fact]
    public async Task OP06_098_RestsOneActiveDonAndTheStageBeforePlayingFromTrash()
    {
        var state = TestScene.New("OP06-080").MyActiveDon(1).Build();
        var me = state.Players[0];
        var stage = Card("OP06-098");
        var target = Card("OP06-082");
        me.StageCard = stage;
        me.Trash.Add(target);

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(target.Id.ToString()));

        Assert.True(stage.IsTapped);
        Assert.Equal(DonState.Rest, me.CostArea.Single().State);
        Assert.DoesNotContain(target, me.Trash);
        Assert.Contains(target, me.Characters);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task OP13_082_RequiresTheWholeCostAndTrashesCharactersWithoutKo()
    {
        var state = TestScene.New("OP13-079").MyActiveDon(1).MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP13-082");
        var ally = Card("OP13-085");
        me.Characters.Add(source);
        me.Characters.Add(ally);
        var discard = me.Hand.Single();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(me.Characters);
        Assert.Contains(source, me.Trash);
        Assert.Contains(ally, me.Trash);
        Assert.Contains(discard, me.Trash);
        Assert.Equal(DonState.Rest, me.CostArea.Single().State);
    }

    [Fact]
    public async Task OP13_082_CancelledDiscardDoesNotPartiallyPayDonCost()
    {
        var state = TestScene.New("OP13-079").MyActiveDon(1).MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP13-082");
        me.Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChooseEmpty());

        Assert.Equal(DonState.Active, me.CostArea.Single().State);
        Assert.Contains(source, me.Characters);
        Assert.Single(me.Hand);
    }

    [Fact]
    public async Task OP13_079_CanTrashOP13_080WithAttachedDonAndDraw()
    {
        var state = TestScene.New("OP13-079").MyCharacter("OP13-080").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var target = me.Characters.Single();
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = target.Id });
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(target, me.Trash);
        Assert.Single(me.Hand);
        Assert.Equal(DonState.Rest, me.CostArea.Single().State);
    }

    [Fact]
    public async Task OP17_098_MainRestsSixDonAndKosUpToTwoTargets()
    {
        var state = TestScene.New().MyActiveDon(6).OppCharacter("OP17-085").Build();
        var me = state.Players[0];
        var highCost = Card("OP17-085");
        highCost.CostModThisTurn = 7;
        me.Characters.Add(highCost);
        var target = state.Players[1].Characters.Single();

        await EffectRuntime.Resolve(state, 0, Card("OP17-098"), EffectTrigger.EventMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(target.Id.ToString()));

        Assert.Equal(6, me.RestDonCount);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public void EB01_001_StartsWithFourLife()
    {
        _ = TestScene.New().Build();
        var deck = "EB01-001\n" + string.Join('\n', Enumerable.Repeat("EB01-002", 10));
        var engine = new GameEngine("eb01-001-life", ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);

        Assert.Equal(4, CardDatabase.Get("EB01-001")!.Cost);
        Assert.Equal(4, engine.State.Players[0].LifeArea.Count);
    }

    [Fact]
    public void BugReportRoot_UsesWritableDataDirectoryOnServer()
    {
        Assert.Equal(
            Path.GetFullPath("/data/grandumi/BugReports"),
            BugReportStore.ResolveRoot(null, "/data/grandumi", "/opt/grandumi"));
        Assert.Equal(
            Path.GetFullPath("/custom/reports"),
            BugReportStore.ResolveRoot("/custom/reports", "/data/grandumi", "/opt/grandumi"));
    }
}
