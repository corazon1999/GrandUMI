using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP04-096 与 OP10-042 缺失效果的定向回归。</summary>
public class OP04_OP10EffectCompletionTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Fact]
    public void OP10_042_RegistersBothLeaveAndKoWatchers()
    {
        _ = TestScene.New().Build();
        var info = CardDatabase.Get("OP10-042")!;
        var script = ScriptedEffectRegistry.TryGet("OP10-042");

        Assert.Contains(nameof(EffectTrigger.OnCharLeaveField), info.EffectTags);
        Assert.Contains(nameof(EffectTrigger.OnAnyCharKOd), info.EffectTags);
        Assert.NotNull(script);
        Assert.True(script.HandlesTrigger(EffectTrigger.OnGameStart));
        Assert.True(script.HandlesTrigger(EffectTrigger.OnCharLeaveField));
        Assert.True(script.HandlesTrigger(EffectTrigger.OnAnyCharKOd));
        Assert.True(OncePerTurnEffectCatalog.Contains("OP10-042"));
    }

    [Fact]
    public async Task OP10_042_DrawsAfterDressrosaCharacterIsKod_OnlyOncePerTurn()
    {
        var state = TestScene.New(myLeaderNumber: "OP10-042")
            .MyDeckTop("OP15-003", "OP15-004")
            .Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        var firstVictim = Card("OP04-091");
        var secondVictim = Card("OP04-092");
        state.Players[0].Trash.Add(firstVictim);
        state.Players[0].Trash.Add(secondVictim);
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnAnyCharKOd, prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = firstVictim.Id.ToString(), ["owner"] = 0, ["reason"] = "battle",
            });
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnAnyCharKOd, prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = secondVictim.Id.ToString(), ["owner"] = 0, ["reason"] = "battle",
            });

        Assert.Single(state.Players[0].Hand);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Contains($"OP10-042-leave:{state.Players[0].Leader.Id}", state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task OP10_042_DrawsWhenOpponentEffectReturnsDressrosaCharacterToHand()
    {
        var state = TestScene.New(myLeaderNumber: "OP10-042")
            .MyCharacter("OP04-091")
            .MyDeckTop("OP15-003")
            .Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        var victim = state.Players[0].Characters.Single();

        // OP07-050 登场时在己方有 2 张《亚马逊·百合》角色时，可将对方 3 费以下角色退回手牌。
        var effectSource = Card("OP07-050", state.TurnCount);
        state.Players[1].Characters.Add(effectSource);
        state.Players[1].Characters.Add(Card("OP07-050"));
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        await EffectRuntime.Resolve(state, 1, effectSource, EffectTrigger.OnEnterField, prompts);

        Assert.DoesNotContain(victim, state.Players[0].Characters);
        Assert.Contains(victim, state.Players[0].Hand);
        Assert.Equal(2, state.Players[0].Hand.Count);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP10_042_DoesNotDrawForOwnTurnOrNonDressrosaCharacter()
    {
        var state = TestScene.New(myLeaderNumber: "OP10-042")
            .MyDeckTop("OP15-003", "OP15-004")
            .Build();
        var dressrosa = Card("OP04-091");
        var unrelated = Card("OP15-003");
        state.Players[0].Trash.Add(dressrosa);
        state.Players[0].Trash.Add(unrelated);
        var prompts = new MockPromptService();

        // 我方回合即使《德莱斯罗兹》角色被 KO 也不发动。
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnAnyCharKOd, prompts,
            new Dictionary<string, object?> { ["cardId"] = dressrosa.Id.ToString(), ["owner"] = 0 });

        // 对方回合中，非《德莱斯罗兹》角色离场也不发动。
        state.CurrentTurnPlayer = 1;
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnAnyCharKOd, prompts,
            new Dictionary<string, object?> { ["cardId"] = unrelated.Id.ToString(), ["owner"] = 0 });

        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP04_096_GrantsCharacterRushAndExposesItInSnapshot()
    {
        var state = TestScene.New(myLeaderNumber: "OP10-042")
            .OppCharacter("OP15-003")
            .Build();
        state.TurnCount = 3;
        var stage = Card("OP04-096");
        var attacker = Card("OP04-091", state.TurnCount);
        var target = state.Players[1].Characters.Single();
        target.IsTapped = true;
        state.Players[0].StageCard = stage;
        state.Players[0].Characters.Add(attacker);

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(ActionValidator.HasKeyword(state, attacker, "速攻：角色"));
        Assert.True(ActionValidator.HasKeyword(state, attacker, "登场回合可攻击角色"));
        Assert.False(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(state, 0, attacker.Id, false, target.Id).Ok);

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 0)));
        var attackerSnapshot = snapshot.RootElement.GetProperty("my").GetProperty("fieldCards")
            .EnumerateArray().Single(card => card.GetProperty("id").GetString() == attacker.Id.ToString());
        Assert.Contains("速攻：角色", attackerSnapshot.GetProperty("gainedKeywords")
            .EnumerateArray().Select(keyword => keyword.GetString()));
        Assert.True(attackerSnapshot.GetProperty("canAttack").GetBoolean());
    }

    [Fact]
    public async Task OP04_096_RequiresBothDressrosaLeaderAndCharacter()
    {
        var dressrosaLeaderState = TestScene.New(myLeaderNumber: "OP10-042").Build();
        dressrosaLeaderState.TurnCount = 3;
        var stage = Card("OP04-096");
        var unrelated = Card("OP15-003", dressrosaLeaderState.TurnCount);
        dressrosaLeaderState.Players[0].StageCard = stage;
        dressrosaLeaderState.Players[0].Characters.Add(unrelated);

        await EffectRuntime.Resolve(dressrosaLeaderState, 0, stage, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(ActionValidator.HasKeyword(dressrosaLeaderState, unrelated, "速攻：角色"));

        var unrelatedLeaderState = TestScene.New(myLeaderNumber: "OP15-001").Build();
        unrelatedLeaderState.TurnCount = 3;
        var secondStage = Card("OP04-096");
        var dressrosa = Card("OP04-091", unrelatedLeaderState.TurnCount);
        unrelatedLeaderState.Players[0].StageCard = secondStage;
        unrelatedLeaderState.Players[0].Characters.Add(dressrosa);

        await EffectRuntime.Resolve(unrelatedLeaderState, 0, secondStage,
            EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(ActionValidator.HasKeyword(unrelatedLeaderState, dressrosa, "速攻：角色"));
    }
}
