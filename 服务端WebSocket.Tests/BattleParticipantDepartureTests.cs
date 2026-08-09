using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class BattleParticipantDepartureTests
{
    [Fact]
    public async Task OP17_033_LeavesWhileBeingAttacked_BattleEndsAfterAttackStep()
    {
        _ = TestScene.New().Build();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine(
            "op17-033-target-departure",
            ("s0", "attacker", deck),
            ("s1", "defender", deck),
            firstPlayer: 0,
            rngSeed: 1);
        var state = engine.State;
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        state.Players[0].Characters.Clear();
        state.Players[1].Characters.Clear();

        var attacker = Card("OP16-082");
        var restTarget = Card("OP16-096");
        var attackedLuckyRou = Card("OP17-033");
        attackedLuckyRou.IsTapped = true;
        state.Players[0].Characters.AddRange([attacker, restTarget]);
        state.Players[1].Characters.Add(attackedLuckyRou);

        var actions = new List<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex != 0) return;
            var snapshot = JsonSerializer.SerializeToElement(payload);
            if (snapshot.TryGetProperty("lastAction", out var action))
                actions.Add(action.GetString() ?? "");
        };

        Assert.True(engine.HandleAction(0, "Attack", JsonSerializer.SerializeToElement(new
        {
            attackerId = attacker.Id.ToString(),
            targetIsLeader = false,
            targetId = attackedLuckyRou.Id.ToString(),
        })));

        var confirm = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        Assert.True(engine.HandleAction(1, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = confirm.PromptId,
            chosen = new[] { "0" },
        })));

        var chooseTarget = await WaitForPrompt(
            engine,
            prompt => prompt.Kind == "OpponentLeaderOrCharacter");
        Assert.True(engine.HandleAction(1, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = chooseTarget.PromptId,
            chosen = new[] { restTarget.Id.ToString() },
        })));
        await engine.WaitSettledAsync();

        Assert.DoesNotContain(attackedLuckyRou, state.Players[1].Characters);
        Assert.Contains(attackedLuckyRou, state.Players[1].Trash);
        Assert.True(restTarget.IsTapped);
        Assert.Null(state.CurrentBattle);
        Assert.Equal(Phase.Main, state.Phase);
        Assert.Contains("BattleEnd", actions);
        Assert.DoesNotContain("AwaitBlock", actions);
        Assert.DoesNotContain("AwaitCounter", actions);
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static async Task<PendingPrompt> WaitForPrompt(
        GameEngine engine,
        Func<PendingPrompt, bool> predicate,
        int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (engine.State.PendingPrompt is { } prompt && predicate(prompt)) return prompt;
            await Task.Delay(10);
        }

        throw new TimeoutException("等待测试 Prompt 超时");
    }
}
