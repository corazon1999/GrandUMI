using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class CardEffectCompletionAuditTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public void 明确缺失与断连卡牌_全部具备所需触发标签()
    {
        _ = TestScene.New().Build();
        var expected = new Dictionary<string, string[]>
        {
            ["OP12-021"] = ["OnEnterField"], ["OP12-036"] = ["OnEnterField"],
            ["OP12-072"] = ["OnDonReturnedToDeck"], ["OP12-081"] = ["OnAttackDeclare", "OnAllyCharEnter"],
            ["ST36-001"] = ["OnKO"], ["ST36-002"] = ["OnEnterField"], ["ST36-004"] = ["OnEnterField"],
            ["ST36-005"] = ["OnOppAttackDeclare", "ActivatedMain"],
            ["EB04-029"] = ["EventMain", "EventCounter"], ["OP04-093"] = ["EventMain"],
            ["OP05-096"] = ["EventMain"], ["EB03-008"] = ["OnEnterField", "OnAttackDeclare", "ActivatedMain"],
            ["EB04-016"] = ["ActivatedMain", "OnAttackDeclare"], ["OP11-028"] = ["OnEnterField"],
            ["OP11-031"] = ["OnEnterField", "ActivatedMain"], ["OP11-084"] = ["OnEnterField", "OnAttackDeclare"],
            ["OP11-119"] = ["OnEnterField", "OnAttackDeclare"], ["OP12-117"] = ["EventMain", "EventCounter"],
            ["OP15-003"] = ["PreKO", "ActivatedMain"], ["OP15-012"] = ["OnAttackDeclare", "OnKO"],
            ["OP15-037"] = ["EventMain"], ["OP15-038"] = ["EventMain", "EventCounter"],
            ["OP15-041"] = ["OnKO", "ActivatedMain"], ["OP15-056"] = ["EventMain"],
            ["OP15-057"] = ["OnEnterField", "OnOppAttackDeclare"], ["OP15-084"] = ["OnEnterField", "OnKO"],
            ["OP15-115"] = ["EventMain"], ["OP16-057"] = ["EventCounter"],
            ["OP16-068"] = ["OnEnterField", "OnAttackDeclare"], ["ST05-010"] = ["OnEnterField", "ActivatedMain"],
            ["OP03-001"] = ["OnAttackDeclare", "OnOppAttackDeclare"],
            ["OP04-021"] = ["OnOppAttackDeclare"], ["OP04-025"] = ["OnOppAttackDeclare"],
            ["OP04-030"] = ["OnOppAttackDeclare"], ["OP04-059"] = ["OnOppAttackDeclare"],
            ["OP04-060"] = ["OnOppAttackDeclare"], ["OP04-063"] = ["OnOppAttackDeclare"],
            ["OP04-069"] = ["OnOppAttackDeclare"], ["OP04-070"] = ["OnOppAttackDeclare"],
            ["OP04-071"] = ["OnOppAttackDeclare"], ["OP04-072"] = ["OnOppAttackDeclare"],
            ["OP07-098"] = ["PreKO"], ["OP10-037"] = ["PreKO"], ["OP10-118"] = ["PreKO"],
            ["OP12-024"] = ["PreKO"], ["OP13-084"] = ["PreKO"], ["ST02-001"] = ["ActivatedMain"],
            ["EB04-001"] = ["OnGameStart"], ["OP01-024"] = ["OnEnterField"],
            ["OP04-082"] = ["PreKO"], ["OP13-109"] = ["OnAllyWillLeaveField"],
            ["OP14-045"] = ["OnHandDiscarded"], ["OP14-049"] = ["OnHandDiscarded"],
        };

        foreach (var (number, tags) in expected)
        {
            var info = Assert.IsType<CardInfo>(CardDatabase.Get(number));
            foreach (var tag in tags) Assert.Contains(tag, info.EffectTags);
        }
    }

    [Fact]
    public void 关键词迁移_仅保留常驻能力并写入规则别名()
    {
        _ = TestScene.New().Build();

        Assert.DoesNotContain("速攻", CardDatabase.Get("OP12-072")!.Abilities);
        Assert.DoesNotContain("速攻", CardDatabase.Get("EB01-045")!.Abilities);
        Assert.DoesNotContain("双重攻击", CardDatabase.Get("OP04-093")!.Abilities);
        Assert.Contains("阻挡者", CardDatabase.Get("OP12-021")!.Abilities);
        Assert.Contains("无法通过效果登场", CardDatabase.Get("OP12-036")!.Abilities);
        Assert.Contains("此角色无法攻击", CardDatabase.Get("OP04-001")!.Abilities);
        Assert.Contains("托尼托尼·乔巴", CardDatabase.Get("EB02-016")!.AlsoNames);
        Assert.Contains("光月御殿", CardDatabase.Get("OP02-042")!.AlsoNames);
        Assert.Contains("撒谎布", CardDatabase.Get("OP03-122")!.AlsoNames);
    }

    [Fact]
    public async Task EB01_027_废弃区事件数量实时决定力量加成()
    {
        var state = TestScene.New("OP01-062").MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = Card("EB01-027");
        me.Characters.Add(source);
        for (var i = 0; i < 4; i++) me.Trash.Add(Card("OP15-095"));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(source.Info.Power + 2000, state.CurrentPowerOf(0, source));
        me.Trash.Add(Card("OP15-095"));
        me.Trash.Add(Card("OP15-095"));
        Assert.Equal(source.Info.Power + 3000, state.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task EB04_016_启动后会阻止后续角色效果活跃既有休息咚()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var bird = Card("EB04-016");
        var inuarashi = Card("OP01-034");
        me.Characters.AddRange([bird, inuarashi]);
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var protectedDon = new DonCard { State = DonState.Rest };
        me.CostArea.Add(protectedDon);
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = inuarashi.Id });
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = inuarashi.Id });
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, bird, EffectTrigger.ActivatedMain, prompts);
        await EffectRuntime.Resolve(state, 0, inuarashi, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(DonState.Rest, protectedDon.State);
    }

    [Fact]
    public async Task OP04_082_可休息领袖来置换自身KO()
    {
        var state = TestScene.New().Build();
        var source = Card("OP04-082");
        state.Players[0].Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.PreKO, new MockPromptService());

        Assert.True(state.Players[0].Leader.IsTapped);
        Assert.Contains(source.Id, state.PreventKOCardIds);
    }

    [Fact]
    public async Task OP08_075_支付咚成本后横置目标并翻回全部生命()
    {
        var state = TestScene.New().MyActiveDon(1).OppCharacter("OP15-003").Build();
        var source = Card("OP08-075");
        var target = state.Players[1].Characters[0];
        target.CostModThisTurn = -target.Info.Cost;
        state.Players[0].LifeArea.AddRange([Card("OP15-003"), Card("OP15-004")]);
        foreach (var life in state.Players[0].LifeArea) life.IsLifeFaceUp = true;
        var prompts = new MockPromptService().QueueChoose(state.Players[0].CostArea[0].Id.ToString()).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.True(target.IsTapped);
        Assert.All(state.Players[0].LifeArea, life => Assert.False(life.IsLifeFaceUp));
    }

    [Fact]
    public async Task OP12_036_在手牌中无法通过效果登场但可正常持有()
    {
        var state = TestScene.New().Build();
        var source = Card("OP12-036");
        state.Players[0].Hand.Add(source);

        await AtomicOps.PlayFromHandFree(state, 0, source);

        Assert.Contains(source, state.Players[0].Hand);
        Assert.DoesNotContain(source, state.Players[0].Characters);
    }

    [Fact]
    public void OP04_001_静态禁攻由统一校验器消费()
    {
        var state = TestScene.New("OP04-001").Build();
        state.TurnCount = 3;

        var result = ActionValidator.CanAttack(state, 0, state.Players[0].Leader.Id, true, null);

        Assert.False(result.Ok);
        Assert.Contains("无法攻击", result.Reason);
    }

    [Fact]
    public async Task OP02_048_取消复合成本时不会先横置舞台()
    {
        var state = TestScene.New().Build();
        var source = Card("OP02-048");
        var discardable = Card("OP02-025");
        state.Players[0].StageCard = source;
        state.Players[0].Hand.Add(discardable);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChooseEmpty());

        Assert.False(source.IsTapped);
        Assert.Contains(discardable, state.Players[0].Hand);
    }

    [Fact]
    public async Task OP03_122_可将任意一方角色退手并完成抽二弃二()
    {
        var state = TestScene.New().Build();
        var source = Card("OP03-122");
        var ownTarget = Card("OP15-003");
        var drawA = Card("OP15-004");
        var drawB = Card("OP15-005");
        state.Players[0].Characters.AddRange([source, ownTarget]);
        state.Players[0].Deck.AddRange([drawA, drawB]);
        var prompts = new MockPromptService()
            .QueueChoose(ownTarget.Id.ToString())
            .QueueChoose(drawA.Id.ToString(), drawB.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(ownTarget, state.Players[0].Hand);
        Assert.Contains(drawA, state.Players[0].Trash);
        Assert.Contains(drawB, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP07_107_生命多于一张时仍会先抽卡但不会登场()
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var source = Card("OP07-107");
        state.Players[0].Trash.Add(source);
        state.Players[0].LifeArea.AddRange([Card("OP15-004"), Card("OP15-005")]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Single(state.Players[0].Hand);
        Assert.Contains(source, state.Players[0].Trash);
        Assert.DoesNotContain(source, state.Players[0].Characters);
    }

    [Fact]
    public async Task OP08_095_力量加成使用跨对方回合持续通道()
    {
        var state = TestScene.New().Build();
        var source = Card("OP08-095");
        var target = Card("OP15-003");
        state.Players[0].Characters.Add(target);
        for (var i = 0; i < 10; i++) state.Players[0].Trash.Add(Card("OP15-095"));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        var modifier = Assert.Single(target.PowerModsUntilOppEnd);
        Assert.Equal(2000, modifier.Delta);
        Assert.Equal(0, modifier.AppliedBySide);
    }

    [Fact]
    public async Task OP09_076_可一次放回多张咚并仅追加一张活跃咚()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var source = Card("OP09-076");
        state.Players[0].Characters.Add(source);
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });
        var returnedIds = state.Players[0].CostArea.Select(don => don.Id.ToString()).ToArray();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(returnedIds));

        Assert.Single(state.Players[0].CostArea);
        Assert.Equal(DonState.Active, state.Players[0].CostArea[0].State);
        Assert.Equal(3, state.Players[0].DonDeck.Count);
    }

    [Fact]
    public async Task OP09_101_将角色正面放入指定生命边并让对方弃牌()
    {
        var state = TestScene.New().OppCharacter("OP09-002").Build();
        var source = Card("OP09-101");
        var target = state.Players[1].Characters[0];
        var discard = Card("OP15-003");
        state.Players[1].Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChoose(discard.Id.ToString())
            .QueueOption(1);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[1].Characters);
        Assert.Same(target, Assert.Single(state.Players[1].LifeArea));
        Assert.True(target.IsLifeFaceUp);
        Assert.Contains(discard, state.Players[1].Trash);
    }
}
