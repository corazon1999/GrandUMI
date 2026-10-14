using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月14日两渠道新增反馈的成本、目标、看牌顺序和可选收益回归。</summary>
public sealed class PlayerFeedback20261014Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 雷利事件反击可以选择雷利领袖并保持只加本战斗两千()
    {
        var state = TestScene.New("OP12-001").Build();
        var leader = state.Players[0].Leader;
        var prompts = new MockPromptService().QueueChoose(leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("EB04-009"), EffectTrigger.EventCounter, prompts);

        Assert.Contains(leader.Id.ToString(), prompts.ChooseHistory.Single().choices);
        Assert.Equal(2000, leader.PowerModThisBattle);
        Assert.Equal(0, leader.PowerModThisTurn);
    }

    [Fact]
    public async Task 雷利事件反击不允许选择其他领袖但允许普通角色()
    {
        var state = TestScene.New("OP15-001").MyCharacter("OP15-003").Build();
        var me = state.Players[0];
        var target = me.Characters[0];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("EB04-009"), EffectTrigger.EventCounter, prompts);

        Assert.DoesNotContain(me.Leader.Id.ToString(), prompts.ChooseHistory.Single().choices);
        Assert.Equal(2000, target.PowerModThisBattle);
    }

    [Fact]
    public async Task 雷利事件主要效果可以给雷利领袖赋予活跃咚支付成本()
    {
        var state = TestScene.New("OP12-001").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var target = state.Players[1].Characters[0];
        me.CostArea.Add(new DonCard { State = DonState.Active });
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("EB04-009"), EffectTrigger.EventMain, prompts);

        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(-2000, target.PowerModThisTurn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 恰卡佩鲁只有活跃领袖时才能支付减五千成本抽两张(bool active)
    {
        var state = TestScene.New("OP12-001").MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var me = state.Players[0];
        me.Leader.IsTapped = !active;
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, Card("EB04-023"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(active ? 2 : 0, me.Hand.Count);
        Assert.Equal(active ? -5000 : 0, me.Leader.PowerModThisTurn);
        Assert.Equal(!active, me.Leader.IsTapped);
    }

    [Fact]
    public async Task 恰卡佩鲁可以拒绝支付可选成本且不抽牌()
    {
        var state = TestScene.New("OP12-001").MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var me = state.Players[0];

        await EffectRuntime.Resolve(state, 0, Card("EB04-023"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(false));

        Assert.Empty(me.Hand);
        Assert.Equal(0, me.Leader.PowerModThisTurn);
    }

    [Fact]
    public async Task 双指七张废弃时同时给巴洛克和原巴洛克角色加费且不影响对方()
    {
        var state = TestScene.New("OP18-041").MyCharacter("OP14-086")
            .MyCharacter("OP16-056").OppCharacter("OP16-056").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var target = me.Characters[1];
        var opponent = state.Players[1].Characters[0];
        me.Trash.AddRange(Enumerable.Range(0, 6).Select(_ => Card("OP15-003")));
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(target.Info.Cost, state.CurrentCostOf(0, target));

        me.Trash.Add(Card("OP15-003"));

        Assert.Equal(source.Info.Cost + 2, state.CurrentCostOf(0, source));
        Assert.Equal(target.Info.Cost + 2, state.CurrentCostOf(0, target));
        Assert.Equal(opponent.Info.Cost, state.CurrentCostOf(1, opponent));
        me.Trash.RemoveAt(0);
        Assert.Equal(target.Info.Cost, state.CurrentCostOf(0, target));
    }

    [Fact]
    public async Task 凯罗特先确认三张牌面再选择顶底并保持自选排序()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-006").Build();
        var me = state.Players[0];
        var top = me.Deck.Take(3).ToArray();
        var untouched = me.Deck[3];
        var prompts = new MockPromptService().QueueChooseEmpty()
            .QueueChoose(top.Reverse().Select(card => card.Id.ToString()).ToArray()).QueueOption(1);
        prompts.OnOptionResponse = () =>
        {
            var preview = Assert.Single(prompts.ChooseHistory);
            Assert.Equal(0, preview.max);
            Assert.Contains(top[0].Info.Number, JsonSerializer.Serialize(preview.extra));
        };

        await EffectRuntime.Resolve(state, 0, Card("OP11-049"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(new[] { untouched }.Concat(top.Reverse()), me.Deck);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 布蕾触发先确认所选生命牌再选择顶底且最后回手(int side)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var target = state.Players[side];
        var top = Card("OP15-003");
        var bottom = Card("OP15-004");
        target.LifeArea.AddRange([top, bottom]);
        var source = Card("ST20-003");
        me.Trash.Add(source);
        var prompts = new MockPromptService().QueueOption(side).QueueOption(1).QueueChooseEmpty();
        var optionCount = 0;
        prompts.OnOptionResponse = () =>
        {
            if (++optionCount != 2) return;
            var preview = Assert.Single(prompts.ChooseHistory);
            Assert.Equal(0, preview.max);
            Assert.Contains(top.Info.Number, JsonSerializer.Serialize(preview.extra));
            Assert.Contains(source, me.Trash);
        };

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Equal(new[] { bottom, top }, target.LifeArea);
        Assert.Contains(source, me.Hand);
        Assert.DoesNotContain(source, me.Trash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 菊之丞KO时可以选择不加生命或只加一张(bool accept)
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var top = me.Deck[0];
        var prompts = new MockPromptService().QueueConfirm(accept);

        await EffectRuntime.Resolve(state, 0, Card("OP06-104"), EffectTrigger.OnKO, prompts);

        Assert.Single(prompts.ConfirmHistory);
        Assert.Equal(accept ? 1 : 0, me.LifeArea.Count);
        Assert.Equal(!accept, me.Deck.Contains(top));
    }

    [Fact]
    public void 朵松鼠棠印刷反击值为一千()
    {
        _ = TestScene.New();
        Assert.Equal(1000, Card("OP08-027").Info.Counter);
    }
}
