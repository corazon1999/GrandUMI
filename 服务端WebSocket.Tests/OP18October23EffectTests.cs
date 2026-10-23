using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18October23EffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(string name = "测试卡", string feature = "CP9", int cost = 1,
        CardKind kind = CardKind.Character) => new()
        {
            Info = new CardInfo { Number = "TEST-OCT23", Name = name, Kind = kind,
                Color = "黄", Property = "斩", Cost = cost, Power = 2000, Keywords = [feature] },
        };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public async Task 佐罗两段持续能力各自动态判断当前费用和废弃区数量(int owner, int giantSide)
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = owner; state.TurnCount = 3;
        var me = state.Players[owner];
        var source = Card("OP18-092"); source.TurnPlayed = 3;
        var other = Card("OP18-092");
        var giant = Custom(cost: 11);
        me.Characters.AddRange([source, other]); state.Players[giantSide].Characters.Add(giant);
        me.Trash.AddRange(Enumerable.Range(0, 9).Select(_ => Custom()));
        state.Players[1 - owner].Trash.AddRange(Enumerable.Range(0, 10).Select(_ => Custom()));
        state.Players[1 - owner].Leader.CostModThisTurn = 12;
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(2000, state.CurrentPowerOf(owner, source));
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        giant.CostModThisTurn = 1;
        Assert.Equal(5000, state.CurrentPowerOf(owner, source));
        Assert.Equal(2000, state.CurrentPowerOf(owner, other));
        Assert.False(ActionValidator.CanAttack(state, owner, source.Id, true, null).Ok);
        me.Trash.Add(Custom());
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, owner, source.Id, true, null).Ok);
        source.IsEffectsNullified = true;
        Assert.Equal(2000, state.CurrentPowerOf(owner, source));
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        source.IsEffectsNullified = false;
        Assert.Equal(5000, state.CurrentPowerOf(owner, source));
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        giant.CostModThisTurn = 0;
        Assert.Equal(2000, state.CurrentPowerOf(owner, source));
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        me.Trash.RemoveAt(0);
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 黑胡子封锁登场时不影响佐罗静态能力且重登不重复叠加(int owner)
    {
        var state = TestScene.New("OP09-081", "OP09-081").Build();
        var me = state.Players[owner]; var opponent = state.Players[1 - owner];
        await EffectRuntime.Resolve(state, 1 - owner, opponent.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        opponent.Hand.Add(Custom());
        await EffectRuntime.Resolve(state, 1 - owner, opponent.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        var giant = Card("OP18-086"); opponent.Characters.Add(giant);
        await EffectRuntime.Resolve(state, 1 - owner, giant, EffectTrigger.OnEnterField, new MockPromptService());
        me.Trash.AddRange(Enumerable.Range(0, 10).Select(_ => Custom()));
        var source = Card("OP18-092"); me.Hand.Add(source);
        await AtomicOps.PlayFromHandFree(state, owner, source);
        var prompts = new MockPromptService();
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Equal(5000, state.CurrentPowerOf(owner, source));
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Empty(prompts.ChooseHistory);
        AtomicOps.TrashFieldCard(state, owner, source, ignoreEffectLeaveGuard: true);
        await AtomicOps.PlayFromTrashFree(state, owner, source);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Equal(5000, state.CurrentPowerOf(owner, source));
        Assert.Equal(2, state.ContinuousEffects.Count(effect => effect.SourceCardId == source.Id.ToString()));
    }

    [Theory]
    [InlineData(0, "CP9")]
    [InlineData(1, "CP9")]
    [InlineData(0, "CP0")]
    [InlineData(1, "CP0")]
    [InlineData(0, "原CP9")]
    [InlineData(1, "CP")]
    public async Task 范克弗里德登场可选择任意卡种的CP手牌支付成本后抽二(int owner, string feature)
    {
        var state = TestScene.New().Build(); var me = state.Players[owner];
        var source = Card("OP18-108"); me.Characters.Add(source);
        var cost = Custom(feature: feature, kind: CardKind.Event);
        var keep = Custom(); var wrong = Custom(feature: "海军");
        me.Hand.AddRange([keep, cost, wrong]);
        me.Deck.AddRange([Custom(), Custom(), Custom()]);
        var prompts = new MockPromptService().QueueChoose(cost.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(cost, me.Trash); Assert.DoesNotContain(cost, me.Hand);
        Assert.Contains(keep, me.Hand); Assert.Contains(wrong, me.Hand);
        Assert.Equal(4, me.Hand.Count); Assert.Single(me.Deck);
        Assert.DoesNotContain(wrong.Id.ToString(), prompts.ChooseHistory.Single().choices);
        Assert.Equal(0, prompts.ChooseHistory.Single().min);
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    [InlineData("wrong")]
    [InlineData("no-cp")]
    public async Task 登场成本取消或非法选择不会弃牌抽牌(string reason)
    {
        var state = TestScene.New().Build(); var me = state.Players[0];
        var source = Card("OP18-108"); me.Characters.Add(source);
        var a = Custom(); var b = Custom(); var wrong = Custom(feature: "海军");
        me.Hand.Add(wrong);
        if (reason != "no-cp") me.Hand.AddRange([a, b]);
        me.Deck.AddRange([Custom(), Custom(), Custom()]);
        var prompts = reason switch
        {
            "duplicate" => new MockPromptService().QueueChoose(a.Id.ToString(), a.Id.ToString()),
            "too-many" => new MockPromptService().QueueChoose(a.Id.ToString(), b.Id.ToString()),
            "wrong" => new MockPromptService().QueueChoose(wrong.Id.ToString()),
            "stale" => new MockPromptService().QueueChoose(a.Id.ToString()),
            _ => new MockPromptService().QueueChooseEmpty(),
        };
        if (reason == "stale") prompts.OnChooseResponse = _ => me.Hand.Remove(a);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.Trash); Assert.Equal(3, me.Deck.Count);
        Assert.False(EffectRuntime.PayingCost);
        if (reason == "no-cp") Assert.Empty(prompts.ChooseHistory);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    public async Task 主要效果可给斯巴达姆领袖或角色加力且重新活跃可再次发动(int owner, bool targetLeader)
    {
        var state = TestScene.New("OP18-079", "OP18-079").Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var source = Card("OP18-108");
        var character = Custom(name: "斯巴达姆"); var wrong = Custom(name: "斯巴达因");
        me.Characters.AddRange([source, character, wrong]);
        var target = targetLeader ? me.Leader : character;
        var before = state.CurrentPowerOf(owner, target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.ActivatedMain, prompts);
        Assert.True(source.IsTapped); Assert.Equal(before + 1000, state.CurrentPowerOf(owner, target));
        Assert.DoesNotContain(wrong.Id.ToString(), prompts.ChooseHistory.Single().choices);
        Assert.DoesNotContain(state.Players[1 - owner].Leader.Id.ToString(), prompts.ChooseHistory.Single().choices);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(before + 1000, state.CurrentPowerOf(owner, target));
        AtomicOps.ActivateCard(source);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(before + 2000, state.CurrentPowerOf(owner, target));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(before, state.CurrentPowerOf(owner, target));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("tapped")]
    [InlineData("missing")]
    [InlineData("wrong-turn")]
    [InlineData("restricted")]
    [InlineData("nullified")]
    public async Task 主要成本未支付时不选择目标也不加力(string reason)
    {
        var state = TestScene.New("OP18-079").Build();
        var source = Card("OP18-108");
        if (reason != "missing") state.Players[0].Characters.Add(source);
        source.IsTapped = reason == "tapped";
        source.IsEffectsNullified = reason == "nullified";
        if (reason == "restricted") source.Restrictions.Add(new CardRestriction
            { Kind = RestrictionKind.CannotBeRested, Duration = KeywordDuration.ThisTurn });
        if (reason == "wrong-turn") state.CurrentTurnPlayer = 1;
        var prompts = new MockPromptService().QueueConfirm(false);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Empty(prompts.ChooseHistory); Assert.Equal(0, state.Players[0].Leader.PowerModThisTurn);
        Assert.Equal(reason == "tapped", source.IsTapped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 黑胡子封锁范克弗里德登场效果时不支付弃牌成本(int owner)
    {
        var state = TestScene.New("OP09-081", "OP09-081").Build();
        var me = state.Players[owner]; var opponent = state.Players[1 - owner];
        await EffectRuntime.Resolve(state, 1 - owner, opponent.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        opponent.Hand.Add(Custom());
        await EffectRuntime.Resolve(state, 1 - owner, opponent.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        var source = Card("OP18-108"); var cost = Custom();
        me.Hand.AddRange([source, cost]); me.Deck.AddRange([Custom(), Custom(), Custom()]);
        await AtomicOps.PlayFromHandFree(state, owner, source);
        var prompts = new MockPromptService();
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Contains(source, me.Characters); Assert.Contains(cost, me.Hand);
        Assert.Empty(me.Trash); Assert.Equal(3, me.Deck.Count); Assert.Empty(prompts.ChooseHistory);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("stale")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    [InlineData("none")]
    public async Task 主要成本已支付但零目标或目标失效时不回退成本(string reason)
    {
        var state = TestScene.New(reason == "none" ? "OP18-001" : "OP18-079").Build(); var me = state.Players[0];
        var source = Card("OP18-108"); var a = Custom(name: "斯巴达姆"); var b = Custom(name: "斯巴达姆");
        me.Characters.Add(source);
        if (reason != "none") me.Characters.AddRange([a, b]);
        var prompts = reason switch
        {
            "stale" => new MockPromptService().QueueChoose(a.Id.ToString()),
            "duplicate" => new MockPromptService().QueueChoose(a.Id.ToString(), a.Id.ToString()),
            "too-many" => new MockPromptService().QueueChoose(a.Id.ToString(), b.Id.ToString()),
            _ => new MockPromptService().QueueChooseEmpty(),
        };
        if (reason == "stale") prompts.OnChooseResponse = _ => me.Characters.Remove(a);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.True(source.IsTapped); Assert.Equal(0, a.PowerModThisTurn); Assert.Equal(0, b.PowerModThisTurn);
        Assert.False(EffectRuntime.PayingCost);
        if (reason == "none") Assert.Empty(prompts.ChooseHistory);
    }
}
