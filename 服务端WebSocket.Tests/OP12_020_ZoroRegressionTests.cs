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
    public async Task CharacterBattleThenActivatedMain_ReactivatesImmediatelyAndAppliesBaseCostLimit()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;
        var lowCostTarget = Card("OP15-003", tapped: true); // 原本费用5
        lowCostTarget.CostModThisTurn = 12; // 当前费用17，仍应按原本费用判定
        var highCostTarget = Card("OP15-008", tapped: true); // 原本费用8
        state.Players[1].Characters.Add(lowCostTarget);
        state.Players[1].Characters.Add(highCostTarget);

        Assert.True(ActionValidator.CanAttack(state, 0, leader.Id, false, lowCostTarget.Id).Ok);

        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: false, lowCostTarget.Id);
        BattleEngine.EndBattle(state);

        Assert.True(leader.IsTapped);
        Assert.True(leader.BattledOpponentCharacterThisTurn);
        Assert.Equal(0, leader.NoAttackCostLeThisTurn);

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.False(leader.IsTapped);
        Assert.Equal(7, leader.NoAttackCostLeThisTurn);
        Assert.False(ActionValidator.CanAttack(state, 0, leader.Id, false, lowCostTarget.Id).Ok);
        Assert.True(ActionValidator.CanAttack(state, 0, leader.Id, false, highCostTarget.Id).Ok);

        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: false, highCostTarget.Id);
        BattleEngine.EndBattle(state);

        Assert.True(leader.IsTapped);
    }

    [Fact]
    public async Task ActivatedMainBeforeCharacterBattle_DoesNotResolveOrConsumeOnce()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;
        leader.IsTapped = true;

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(leader.IsTapped);
        Assert.Equal(0, leader.NoAttackCostLeThisTurn);
        Assert.DoesNotContain("OP12-020-act:" + leader.Id, state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task LeaderBattleDoesNotSatisfyCharacterBattleCondition()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;

        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: true, targetId: null);
        BattleEngine.EndBattle(state);
        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(leader.IsTapped);
        Assert.False(leader.BattledOpponentCharacterThisTurn);
        Assert.Equal(0, leader.NoAttackCostLeThisTurn);
        Assert.DoesNotContain("OP12-020-act:" + leader.Id, state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task LeaderAttackBlockedByCharacter_SatisfiesCharacterBattleCondition()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        state.TurnCount = 3;
        var leader = state.Players[0].Leader;
        var blocker = Card("ST01-005");
        state.Players[1].Characters.Add(blocker);

        BattleEngine.StartAttack(state, leader.Id, targetIsLeader: true, targetId: null);
        BattleEngine.DeclareBlocker(state, blocker.Id);
        BattleEngine.EndBattle(state);

        Assert.True(leader.BattledOpponentCharacterThisTurn);
        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.False(leader.IsTapped);
        Assert.Equal(7, leader.NoAttackCostLeThisTurn);
    }
}
