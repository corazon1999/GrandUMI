using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>QQ群最新反馈的选择归属、返咚归属、批量保护及现行规则回归。</summary>
public sealed class QqFeedback20261030Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static GameEngine Engine(int owner, string leader = "OP12-061")
    {
        _ = TestScene.New();
        var engine = new GameEngine("QQ群反馈专项", ("s0", "p0", leader + "\nOP15-003"),
            ("s1", "p1", leader + "\nOP15-003"), firstPlayer: 0, rngSeed: 30);
        engine.State.Phase = Phase.Main; engine.State.TurnCount = 5; engine.State.CurrentTurnPlayer = owner;
        foreach (var player in engine.State.Players)
        {
            player.Hand.Clear(); player.Characters.Clear(); player.LifeArea.Clear();
            player.Deck.Clear(); player.Deck.Add(Card("OP15-003"));
        }
        return engine;
    }
    private static void Reply(GameEngine engine, int owner, params string[] chosen)
        => engine.HandleAction(owner, "PromptResponse", JsonSerializer.SerializeToElement(
            new { promptId = engine.State.PendingPrompt!.PromptId, chosen }));
    private static CardInstance Law(int cost) => new() { Info = new CardInfo
    {
        Number = "测试罗" + cost, Name = "特拉法尔加·罗", Kind = CardKind.Character,
        Cost = cost, Power = 5000, Color = "紫", Property = "斩",
    } };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 贝莱特王子受伤进入手牌时没有不存在的生命触发(int owner)
    {
        var engine = Engine(owner); var me = engine.State.Players[owner]; var prince = Card("EB01-026");
        Assert.Empty(prince.Info.Trigger);
        Assert.False(EffectRuntime.HasEffectForTrigger(prince, EffectTrigger.OnLifeRevealTrigger));
        me.LifeArea.Add(prince);
        await LifeRevealManager.DealDamageToLeader(engine, owner, 1).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(prince, me.Hand); Assert.Null(engine.State.PendingPrompt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 马奇弹主要效果由对手选择自己的角色(int owner)
    {
        var engine = Engine(owner); var state = engine.State; var opponent = state.Players[1 - owner];
        var valid = Law(7); valid.CostModThisTurn = -1;
        var invalid = Law(7); opponent.Characters.AddRange([valid, invalid]);
        var task = EffectRuntime.Resolve(state, owner, Card("OP09-058"), EffectTrigger.EventMain, engine.Prompts);
        var prompt = state.PendingPrompt;
        Assert.NotNull(prompt); Assert.Equal(1 - owner, prompt!.PlayerIndex);
        Assert.Contains(valid.Id.ToString(), prompt.ValidChoices); Assert.DoesNotContain(invalid.Id.ToString(), prompt.ValidChoices);
        Assert.Equal(1, prompt.MinChoose); Assert.Equal(1, prompt.MaxChoose);
        Reply(engine, 1 - owner, valid.Id.ToString()); await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(valid, opponent.Hand); Assert.Contains(invalid, opponent.Characters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 马奇弹生命触发由发动方选择双方角色(int owner)
    {
        var engine = Engine(owner); var state = engine.State;
        var own = Law(3); var opposing = Law(3);
        state.Players[owner].Characters.Add(own); state.Players[1 - owner].Characters.Add(opposing);
        var task = EffectRuntime.Resolve(state, owner, Card("OP09-058"), EffectTrigger.OnLifeRevealTrigger, engine.Prompts);
        Assert.Equal(owner, state.PendingPrompt!.PlayerIndex);
        Assert.Contains(own.Id.ToString(), state.PendingPrompt.ValidChoices);
        Assert.Contains(opposing.Id.ToString(), state.PendingPrompt.ValidChoices);
        Assert.Equal(0, state.PendingPrompt.MinChoose);
        Reply(engine, owner, own.Id.ToString()); await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(own, state.Players[owner].Hand);
        Assert.Contains(opposing, state.Players[1 - owner].Characters);
    }

    [Theory]
    [InlineData(0, true, false, 2)]
    [InlineData(1, true, false, 2)]
    [InlineData(0, false, true, 2)]
    [InlineData(1, false, true, 2)]
    [InlineData(0, true, true, 1)]
    [InlineData(1, true, true, 1)]
    public async Task 山治布玲不响应对方返咚或错误回合且不占用后续我方机会(int owner, bool ownTurn, bool ownDon, int count)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = ownTurn ? owner : 1 - owner;
        var me = state.Players[owner]; var source = Card("EB02-035"); me.Characters.Add(source);
        me.DonDeck.AddRange([new DonCard(), new DonCard()]);
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnDonReturnedToDeck, prompts,
            new Dictionary<string, object?> { ["owner"] = ownDon ? owner : 1 - owner, ["count"] = count });
        Assert.Empty(prompts.ConfirmHistory); Assert.Empty(me.CostArea); Assert.Empty(me.TurnOnceUsed);
        state.CurrentTurnPlayer = owner;
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnDonReturnedToDeck, prompts,
            new Dictionary<string, object?> { ["owner"] = owner, ["count"] = 2 });
        Assert.Single(prompts.ConfirmHistory); Assert.Equal(DonState.Active, Assert.Single(me.CostArea).State);
    }

    [Theory]
    [InlineData(0, "EB04-059")]
    [InlineData(1, "EB04-059")]
    [InlineData(0, "OP10-098")]
    [InlineData(1, "OP10-098")]
    public async Task 同一事件一次保护四费六费罗且下一次效果不能再保护(int attacker, string eventNumber)
    {
        var state = TestScene.New("OP12-061", "OP12-061").Build(); state.CurrentTurnPlayer = attacker;
        var me = state.Players[attacker]; var defender = state.Players[1 - attacker];
        var first = Law(6); var second = Law(4); defender.Characters.AddRange([first, second]);
        me.LifeArea.Add(Card("OP15-003")); defender.LifeArea.Add(Card("OP15-004"));
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString()).QueueChoose(second.Id.ToString());
        await EffectRuntime.Resolve(state, attacker, Card(eventNumber), EffectTrigger.EventMain, prompts);
        Assert.Equal(2, defender.Characters.Count); Assert.Empty(defender.LifeArea); Assert.Single(defender.Hand);
        Assert.Equal(eventNumber == "EB04-059" ? 2 : 1, prompts.ConfirmHistory.Count);
        Assert.True(await AtomicOps.KOByEffectAsync(state, 1 - attacker, first, new MockPromptService(), attacker));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task 屠魔令允许佐罗一次返三张代替整批对方效果离场(int attacker, bool guardLast)
    {
        var state = TestScene.New("OP18-079", "OP18-079").Build(); state.CurrentTurnPlayer = attacker;
        var me = state.Players[attacker]; var defender = state.Players[1 - attacker];
        me.LifeArea.Add(Card("OP15-003"));
        var zoro = Card("OP17-095"); var ally = Law(4);
        defender.Characters.AddRange(guardLast ? [ally, zoro] : [zoro, ally]);
        var costs = new[] { Card("OP15-003"), Card("OP15-004"), Card("OP15-005") };
        defender.Trash.AddRange(costs);
        var prompts = new MockPromptService().QueueChoose(costs.Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, attacker, Card("OP18-117"), EffectTrigger.EventMain, prompts);
        Assert.Contains(zoro, defender.Characters); Assert.Contains(ally, defender.Characters);
        Assert.Empty(defender.Trash); Assert.Equal(costs, defender.Deck);
        Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "OwnTrashToDeckBottom"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 十费洛克斯可以选择本次刚抽到的角色(int owner)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var source = Card("OP17-118"); var drawn = Card("OP17-045");
        me.Characters.Add(source); me.Deck.AddRange([drawn, Card("OP15-003"), Card("OP15-004")]);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(drawn.Id.ToString()).QueueChooseEmpty());
        Assert.Contains(drawn, me.Characters); Assert.DoesNotContain(drawn, me.Hand);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task 新奈美生命触发登场后只在我方回合补生命(int owner, bool ownTurn)
    {
        var state = TestScene.New("EB05-010", "EB05-010").Build();
        state.CurrentTurnPlayer = ownTurn ? owner : 1 - owner;
        var me = state.Players[owner]; var nami = Card("EB05-055"); var cost = Card("OP15-003");
        me.Trash.Add(nami); me.Hand.Add(cost);
        me.LifeArea.AddRange([Card("OP15-003"), Card("OP15-004")]);
        me.Deck.AddRange([Card("OP15-003"), Card("OP15-004")]);
        await EffectRuntime.Resolve(state, owner, nami, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(cost.Id.ToString()));
        Assert.Contains(nami, me.Characters);
        Assert.Equal(ownTurn ? 3 : 2, me.LifeArea.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 一费佐罗在先后手位置都可以休息合法角色(int owner)
    {
        var state = TestScene.New("ST32-001", "ST32-001").Build();
        state.CurrentTurnPlayer = owner; state.TurnCount = owner + 1;
        var source = Card("ST32-005"); var target = Card("OP17-023");
        state.Players[owner].Characters.Add(source); state.Players[1 - owner].Characters.Add(target);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(target.IsTapped);
    }
}
