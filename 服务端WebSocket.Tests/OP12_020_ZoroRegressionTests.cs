using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP12_020_ZoroRegressionTests
{
    private static CardInstance Card(string number, bool tapped = false)
        => new() { Info = CardDatabase.Get(number)!, IsTapped = tapped, TurnPlayed = 0 };

    [Fact]
    public async Task ActivatedMain_FirstCharacterBattleReactivatesThenAppliesBaseCostLimitOnce()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;
        var lowCostTarget = Card("OP15-003", tapped: true); // 原本费用5
        var highCostTarget = Card("OP15-008", tapped: true); // 原本费用8
        state.Players[1].Characters.Add(lowCostTarget);
        state.Players[1].Characters.Add(highCostTarget);

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(leader.ReactivateAfterBattleThisTurn);
        Assert.Equal(0, leader.NoAttackCostLeThisTurn);
        Assert.True(ActionValidator.CanAttack(state, 0, leader.Id, false, lowCostTarget.Id).Ok);

        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: false, lowCostTarget.Id);
        BattleEngine.EndBattle(state);

        Assert.False(leader.IsTapped);
        Assert.False(leader.ReactivateAfterBattleThisTurn);
        Assert.Equal(7, leader.NoAttackCostLeThisTurn);
        Assert.False(ActionValidator.CanAttack(state, 0, leader.Id, false, lowCostTarget.Id).Ok);
        Assert.True(ActionValidator.CanAttack(state, 0, leader.Id, false, highCostTarget.Id).Ok);

        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: false, highCostTarget.Id);
        BattleEngine.EndBattle(state);

        Assert.True(leader.IsTapped);
    }

    [Fact]
    public async Task ActivatedMain_LeaderBattleDoesNotConsumePendingReactivation()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());
        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: true, targetId: null);
        BattleEngine.EndBattle(state);

        Assert.True(leader.IsTapped);
        Assert.True(leader.ReactivateAfterBattleThisTurn);
        Assert.Equal(0, leader.NoAttackCostLeThisTurn);
    }
}
