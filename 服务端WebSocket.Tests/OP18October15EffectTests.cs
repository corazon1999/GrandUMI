using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18October15EffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(int cost = 3, int power = 4000, CardKind kind = CardKind.Character,
        string[]? keywords = null, string name = "测试卡") => new() {
        Info = new CardInfo { Number = "TEST-OCT15", Name = name, Color = "黑", Kind = kind,
            Property = "打", Cost = cost, Power = power, Keywords = keywords ?? [] },
    };

    [Theory]
    [InlineData(4000, CardKind.Character, true)]
    [InlineData(4001, CardKind.Character, false)]
    [InlineData(0, CardKind.Character, true)]
    [InlineData(0, CardKind.Event, false)]
    [InlineData(0, CardKind.Stage, false)]
    public async Task 乔巴只回收低力量角色(int power, CardKind kind, bool recovered)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var target = Custom(power: power, kind: kind);
        me.Trash.Add(target);
        await EffectRuntime.Resolve(state, 0, Card("OP18-010"), EffectTrigger.OnKO, new MockPromptService());
        Assert.Equal(recovered, me.Hand.Contains(target));
        Assert.Equal(!recovered, me.Trash.Contains(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 乔巴允许不回收且拒绝已离开废弃区的响应(bool stale)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var target = Custom();
        me.Trash.Add(target);
        var prompts = new MockPromptService();
        if (stale) prompts.OnChooseResponse = _ => me.Trash.Remove(target);
        else prompts.QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, Card("OP18-010"), EffectTrigger.OnKO, prompts);
        Assert.Empty(me.Hand);
    }

    [Theory]
    [InlineData(EffectTrigger.OnEnterField)]
    [InlineData(EffectTrigger.OnKO)]
    public async Task 巴奇按当前力量冻结休息角色且只跳过一次重置(EffectTrigger trigger)
    {
        var state = TestScene.New().Build();
        var eligible = Custom(power: 9000);
        eligible.PowerModThisTurn = -1000;
        eligible.IsTapped = true;
        var active = Custom(power: 1000);
        var tooStrong = Custom(power: 8001);
        tooStrong.IsTapped = true;
        state.Players[1].Characters.AddRange([eligible, active, tooStrong]);
        var prompts = new MockPromptService().QueueChoose(eligible.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP18-032"), trigger, prompts);
        Assert.True(eligible.CannotActivateNextReset);
        Assert.Equal(new[] { eligible.Id.ToString() }, prompts.ChooseHistory.Single().choices);
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterResetPhase(state);
        Assert.True(eligible.IsTapped);
        Assert.False(eligible.CannotActivateNextReset);
        TurnEngine.EnterResetPhase(state);
        Assert.False(eligible.IsTapped);
    }

    [Theory]
    [InlineData("power")]
    [InlineData("active")]
    [InlineData("leave")]
    [InlineData("cancel")]
    public async Task 巴奇拒绝失效或取消的目标(string change)
    {
        var state = TestScene.New().Build();
        var target = Custom(power: 8000);
        target.IsTapped = true;
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        if (change == "cancel") prompts = new MockPromptService().QueueChooseEmpty();
        else prompts.OnChooseResponse = _ => {
            if (change == "power") target.PowerModThisTurn = 1;
            if (change == "active") target.IsTapped = false;
            if (change == "leave") state.Players[1].Characters.Remove(target);
        };
        await EffectRuntime.Resolve(state, 0, Card("OP18-032"), EffectTrigger.OnEnterField, prompts);
        Assert.False(target.CannotActivateNextReset);
    }

    [Theory]
    [InlineData(3, CardKind.Character, "巴洛克工作室", true)]
    [InlineData(3, CardKind.Character, "新巴洛克工作室", true)]
    [InlineData(4, CardKind.Character, "巴洛克工作室", false)]
    [InlineData(3, CardKind.Character, "草帽一伙", false)]
    [InlineData(3, CardKind.Event, "巴洛克工作室", false)]
    public async Task 情人节组合只登场符合费用类型和包含特征的手牌(int cost, CardKind kind, string keyword, bool played)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP18-052");
        me.Characters.Add(source);
        var target = Custom(cost, kind: kind, keywords: [keyword]);
        me.Hand.Add(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(played, me.Characters.Contains(target));
        Assert.Equal(!played, me.Hand.Contains(target));
    }

    [Fact]
    public async Task 情人节组合的加攻随赋予咚动态变化且只作用自身()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP18-052");
        var other = Custom();
        me.Characters.AddRange([source, other]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(4000, state.CurrentPowerOf(0, source));
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.Add(don);
        Assert.Equal(6000, state.CurrentPowerOf(0, source));
        Assert.Equal(4000, state.CurrentPowerOf(0, other));
        don.State = DonState.Rest;
        don.AttachedToCardId = null;
        Assert.Equal(4000, state.CurrentPowerOf(0, source));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("leave")]
    [InlineData("cost")]
    public async Task 情人节组合拒绝取消和失效的手牌(string change)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var target = Custom(keywords: ["巴洛克工作室"]);
        me.Hand.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        if (change == "cancel") prompts = new MockPromptService().QueueChooseEmpty();
        else prompts.OnChooseResponse = _ => {
            if (change == "leave") me.Hand.Remove(target);
            else AtomicOps.AddCostModifier(target, 1, KeywordDuration.ThisTurn);
        };
        await EffectRuntime.Resolve(state, 0, Card("OP18-052"), EffectTrigger.OnEnterField, prompts);
        Assert.DoesNotContain(target, me.Characters);
    }

    [Theory]
    [InlineData(5, false, true, true)]
    [InlineData(6, false, true, false)]
    [InlineData(5, true, true, false)]
    [InlineData(5, false, false, true)]
    public async Task 基威摩兹必须实际支付舞台成本且追加的咚为休息(int stageCost, bool cancel, bool addDon, bool paid)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var stage = Custom(stageCost, kind: CardKind.Stage);
        me.StageCard = stage;
        var don = new DonCard { State = DonState.InDeck };
        me.DonDeck.Add(don);
        var prompts = new MockPromptService().QueueConfirm(addDon);
        if (cancel) prompts.QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, Card("OP18-063"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(paid, me.Trash.Contains(stage));
        Assert.Equal(paid && addDon, me.CostArea.Contains(don));
        if (paid && addDon) Assert.Equal(DonState.Rest, don.State);
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("cost")]
    public async Task 基威摩兹拒绝过期舞台成本(string change)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var stage = Custom(5, kind: CardKind.Stage);
        me.StageCard = stage;
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        var prompts = new MockPromptService { OnChooseResponse = _ => {
            if (change == "leave") me.StageCard = null;
            else AtomicOps.AddCostModifier(stage, 1, KeywordDuration.ThisTurn);
        } };
        await EffectRuntime.Resolve(state, 0, Card("OP18-063"), EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.CostArea);
        Assert.DoesNotContain(stage, me.Trash);
    }

    [Theory]
    [InlineData(0, "速攻")]
    [InlineData(1, "流放")]
    [InlineData(2, "不可阻挡")]
    public async Task 刻耳贝洛斯退手后只赋予所选能力并在回合末清除(int choice, string keyword)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP18-085");
        var target = Card("OP18-119");
        me.Characters.AddRange([source, target]);
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString()).QueueOption(choice);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Contains(source, me.Hand);
        Assert.DoesNotContain(source, me.Characters);
        Assert.Equal(DonState.Rest, don.State);
        Assert.Null(don.AttachedToCardId);
        Assert.Equal(keyword, Assert.Single(target.GainedKeywords).Keyword);
        Assert.True(ActionValidator.HasKeyword(state, target, keyword));
        TurnEngine.EnterEndPhase(state);
        Assert.False(ActionValidator.HasKeyword(state, target, keyword));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("opponent")]
    [InlineData("missing")]
    public async Task 刻耳贝洛斯取消或不满足发动条件时不支付成本(string reason)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP18-085");
        if (reason != "missing") me.Characters.Add(source);
        if (reason == "opponent") state.CurrentTurnPlayer = 1;
        var prompts = new MockPromptService().QueueConfirm(false);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.DoesNotContain(source, me.Hand);
    }

    [Theory]
    [InlineData("wrongname")]
    [InlineData("leave")]
    [InlineData("invalidmode")]
    [InlineData("skiptarget")]
    public async Task 刻耳贝洛斯不会赋予错误或失效目标且保留已支付成本(string reason)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP18-085");
        var target = reason == "wrongname" ? Custom(name: "夏姆洛克圣的随从") : Card("OP18-119");
        me.Characters.AddRange([source, target]);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString()).QueueOption(reason == "invalidmode" ? 3 : 0);
        if (reason == "skiptarget") prompts = new MockPromptService().QueueChooseEmpty();
        if (reason == "leave") prompts.OnOptionResponse = () => me.Characters.Remove(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Contains(source, me.Hand);
        Assert.Empty(target.GainedKeywords);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 脸谱将任意CP手牌正面加入所选生命位置(int position)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var target = Custom(kind: CardKind.Event, keywords: ["CP9"]);
        var oldLife = Custom();
        me.Hand.Add(target);
        me.LifeArea.Add(oldLife);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString()).QueueOption(position);
        await EffectRuntime.Resolve(state, 0, Card("OP18-101"), EffectTrigger.OnKO, prompts);
        Assert.Same(target, me.LifeArea[position == 0 ? 0 : 1]);
        Assert.True(target.IsLifeFaceUp);
        Assert.Empty(me.Hand);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("leave")]
    [InlineData("wrongfeature")]
    [InlineData("invalidposition")]
    public async Task 脸谱补生命拒绝取消和失效响应(string reason)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var target = Custom(keywords: [reason == "wrongfeature" ? "海军" : "CP0"]);
        me.Hand.Add(target);
        var prompts = new MockPromptService().QueueOption(reason == "invalidposition" ? 2 : 0);
        if (reason == "cancel") prompts.QueueChooseEmpty();
        if (reason == "leave") prompts.OnOptionResponse = () => me.Hand.Remove(target);
        await EffectRuntime.Resolve(state, 0, Card("OP18-101"), EffectTrigger.OnKO, prompts);
        Assert.Empty(me.LifeArea);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 脸谱触发按当前费用执行KO或休息且不同时执行(int mode)
    {
        var state = TestScene.New("OP18-079").Build();
        var target = Custom(7);
        AtomicOps.AddCostModifier(target, -1, KeywordDuration.ThisTurn);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueOption(mode).QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP18-101"), EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.Equal(mode == 0, state.Players[1].Trash.Contains(target));
        Assert.Equal(mode == 1, target.IsTapped);
        Assert.Equal(mode == 1, state.Players[1].Characters.Contains(target));
    }

    [Theory]
    [InlineData("leader")]
    [InlineData("cost")]
    [InlineData("leave")]
    [InlineData("cancel")]
    [InlineData("invalidmode")]
    public async Task 脸谱触发拒绝非CP领袖和失效目标(string reason)
    {
        var state = TestScene.New(reason == "leader" ? "OP18-022" : "OP18-079").Build();
        var target = Custom(6);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueOption(reason == "invalidmode" ? 2 : 0);
        if (reason == "cancel") prompts.QueueChooseEmpty();
        prompts.OnChooseResponse = _ => {
            if (reason == "cost") AtomicOps.AddCostModifier(target, 1, KeywordDuration.ThisTurn);
            if (reason == "leave") state.Players[1].Characters.Remove(target);
        };
        await EffectRuntime.Resolve(state, 0, Card("OP18-101"), EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.Empty(state.Players[1].Trash);
        Assert.False(target.IsTapped);
        if (reason == "leader") Assert.Empty(prompts.OptionHistory);
    }

    [Fact]
    public void 鼹鼠通过基础速攻在登场回合攻击()
    {
        var state = TestScene.New().Build();
        state.TurnCount = 3;
        var source = Card("OP18-111");
        source.TurnPlayed = state.TurnCount;
        state.Players[0].Characters.Add(source);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, source.Id, true, null).Ok);
    }
}
