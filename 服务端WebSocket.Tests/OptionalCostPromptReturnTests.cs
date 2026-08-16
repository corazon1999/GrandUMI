using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class OptionalCostPromptReturnTests
{
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

        throw new TimeoutException("等待成本返回测试 Prompt 超时");
    }

    private static GameEngine CreateEngine(string leaderNumber = "OP17-039", string setCode = "OP17")
    {
        _ = TestScene.New(leaderNumber);
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet(setCode)
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leader.Number };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
            lines.Add(card.Number);
        }

        var deck = string.Join('\n', lines);
        return new GameEngine(
            "optional-cost-return",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260817);
    }

    [Fact]
    public async Task 取消必选成本后应返回发动确认并可重新进入成本选择()
    {
        var engine = CreateEngine();
        var prompts = engine.Prompts;
        var me = engine.State.Players[0];
        var choices = me.Hand.Take(2).Select(card => card.Id.ToString()).ToArray();
        const string confirmText = "伊佐【对方的攻击时】：丢弃手牌2张，使对方力量-2000？";

        var confirmTask = prompts.ConfirmOptional(0, confirmText);
        var initialConfirm = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        prompts.Resolve(initialConfirm.PromptId, new[] { "0" });
        Assert.True(await confirmTask);

        // 网络对局处理 PromptResponse 时会推进这些外围流程状态；它们不代表已经支付成本。
        engine.State.Tick++;
        engine.State.OperationClockRemainingMs[0] -= 1000;
        engine.State.Phase = Phase.BattleAttack;
        engine.State.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = engine.State.Players[1].Leader.Id,
            TargetIsLeader = true,
        };

        // 同一效果在成本前出现的非成本选择不能提前吃掉返回上下文。
        var intermediateTask = prompts.ChooseCards(
            0,
            "OpponentCharacter",
            "选择对方1张角色",
            new[] { choices[0] },
            min: 1,
            max: 1);
        var intermediatePrompt = await WaitForPrompt(engine, prompt => prompt.Kind == "OpponentCharacter");
        Assert.False(intermediatePrompt.Extra.ContainsKey("canReturnToEffectConfirm"));
        prompts.Resolve(intermediatePrompt.PromptId, new[] { choices[0] });
        Assert.Equal(new[] { choices[0] }, await intermediateTask);

        var costTask = prompts.ChooseCards(
            0,
            "OwnHand",
            "丢弃我方2张手牌",
            choices,
            min: 2,
            max: 2);
        var firstCost = await WaitForPrompt(engine, prompt => prompt.Kind == "OwnHand");
        Assert.True((bool)firstCost.Extra["canReturnToEffectConfirm"]!);
        var returnChoices = Assert.IsType<string[]>(firstCost.Extra["returnChoiceIds"]);
        Assert.Equal(2, returnChoices.Length);
        Assert.All(returnChoices, choice => Assert.Contains(choice, firstCost.ValidChoices));

        Assert.True(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = firstCost.PromptId,
            chosen = returnChoices,
        })));
        var returnedConfirm = await WaitForPrompt(
            engine,
            prompt => prompt.Kind == "Option" && prompt.PromptId != initialConfirm.PromptId);
        Assert.Equal(confirmText, returnedConfirm.PromptText);

        prompts.Resolve(returnedConfirm.PromptId, new[] { "0" });
        var secondCost = await WaitForPrompt(
            engine,
            prompt => prompt.Kind == "OwnHand" && prompt.PromptId != firstCost.PromptId);
        prompts.Resolve(secondCost.PromptId, choices);

        Assert.Equal(choices, await costTask);
        Assert.Null(engine.State.PendingPrompt);
    }

    [Fact]
    public async Task 返回后放弃发动应结束成本选择并恢复每回合一次标记()
    {
        var engine = CreateEngine();
        var prompts = engine.Prompts;
        var me = engine.State.Players[0];
        var choice = me.Hand[0].Id.ToString();
        const string usageKey = "optional-cost-test";

        var confirmTask = prompts.ConfirmOptional(
            0,
            "山智【对方的攻击时】：丢弃手牌中1张事件，使我方力量+2000？");
        var initialConfirm = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        prompts.Resolve(initialConfirm.PromptId, new[] { "0" });
        Assert.True(await confirmTask);

        me.TurnOnceUsed.Add(usageKey);
        var costTask = prompts.ChooseCards(
            0,
            "OwnHandDiscard",
            "丢弃手牌中的1张事件",
            new[] { choice },
            min: 1,
            max: 1);
        var costPrompt = await WaitForPrompt(engine, prompt => prompt.Kind == "OwnHandDiscard");
        var returnChoices = Assert.IsType<string[]>(costPrompt.Extra["returnChoiceIds"]);

        prompts.Resolve(costPrompt.PromptId, returnChoices);
        var returnedConfirm = await WaitForPrompt(
            engine,
            prompt => prompt.Kind == "Option" && prompt.PromptId != initialConfirm.PromptId);
        prompts.Resolve(returnedConfirm.PromptId, new[] { "1" });

        await Assert.ThrowsAsync<OptionalEffectDeclinedException>(() => costTask);
        Assert.DoesNotContain(usageKey, me.TurnOnceUsed);
        Assert.Null(engine.State.PendingPrompt);
    }

    [Fact]
    public async Task 已先支付状态成本时后续目标选择不应错误提供返回()
    {
        var engine = CreateEngine();
        var prompts = engine.Prompts;
        var me = engine.State.Players[0];
        var choice = me.Deck[0].Id.ToString();

        var confirmTask = prompts.ConfirmOptional(
            0,
            "空扎【攻击时】：将我方活跃领袖力量-5000，使对方角色力量-3000？");
        var initialConfirm = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        prompts.Resolve(initialConfirm.PromptId, new[] { "0" });
        Assert.True(await confirmTask);

        me.Leader.PowerModThisTurn -= 5000;
        var targetTask = prompts.ChooseCards(
            0,
            "OpponentCharacter",
            "选择对方角色，本回合力量-3000",
            new[] { choice },
            min: 1,
            max: 1);
        var targetPrompt = await WaitForPrompt(engine, prompt => prompt.Kind == "OpponentCharacter");

        Assert.False(targetPrompt.Extra.ContainsKey("canReturnToEffectConfirm"));
        prompts.Resolve(targetPrompt.PromptId, new[] { choice });
        Assert.Equal(new[] { choice }, await targetTask);
    }

    [Fact]
    public async Task 真实伊佐效果返回后不发动应正常结束且不支付成本()
    {
        var engine = CreateEngine("EB01-001", "EB01");
        var me = engine.State.Players[0];
        var source = new CardInstance { Info = CardDatabase.Get("EB01-002")! };
        me.Characters.Add(source);
        var handBefore = me.Hand.Select(card => card.Id).ToArray();

        var resolveTask = EffectRuntime.Resolve(
            engine.State,
            0,
            source,
            EffectTrigger.OnOppAttackDeclare,
            engine.Prompts);
        var initialConfirm = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        engine.Prompts.Resolve(initialConfirm.PromptId, new[] { "0" });

        var costPrompt = await WaitForPrompt(engine, prompt => prompt.Kind == "OwnHand");
        var returnChoices = Assert.IsType<string[]>(costPrompt.Extra["returnChoiceIds"]);
        engine.Prompts.Resolve(costPrompt.PromptId, returnChoices);

        var returnedConfirm = await WaitForPrompt(
            engine,
            prompt => prompt.Kind == "Option" && prompt.PromptId != initialConfirm.PromptId);
        engine.Prompts.Resolve(returnedConfirm.PromptId, new[] { "1" });

        await resolveTask;
        Assert.Equal(handBefore, me.Hand.Select(card => card.Id));
        Assert.DoesNotContain(me.TurnOnceUsed, key => key.Contains("EB01-002-oppatk", StringComparison.Ordinal));
        Assert.Null(engine.State.PendingPrompt);
    }

    [Fact]
    public async Task 弃手防止战斗KO的置换效果返回后放弃应继续完成KO()
    {
        var engine = CreateEngine("EB01-001", "EB01");
        var me = engine.State.Players[0];
        var victim = new CardInstance { Info = CardDatabase.Get("EB01-002")! };
        var source = new CardInstance { Info = CardDatabase.Get("EB02-030")! };
        me.Characters.Add(victim);
        me.Trash.Add(source);
        var handBefore = me.Hand.Select(card => card.Id).ToArray();

        await EffectRuntime.Resolve(
            engine.State,
            0,
            source,
            EffectTrigger.EventCounter,
            engine.Prompts);

        var koTask = BattleEngine.KOCardAsync(engine.State, 0, victim, engine.Prompts);
        var initialConfirm = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        engine.Prompts.Resolve(initialConfirm.PromptId, new[] { "0" });

        var costPrompt = await WaitForPrompt(engine, prompt => prompt.Kind == "OwnHand");
        var returnChoices = Assert.IsType<string[]>(costPrompt.Extra["returnChoiceIds"]);
        engine.Prompts.Resolve(costPrompt.PromptId, returnChoices);

        var returnedConfirm = await WaitForPrompt(
            engine,
            prompt => prompt.Kind == "Option" && prompt.PromptId != initialConfirm.PromptId);
        engine.Prompts.Resolve(returnedConfirm.PromptId, new[] { "1" });

        Assert.True(await koTask);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.Contains(victim, me.Trash);
        Assert.Equal(handBefore, me.Hand.Select(card => card.Id));
        Assert.Null(engine.State.PendingPrompt);
    }
}
