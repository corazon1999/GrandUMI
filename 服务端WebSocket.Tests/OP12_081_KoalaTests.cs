using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class OP12_081_KoalaTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static Task NotifyCharacterEntered(
        GameState state,
        CardInstance entered,
        IPromptService prompts,
        CardKind? effectSourceKind = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cardId"] = entered.Id.ToString(),
            ["owner"] = 1,
        };
        if (effectSourceKind is not null)
            payload["effectSourceKind"] = effectSourceKind.Value.ToString();
        return EffectRuntime.TriggerEvent(state, EffectTrigger.OnAllyCharEnter, prompts, payload);
    }

    private static async Task<PendingPrompt?> WaitForCompletionOrPrompt(
        GameEngine engine,
        Task operation)
    {
        for (var i = 0; i < 200; i++)
        {
            if (operation.IsCompleted) return null;
            if (engine.State.PendingPrompt is { } prompt) return prompt;
            await Task.Delay(5);
        }
        throw new TimeoutException("等待生命触发结算完成或产生交互超时");
    }

    [Fact]
    public async Task 攻击对方领袖时_两张当前费用八的角色会抽一张牌()
    {
        var state = TestScene.New("OP12-081")
            .MyCharacter("OP12-087")
            .MyCharacter("OP12-087")
            .MyDeckTop("OP15-003")
            .Build();
        var me = state.Players[0];
        var prompts = new MockPromptService();

        foreach (var robin in me.Characters)
            await EffectRuntime.Resolve(state, 0, robin, EffectTrigger.OnEnterField, prompts);

        Assert.All(me.Characters, robin => Assert.True(state.CurrentCostOf(0, robin) >= 8));
        Assert.All(me.Characters, robin => Assert.True(robin.Info.Cost < 8));

        BattleEngine.StartAttack(state, me.Leader.Id, targetIsLeader: true, targetId: null);
        await BattleEngine.TriggerAttackDeclareAsync(state, prompts);

        Assert.Single(me.Hand);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task 对方通常登场原本费用八的角色时_会让对方生命顶加入手牌()
    {
        var state = TestScene.New("OP12-081").OppCharacter("OP16-003").Build();
        var opponent = state.Players[1];
        var entered = Assert.Single(opponent.Characters);
        var life = Card("OP15-003");
        opponent.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);

        await NotifyCharacterEntered(state, entered, prompts);

        Assert.Empty(opponent.LifeArea);
        Assert.Contains(life, opponent.Hand);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task 对方通过角色效果登场低费用角色时_仍会触发()
    {
        var state = TestScene.New("OP12-081").OppCharacter("OP16-013").Build();
        var opponent = state.Players[1];
        var entered = Assert.Single(opponent.Characters);
        var life = Card("OP15-003");
        opponent.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);

        await NotifyCharacterEntered(state, entered, prompts, CardKind.Character);

        Assert.Empty(opponent.LifeArea);
        Assert.Contains(life, opponent.Hand);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task 对方因生命触发效果登场角色时_不会触发()
    {
        var state = TestScene.New("OP12-081").Build();
        var opponent = state.Players[1];
        var triggerCharacter = Card("OP16-111");
        var life = Card("OP15-003");
        opponent.Trash.Add(triggerCharacter);
        opponent.LifeArea.Add(life);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(
            state,
            1,
            triggerCharacter,
            EffectTrigger.OnLifeRevealTrigger,
            prompts,
            lifeTriggerOrigin: true);

        Assert.Contains(triggerCharacter, opponent.Characters);
        Assert.Contains(life, opponent.LifeArea);
        Assert.DoesNotContain(life, opponent.Hand);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.DoesNotContain($"OP12-081-trigger:{state.Players[0].Leader.Id}",
            state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task 真实生命揭示流程会把触发登场来源传递给克尔拉()
    {
        _ = TestScene.New().Build();
        string koalaDeck = "OP12-081\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        string opponentDeck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine(
            "op12-081-life-trigger-test",
            ("s0", "p0", koalaDeck),
            ("s1", "p1", opponentDeck),
            firstPlayer: 0,
            rngSeed: 1);
        var opponent = engine.State.Players[1];
        var triggerCharacter = Card("OP16-111");
        var remainingLife = Card("OP15-003");
        opponent.LifeArea.Clear();
        opponent.Hand.Clear();
        opponent.Trash.Clear();
        opponent.Characters.Clear();
        opponent.LifeArea.AddRange([triggerCharacter, remainingLife]);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 1, 1);
        var lifePrompt = await WaitForCompletionOrPrompt(engine, damage);
        Assert.NotNull(lifePrompt);
        Assert.Equal("LifeTrigger", lifePrompt.Kind);
        engine.Prompts.Resolve(lifePrompt.PromptId, ["trigger"]);

        var unexpectedPrompt = await WaitForCompletionOrPrompt(engine, damage);
        if (unexpectedPrompt is not null)
        {
            // 失败路径也完成异步结算，避免给后续测试遗留悬挂任务。
            engine.Prompts.Resolve(unexpectedPrompt.PromptId,
                unexpectedPrompt.Kind == "Option" ? ["1"] : unexpectedPrompt.ValidChoices.Take(1).ToArray());
        }
        await damage.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(unexpectedPrompt);
        Assert.Contains(triggerCharacter, opponent.Characters);
        Assert.Equal([remainingLife], opponent.LifeArea);
        Assert.Empty(opponent.Hand);
        Assert.Empty(engine.State.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task 可选效果取消后仍可发动_成功发动后同回合不再重复触发()
    {
        var state = TestScene.New("OP12-081")
            .OppCharacter("OP16-003")
            .OppCharacter("OP16-005")
            .OppCharacter("OP16-030")
            .Build();
        var opponent = state.Players[1];
        var lifeTop = Card("OP15-003");
        var lifeBottom = Card("OP15-004");
        opponent.LifeArea.AddRange([lifeTop, lifeBottom]);
        var prompts = new MockPromptService()
            .QueueConfirm(false)
            .QueueConfirm(true);

        await NotifyCharacterEntered(state, opponent.Characters[0], prompts);
        Assert.Equal(2, opponent.LifeArea.Count);
        Assert.Empty(state.Players[0].TurnOnceUsed);

        await NotifyCharacterEntered(state, opponent.Characters[1], prompts);
        await NotifyCharacterEntered(state, opponent.Characters[2], prompts);

        Assert.Equal([lifeBottom], opponent.LifeArea);
        Assert.Contains(lifeTop, opponent.Hand);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
        Assert.Single(state.Players[0].TurnOnceUsed);
    }
}
