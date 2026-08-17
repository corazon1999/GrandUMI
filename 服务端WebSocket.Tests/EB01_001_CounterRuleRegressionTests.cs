using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>EB01-001「光月御殿」规则反击值的真实流程回归测试。</summary>
public class EB01_001_CounterRuleRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task CounterlessWanoCharacter_IsShownAndUsedAsCounter1000()
    {
        _ = TestScene.New().Build();
        string odenDeck = "EB01-001\n" + string.Join('\n', Enumerable.Repeat("EB01-002", 10));
        string opponentDeck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("eb01-001-counter-rule", ("s0", "p0", odenDeck), ("s1", "p1", opponentDeck), 1, 23);
        var state = engine.State;
        var defender = state.Players[0];
        var counterCard = Card("EB01-002");

        defender.Hand.Clear();
        defender.Hand.Add(counterCard);
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 3;
        state.Phase = Phase.Main;

        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.Equal(1000, snapshot.GetProperty("my").GetProperty("handCardCounters")[0].GetInt32());

        BattleEngine.StartAttack(state, state.Players[1].Leader.Id, targetIsLeader: true, targetId: null);
        await BattleEngine.TriggerAttackDeclareAsync(state, new MockPromptService());
        BattleEngine.PassBlock(state);

        Assert.True(engine.HandleAction(0, "PlayCounter", JsonSerializer.SerializeToElement(new
        {
            handIndex = 0,
            useCounterIcon = true,
        })));

        Assert.Empty(defender.Hand);
        Assert.Contains(counterCard, defender.Trash);
        Assert.Equal(1000, defender.Leader.PowerModThisBattle);
    }
}
