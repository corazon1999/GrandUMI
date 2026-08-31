using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-31 下一批已确认玩家反馈回归。</summary>
public class ConfirmedFeedback20260831NextBatchTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ST36_005_真实攻击触发链_可将目标转移至原本力量五千以上的基德()
    {
        var state = TestScene.New().Build();
        var kid = Card("ST36-005");
        var life = Card("OP15-003");
        life.IsLifeFaceUp = true;
        state.Players[0].Characters.Add(kid);
        state.Players[0].LifeArea.Add(life);
        state.CurrentTurnPlayer = 1;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = true,
        };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(kid.Id.ToString());

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnOppAttackDeclare, prompts,
            new Dictionary<string, object?> { ["AttackerIdx"] = 1 });

        Assert.False(state.CurrentBattle.TargetIsLeader);
        Assert.Equal(kid.Id, state.CurrentBattle.TargetCardId);
        Assert.False(life.IsLifeFaceUp);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "OwnLeaderOrCharacter");
    }

    [Fact]
    public async Task ST36_005_完整动作与提示响应链_最终快照保留重定向目标()
    {
        var engine = CreateEngine("st36-005-full-action");
        var state = engine.State;
        var defender = state.Players[0];
        var attacker = state.Players[1];
        defender.Characters.Clear();
        defender.LifeArea.Clear();
        var kid = Card("ST36-005");
        var life = Card("OP15-003");
        life.IsLifeFaceUp = true;
        defender.Characters.Add(kid);
        defender.LifeArea.Add(life);
        attacker.Leader.IsTapped = false;
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        state.Phase = Phase.Main;
        state.OpeningStage = OpeningStage.Playing;
        state.Players[0].MulliganDone = true;
        state.Players[1].MulliganDone = true;
        state.CurrentBattle = null;
        var snapshots = new List<JsonElement>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index == 0) snapshots.Add(JsonSerializer.SerializeToElement(payload));
        };

        var attack = JsonSerializer.SerializeToElement(new
        {
            attackerId = attacker.Leader.Id.ToString(),
            targetIsLeader = true,
        });
        Assert.True(engine.HandleAction(1, "Attack", attack));
        var confirm = await WaitForPrompt(engine, "Option", playerIndex: 0);
        Assert.True(RespondPrompt(engine, 0, confirm, "0"));
        var redirect = await WaitForPrompt(engine, "OwnLeaderOrCharacter", playerIndex: 0);
        Assert.True(RespondPrompt(engine, 0, redirect, kid.Id.ToString()));
        await engine.WaitSettledAsync();

        Assert.NotNull(state.CurrentBattle);
        Assert.False(state.CurrentBattle!.TargetIsLeader);
        Assert.Equal(kid.Id, state.CurrentBattle.TargetCardId);
        Assert.Equal(Phase.BattleCounter, state.Phase);
        Assert.Null(state.PendingPrompt);
        Assert.False(life.IsLifeFaceUp);
        var latest = snapshots[^1];
        var battle = latest.GetProperty("battle");
        Assert.False(battle.GetProperty("targetIsLeader").GetBoolean());
        Assert.Equal(kid.Id.ToString(), battle.GetProperty("targetCardId").GetString());
    }

    [Fact]
    public async Task EB03_055_KO伤害_OP17_107生命触发可跨玩家提示并登场()
    {
        var engine = CreateEngine("eb03-055-op17-107");
        var state = engine.State;
        var damaged = state.Players[0];
        var robinOwner = state.Players[1];
        damaged.LifeArea.Clear();
        damaged.Characters.Clear();
        damaged.Trash.Clear();
        var daifuku = Card("OP17-107");
        damaged.LifeArea.Add(daifuku);
        var robin = Card("EB03-055");
        robinOwner.Trash.Add(robin);
        state.CurrentTurnPlayer = 0;

        var resolution = EffectRuntime.Resolve(
            state, 1, robin, EffectTrigger.OnKO, engine.Prompts);
        var robinPrompt = await WaitForPrompt(engine, "Option", playerIndex: 1);
        engine.Prompts.Resolve(robinPrompt.PromptId, ["0"]);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger", playerIndex: 0);
        Assert.True(JsonSerializer.SerializeToElement(lifePrompt.Extra)
            .GetProperty("hasRealTrigger").GetBoolean());
        engine.Prompts.Resolve(lifePrompt.PromptId, ["trigger"]);
        await resolution;

        Assert.Contains(daifuku, damaged.Characters);
        Assert.DoesNotContain(daifuku, damaged.LifeArea);
        Assert.DoesNotContain(daifuku, damaged.Trash);
        Assert.Null(state.PendingPrompt);
    }

    [Fact]
    public async Task EB03_055_完整战斗KO链_OP17_107触发后无提示残留()
    {
        var engine = CreateEngine("eb03-055-op17-107-battle");
        var state = engine.State;
        var attackerSide = state.Players[0];
        var defenderSide = state.Players[1];
        attackerSide.Characters.Clear();
        attackerSide.LifeArea.Clear();
        attackerSide.Trash.Clear();
        defenderSide.Characters.Clear();
        var attacker = Card("OP15-003");
        attacker.PowerModThisTurn = 10000;
        attackerSide.Characters.Add(attacker);
        var daifuku = Card("OP17-107");
        attackerSide.LifeArea.Add(daifuku);
        var robin = Card("EB03-055");
        robin.IsTapped = true;
        defenderSide.Characters.Add(robin);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        state.OpeningStage = OpeningStage.Playing;
        state.Players[0].MulliganDone = true;
        state.Players[1].MulliganDone = true;
        state.CurrentBattle = null;

        var attack = JsonSerializer.SerializeToElement(new
        {
            attackerId = attacker.Id.ToString(),
            targetIsLeader = false,
            targetId = robin.Id.ToString(),
        });
        Assert.True(engine.HandleAction(0, "Attack", attack));
        await WaitForPhase(state, Phase.BattleCounter);
        Assert.True(engine.HandleAction(1, "PassCounter", EmptyData()));
        var robinPrompt = await WaitForPrompt(engine, "Option", playerIndex: 1);
        Assert.True(RespondPrompt(engine, 1, robinPrompt, "0"));
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger", playerIndex: 0);
        Assert.True(RespondPrompt(engine, 0, lifePrompt, "trigger"));
        await engine.WaitSettledAsync();

        Assert.Contains(robin, defenderSide.Trash);
        Assert.Contains(daifuku, attackerSide.Characters);
        Assert.DoesNotContain(daifuku, attackerSide.LifeArea);
        Assert.DoesNotContain(daifuku, attackerSide.Trash);
        Assert.Null(state.PendingPrompt);
        Assert.Null(state.CurrentBattle);
        Assert.Equal(Phase.Main, state.Phase);
    }

    [Fact]
    public async Task OP17_062_对方回合归还咚_不进入效果发动队列且不改变咚区()
    {
        var engine = CreateEngine("op17-062-opponent-turn");
        var state = engine.State;
        var kaidoOwner = state.Players[0];
        var kaido = Card("OP17-062");
        kaidoOwner.Characters.Add(kaido);
        kaidoOwner.DonDeck.Clear();
        kaidoOwner.DonDeck.Add(new DonCard { State = DonState.InDeck });
        state.CurrentTurnPlayer = 1;
        var snapshots = new List<JsonElement>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index == 0) snapshots.Add(JsonSerializer.SerializeToElement(payload));
        };

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, engine.Prompts,
            new Dictionary<string, object?> { ["owner"] = 1, ["count"] = 1 });
        engine.Broadcast("OpponentDonReturned");

        Assert.Single(kaidoOwner.DonDeck);
        Assert.Empty(kaidoOwner.CostArea);
        var snapshot = Assert.Single(snapshots);
        Assert.DoesNotContain(snapshot.GetProperty("effectActivations").EnumerateArray(), item =>
            item.GetProperty("sourceId").GetString() == kaido.Id.ToString());
    }

    [Fact]
    public async Task OP17_062_恢复后的Json事件仍按咚持有者过滤()
    {
        var engine = CreateEngine("op17-062-restored-payload");
        var state = engine.State;
        var kaidoOwner = state.Players[0];
        var kaido = Card("OP17-062");
        kaidoOwner.Characters.Add(kaido);
        kaidoOwner.DonDeck.Clear();
        kaidoOwner.DonDeck.Add(new DonCard { State = DonState.InDeck });
        state.CurrentTurnPlayer = 0;
        var snapshots = new List<JsonElement>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index == 0) snapshots.Add(JsonSerializer.SerializeToElement(payload));
        };

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, engine.Prompts,
            new Dictionary<string, object?>
            {
                ["owner"] = JsonSerializer.SerializeToElement(1),
                ["count"] = JsonSerializer.SerializeToElement(1),
            });
        engine.Broadcast("RestoredOpponentDonReturned");

        Assert.Single(kaidoOwner.DonDeck);
        Assert.Empty(kaidoOwner.CostArea);
        var snapshot = Assert.Single(snapshots);
        Assert.DoesNotContain(snapshot.GetProperty("effectActivations").EnumerateArray(), item =>
            item.GetProperty("sourceId").GetString() == kaido.Id.ToString());
    }

    [Fact]
    public async Task OP17_062_我方回合我方归还咚_每回合仅结算一次()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var kaido = Card("OP17-062");
        me.Characters.Add(kaido);
        me.DonDeck.AddRange([
            new DonCard { State = DonState.InDeck },
            new DonCard { State = DonState.InDeck },
        ]);
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        state.CurrentTurnPlayer = 0;
        var payload = new Dictionary<string, object?> { ["owner"] = 0, ["count"] = 1 };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(true);

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck,
            prompts, payload);
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck,
            prompts, payload);

        Assert.Equal(2, me.CostArea.Count);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
        Assert.Single(me.DonDeck);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
    }

    [Fact]
    public async Task OP17_062_最多一张可选择零张但仍消耗本次每回合一次()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var kaido = Card("OP17-062");
        var inDeck = new DonCard { State = DonState.InDeck };
        var rested = new DonCard { State = DonState.Rest };
        me.Characters.Add(kaido);
        me.DonDeck.Add(inDeck);
        me.CostArea.Add(rested);
        state.CurrentTurnPlayer = 0;
        var payload = new Dictionary<string, object?> { ["owner"] = 0, ["count"] = 1 };
        var prompts = new MockPromptService()
            .QueueConfirm(false)
            .QueueConfirm(false);

        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, prompts, payload);
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, prompts, payload);

        Assert.Same(inDeck, Assert.Single(me.DonDeck));
        Assert.Same(rested, Assert.Single(me.CostArea));
        Assert.Equal(DonState.Rest, rested.State);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
        Assert.Contains($"OP17-062-don:{kaido.Id}", me.TurnOnceUsed);
    }

    private static async Task<PendingPrompt> WaitForPrompt(
        GameEngine engine, string kind, int playerIndex)
    {
        for (var i = 0; i < 200; i++)
        {
            if (engine.State.PendingPrompt is { } prompt
                && prompt.Kind == kind
                && prompt.PlayerIndex == playerIndex)
                return prompt;
            await Task.Delay(5);
        }
        throw new TimeoutException($"未等到玩家 {playerIndex} 的 {kind} 提示");
    }

    private static async Task WaitForPhase(GameState state, Phase phase)
    {
        for (var i = 0; i < 200; i++)
        {
            if (state.Phase == phase && state.PendingPrompt is null) return;
            await Task.Delay(5);
        }
        throw new TimeoutException($"未等到阶段 {phase}");
    }

    private static bool RespondPrompt(
        GameEngine engine, int playerIndex, PendingPrompt prompt, params string[] chosen)
        => engine.HandleAction(playerIndex, "PromptResponse",
            JsonSerializer.SerializeToElement(new { promptId = prompt.PromptId, chosen }));

    private static JsonElement EmptyData()
        => JsonSerializer.SerializeToElement(new { });

    private static GameEngine CreateEngine(string suffix)
    {
        var deck = BuildLegalDeck("OP15-001");
        return new GameEngine(
            $"confirmed-feedback-{suffix}",
            ($"session-0-{suffix}", $"account-0-{suffix}", deck),
            ($"session-1-{suffix}", $"account-1-{suffix}", deck),
            firstPlayer: 0,
            rngSeed: 20260831);
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            var count = counts.GetValueOrDefault(card.Number);
            if (count >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = count + 1;
        }
        return string.Join('\n', lines);
    }
}
