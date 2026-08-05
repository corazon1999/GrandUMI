using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP17_102_TurnBoundaryRegressionTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Fact]
    public async Task OP17_102_KO效果未完成时不能结束回合_效果登场角色下回合可以攻击()
    {
        _ = TestScene.New().Build();
        string deck = "OP16-080\n" + string.Join('\n', Enumerable.Repeat("OP09-095", 10));
        var engine = new GameEngine(
            "op17-102-turn-boundary",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 1);
        var state = engine.State;
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        state.Phase = Phase.Main;

        var oven = Card("OP17-102");
        var summoned = Card("OP17-107");
        state.Players[0].Characters.Add(oven);
        state.Players[0].Trash.Add(summoned);

        var koTask = BattleEngine.KOCardAsync(state, 0, oven, engine.Prompts);
        var prompt = await WaitForPromptAsync(state);

        engine.HandleAction(1, "EndTurn", JsonSerializer.SerializeToElement(new { }));

        Assert.Equal(1, state.CurrentTurnPlayer);
        Assert.Equal(4, state.TurnCount);
        Assert.Equal(Phase.Main, state.Phase);
        Assert.Same(prompt, state.PendingPrompt);

        engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { summoned.Id.ToString() },
        }));
        await koTask;

        Assert.Contains(summoned, state.Players[0].Characters);
        Assert.Equal(4, summoned.TurnPlayed);

        engine.HandleAction(1, "EndTurn", JsonSerializer.SerializeToElement(new { }));
        await engine.WaitSettledAsync();

        Assert.Equal(0, state.CurrentTurnPlayer);
        Assert.Equal(5, state.TurnCount);
        Assert.True(ActionValidator.CanAttack(
            state,
            0,
            summoned.Id,
            targetIsLeader: true,
            targetId: null).Ok);
    }

    private static async Task<PendingPrompt> WaitForPromptAsync(GameState state)
    {
        for (int i = 0; i < 100 && state.PendingPrompt is null; i++)
            await Task.Delay(10);

        return Assert.IsType<PendingPrompt>(state.PendingPrompt);
    }
}
