using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月14日到15日新增反馈的真实成本、持续能力与KO触发回归。</summary>
public sealed class PlayerFeedback20261015Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 黑胡子封锁登场时仍注册八费路飞的条件速攻()
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP17-093")
            .OppCharacter("OP15-003").MyDeckTop("OP15-004", "OP15-005").Build();
        var source = state.Players[0].Characters[0];
        var highCost = state.Players[1].Characters[0];
        highCost.CostModThisTurn = 20;
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Empty(state.Players[0].Hand);
        highCost.CostModThisTurn = 0;
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        highCost.CostModThisTurn = 20;
        source.IsEffectsNullified = true;
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        source.IsEffectsNullified = false;
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 黑胡子封锁同样覆盖舞台登场时且不封锁舞台启动主要(bool opponent)
    {
        var state = TestScene.New(opponent ? "ST30-001" : "OP09-081", opponent ? "OP09-081" : "ST30-001")
            .MyCharacter("ST21-014").MyHandAdd("OP15-003").MyDeckTop("OP15-004", "OP15-005", "OP15-006", "OP15-007", "OP15-008", "OP15-009").Build();
        var me = state.Players[0];
        var stage = Card("ST31-005");
        me.StageCard = stage;
        int teachOwner = opponent ? 1 : 0;
        var teach = state.Players[teachOwner].Leader;
        await EffectRuntime.Resolve(state, teachOwner, teach, EffectTrigger.OnGameStart, new MockPromptService());
        if (opponent)
        {
            state.Players[1].Hand.Add(Card("OP15-003"));
            await EffectRuntime.Resolve(state, 1, teach, EffectTrigger.ActivatedMain, new MockPromptService());
        }
        int handCount = me.Hand.Count;
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(handCount, me.Hand.Count);
        Assert.Empty(prompts.ChooseHistory);
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.True(stage.IsTapped);
        Assert.Equal(1, me.AttachedDonCount(me.Characters[0].Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 白星阻挡被战斗KO不弹白星或罗宾的无效排序也不消耗领袖次数(bool effectKO)
    {
        var state = TestScene.New("EB05-010").MyCharacter("EB01-057")
            .MyCharacter("OP12-102").MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var me = state.Players[0];
        var blocker = me.Characters[0];
        var prompts = new MockPromptService();
        if (effectKO) await AtomicOps.KOByEffectAsync(state, 0, blocker, prompts, actingSide: 1);
        else await BattleEngine.KOCardAsync(state, 0, blocker, prompts);
        Assert.DoesNotContain(prompts.ChooseHistory, item => item.kind == "EffectOrder");
        Assert.Equal(effectKO ? 1 : 0, me.LifeCount);
        Assert.DoesNotContain($"EB05-010-ko:{me.Leader.Id}", me.TurnOnceUsed);
        await BattleEngine.KOCardAsync(state, 0, me.Characters.Single(), prompts);
        Assert.Equal(effectKO ? 2 : 1, me.LifeCount);
        Assert.Contains($"EB05-010-ko:{me.Leader.Id}", me.TurnOnceUsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 路飞重置只返还两张活跃咚并保留休息与附着咚(bool accept)
    {
        var state = TestScene.New("OP18-021").MyCharacter("EB02-061").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        source.IsTapped = true;
        var active = new[] { new DonCard { State = DonState.Active }, new DonCard { State = DonState.Active } };
        me.CostArea.AddRange(active);
        var rest = new DonCard { State = DonState.Rest };
        var attached = new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id };
        me.CostArea.AddRange(new[] { rest, attached });
        me.LifeArea.Add(Card("OP15-003"));
        var prompts = new MockPromptService().QueueConfirm(accept);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Contains(rest, me.CostArea);
        Assert.Contains(attached, me.CostArea);
        Assert.Equal(accept ? 0 : 2, me.ActiveDonCount);
        Assert.Equal(accept ? 2 : 0, me.DonDeck.Count);
        Assert.Equal(!accept, source.IsTapped);
        Assert.Equal(accept ? 1 : 0, me.Hand.Count);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public async Task 弗兰奇登场筛选计入山治手牌减费而不把登场优惠当成卡牌费用(int donDifference, bool eligible)
    {
        var state = TestScene.New("OP18-021").MyHandAdd("OP07-064").Build();
        var me = state.Players[0];
        var sanji = me.Hand.Single();
        state.Players[1].CostArea.AddRange(Enumerable.Range(0, donDifference).Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new MockPromptService().QueueChoose(sanji.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP18-034"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(eligible, me.Characters.Contains(sanji));
        Assert.Equal(!eligible, me.Hand.Contains(sanji));
        if (eligible) Assert.Equal(6, state.CurrentCostOf(sanji));
    }

    [Fact]
    public async Task 手牌筛选不把一次性登场优惠计入卡牌费用()
    {
        var state = TestScene.New("OP18-021").MyHandAdd("OP07-064").Build();
        var me = state.Players[0];
        var sanji = me.Hand.Single();
        state.OneShotPlayDiscounts.Add(new OneShotPlayDiscount { Owner = 0, Amount = 3, MinCost = 0 });
        Assert.Equal(3, state.HandPlayCost(0, sanji));
        Assert.Equal(6, state.CurrentCostOf(0, sanji));
        Assert.Equal(6, state.CurrentCostOf(sanji));
        await EffectRuntime.Resolve(state, 0, Card("OP18-034"), EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Contains(sanji, me.Hand);
        Assert.Empty(me.Characters);
    }

    [Fact]
    public async Task 路飞活跃咚不足两张时不使用休息咚补成本也不消耗次数()
    {
        var state = TestScene.New("OP18-021").MyCharacter("EB02-061").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        source.IsTapped = true;
        me.CostArea.AddRange(new[] { new DonCard { State = DonState.Active }, new DonCard { State = DonState.Rest } });
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(me.TurnOnceUsed);
        Assert.Equal(2, me.CostArea.Count);
        Assert.True(source.IsTapped);
    }

    [Fact]
    public async Task 路飞选择成本期间咚变休息时整笔拒绝而不部分支付()
    {
        var state = TestScene.New("OP18-021").MyCharacter("EB02-061").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        source.IsTapped = true;
        var dons = Enumerable.Range(0, 3).Select(_ => new DonCard { State = DonState.Active }).ToArray();
        me.CostArea.AddRange(dons);
        var prompts = new MockPromptService().QueueChoose(dons[0].Id.ToString(), dons[1].Id.ToString());
        prompts.OnChooseResponse = kind => { if (kind == "ReturnOwnDon") dons[1].State = DonState.Rest; };
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Empty(me.DonDeck);
        Assert.Equal(3, me.CostArea.Count);
        Assert.Empty(me.TurnOnceUsed);
        Assert.True(source.IsTapped);
    }
}
