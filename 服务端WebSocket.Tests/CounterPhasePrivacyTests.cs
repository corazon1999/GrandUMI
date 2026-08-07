using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class CounterPhasePrivacyTests
{
    [Fact]
    public async Task 只有零反击值手牌时仍等待防守方手动结束反击()
    {
        _ = TestScene.New().Build();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine(
            "counter-phase-privacy",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 1);
        var state = engine.State;
        var defender = state.Players[1];
        var zeroCounterCard = new CardInstance { Info = CardDatabase.Get("OP15-008")! };
        defender.Hand.Clear();
        defender.Hand.Add(zeroCounterCard);
        defender.Characters.Clear();

        Assert.Equal(0, HandStaticCounter.Value(state, 1, zeroCounterCard));
        Assert.DoesNotContain("EventCounter", zeroCounterCard.Info.EffectTags);

        var actions = new List<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex != 1) return;
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            if (document.RootElement.TryGetProperty("lastAction", out var action))
                actions.Add(action.GetString() ?? "");
        };

        BattleEngine.StartAttack(state, state.Players[0].Leader.Id, targetIsLeader: true, targetId: null);
        await BattleEngine.TriggerAttackDeclareAsync(state, new MockPromptService());
        Assert.True(engine.HandleAction(1, "PassBlock", JsonSerializer.SerializeToElement(new { })));
        await engine.WaitSettledAsync();

        Assert.Equal(Phase.BattleCounter, state.Phase);
        Assert.NotNull(state.CurrentBattle);
        Assert.Contains("AwaitCounter", actions);
        Assert.DoesNotContain("ResolveBattle", actions);

        Assert.True(engine.HandleAction(1, "PassCounter", JsonSerializer.SerializeToElement(new { })));
        await engine.WaitSettledAsync();

        Assert.Null(state.CurrentBattle);
        Assert.Contains("BattleEnd", actions);
    }
}
