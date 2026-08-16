using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP12-037「亡者游戏」反击效果的目标范围回归测试。</summary>
public class OP12_037_AsuraRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task CounterDuringCharacterBattle_BoostsLeaderOnly()
    {
        _ = TestScene.New().Build();
        string deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("op12-037-character-target", ("s0", "p0", deck), ("s1", "p1", deck), 0, 19);
        var state = engine.State;
        var defender = state.Players[1];
        var defendedCharacter = Card("OP15-050");

        defender.Characters.Clear();
        defender.Characters.Add(defendedCharacter);
        defender.Hand.Clear();
        defender.Hand.Add(Card("OP12-037"));
        defender.CostArea.Clear();
        defender.CostArea.Add(new DonCard { State = DonState.Active });
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;

        BattleEngine.StartAttack(
            state,
            state.Players[0].Leader.Id,
            targetIsLeader: false,
            targetId: defendedCharacter.Id);
        await BattleEngine.TriggerAttackDeclareAsync(state, new MockPromptService());
        BattleEngine.PassBlock(state);

        Assert.True(engine.HandleAction(1, "PlayCounter", System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            handIndex = 0,
            useCounterIcon = false,
        })));
        await engine.WaitSettledAsync();

        Assert.Equal(3000, defender.Leader.PowerModThisBattle);
        Assert.Equal(0, defendedCharacter.PowerModThisBattle);
        Assert.Contains(defender.Trash, card => card.Info.Number == "OP12-037");
    }
}
