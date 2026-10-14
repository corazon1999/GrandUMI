using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>对局终止、真实生命触发与第三批卡效反馈回归。</summary>
public sealed class PlayerFeedback20261014FlowTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static GameEngine Engine()
    {
        _ = TestScene.New();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 15));
        var engine = new GameEngine("feedback-flow", ("s0", "p0", deck), ("s1", "p1", deck), 0, rngSeed: 23);
        engine.State.Phase = Phase.Main;
        foreach (var player in engine.State.Players)
        {
            player.Characters.Clear(); player.Hand.Clear(); player.Trash.Clear(); player.LifeArea.Clear();
        }
        return engine;
    }

    [Fact]
    public async Task 龙马可KO减费后当前费用四以内的休息角色()
    {
        var state = TestScene.New().OppCharacter("OP15-003").Build();
        var target = state.Players[1].Characters[0];
        target.IsTapped = true;
        target.CostModThisTurn = -2;
        await EffectRuntime.Resolve(state, 0, Card("OP06-036"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public async Task 马幻刃先抽牌加攻再按废弃十张门槛KO(int trashCount)
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-003").OppCharacter("OP06-052").Build();
        var me = state.Players[0];
        var target = state.Players[1].Characters[0];
        me.Trash.AddRange(Enumerable.Range(0, trashCount).Select(_ => Card("OP15-003")));
        await EffectRuntime.Resolve(state, 0, Card("OP16-101"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()).QueueChoose(target.Id.ToString()));
        Assert.Single(me.Hand);
        Assert.Equal(3000, me.Leader.PowerModThisTurn);
        Assert.Equal(trashCount >= 10, state.Players[1].Trash.Contains(target));
    }

    [Fact]
    public async Task 满场生命触发登场必须由玩家选择腾位并保留其余角色()
    {
        var engine = Engine();
        var me = engine.State.Players[0];
        var source = Card("EB05-056");
        me.LifeArea.Add(source);
        me.Characters.AddRange(Enumerable.Range(0, 5).Select(_ => Card("OP15-003")));
        var original = me.Characters.ToArray();
        var victim = original[3];
        var kinds = new List<string>();
        var answered = new HashSet<string>();
        engine.OnSendToPlayer = (_, _) =>
        {
            if (engine.State.PendingPrompt is not { } prompt || !answered.Add(prompt.PromptId)) return;
            kinds.Add(prompt.Kind);
            string[] choices = prompt.Kind switch
            {
                "LifeTrigger" => ["trigger"],
                "OverflowTrash" => [victim.Id.ToString()],
                "Option" => ["1"],
                _ => [],
            };
            engine.Prompts.Resolve(prompt.PromptId, choices);
        };
        await LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        Assert.Contains("OverflowTrash", kinds);
        Assert.Contains(victim, me.Trash);
        Assert.Contains(source, me.Characters);
        Assert.All(original.Where(card => card != victim), card => Assert.Contains(card, me.Characters));
        Assert.Equal(5, me.Characters.Count);
    }

    [Fact]
    public async Task 对手投降清除成本提示并中止等待中的结算()
    {
        var engine = Engine();
        var waiting = engine.Prompts.ChooseCards(0, "ReturnOwnDon", "选择成本", ["don"], 1, 1);
        Assert.NotNull(engine.State.PendingPrompt);
        engine.HandleAction(1, "Surrender", JsonSerializer.SerializeToElement(new { }));
        Assert.Null(engine.State.PendingPrompt);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(engine.State.IsGameOver);
    }

    [Fact]
    public async Task 被盖德无效的规则别名不能让罗西南德保护KO()
    {
        var state = TestScene.New("OP12-061").MyCharacter("EB04-038").Build();
        var me = state.Players[0];
        var target = me.Characters[0];
        me.LifeArea.Add(Card("OP15-003"));
        target.IsEffectsNullified = true;
        Assert.False(target.MatchesName("特拉法尔加·罗"));
        Assert.True(await AtomicOps.KOByEffectAsync(state, 0, target, new MockPromptService(), 1));
        Assert.Single(me.LifeArea);
        Assert.Contains(target, me.Trash);
        Assert.True(target.MatchesName("特拉法尔加·罗", state));
    }

    [Fact]
    public async Task 斩特双属性战斗不能KO巴基且登场效果无效不抹去持续防护()
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP03-008").OppCharacter("ST25-003").Build();
        var source = state.Players[0].Characters[0];
        var attacker = state.Players[1].Characters[0];
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(attacker.HasProperty("斩"));
        state.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = attacker.Id, TargetCardId = source.Id, TargetIsLeader = false };
        Assert.True(state.IsKoGuarded(source, "battle"));
    }

    [Fact]
    public async Task 邦尼可同时登场两张两千以下角色()
    {
        var state = TestScene.New("OP14-001").MyCharacter("EB05-001").MyHandAdd("OP15-004").MyHandAdd("OP15-004")
            .MyDeckTop("OP15-003", "OP15-003", "OP15-003").Build();
        var me = state.Players[0];
        var selected = me.Hand.ToArray();
        await EffectRuntime.Resolve(state, 0, me.Characters[0], EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(selected.Select(c => c.Id.ToString()).ToArray()));
        Assert.All(selected, card => Assert.Contains(card, me.Characters));
    }

    [Fact]
    public async Task ST30加三千不受其它角色KO后附咚总数影响()
    {
        var state = TestScene.New("ST30-001").MyCharacter("OP12-015").MyCharacter("OP15-004").Build();
        var me = state.Players[0];
        var luffy = me.Characters[0];
        var victim = me.Characters[1];
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, luffy, EffectTrigger.OnEnterField, new MockPromptService());
        me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ => new DonCard { State = DonState.Attached, AttachedToCardId = victim.Id }));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(luffy.Info.Power + 5000, state.CurrentPowerOf(0, luffy));
        await AtomicOps.KOByEffectAsync(state, 0, victim, new MockPromptService(), 1);
        Assert.Equal(luffy.Info.Power + 3000, state.CurrentPowerOf(0, luffy));
    }
}
