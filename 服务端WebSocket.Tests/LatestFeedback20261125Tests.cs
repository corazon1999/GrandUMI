using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>11月25日双源反馈：覆盖真实效果入口、来源和持续时间边界。</summary>
public sealed class LatestFeedback20261125Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static async Task SuppressEnter(GameState s, int owner)
    {
        var opponent = s.Players[1 - owner];
        var discard = Card("OP15-003");
        opponent.Hand.Add(discard);
        await EffectRuntime.Resolve(s, 1 - owner, opponent.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task 黑胡子也封锁生命触发复用的登场效果但不封普通触发(int owner, bool suppressed)
    {
        var s = TestScene.New("OP09-081", "OP09-081").Build();
        var me = s.Players[owner];
        if (suppressed) await SuppressEnter(s, owner);
        var lilith = Card("OP13-113"); var draw = Card("EB02-030");
        me.LifeArea.Add(lilith);
        me.Deck.AddRange([draw, Card("OP15-003"), Card("OP15-003"), Card("OP15-003")]);
        var prompts = new MockPromptService().QueueLifeTrigger(true).QueueChoose(draw.Id.ToString());
        await LifeRevealManager.DealDamageToLeader(s, prompts, owner, 1);
        Assert.Equal(!suppressed, me.Hand.Contains(draw));
        Assert.Contains(lilith, me.Trash);
        Assert.DoesNotContain(lilith, me.Characters);
        var plainTrigger = Card("EB02-030"); me.LifeArea.Add(plainTrigger);
        int before = me.Hand.Count;
        await LifeRevealManager.DealDamageToLeader(s, new MockPromptService().QueueLifeTrigger(true), owner, 1);
        Assert.Equal(before + 1, me.Hand.Count);
    }

    [Fact]
    public async Task 人妖之道识别旧卡BW领袖并改变攻击目标()
    {
        var s = TestScene.New("OP04-058").MyCharacter("OP09-046").Build(); var me = s.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        s.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = s.Players[1].Leader.Id, TargetIsLeader = true };
        await EffectRuntime.Resolve(s, 0, Card("EB01-038"), EffectTrigger.EventCounter, new MockPromptService());
        Assert.False(s.CurrentBattle.TargetIsLeader);
        Assert.Equal(me.Characters[0].Id, s.CurrentBattle.TargetCardId);
        Assert.Empty(me.CostArea);
        Assert.False(Card("OP09-046").Info.HasKeyword("巴洛克工作室"));
        Assert.True(Card("OP09-046").Info.HasKeywordContaining("巴洛克工作室"));
    }

    [Theory]
    [InlineData("OP15-009")]
    [InlineData("OP18-054")]
    public async Task 克比和巴洛克守护各付一次成本防止同批沉底(string guardian)
    {
        var s = TestScene.New().MyCharacter(guardian).MyCharacter("OP09-046").Build(); s.CurrentTurnPlayer = 1;
        var me = s.Players[0]; var cost = Card("OP15-003"); me.Hand.Add(cost);
        var source = Card("OP18-051"); s.Players[1].Characters.Add(source);
        s.Players[1].CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new MockPromptService().QueueChoose(me.Characters[0].Id.ToString()).QueueChoose(me.Characters[1].Id.ToString());
        await EffectRuntime.Resolve(s, 1, source, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(2, me.Characters.Count);
        if (guardian == "OP15-009") Assert.Equal(-2000, me.Leader.PowerModThisTurn);
        else { Assert.Empty(me.Hand); Assert.Contains(cost, me.Trash); }
    }

    [Theory]
    [InlineData("OP12-015", "OP04-058", 2000)]
    [InlineData("OP16-027", "OP04-058", 2000)]
    [InlineData("OP15-118", "OP04-058", 2000)]
    [InlineData("OP15-060", "OP04-058", 2000)]
    [InlineData("EB01-027", "OP04-058", 4000)]
    public async Task 黑胡子封登场不吞掉动态力量(string number, string leader, int bonus)
    {
        var s = TestScene.New(leader, "OP09-081").Build();
        var me = s.Players[0];
        var card = Card(number);
        me.Characters.Add(card);
        me.Trash.AddRange(Enumerable.Range(0, 8).Select(_ => Card("OP01-029")));
        me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ =>
            new DonCard { State = DonState.Attached, AttachedToCardId = card.Id }));
        await SuppressEnter(s, 0);
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(card.Info.Power + 2000 + bonus, s.CurrentPowerOf(0, card));
        card.IsEffectsNullified = true;
        Assert.Equal(card.Info.Power + 2000, s.CurrentPowerOf(0, card));
        card.IsEffectsNullified = false;
        Assert.Equal(card.Info.Power + 2000 + bonus, s.CurrentPowerOf(0, card));
    }

    [Theory]
    [InlineData("OP10-085")]
    [InlineData("OP13-080")]
    [InlineData("OP14-004")]
    public async Task 黑胡子封登场不吞掉条件速攻(string number)
    {
        var s = TestScene.New("OP04-058", "OP09-081").Build();
        var card = Card(number);
        s.Players[0].Characters.Add(card);
        s.Players[0].Trash.AddRange(Enumerable.Range(0, 8).Select(_ => Card("OP15-003")));
        s.Players[0].CostArea.AddRange(Enumerable.Range(0, 3).Select(_ =>
            new DonCard { State = DonState.Attached, AttachedToCardId = card.Id }));
        await SuppressEnter(s, 0);
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(ActionValidator.HasKeyword(s, card, "速攻"));
        card.IsEffectsNullified = true;
        Assert.False(ActionValidator.HasKeyword(s, card, "速攻"));
    }

    [Fact]
    public async Task 葛尔兹封登场时仍按埃鲁巴夫领袖增加费用()
    {
        var s = TestScene.New("OP17-079", "OP09-081").MyCharacter("OP17-081").Build();
        var card = s.Players[0].Characters[0];
        await SuppressEnter(s, 0);
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(card.Info.Cost + 12, s.CurrentCostOf(0, card));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 克比不防战斗KO也不显示发动提示(int owner)
    {
        var s = TestScene.New().Build();
        var me = s.Players[owner];
        me.Characters.Add(Card("OP15-009"));
        var victim = Card("OP15-003");
        me.Characters.Add(victim);
        s.KOReason = "battle";
        s.KOActingSide = 1 - owner;
        var prompts = new MockPromptService();
        Assert.True(await BattleEngine.KOCardAsync(s, owner, victim, prompts));
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(0, me.Leader.PowerModThisTurn);
        Assert.Contains(victim, me.Trash);
    }

    [Fact]
    public async Task 克比不保护我方效果KO但一次成本覆盖对方批量KO()
    {
        var s = TestScene.New().MyCharacter("OP15-009").MyCharacter("OP15-003").MyCharacter("OP15-003").Build();
        var me = s.Players[0];
        var prompts = new MockPromptService();
        Assert.True(await AtomicOps.KOByEffectAsync(s, 0, me.Characters[1], prompts, 0));
        Assert.Empty(prompts.ConfirmHistory);
        var victims = me.Characters.ToList();
        Assert.Equal(0, await AtomicOps.KOCardsByEffectAsync(s, 0, victims, prompts, 1));
        Assert.Equal(-2000, me.Leader.PowerModThisTurn);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Equal(2, me.Characters.Count);
    }

    [Fact]
    public async Task 瓦波尔加攻持续至对方结束而减攻只持续本回合()
    {
        var s = TestScene.New().MyCharacter("OP08-014").OppCharacter("OP15-003").Build();
        var card = s.Players[0].Characters[0];
        var victim = s.Players[1].Characters[0];
        s.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = card.Id });
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.OnAttackDeclare, new MockPromptService());
        Assert.Equal(-2000, victim.PowerModThisTurn);
        Assert.Equal(2000, Assert.Single(card.PowerModsUntilOppEnd).Delta);
        Assert.Equal(0, card.PowerModThisTurn);
    }

    [Fact]
    public async Task 弗兰奇没有场地仍可支付返咚成本且每回合只一次()
    {
        var s = TestScene.New("OP18-021").Build();
        var me = s.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Active });
        me.CostArea.Add(new DonCard { State = DonState.Active });
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Single(me.CostArea);
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Single(me.CostArea);
    }

    [Fact]
    public async Task 白星赋予的登场回合攻击只允许攻击角色()
    {
        var s = TestScene.New().MyCharacter("P-091").MyCharacter("EB04-016").OppCharacter("OP15-003").Build();
        s.TurnCount = 4;
        var target = s.Players[0].Characters[1];
        target.TurnPlayed = s.TurnCount;
        s.Players[1].Characters[0].IsTapped = true;
        await EffectRuntime.Resolve(s, 0, s.Players[0].Characters[0], EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.False(ActionValidator.CanAttack(s, 0, target.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(s, 0, target.Id, false, s.Players[1].Characters[0].Id).Ok);
    }

    [Fact]
    public async Task 人类金刚钻攻击横置也触发休息对方角色()
    {
        var s = TestScene.New().MyCharacter("OP14-032").OppCharacter("OP09-095").Build();
        var self = s.Players[0].Characters[0];
        self.IsTapped = true;
        await EffectRuntime.Resolve(s, 0, self, EffectTrigger.OnCharRested, new MockPromptService(),
            new() { ["restedCardId"] = self.Id.ToString(), ["reason"] = "attack" });
        Assert.True(s.Players[1].Characters[0].IsTapped);
    }

    [Fact]
    public async Task 哈库不能给没有德莱斯罗兹特征的领袖贴咚()
    {
        var s = TestScene.New("OP11-041").MyCharacter("OP18-042").Build();
        var me = s.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService().QueueChoose(me.Leader.Id.ToString());
        await EffectRuntime.Resolve(s, 0, me.Characters[0], EffectTrigger.ActivatedMain, prompts);
        Assert.DoesNotContain(me.Leader.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task 愿幸运与你同在可以弃原巴洛克角色支付主要成本()
    {
        var s = TestScene.New().Build();
        var me = s.Players[0];
        var cost = Card("OP09-046");
        Assert.True(cost.Info.HasKeywordContaining("巴洛克工作室"));
        me.Hand.Add(cost);
        me.Deck.AddRange([Card("OP15-003"), Card("OP15-003")]);
        me.CostArea.Add(new DonCard { State = DonState.Active });
        await EffectRuntime.Resolve(s, 0, Card("OP18-059"), EffectTrigger.EventMain, new MockPromptService());
        Assert.Contains(cost, me.Trash);
        Assert.Equal(2, me.Hand.Count);
    }

    [Fact]
    public async Task 罗登场也给罗西南德领袖增加力量()
    {
        var s = TestScene.New("OP12-061").MyCharacter("OP12-073").Build();
        var me = s.Players[0];
        await EffectRuntime.Resolve(s, 0, me.Characters[0], EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(me.Leader.Info.Power + 1000, s.CurrentPowerOf(0, me.Leader));
        Assert.Equal(1000, Assert.Single(me.Leader.PowerModsUntilOppEnd).Delta);
    }
}
