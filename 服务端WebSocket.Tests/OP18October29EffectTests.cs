using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18October29EffectTests
{
    private static CardInstance Card(string suffix) => new() { Info = CardDatabase.Get($"OP18-{suffix}")! };
    private static CardInstance Custom(string name = "测试角色", int cost = 3, int power = 3000,
        string feature = "阿拉巴斯坦王国", CardKind kind = CardKind.Character) => new() { Info = new CardInfo
        { Number = "TEST-OCT29", Name = name, Cost = cost, Power = power, Color = "红", Kind = kind,
            Keywords = feature.Split('/'), Property = "打" } };
    private static DonCard Don(PlayerState me, DonState state = DonState.Active, CardInstance? target = null)
    { var don = new DonCard { State = state, AttachedToCardId = target?.Id }; me.CostArea.Add(don); return don; }
    private static Task Resolve(GameState s, int owner, CardInstance card, EffectTrigger trigger, MockPromptService? prompts = null,
        Dictionary<string, object?>? payload = null) => EffectRuntime.Resolve(s, owner, card, trigger, prompts ?? new MockPromptService(), payload);
    private static GameState Scene(int owner = 0, string leader = "OP18-001")
    { var s = TestScene.New(leader, leader).Build(); s.CurrentTurnPlayer = owner; return s; }
    private static PlayerState Leader(GameState s, CardInstance leader)
    {
        var old = s.Players[0]; var next = new PlayerState { SessionId = old.SessionId, AccountName = old.AccountName, Leader = leader };
        next.Characters.AddRange(old.Characters); next.Hand.AddRange(old.Hand); next.Deck.AddRange(old.Deck);
        next.Trash.AddRange(old.Trash); next.LifeArea.AddRange(old.LifeArea); next.CostArea.AddRange(old.CostArea);
        next.DonDeck.AddRange(old.DonDeck); s.Players[0] = next; return next;
    }

    [Theory]
    [InlineData("002", "阿拉巴斯坦王国")]
    [InlineData("067", "大妈海盗团")]
    [InlineData("008", "任意")]
    public async Task 弃手牌抽二严格筛选且保留独立条件速攻(string suffix, string feature)
    {
        foreach (int owner in new[] { 0, 1 })
        {
            var s = Scene(owner); var me = s.Players[owner]; var source = Card(suffix); me.Characters.Add(source);
            var cost = Custom(feature: feature, power: 5000); var wrong = Custom(feature: "海军", kind: CardKind.Event);
            me.Hand.AddRange([cost, wrong]); me.Deck.AddRange([Custom(), Custom(), Custom()]);
            await Resolve(s, owner, source, EffectTrigger.OnEnterField, new MockPromptService().QueueChoose(cost.Id.ToString()));
            Assert.Contains(cost, me.Trash); Assert.Contains(wrong, me.Hand); Assert.Equal(3, me.Hand.Count); Assert.Single(me.Deck);
            if (suffix == "008") { Assert.True(ActionValidator.HasKeyword(s, source, "流放")); Assert.True(ActionValidator.HasKeyword(s, source, "速攻")); }
            Assert.False(EffectRuntime.PayingCost);
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    public async Task 复合弃牌休咚成本无效时保持零支付(string reason)
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("059"); var cost = Custom(feature: "巴洛克工作室");
        me.Hand.Add(cost); me.Deck.AddRange([Custom(), Custom(), Custom()]); var don = Don(me);
        var p = reason == "cancel" ? new MockPromptService().QueueChooseEmpty()
            : reason == "duplicate" ? new MockPromptService().QueueChoose(cost.Id.ToString(), cost.Id.ToString())
            : new MockPromptService().QueueChoose(cost.Id.ToString());
        if (reason == "stale") p.OnChooseResponse = _ => me.Hand.Remove(cost);
        await Resolve(s, 0, source, EffectTrigger.EventMain, p);
        Assert.Empty(me.Trash); Assert.Equal(DonState.Active, don.State); Assert.Equal(3, me.Deck.Count); Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData(4999, false, false, false)]
    [InlineData(5000, true, false, false)]
    [InlineData(6000, true, true, false)]
    [InlineData(7000, true, true, true)]
    public async Task 卡鲁三档能力按实时力量独立开关(int power, bool rush, bool banish, bool unblockable)
    {
        var s = Scene(leader: "OP18-079"); var source = Card("004"); s.Players[0].Characters.Add(source);
        await Resolve(s, 0, source, EffectTrigger.OnEnterField); source.PowerModThisTurn = power - source.Info.Power;
        Assert.Equal(rush, ActionValidator.HasKeyword(s, source, "速攻")); Assert.Equal(banish, ActionValidator.HasKeyword(s, source, "流放"));
        Assert.Equal(unblockable, ActionValidator.HasKeyword(s, source, "不可阻挡"));
        source.IsEffectsNullified = true; Assert.False(ActionValidator.HasKeyword(s, source, "速攻"));
    }

    [Theory]
    [InlineData("005", "功夫海牛", 3000)]
    [InlineData("006", "奈菲特·薇薇", 1000)]
    [InlineData("081", "月光·莫利亚", 0)]
    public async Task 实时场况能力跨双方角色变动与离场正确更新(string suffix, string name, int delta)
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var source = Card(suffix); me.Characters.Add(source);
        var ally = Custom(name); me.Characters.Add(ally);
        if (suffix == "005") s.Players[1].Characters.Add(Custom("功夫海牛"));
        await Resolve(s, 0, source, EffectTrigger.OnEnterField);
        Assert.Equal(source.Info.Power + delta, s.CurrentPowerOf(0, source));
        if (suffix == "006") Assert.True(ActionValidator.HasKeyword(s, source, "速攻"));
        if (suffix == "081") { Assert.True(ActionValidator.HasKeyword(s, source, "阻挡者")); Assert.True(ActionValidator.HasKeyword(s, source, "双重攻击")); }
        me.Characters.Remove(ally);
        if (suffix == "006") Assert.False(ActionValidator.HasKeyword(s, source, "速攻"));
        if (suffix == "081") Assert.False(ActionValidator.HasKeyword(s, source, "阻挡者"));
    }

    [Fact]
    public async Task 斯摩格海军条件及奈美对方回合休息减力()
    {
        var s = Scene(); var me = s.Players[0]; var sm = Card("007"); var nami = Card("030"); me.Characters.AddRange([sm, nami]);
        await Resolve(s, 0, sm, EffectTrigger.OnEnterField); await Resolve(s, 0, nami, EffectTrigger.OnEnterField);
        Assert.Equal(5000, s.CurrentPowerOf(0, sm));
        me = Leader(s, Custom(feature: "海军", kind: CardKind.Leader));
        Assert.Equal(6000, s.CurrentPowerOf(0, sm)); Assert.True(ActionValidator.HasKeyword(s, sm, "流放"));
        s.CurrentTurnPlayer = 1; nami.IsTapped = true; Assert.Equal(4000, s.CurrentPowerOf(0, nami));
        nami.IsTapped = false; Assert.Equal(6000, s.CurrentPowerOf(0, nami));
    }

    [Fact]
    public async Task 超级卡鲁鸭废弃成本不KO并免费登场卡鲁()
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var source = Card("009"); me.Characters.Add(source);
        var target = Card("004"); me.Hand.Add(target); for (int i = 0; i < 9; i++) Don(me);
        await Resolve(s, 0, source, EffectTrigger.ActivatedMain);
        Assert.Contains(source, me.Trash); Assert.Contains(target, me.Characters); Assert.Equal(9, me.CostArea.Count);
    }

    [Theory]
    [InlineData("012", "动物/阿拉巴斯坦王国", 1000, null)]
    [InlineData("014", "阿拉巴斯坦王国", 0, "速攻")]
    [InlineData("023", "东海", 0, "流放")]
    public async Task 单体赋予效果按特征严格筛选(string suffix, string feature, int delta, string? keyword)
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var source = Card(suffix); var target = Custom(feature: feature);
        me.Characters.AddRange([source, target]); var wrong = Custom(feature: "海军"); me.Characters.Add(wrong);
        var p = new MockPromptService().QueueChoose(target.Id.ToString());
        await Resolve(s, 0, source, suffix == "023" ? EffectTrigger.OnEnterField : EffectTrigger.OnAttackDeclare, p);
        Assert.Equal(delta, target.PowerModThisTurn); Assert.Equal(0, wrong.PowerModThisTurn);
        if (keyword is not null) Assert.True(ActionValidator.HasKeyword(s, target, keyword));
        Assert.DoesNotContain(wrong.Id.ToString(), p.ChooseHistory[0].choices);
    }

    [Fact]
    public async Task 嘎事件原本力量门槛与反击以及薇薇全部生命成本()
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var enemy = Custom(); s.Players[1].Characters.Add(enemy);
        await Resolve(s, 0, Card("019"), EffectTrigger.EventMain); Assert.Equal(-3000, enemy.PowerModThisTurn);
        me.Characters.Add(Custom(power: 7000)); await Resolve(s, 0, Card("019"), EffectTrigger.EventMain); Assert.Equal(-3000, enemy.PowerModThisTurn);
        await Resolve(s, 0, Card("019"), EffectTrigger.EventCounter); Assert.Equal(3000, me.Leader.PowerModThisBattle);
        me = Leader(s, Custom("奈菲特·薇薇", kind: CardKind.Leader)); me.LifeArea.AddRange([Custom(), Custom()]);
        await Resolve(s, 0, Card("020"), EffectTrigger.EventCounter);
        Assert.Empty(me.LifeArea); Assert.Equal(2, me.Hand.Count); Assert.Equal(-8000, enemy.PowerModThisTurn); Assert.Equal(-5000, s.Players[1].Leader.PowerModThisTurn);
    }

    [Fact]
    public async Task 佐罗狙击王仅覆盖活跃路飞领袖且登场返活两咚并封休息()
    {
        var s = Scene(leader: "OP18-022"); var me = s.Players[0]; var source = Card("027"); me.Characters.Add(source);
        for (int i = 0; i < 3; i++) Don(me, DonState.Attached, me.Leader);
        var d1 = Don(me, DonState.Rest); var d2 = Don(me, DonState.Rest); var target = Custom(cost: 6); s.Players[1].Characters.Add(target);
        await Resolve(s, 0, source, EffectTrigger.OnEnterField);
        Assert.Equal(DonState.Active, d1.State); Assert.Equal(DonState.Active, d2.State);
        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));
        s.CurrentTurnPlayer = 1; Assert.Equal(7000, s.OriginalPowerOf(0, me.Leader));
        me.Leader.IsTapped = true; Assert.Equal(5000, s.OriginalPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task 烹烹回合末废弃重置角色横纲KO可休息舞台巨人斧独立抽牌()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("033"); var target = Custom(); target.IsTapped = true;
        me.Characters.AddRange([source, target]); await Resolve(s, 0, source, EffectTrigger.OnMyTurnEnd);
        Assert.Contains(source, me.Trash); Assert.False(target.IsTapped);
        var stage = Custom(kind: CardKind.Stage); s.Players[1].StageCard = stage;
        await Resolve(s, 0, Card("037"), EffectTrigger.OnKO, new MockPromptService().QueueChoose(stage.Id.ToString())); Assert.True(stage.IsTapped);
        me.Deck.AddRange([Custom(), Custom()]); await Resolve(s, 0, Card("039"), EffectTrigger.EventCounter, new MockPromptService().QueueChooseEmpty());
        Assert.Single(me.Hand); me.Leader.IsTapped = true;
        await Resolve(s, 0, Card("039"), EffectTrigger.EventCounter, new MockPromptService().QueueChooseEmpty()); Assert.Single(me.Hand);
    }

    [Theory]
    [InlineData("038", "草帽一伙", 5)]
    [InlineData("049", "巴洛克工作室/推进城", 5)]
    [InlineData("094", "埃鲁巴夫", 5)]
    [InlineData("095", "神之骑士团", 5)]
    [InlineData("110", "CP7", 4)]
    public async Task 检索公开一张并允许剩余卡自选回底(string suffix, string feature, int count)
    {
        var s = Scene(leader: "OP18-119"); var me = s.Players[0]; var wanted = Custom("检索目标", feature: feature);
        var rest = Enumerable.Range(0, count - 1).Select(i => Custom($"非目标{i}", feature: "海军")).ToList(); var tail = Custom("未查看卡");
        me.Deck.Add(wanted); me.Deck.AddRange(rest); me.Deck.Add(tail);
        var p = new MockPromptService().QueueChoose(wanted.Id.ToString()).QueueChoose(rest.AsEnumerable().Reverse().Select(x => x.Id.ToString()).ToArray());
        await Resolve(s, 0, Card(suffix), suffix is "049" or "110" ? EffectTrigger.OnEnterField : EffectTrigger.EventMain, p);
        Assert.Contains(wanted, me.Hand); Assert.Equal(tail, me.Deck[0]); Assert.Equal(rest.AsEnumerable().Reverse(), me.Deck.Skip(1));
    }

    [Theory]
    [InlineData("040", 7, true)]
    [InlineData("115", 5, false)]
    public async Task 事件KO门槛区分当前费用并遵守休息要求(string suffix, int limit, bool rested)
    {
        var s = Scene(); var enemy = Custom(cost: limit + 1); enemy.CostModThisTurn = -1; enemy.IsTapped = rested;
        s.Players[1].Characters.Add(enemy); await Resolve(s, 0, Card(suffix), EffectTrigger.EventMain);
        Assert.Contains(enemy, s.Players[1].Trash); Assert.Empty(s.Players[1].Characters);
    }

    [Fact]
    public async Task 哈库KO抽牌且主要阶段仅赋予德莱斯罗兹角色或领袖且每回合一次()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("042"); me.Characters.Add(source); var target = Custom(feature: "德莱斯罗兹"); me.Characters.Add(target);
        var don = Don(me, DonState.Rest); me.Deck.AddRange([Custom(), Custom()]);
        await Resolve(s, 0, source, EffectTrigger.ActivatedMain, new MockPromptService().QueueChoose(target.Id.ToString()).QueueOption(1));
        Assert.Equal(DonState.Attached, don.State); Assert.Equal(target.Id, don.AttachedToCardId);
        await Resolve(s, 0, source, EffectTrigger.ActivatedMain); Assert.Single(me.TurnOnceUsed);
        await Resolve(s, 0, source, EffectTrigger.OnKO); Assert.Single(me.Hand);
    }

    [Fact]
    public async Task 王子别名废弃事件动态加力双人角色贴咚加力与重排()
    {
        var s = Scene(); var me = s.Players[0]; var prince = Card("045"); var seven = Card("053"); var eight = Card("054");
        me.Characters.AddRange([prince, seven, eight]);
        foreach (var card in me.Characters.ToList()) await Resolve(s, 0, card, EffectTrigger.OnEnterField);
        Assert.True(prince.MatchesName("山智"));
        for (int i = 0; i < 10; i++) me.Trash.Add(Custom(kind: CardKind.Event));
        Assert.Equal(6000, s.CurrentPowerOf(0, prince));
        Don(me, DonState.Attached, seven); Don(me, DonState.Attached, eight); s.CurrentTurnPlayer = 1;
        Assert.Equal(4000, s.CurrentPowerOf(0, seven)); Assert.Equal(4000, s.CurrentPowerOf(0, eight));
        s.CurrentTurnPlayer = 0; var top = new[] { Custom("甲"), Custom("乙"), Custom("丙") }; me.Deck.AddRange(top); var tail = Custom("丁"); me.Deck.Add(tail);
        await Resolve(s, 0, seven, EffectTrigger.OnEnterField, new MockPromptService().QueueChoose(top.Reverse().Select(x => x.Id.ToString()).ToArray()).QueueOption(1));
        Assert.Equal(new[] { tail }.Concat(top.Reverse()), me.Deck);
    }

    [Fact]
    public async Task 黄金周条件封攻与微尘斩两种模式以及返手成本回底()
    {
        var s = Scene(leader: "OP18-041"); var me = s.Players[0]; var enemy = Custom(cost: 5); s.Players[1].Characters.Add(enemy);
        await Resolve(s, 0, Card("050"), EffectTrigger.OnEnterField); Assert.True(enemy.HasRestriction(RestrictionKind.CannotAttack));
        var dons = Enumerable.Range(0, 3).Select(_ => Don(me)).ToList();
        await Resolve(s, 0, Card("057"), EffectTrigger.EventMain); Assert.Contains(enemy, s.Players[1].Hand); Assert.All(dons, d => Assert.Equal(DonState.Rest, d.State));
        await Resolve(s, 0, Card("057"), EffectTrigger.EventCounter); Assert.Equal(3000, me.Leader.PowerModThisBattle);
        var cost = Custom(); me.Characters.Add(cost); var victim = Custom(cost: 4); s.Players[1].Characters.Add(victim);
        var attached = Don(me, DonState.Attached, cost);
        await Resolve(s, 0, Card("058"), EffectTrigger.EventMain);
        Assert.Contains(cost, me.Hand); Assert.Contains(victim, s.Players[1].Deck); Assert.Equal(DonState.Rest, attached.State);
    }

    [Theory]
    [InlineData("026", "草帽一伙")]
    [InlineData("054", "巴洛克工作室/推进城")]
    [InlineData("071", "堂吉诃德海盗团")]
    public async Task 离场替代只抵挡对方效果并支付相应成本(string suffix, string feature)
    {
        foreach (int owner in new[] { 0, 1 })
        {
            var s = Scene(1 - owner); var me = s.Players[owner]; var source = Card(suffix); var victim = Custom(feature: feature); me.Characters.AddRange([source, victim]);
            var cost = Custom(); me.Hand.Add(cost); var don = Don(me);
            var payload = new Dictionary<string, object?> { ["victimId"] = victim.Id.ToString(), ["victimOwner"] = owner, ["actingSide"] = 1 - owner };
            await Resolve(s, owner, source, EffectTrigger.OnAllyWillLeaveField, payload: payload);
            Assert.Contains(victim.Id, s.PreventLeaveCardIds);
            if (suffix == "026") Assert.Contains(source, me.Trash);
            else if (suffix == "054") Assert.Contains(cost, me.Trash);
            else Assert.Contains(don, me.DonDeck);
        }
    }

    [Fact]
    public async Task 平克返附着咚防KO每回合一次且登场封休息托雷波尔条件能力()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("068"); me.Characters.Add(source); var d = Don(me, DonState.Attached, source);
        await Resolve(s, 0, source, EffectTrigger.PreKO); Assert.Contains(d, me.DonDeck); Assert.Contains(source.Id, s.PreventKOCardIds);
        var p = new MockPromptService(); Don(me); await Resolve(s, 0, source, EffectTrigger.PreKO, p); Assert.Empty(p.ChooseHistory);
        var victim = Custom(cost: 7); s.Players[1].Characters.Add(victim);
        await Resolve(s, 0, source, EffectTrigger.OnEnterField); Assert.True(victim.HasRestriction(RestrictionKind.CannotBeRested));
        var trebol = Card("071"); me.Characters.Add(trebol); await Resolve(s, 0, trebol, EffectTrigger.OnEnterField);
        me = Leader(s, Custom("糖糖", kind: CardKind.Leader)); s.CurrentTurnPlayer = 1;
        Assert.Equal(8000, s.CurrentPowerOf(0, trebol)); Assert.True(ActionValidator.HasKeyword(s, trebol, "阻挡者"));
    }

    [Fact]
    public async Task 弗兰奇一家废弃舞台仅登场回合减力且每回合一次()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("072"); source.TurnPlayed = s.TurnCount; me.Characters.Add(source);
        var stage = Custom(cost: 5, kind: CardKind.Stage); me.ExtraStageCard = stage; var enemy = Custom(); s.Players[1].Characters.Add(enemy);
        await Resolve(s, 0, source, EffectTrigger.ActivatedMain); Assert.Contains(stage, me.Trash); Assert.Equal(-3000, enemy.PowerModThisTurn);
        me.StageCard = Custom(kind: CardKind.Stage); await Resolve(s, 0, source, EffectTrigger.ActivatedMain); Assert.NotNull(me.StageCard);
    }

    [Fact]
    public async Task 旺奥加仅领袖提奇弃牌监听一次且KO独立抽牌()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("080"); me.Characters.Add(source);
        me = Leader(s, Custom("马歇尔·D·提奇", kind: CardKind.Leader)); me.Deck.AddRange([Custom(), Custom(), Custom()]);
        var payload = new Dictionary<string, object?> { ["owner"] = 0, ["sourceNumber"] = me.Leader.Info.Number };
        await Resolve(s, 0, source, EffectTrigger.OnHandDiscarded, payload: payload);
        await Resolve(s, 0, source, EffectTrigger.OnHandDiscarded, payload: payload); Assert.Single(me.Hand);
        await Resolve(s, 0, source, EffectTrigger.OnKO); Assert.Equal(2, me.Hand.Count);
    }

    [Theory]
    [InlineData("082", 1, true)]
    [InlineData("083", 2, false)]
    [InlineData("088", 2, false)]
    public async Task KO弃牌复活尊重数量与休息状态(string suffix, int count, bool rested)
    {
        var s = Scene(); var me = Leader(s, Custom(feature: "黑胡子海盗团", kind: CardKind.Leader));
        var source = Card(suffix); me.Trash.Add(source); for (int i = 0; i < count; i++) me.Hand.Add(Custom());
        await Resolve(s, 0, source, EffectTrigger.OnKO); Assert.Contains(source, me.Characters); Assert.Equal(rested, source.IsTapped); Assert.Equal(count, me.Trash.Count); Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task 蝶美复制对方领袖实时力量麒麟戈姆登场MMA索玛兹按原费用KO()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("082"); me.Characters.Add(source); s.Players[1].Leader.PowerModThisTurn = 2000;
        await Resolve(s, 0, source, EffectTrigger.OnAttackDeclare); Assert.Equal(s.CurrentPowerOf(1, s.Players[1].Leader), source.OriginalPowerOverride);
        var mma = Card("093"); me.Trash.Add(mma); await Resolve(s, 0, Card("083"), EffectTrigger.OnEnterField); Assert.Contains(mma, me.Characters);
        for (int i = 0; i < 6; i++) Don(me); var victim = Custom(cost: 4); victim.CostModThisTurn = 20; s.Players[1].Characters.Add(victim);
        await Resolve(s, 0, Card("088"), EffectTrigger.OnEnterField); Assert.Contains(victim, s.Players[1].Trash);
    }

    [Fact]
    public async Task 斯坦森费用只加场上自身且动态检查全队费用()
    {
        var s = Scene(); var me = s.Players[0]; var source = Card("087"); me.Characters.Add(source); var hand = Card("087"); me.Hand.Add(hand);
        await Resolve(s, 0, source, EffectTrigger.OnEnterField); Assert.Equal(15, s.CurrentCostOf(0, source)); Assert.Equal(3, s.HandPlayCost(0, hand)); Assert.Equal(6000, s.CurrentPowerOf(0, source));
        me.Characters.Add(Custom()); Assert.Equal(4000, s.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task 召唤复活与反击五芒星仅减手牌费并抽一堆二()
    {
        var s = Scene(leader: "OP18-119"); var me = s.Players[0]; var target = Custom(cost: 6, feature: "神之骑士团"); me.Trash.Add(target);
        for (int i = 0; i < 3; i++) Don(me); await Resolve(s, 0, Card("096"), EffectTrigger.EventMain); Assert.Contains(target, me.Characters);
        for (int i = 0; i < 10; i++) me.Trash.Add(Custom()); await Resolve(s, 0, Card("096"), EffectTrigger.EventCounter, new MockPromptService().QueueChoose(me.Leader.Id.ToString())); Assert.Equal(4000, me.Leader.PowerModThisBattle);
        var stage = Card("097"); me.StageCard = stage; var hand = Custom(cost: 2, feature: "五老星"); me.Hand.Add(hand); me.Deck.AddRange([Custom(), Custom(), Custom(), Custom()]);
        await Resolve(s, 0, stage, EffectTrigger.OnEnterField); Assert.Equal(1, s.HandPlayCost(0, hand)); Assert.Equal(6, s.CurrentCostOf(0, target)); Assert.Equal(2, me.Hand.Count); Assert.Single(me.Deck);
        stage.IsEffectsNullified = true; Assert.Equal(2, s.HandPlayCost(0, hand));
    }

    [Fact]
    public async Task 卡古正面生命顶底与瓦杰私密重排不改变正反面()
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var cp = Card("110"); var original = Custom(); me.LifeArea.Add(original); me.Hand.Add(cp);
        await Resolve(s, 0, Card("099"), EffectTrigger.OnEnterField, new MockPromptService().QueueChoose(cp.Id.ToString()).QueueOption(1));
        Assert.Equal(cp, me.LifeArea[^1]); Assert.True(cp.IsLifeFaceUp);
        await Resolve(s, 0, Card("114"), EffectTrigger.OnEnterField, new MockPromptService().QueueChoose(cp.Id.ToString(), original.Id.ToString()));
        Assert.Equal(cp, me.LifeArea[0]); Assert.True(cp.IsLifeFaceUp); Assert.False(original.IsLifeFaceUp);
    }

    [Theory]
    [InlineData("038")]
    [InlineData("094")]
    [InlineData("110")]
    [InlineData("099")]
    [InlineData("115")]
    public async Task 抽牌型生命触发实际抽牌并继续处理后续目标(string suffix)
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; me.Deck.AddRange([Custom(), Custom()]);
        var enemy = Custom(cost: 2); s.Players[1].Characters.Add(enemy);
        await Resolve(s, 0, Card(suffix), EffectTrigger.OnLifeRevealTrigger); Assert.Single(me.Hand);
        if (suffix == "110") Assert.Contains(enemy, s.Players[1].Trash);
        if (suffix is "099" or "115") Assert.Contains(enemy, s.Players[1].Deck);
    }

    [Theory]
    [InlineData("107", "deck")]
    [InlineData("114", "deck")]
    [InlineData("109", "trash")]
    [InlineData("116", "rest")]
    [InlineData("040", "rest")]
    public async Task 移区型生命触发按各自门槛结算(string suffix, string destination)
    {
        var s = Scene(leader: "OP18-079"); var victim = Custom(cost: 5); s.Players[1].Characters.Add(victim);
        await Resolve(s, 0, Card(suffix), EffectTrigger.OnLifeRevealTrigger);
        if (destination == "deck") Assert.Contains(victim, s.Players[1].Deck);
        else if (destination == "trash") Assert.Contains(victim, s.Players[1].Trash);
        else Assert.True(victim.IsTapped);
    }

    [Fact]
    public async Task 奥兹触发休息复活恐怖之船弗兰奇事件回收舞台及反击()
    {
        var s = Scene(); var me = s.Players[0]; var target = Custom(cost: 4, feature: "恐怖之船海盗团"); me.Trash.Add(target);
        await Resolve(s, 0, Card("081"), EffectTrigger.OnLifeRevealTrigger); Assert.Contains(target, me.Characters); Assert.True(target.IsTapped);
        var stage = Custom(cost: 5, kind: CardKind.Stage); me.Trash.Add(stage); Don(me);
        await Resolve(s, 0, Card("074"), EffectTrigger.OnLifeRevealTrigger); Assert.Contains(stage, me.Hand);
        me.StageCard = stage; me.Hand.Remove(stage); await Resolve(s, 0, Card("074"), EffectTrigger.EventCounter, new MockPromptService().QueueChoose(me.Leader.Id.ToString())); Assert.Equal(4000, me.Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task 斯巴达姆公开手牌并实际发动所选触发但不丢失该手牌()
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var selected = Card("110"); me.Hand.Add(selected); me.Deck.AddRange([Custom(), Custom()]);
        var victim = Custom(cost: 2); s.Players[1].Characters.Add(victim);
        await Resolve(s, 0, Card("105"), EffectTrigger.OnLifeRevealTrigger);
        Assert.Contains(selected, me.Hand); Assert.Equal(2, me.Hand.Count); Assert.Contains(victim, s.Players[1].Trash);
    }

    [Theory]
    [InlineData("059")]
    [InlineData("116")]
    public async Task 弃牌反击只增加本次战斗力量(string suffix)
    {
        var s = Scene(); var me = s.Players[0]; me.Hand.Add(Custom());
        await Resolve(s, 0, Card(suffix), EffectTrigger.EventCounter, new MockPromptService().QueueChoose(me.Hand[0].Id.ToString()).QueueChoose(me.Leader.Id.ToString()));
        Assert.Empty(me.Hand); Assert.Single(me.Trash); Assert.Equal(3000, me.Leader.PowerModThisBattle); Assert.Equal(0, me.Leader.PowerModThisTurn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 屠魔令送墓全场不触发KO并从两区登场名称不同中将(int owner)
    {
        var s = Scene(owner, "OP18-079"); var me = s.Players[owner]; var enemy = s.Players[1 - owner];
        var ally = Custom(); var victim = Card("064"); me.Characters.Add(ally); enemy.Characters.Add(victim); Don(enemy);
        me.LifeArea.AddRange([Custom(), Custom()]); var deck = Card("098"); var trash = Card("104"); me.Deck.AddRange([deck, Custom()]); me.Trash.Add(trash);
        await Resolve(s, owner, Card("117"), EffectTrigger.EventMain, new MockPromptService().QueueChoose(deck.Id.ToString(), trash.Id.ToString()));
        Assert.Empty(me.LifeArea); Assert.Contains(ally, me.Trash); Assert.Contains(victim, enemy.Trash); Assert.Empty(enemy.Characters);
        Assert.Contains(deck, me.Characters); Assert.Contains(trash, me.Characters); Assert.Equal(2, me.Characters.Count); Assert.Single(enemy.CostArea);
        Assert.False(EffectRuntime.PayingCost);
    }

    [Fact]
    public async Task 屠魔令拒绝同名重复登场且错误领袖仅支付生命成本()
    {
        var s = Scene(leader: "OP18-079"); var me = s.Players[0]; var a = Card("098"); var b = Card("098"); me.Deck.Add(a); me.Trash.Add(b);
        me.LifeArea.Add(Custom());
        await Resolve(s, 0, Card("117"), EffectTrigger.EventMain, new MockPromptService().QueueChoose(a.Id.ToString(), b.Id.ToString()));
        Assert.Empty(me.Characters); Assert.Contains(a, me.Deck); Assert.Contains(b, me.Trash);
        var wrong = Scene(); wrong.Players[0].LifeArea.Add(Custom()); wrong.Players[1].Characters.Add(Custom());
        await Resolve(wrong, 0, Card("117"), EffectTrigger.EventMain); Assert.Empty(wrong.Players[0].LifeArea); Assert.Single(wrong.Players[1].Characters);
    }

    [Fact]
    public async Task 八张白板或基础能力卡正常入库且可使用()
    {
        var s = Scene();
        foreach (string suffix in "035 043 062 073 090 098 102 104".Split(' '))
        { var card = Card(suffix); Assert.NotNull(card.Info); Assert.True(card.Info.Cost > 0); Assert.True(card.Info.Power > 0); Assert.Empty(card.Info.EffectTags); }
        Assert.True(ActionValidator.HasKeyword(s, Card("098"), "不可阻挡")); Assert.True(ActionValidator.HasKeyword(s, Card("104"), "流放"));
        Assert.Equal(6000, Card("102").Info.Power); Assert.True(Card("102").Info.HasKeyword("CP6"));
    }

    [Theory]
    [InlineData("026", "草帽一伙")]
    [InlineData("054", "巴洛克工作室")]
    [InlineData("071", "堂吉诃德海盗团")]
    public async Task 实际离场流程允许替代且只支付一次(string suffix, string feature)
    {
        var s = Scene(1); var me = s.Players[0]; var source = Card(suffix); var target = Custom(feature: feature);
        me.Characters.AddRange([source, target]); me.Hand.Add(Custom()); Don(me);
        await Resolve(s, 0, source, EffectTrigger.OnEnterField);
        var actingSource = Card("058");
        // 使用真实效果上下文设置对方为行动方，再通过返回角色流程执行非KO移区。
        s.Players[1].Characters.Add(Custom());
        var p = new MockPromptService().QueueChoose(s.Players[1].Characters[0].Id.ToString()).QueueChoose(target.Id.ToString());
        await Resolve(s, 1, actingSource, EffectTrigger.EventMain, p);
        Assert.Contains(target, me.Characters); Assert.DoesNotContain(target, me.Deck);
        if (suffix == "026") Assert.Contains(source, me.Trash);
        else Assert.Single(me.TurnOnceUsed);
    }

    [Theory]
    [InlineData("054")]
    [InlineData("071")]
    [InlineData("068")]
    public async Task 替代成本选择期间目标离场不会扣除手牌或咚(string suffix)
    {
        var s = Scene(1); var me = s.Players[0]; var source = Card(suffix); me.Characters.Add(source);
        var victim = suffix == "068" ? source : Custom(feature: suffix == "054" ? "巴洛克工作室" : "堂吉诃德海盗团");
        if (victim != source) me.Characters.Add(victim);
        me.Hand.Add(Custom()); var don = Don(me); var p = new MockPromptService { OnChooseResponse = _ => me.Characters.Remove(victim) };
        var payload = new Dictionary<string, object?> { ["victimId"] = victim.Id.ToString(), ["actingSide"] = 1 };
        await Resolve(s, 0, source, suffix == "068" ? EffectTrigger.PreKO : EffectTrigger.OnAllyWillLeaveField, p, payload);
        Assert.Single(me.Hand); Assert.Empty(me.Trash); Assert.Equal(DonState.Active, don.State); Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task 全部新卡触发标签与实际脚本连线一致()
    {
        var s = Scene();
        foreach (string suffix in "002 004 005 006 007 008 009 012 014 019 020 023 026 027 030 033 035 037 038 039 040 042 043 045 049 050 053 054 057 058 059 062 067 068 071 072 073 074 080 081 082 083 087 088 090 094 095 096 097 098 099 102 104 105 107 109 110 114 115 116 117".Split(' '))
        {
            var card = Card(suffix); var script = CardRulesetManager.For(s).TryGetScriptedEffect(card.Info.Number);
            foreach (string tag in card.Info.EffectTags) Assert.True(script?.HandlesTrigger(Enum.Parse<EffectTrigger>(tag)) == true, $"{card.Info.Number} {tag}未连接");
            if (!string.IsNullOrEmpty(card.Info.Trigger)) Assert.True(script?.HandlesTrigger(EffectTrigger.OnLifeRevealTrigger) == true, $"{card.Info.Number}生命触发未连接");
            Assert.Equal(CardPlayability.Playable, card.Info.Playability);
        }
    }

    [Theory]
    [InlineData("038", "草帽一伙")]
    [InlineData("049", "巴洛克工作室")]
    [InlineData("094", "埃鲁巴夫")]
    [InlineData("095", "神之骑士团")]
    [InlineData("105", "CP9")]
    [InlineData("110", "CP7")]
    public async Task 公开效果确实向对手广播所选卡而不泄漏余牌(string suffix, string feature)
    {
        var initialized = Scene();
        var engine = new GameEngine($"op18-public-{suffix}", ("s0", "甲", "OP17-099\nOP17-100"), ("s1", "乙", "OP17-099\nOP17-100"), 0, 47);
        var s = engine.State;
        s.Players[0] = new PlayerState { SessionId = "s0", AccountName = "甲", Leader = Card(suffix == "105" ? "079" : "119") };
        var me = s.Players[0]; var wanted = suffix == "105" ? Card("110") : Custom("选中卡", feature: feature);
        var hidden = Custom("未选中卡", feature: "海军"); me.Deck.AddRange([Custom(), Custom()]);
        if (suffix == "105") me.Hand.Add(wanted);
        else { me.Deck.Clear(); me.Deck.AddRange([wanted, hidden]); }
        var reveals = new List<string[]>();
        engine.OnSendToPlayer = (side, payload) =>
        {
            if (side != 1) return;
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            if (document.RootElement.TryGetProperty("lastAction", out var action) && action.GetString() == "RevealCards")
                reveals.Add(document.RootElement.GetProperty("reveal").GetProperty("cardNumbers").EnumerateArray().Select(x => x.GetString()!).ToArray());
        };
        var source = Card(suffix); var script = CardRulesetManager.For(s).TryGetScriptedEffect(source.Info.Number)!;
        await script.Resolve(new EffectContext { State = s, OwnerIndex = 0, Source = source, ExecutionId = "公开验证",
            Trigger = suffix == "105" ? EffectTrigger.OnLifeRevealTrigger : suffix is "049" or "110" ? EffectTrigger.OnEnterField : EffectTrigger.EventMain,
            Engine = engine, Prompts = new MockPromptService().QueueChoose(wanted.Id.ToString()) });
        Assert.Equal(new[] { wanted.Info.Number }, Assert.Single(reveals));
    }
}
