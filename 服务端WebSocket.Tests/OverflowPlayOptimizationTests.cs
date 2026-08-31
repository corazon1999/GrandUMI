using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OverflowPlayOptimizationTests
{
    [Fact]
    public async Task PlayCard_携带腾位目标时一次请求完成且只广播一次()
    {
        TestScene.New(); // 确保测试卡库已加载
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var character = CardDatabase.GetBySet("OP15")
            .First(c => c.Kind == CardKind.Character && !c.EffectTags.Contains("OnEnterField"));

        PrepareMainPhase(state, character);
        var victim = me.Characters[3];
        var snapshots = new List<JsonElement>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index == 0) snapshots.Add(JsonSerializer.SerializeToElement(payload));
        };

        engine.HandleAction(0, "PlayCard", JsonSerializer.SerializeToElement(new
        {
            handIndex = 0,
            overflowTrashCardId = victim.Id.ToString(),
        }));
        await engine.WaitSettledAsync();

        Assert.Null(state.PendingPrompt);
        Assert.Equal(5, me.Characters.Count);
        Assert.Contains(victim, me.Trash);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.Contains(me.Characters, c => c.Info.Number == character.Number);
        Assert.Empty(me.Hand);
        Assert.Single(snapshots);
        Assert.Equal("PlayCard", snapshots[0].GetProperty("lastAction").GetString());
    }

    [Fact]
    public void PlayCard_腾位目标失效时拒绝且不改动牌桌()
    {
        TestScene.New();
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var character = CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Character);
        PrepareMainPhase(state, character);

        engine.HandleAction(0, "PlayCard", JsonSerializer.SerializeToElement(new
        {
            handIndex = 0,
            overflowTrashCardId = Guid.NewGuid().ToString(),
        }));

        Assert.Equal(5, me.Characters.Count);
        Assert.Single(me.Hand);
        Assert.Empty(me.Trash);
        Assert.Null(state.PendingPrompt);
    }

    [Fact]
    public async Task AttachDon_效果稳定后只发送一份最终快照()
    {
        TestScene.New();
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 2;
        state.Phase = Phase.Main;
        me.CostArea.Clear();
        me.CostArea.Add(new DonCard { State = DonState.Active });

        var actions = CaptureActions(engine);
        engine.HandleAction(0, "AttachDon", JsonSerializer.SerializeToElement(new
        {
            targetId = "leader",
            count = 1,
        }));
        await engine.WaitSettledAsync();

        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(new[] { "AttachDon" }, actions);
    }

    [Fact]
    public async Task 满场旧流程_Prompt立即发送且最终只追加PlayCard快照()
    {
        TestScene.New();
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var character = CardDatabase.GetBySet("OP15")
            .First(c => c.Kind == CardKind.Character && !c.EffectTags.Contains("OnEnterField"));
        PrepareMainPhase(state, character);
        var victim = me.Characters[0];
        var actions = CaptureActions(engine);

        // 不携带 overflowTrashCardId，验证兼容旧客户端的服务端 Prompt 路径。
        engine.HandleAction(0, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 }));
        Assert.NotNull(state.PendingPrompt);
        Assert.Equal(new[] { "Prompt" }, actions);

        engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = state.PendingPrompt!.PromptId,
            chosen = new[] { victim.Id.ToString() },
        }));
        for (var i = 0; i < 100 && state.PendingPrompt is not null; i++)
            await Task.Delay(1);
        await engine.WaitSettledAsync();

        Assert.Null(state.PendingPrompt);
        Assert.Equal(new[] { "Prompt", "PlayCard" }, actions);
    }

    [Fact]
    public async Task 满场旧流程_同卡号角色按实例ID精确腾位()
    {
        TestScene.New();
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var character = CardDatabase.GetBySet("OP15")
            .First(c => c.Kind == CardKind.Character && !c.EffectTags.Contains("OnEnterField"));
        PrepareMainPhase(state, character);

        // PrepareMainPhase 放入的 5 张角色卡号相同，只有实例 ID 不同。
        var originalCharacters = me.Characters.ToList();
        Assert.Single(originalCharacters.Select(c => c.Info.Number).Distinct());
        var victim = originalCharacters[3];
        var untouched = originalCharacters.Where(c => c.Id != victim.Id).ToList();

        engine.HandleAction(0, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 }));
        Assert.NotNull(state.PendingPrompt);

        engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = state.PendingPrompt!.PromptId,
            chosen = new[] { victim.Id.ToString() },
        }));
        for (var i = 0; i < 100 && state.PendingPrompt is not null; i++)
            await Task.Delay(1);
        await engine.WaitSettledAsync();

        Assert.Contains(victim, me.Trash);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.All(untouched, card => Assert.Contains(card, me.Characters));
        Assert.Single(me.Trash);
    }

    [Fact]
    public async Task 无阻挡者战斗_保留Attack与AwaitCounter屏障并等待手动结束反击()
    {
        TestScene.New();
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        me.Leader.IsTapped = false;
        opponent.Hand.Clear();
        opponent.Characters.Clear();
        var targetInfo = CardDatabase.GetBySet("OP15")
            .First(c => c.Kind == CardKind.Character && c.Power <= 4000 && c.EffectTags.Length == 0);
        var target = new CardInstance { Info = targetInfo, IsTapped = true, TurnPlayed = 0 };
        opponent.Characters.Add(target);
        var actions = CaptureActions(engine);

        engine.HandleAction(0, "Attack", JsonSerializer.SerializeToElement(new
        {
            attackerId = me.Leader.Id.ToString(),
            targetIsLeader = false,
            targetId = target.Id.ToString(),
        }));
        await engine.WaitSettledAsync();

        Assert.Equal("Attack", actions[0]);
        Assert.Equal("AwaitCounter", actions[^1]);
        Assert.DoesNotContain("AutoPassBlock", actions);
        Assert.DoesNotContain("ResolveBattle", actions);
        Assert.Equal(2, actions.Count);
        Assert.Equal(Phase.BattleCounter, state.Phase);
        Assert.NotNull(state.CurrentBattle);

        engine.HandleAction(1, "PassCounter", JsonSerializer.SerializeToElement(new { }));
        await engine.WaitSettledAsync();

        Assert.Equal("BattleEnd", actions[^1]);
        Assert.Equal(3, actions.Count);
        Assert.Null(state.CurrentBattle);
    }

    [Fact]
    public async Task 只有非法阻挡者时自动进入反击_重复阻挡与错误方结束反击均不取消战斗()
    {
        TestScene.New();
        var engine = CreateEngine();
        var state = engine.State;
        var attacker = state.Players[0];
        var defender = state.Players[1];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        attacker.Leader.IsTapped = false;
        defender.Hand.Clear();
        defender.Characters.Clear();

        var blockerInfo = CardDatabase.GetBySet("OP15")
            .First(card => card.Kind == CardKind.Character && card.Abilities.Contains("阻挡者"));
        var blockedBlocker = new CardInstance { Info = blockerInfo };
        AtomicOps.AddRestriction(blockedBlocker, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        defender.Characters.Add(blockedBlocker);
        var actions = CaptureActions(engine);

        engine.HandleAction(0, "Attack", JsonSerializer.SerializeToElement(new
        {
            attackerId = attacker.Leader.Id.ToString(),
            targetIsLeader = true,
        }));
        await engine.WaitSettledAsync();

        Assert.Equal(new[] { "Attack", "AwaitCounter" }, actions);
        Assert.Equal(Phase.BattleCounter, state.Phase);
        var battle = Assert.IsType<BattleContext>(state.CurrentBattle);

        // 自动跳过阻挡后到达的重复/乱序 PassBlock 必须被拒绝，不能把战斗清掉。
        engine.HandleAction(1, "PassBlock", JsonSerializer.SerializeToElement(new { }));
        Assert.Same(battle, state.CurrentBattle);
        Assert.Equal(Phase.BattleCounter, state.Phase);

        // 反击阶段永远只允许防守方明确结束；攻击方不能代为跳过。
        engine.HandleAction(0, "PassCounter", JsonSerializer.SerializeToElement(new { }));
        Assert.Same(battle, state.CurrentBattle);
        Assert.Equal(Phase.BattleCounter, state.Phase);

        engine.HandleAction(1, "PassCounter", JsonSerializer.SerializeToElement(new { }));
        await engine.WaitSettledAsync();

        Assert.Null(state.CurrentBattle);
        Assert.Equal(Phase.Main, state.Phase);
    }

    private static List<string> CaptureActions(GameEngine engine)
    {
        var actions = new List<string>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index != 0) return;
            var snapshot = JsonSerializer.SerializeToElement(payload);
            if (snapshot.TryGetProperty("lastAction", out var action))
                actions.Add(action.GetString() ?? "");
        };
        return actions;
    }

    private static void PrepareMainPhase(GameState state, CardInfo playable)
    {
        var me = state.Players[0];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 2;
        state.Phase = Phase.Main;
        me.Hand.Clear();
        me.Hand.Add(new CardInstance { Info = playable });
        me.Characters.Clear();
        var filler = CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Character);
        for (var i = 0; i < 5; i++) me.Characters.Add(new CardInstance { Info = filler });
        me.Trash.Clear();
        me.CostArea.Clear();
        for (var i = 0; i < 10; i++) me.CostArea.Add(new DonCard { State = DonState.Active });
    }

    private static GameEngine CreateEngine()
    {
        var deck = BuildLegalDeck("OP15-001");
        return new GameEngine(
            "overflow-optimization-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260805);
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader))
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
