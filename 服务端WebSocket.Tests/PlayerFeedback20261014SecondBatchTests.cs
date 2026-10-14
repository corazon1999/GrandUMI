using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>真实结算链上的持续能力、离场保护与可选收益回归。</summary>
public sealed class PlayerFeedback20261014SecondBatchTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 希流在黑胡子无效登场时的场合仍有阻挡及加费且KO可发动()
    {
        var state = TestScene.New("OP09-081").MyCharacter("PRB02-015").OppCharacter("OP06-052").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var target = state.Players[1].Characters[0];
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(state.HasContinuousKeyword(source, "阻挡者"));
        Assert.Equal(8, state.CurrentCostOf(0, source));
        me.Characters.Remove(source);
        me.Trash.Add(source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task 路飞返咚后活跃与休息追加分别可选且不会重复触发(int selection)
    {
        var state = TestScene.New("OP09-061").Build();
        var me = state.Players[0];
        me.DonDeck.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.InDeck }));
        var prompts = new MockPromptService().QueueConfirm((selection & 1) != 0).QueueConfirm((selection & 2) != 0);
        var payload = new Dictionary<string, object?> { ["owner"] = 0, ["count"] = 2 };
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, prompts, payload);
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, prompts, payload);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
        Assert.Equal((selection & 1) != 0 ? 1 : 0, me.ActiveDonCount);
        Assert.Equal((selection & 2) != 0 ? 1 : 0, me.RestDonCount);
    }

    [Fact]
    public async Task 荒牧目标被保护后仍加三千并消耗每回合一次()
    {
        var state = TestScene.New().MyCharacter("OP06-043").MyHandAdd("OP15-003").MyHandAdd("OP15-004")
            .OppCharacter("OP17-095").Build();
        var me = state.Players[0];
        var opp = state.Players[1];
        var source = me.Characters[0];
        var target = opp.Characters[0];
        target.CostModThisTurn = 2 - target.Info.Cost;
        opp.Trash.AddRange(Enumerable.Range(0, 3).Select(_ => Card("OP15-003")));
        var prompts = new MockPromptService().QueueChoose(me.Hand[0].Id.ToString()).QueueChoose(target.Id.ToString())
            .QueueChoose(opp.Trash.Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Contains(target, opp.Characters);
        Assert.Equal(3000, source.PowerModThisTurn);
        Assert.Contains($"{source.Id}-Activated", me.TurnOnceUsed);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Single(me.Hand);
        Assert.Equal(3000, source.PowerModThisTurn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 库赞不会被对方回转弹移走但整卡无效后可回手(bool nullified)
    {
        var state = TestScene.New("EB01-021", "OP09-081").OppCharacter("OP10-082").Build();
        var target = state.Players[1].Characters[0];
        target.IsEffectsNullified = nullified;
        await EffectRuntime.Resolve(state, 1, state.Players[1].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 1, target, EffectTrigger.OnEnterField, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, Card("EB01-028"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChooseEmpty().QueueChoose(target.Id.ToString()));
        Assert.Equal(!nullified, state.Players[1].Characters.Contains(target));
        Assert.Equal(nullified, state.Players[1].Hand.Contains(target));
    }

    [Fact]
    public async Task 库赞免疫对方效果KO但不免疫自身成本()
    {
        var state = TestScene.New().MyCharacter("OP10-082").Build();
        var me = state.Players[0];
        var target = me.Characters[0];
        await EffectRuntime.Resolve(state, 0, target, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(await AtomicOps.KOByEffectAsync(state, 0, target, new MockPromptService(), 1));
        Assert.Contains(target, me.Characters);
        Assert.True(await AtomicOps.KOByEffectAsync(state, 0, target, new MockPromptService(), 0));
    }

    [Fact]
    public async Task 蛮牛突进力量持续到下个对方结束阶段()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004").Build();
        var leader = state.Players[0].Leader;
        await EffectRuntime.Resolve(state, 0, Card("OP15-019"), EffectTrigger.EventMain, new MockPromptService());
        TurnEngine.EnterEndPhase(state);
        TurnEngine.StartNextTurnAtReset(state);
        Assert.Equal(leader.Info.Power + 1000, state.CurrentPowerOf(0, leader));
        TurnEngine.EnterEndPhase(state);
        TurnEngine.StartNextTurnAtReset(state);
        Assert.Equal(leader.Info.Power, state.CurrentPowerOf(0, leader));
    }

    [Fact]
    public async Task 九费罗交换当前原本力量且不叠加已变八千的旧差值()
    {
        var state = TestScene.New("OP14-001").MyCharacter("OP17-005").MyCharacter("OP14-009")
            .MyHandAdd("OP15-003").MyHandAdd("OP15-004").Build();
        var me = state.Players[0];
        var whitebeard = me.Characters[0];
        await EffectRuntime.Resolve(state, 0, whitebeard, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(8000, state.OriginalPowerOf(0, me.Leader));
        me.Leader.PowerModThisBattle = 1000;
        var prompts = new MockPromptService().QueueChoose(me.Hand.Select(c => c.Id.ToString()).ToArray())
            .QueueChoose(whitebeard.Id.ToString());
        await EffectRuntime.Resolve(state, 0, me.Characters[1], EffectTrigger.OnOppAttackDeclare, prompts);
        Assert.Equal(12000, state.OriginalPowerOf(0, me.Leader));
        Assert.Equal(8000, state.OriginalPowerOf(0, whitebeard));
        Assert.Equal(13000, state.CurrentPowerOf(0, me.Leader));
        BattleEngine.EndBattle(state);
        Assert.Equal(8000, state.OriginalPowerOf(0, me.Leader));
        Assert.Equal(12000, state.OriginalPowerOf(0, whitebeard));
    }

    [Fact]
    public async Task 四费罗可以在对方手牌不足七张时先支付返咚成本()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        await EffectRuntime.Resolve(state, 0, Card("ST10-010"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(don.Id.ToString()));
        Assert.Empty(me.CostArea);
        Assert.Contains(don, me.DonDeck);
        Assert.Empty(state.Players[1].Trash);
    }

    [Fact]
    public async Task 瓦帕无命中神之岛也展示顶五张并允许排序放底()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007", "OP15-008").Build();
        var me = state.Players[0];
        var top = me.Deck.Take(5).ToList();
        var last = me.Deck[5];
        var prompts = new MockPromptService().QueueChooseEmpty().QueueChoose(top.AsEnumerable().Reverse().Select(c => c.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, Card("OP08-110"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.Equal(0, prompts.ChooseHistory[0].max);
        Assert.Equal(new[] { last }.Concat(top.AsEnumerable().Reverse()), me.Deck);
    }

    [Fact]
    public async Task 卡塔库栗没贴咚时不能看生命或加一千()
    {
        var state = TestScene.New("OP03-099").Build();
        state.Players[0].LifeArea.Add(Card("OP15-003"));
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Empty(prompts.OptionHistory);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }
}
