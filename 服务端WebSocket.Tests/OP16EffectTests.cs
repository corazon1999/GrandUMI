using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP16EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP16_003_RevealsTwo8000Characters_ThenReducesChosenOpponentBy6000()
    {
        var state = TestScene.New("OP16-001").Build();
        var newgate = Card("OP16-003");
        var revealA = Card("OP16-004");
        var revealB = Card("OP16-005");
        var target = Card("OP15-050");
        state.Players[0].Characters.Add(newgate);
        state.Players[0].Hand.AddRange([revealA, revealB]);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(revealA.Id.ToString(), revealB.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, newgate, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(-6000, target.PowerModThisTurn);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.All(prompts.ChooseHistory[0].choices, id => Assert.Contains(id, new[] { revealA.Id.ToString(), revealB.Id.ToString() }));
    }

    [Fact]
    public async Task OP16_003_BuffsOnlyLeaderDuringOwnerTurn()
    {
        var state = TestScene.New("OP16-001").Build();
        var newgate = Card("OP16-003");
        state.Players[0].Characters.Add(newgate);

        Assert.DoesNotContain("双重攻击", newgate.Info.Abilities);

        await EffectRuntime.Resolve(
            state,
            0,
            newgate,
            EffectTrigger.OnEnterField,
            new MockPromptService());

        var leader = state.Players[0].Leader;
        Assert.True(ActionValidator.HasKeyword(state, leader, "双重攻击"));
        Assert.False(ActionValidator.HasKeyword(state, newgate, "双重攻击"));
        Assert.Equal(7000, state.CurrentPowerOf(0, leader));
        Assert.Equal(10000, state.CurrentPowerOf(0, newgate));

        using (var ownerSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(
                   StateSnapshotBuilder.Build(state, viewerIndex: 0))))
        {
            var leaderKeywords = ownerSnapshot.RootElement
                .GetProperty("my")
                .GetProperty("leaderGainedKeywords")
                .EnumerateArray()
                .Select(keyword => keyword.GetString())
                .ToArray();
            Assert.Contains("双重攻击", leaderKeywords);
        }

        using (var opponentViewSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(
                   StateSnapshotBuilder.Build(state, viewerIndex: 1))))
        {
            var leaderKeywords = opponentViewSnapshot.RootElement
                .GetProperty("opponent")
                .GetProperty("leaderGainedKeywords")
                .EnumerateArray()
                .Select(keyword => keyword.GetString())
                .ToArray();
            Assert.Contains("双重攻击", leaderKeywords);
        }

        state.CurrentTurnPlayer = 1;

        Assert.False(ActionValidator.HasKeyword(state, leader, "双重攻击"));
        Assert.Equal(5000, state.CurrentPowerOf(0, leader));
        Assert.Equal(10000, state.CurrentPowerOf(0, newgate));

        using var opponentTurnSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(
            StateSnapshotBuilder.Build(state, viewerIndex: 0)));
        Assert.Empty(opponentTurnSnapshot.RootElement
            .GetProperty("my")
            .GetProperty("leaderGainedKeywords")
            .EnumerateArray());
    }

    [Fact]
    public async Task OP16_015_Discards8000PowerCharacter_AndChangesOriginalPowerTo7000()
    {
        var state = TestScene.New("OP16-001").Build();
        state.CurrentTurnPlayer = 1;

        var luffy = Card("OP16-015");
        var discard = Card("OP16-011");
        state.Players[0].Characters.Add(luffy);
        state.Players[0].Hand.Add(discard);

        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.TriggerEvent(
            state,
            EffectTrigger.OnOppAttackDeclare,
            prompts,
            new Dictionary<string, object?> { ["AttackerIdx"] = 1 });

        Assert.DoesNotContain(discard, state.Players[0].Hand);
        Assert.Contains(discard, state.Players[0].Trash);
        Assert.Equal(7000, state.CurrentPowerOf(0, state.Players[0].Leader));
        Assert.Equal(7000, state.CurrentPowerOf(0, luffy));
        Assert.Equal(7000, Assert.Single(state.Players[0].Leader.OriginalPowerOverridesUntilOppEnd).Value);
        Assert.Equal(7000, Assert.Single(luffy.OriginalPowerOverridesUntilOppEnd).Value);
    }

    [Fact]
    public async Task OP16_015_OriginalPowerOverride_StacksWithModifiers_AndExpiresAtOpponentTurnEnd()
    {
        var state = TestScene.New("OP16-001").Build();
        state.CurrentTurnPlayer = 1;

        var leader = state.Players[0].Leader;
        var luffy = Card("OP16-015");
        var discard = Card("OP16-011");
        leader.PowerModThisTurn = 1000;
        luffy.PowerModThisTurn = -1000;
        state.Players[0].Characters.Add(luffy);
        state.Players[0].Hand.Add(discard);

        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.TriggerEvent(
            state,
            EffectTrigger.OnOppAttackDeclare,
            prompts,
            new Dictionary<string, object?> { ["AttackerIdx"] = 1 });

        Assert.Equal(8000, state.CurrentPowerOf(0, leader));
        Assert.Equal(6000, state.CurrentPowerOf(0, luffy));

        TurnEngine.EnterEndPhase(state);

        Assert.Empty(leader.OriginalPowerOverridesUntilOppEnd);
        Assert.Empty(luffy.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(5000, state.CurrentPowerOf(0, leader));
        Assert.Equal(6000, state.CurrentPowerOf(0, luffy));
    }

    [Fact]
    public async Task OP16_082_ContinuouslyAddsThreeToOwnCost_WithoutStacking()
    {
        var state = TestScene.New().Build();
        var kinemon = Card("OP16-082");
        var otherCharacter = Card("OP16-083");
        state.Players[0].Characters.AddRange([kinemon, otherCharacter]);

        Assert.Equal(4, state.CurrentCostOf(0, kinemon));

        await EffectRuntime.Resolve(
            state,
            0,
            kinemon,
            EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.Equal(7, state.CurrentCostOf(0, kinemon));
        Assert.Equal(otherCharacter.Info.Cost, state.CurrentCostOf(0, otherCharacter));

        await EffectRuntime.Resolve(
            state,
            0,
            kinemon,
            EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.Equal(7, state.CurrentCostOf(0, kinemon));

        kinemon.IsEffectsNullified = true;
        Assert.Equal(4, state.CurrentCostOf(0, kinemon));
    }

    [Fact]
    public async Task OP16_082_KeepsPrintedCostIncreaseWhenOnEnterEffectIsNullified()
    {
        var state = TestScene.New("OP09-081").Build();
        var leader = state.Players[0].Leader;
        var kinemon = Card("OP16-082");
        state.Players[0].Characters.Add(kinemon);

        await EffectRuntime.Resolve(
            state,
            0,
            leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());
        await EffectRuntime.Resolve(
            state,
            0,
            kinemon,
            EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.True(state.IsTriggerNullified(kinemon, EffectTrigger.OnEnterField));
        Assert.Equal(7, state.CurrentCostOf(0, kinemon));
    }

    [Fact]
    public async Task OP16_003_KeepsStaticLeaderBuffWhenOwnOnEnterEffectIsNullified()
    {
        var state = TestScene.New("OP09-081").OppCharacter("OP15-050").Build();
        var me = state.Players[0];
        var newgate = Card("OP16-003");
        me.Characters.Add(newgate);
        me.Hand.AddRange([Card("OP16-004"), Card("OP16-005")]);
        var target = Assert.Single(state.Players[1].Characters);

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        var prompts = new MockPromptService().QueueConfirm(true);
        await EffectRuntime.Resolve(
            state, 0, newgate, EffectTrigger.OnEnterField, prompts);

        Assert.True(state.IsTriggerNullified(newgate, EffectTrigger.OnEnterField));
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Equal(0, target.PowerModThisTurn);
        Assert.Equal(me.Leader.Info.Power + 2000, state.CurrentPowerOf(0, me.Leader));
        Assert.True(ActionValidator.HasKeyword(state, me.Leader, "双重攻击"));
    }

    [Fact]
    public async Task OP16_003_KeepsStaticLeaderBuffWhenOpponentTeachNullifiesOnEnterEffect()
    {
        var state = TestScene.New("OP09-081").Build();
        var teach = state.Players[0];
        var newgateOwner = state.Players[1];
        var discard = Card("OP15-003");
        var target = Card("OP15-050");
        var newgate = Card("OP16-003");
        teach.Hand.Add(discard);
        teach.Characters.Add(target);
        newgateOwner.Characters.Add(newgate);
        newgateOwner.Hand.AddRange([Card("OP16-004"), Card("OP16-005")]);

        await EffectRuntime.Resolve(
            state,
            0,
            teach.Leader,
            EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));

        state.CurrentTurnPlayer = 1;
        var prompts = new MockPromptService().QueueConfirm(true);
        await EffectRuntime.Resolve(
            state, 1, newgate, EffectTrigger.OnEnterField, prompts);

        Assert.True(state.IsTriggerNullified(newgate, EffectTrigger.OnEnterField));
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Equal(0, target.PowerModThisTurn);
        Assert.Equal(newgateOwner.Leader.Info.Power + 2000, state.CurrentPowerOf(1, newgateOwner.Leader));
        Assert.True(ActionValidator.HasKeyword(state, newgateOwner.Leader, "双重攻击"));
    }

    [Fact]
    public void OP16_118_Changes8000PowerCharactersInHandToCounter2000()
    {
        var state = TestScene.New("OP16-001").Build();
        var ace = Card("OP16-118");
        var counter1000 = Card("OP16-017");
        var counter0 = Card("OP16-011");
        var non8000 = Card("OP16-009");

        state.Players[0].Characters.Add(ace);
        state.Players[0].Hand.AddRange([counter1000, counter0, non8000]);

        Assert.Equal(2000, HandStaticCounter.Value(state, 0, counter1000));
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, counter0));
        Assert.Equal(non8000.Info.Counter, HandStaticCounter.Value(state, 0, non8000));
    }

    [Fact]
    public void OP16_118_StopsChangingCountersAfterLeavingFieldOrBeingNullified()
    {
        var state = TestScene.New("OP16-001").Build();
        var ace = Card("OP16-118");
        var target = Card("OP16-017");

        state.Players[0].Characters.Add(ace);
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, target));

        ace.IsEffectsNullified = true;
        Assert.Equal(target.Info.Counter, HandStaticCounter.Value(state, 0, target));

        ace.IsEffectsNullified = false;
        state.Players[0].Characters.Remove(ace);
        Assert.Equal(target.Info.Counter, HandStaticCounter.Value(state, 0, target));
    }
}
