using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月11日游戏内F反馈的实际结算、区域移动及规则边界回归。</summary>
public sealed class PlayerFeedback20261011Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData("OP15-058", true)]
    [InlineData("OP05-098", true)]
    [InlineData("OP15-001", false)]
    public async Task 放电反击能够选择艾尼路领袖且排除其他领袖(string leader, bool eligible)
    {
        var state = TestScene.New(leader).MyDeckTop("OP15-003").Build();
        var target = state.Players[0].Leader;
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-074"), EffectTrigger.EventCounter, prompts);

        Assert.Equal(eligible, Assert.Single(prompts.ChooseHistory).choices.Contains(target.Id.ToString()));
        Assert.Equal(eligible ? 2000 : 0, target.PowerModThisBattle);
    }

    [Fact]
    public async Task 甚平公开两张事件后加攻持续到下个对方结束阶段而速攻仅本回合()
    {
        var state = TestScene.New().MyHandAdd("OP12-009").MyHandAdd("OP15-074")
            .MyHandAdd("OP14-018").MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = me.Hand[0];
        var events = me.Hand.Skip(1).ToArray();
        var prompts = new MockPromptService().QueueChoose(events.Select(card => card.Id.ToString()).ToArray());

        await AtomicOps.PlayFromHandFree(state, 0, source);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Equal(5000, state.CurrentPowerOf(0, source));
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Equal(events, me.Hand);
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5000, state.CurrentPowerOf(0, source));
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(4000, state.CurrentPowerOf(0, source));
        Assert.Empty(source.PowerModsUntilOppEnd);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 0)]
    [InlineData(7, 0)]
    public async Task 毛奇卡巴奇仅在登场后手牌不多于五张时抽两张(int remainingHand, int drawCount)
    {
        var state = TestScene.New().MyHandAdd("OP16-051")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var me = state.Players[0];
        var source = me.Hand[0];
        for (int i = 0; i < remainingHand; i++) me.Hand.Add(Card("OP15-003"));

        await AtomicOps.PlayFromHandFree(state, 0, source);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());

        Assert.Equal(remainingHand + drawCount, me.Hand.Count);
        Assert.Equal(3 - drawCount, me.Deck.Count);
    }

    [Theory]
    [InlineData("OP06-097", true)]
    [InlineData("OP06-098", true)]
    [InlineData("OP06-091", true)]
    [InlineData("OP06-090", false)]
    [InlineData("OP15-074", false)]
    public async Task 豪格巴克支付两张废弃区成本后可回收本家事件舞台及角色(string number, bool eligible)
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var costs = new[] { Card("OP15-004"), Card("OP15-005") };
        var target = Card(number);
        me.Trash.AddRange(costs.Append(target));
        var prompts = new MockPromptService().QueueChoose(costs.Reverse().Select(card => card.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP06-090"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(eligible, me.Hand.Contains(target));
        Assert.Equal(!eligible, me.Trash.Contains(target));
        Assert.Equal(costs.Reverse(), me.Deck.Skip(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task 卡莉法回收所选全部合法角色且没有复制未选卡(int count)
    {
        var state = TestScene.New("OP03-058").MyActiveDon(1).MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var cards = new[] { Card("OP14-013"), Card("OP14-010"), Card("OP06-090") };
        me.Trash.AddRange(cards);
        var prompts = new MockPromptService().QueueChoose(me.CostArea[0].Id.ToString())
            .QueueChoose(cards.Take(count).Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, Card("EB01-031"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(cards.Take(count), me.Hand);
        Assert.Equal(cards.Skip(count), me.Trash);
        Assert.Empty(me.CostArea);
        Assert.Single(me.DonDeck);
    }

    [Theory]
    [InlineData("巴洛克工作室", 3000, true)]
    [InlineData("原巴洛克工作室", 3000, true)]
    [InlineData("原巴洛克工作室", 2000, false)]
    [InlineData("草帽一伙", 5000, false)]
    public async Task 蓝罗宾领袖在包含巴洛克特征且原本力量达标的角色被KO后抽牌并让对方弃牌(
        string feature, int power, bool eligible)
    {
        var state = TestScene.New("OP18-041").MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var victim = new CardInstance { Info = new CardInfo
        {
            Number = "TEST-BAROQUE", Name = "回归角色", Kind = CardKind.Character,
            Color = "蓝", Property = "知", Cost = 3, Power = power, Keywords = [feature],
        }};
        me.Characters.Add(victim);
        var discarded = Card("OP15-003");
        state.Players[1].Hand.Add(discarded);
        var prompts = new MockPromptService().QueueChoose(discarded.Id.ToString());

        await AtomicOps.KOByEffectAsync(state, 0, victim, prompts, 1);

        Assert.Contains(victim, me.Trash);
        Assert.Equal(eligible ? 1 : 0, me.Hand.Count);
        Assert.Equal(eligible, state.Players[1].Trash.Contains(discarded));
    }

    [Fact]
    public async Task 不吉利二人组能将原巴洛克角色放底作为成本让对方弃牌()
    {
        var state = TestScene.New().MyCharacter("OP18-056").MyCharacter("OP09-056")
            .MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var target = me.Characters[1];
        Assert.True(target.Info.HasKeywordContaining("巴洛克工作室"));
        Assert.False(target.Info.HasKeyword("巴洛克工作室"));
        var discarded = Card("OP15-004");
        state.Players[1].Hand.Add(discarded);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString()).QueueChoose(discarded.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.DoesNotContain(target, me.Characters);
        Assert.Same(target, me.Deck.Last());
        Assert.Contains(discarded, state.Players[1].Trash);
    }

    [Theory]
    [InlineData("alreadyRested")]
    [InlineData("cannotRest")]
    public async Task 薇薇领袖不能在无法支付休息成本时重复减攻或赋予速攻(string mode)
    {
        var state = TestScene.New("EB03-001").MyCharacter("OP15-003").OppCharacter("OP15-003")
            .MyDeckTop("OP15-004").Build();
        var me = state.Players[0];
        me.Leader.IsTapped = mode == "alreadyRested";
        if (mode == "cannotRest") AtomicOps.AddRestriction(me.Leader, RestrictionKind.CannotBeRested, KeywordDuration.ThisTurn);
        var prompts = new MockPromptService();

        Assert.False(ActionValidator.CanUseEffect(state, 0, me.Leader.Id).Ok);
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(0, state.Players[1].Characters[0].PowerModThisTurn);
        Assert.False(ActionValidator.HasKeyword(state, me.Characters[0], "速攻"));
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 薇薇领袖被合法重置后仍可再次支付成本发动()
    {
        var state = TestScene.New("EB03-001").OppCharacter("OP15-003").MyDeckTop("OP15-004").Build();
        var source = state.Players[0].Leader;
        var target = state.Players[1].Characters[0];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString()).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.False(ActionValidator.CanUseEffect(state, 0, source.Id).Ok);
        AtomicOps.ActivateCard(source);
        Assert.True(ActionValidator.CanUseEffect(state, 0, source.Id).Ok);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(-4000, target.PowerModThisTurn);
    }

    [Theory]
    [InlineData("OP09-056", 1, true)]
    [InlineData("OP09-056", 9, false)]
    [InlineData("OP14-092", 4, true)]
    [InlineData("OP15-003", 1, false)]
    public async Task 贝布KO后可回收费用合格的原巴洛克角色且排除超费与非本家(string number, int printedCost, bool eligible)
    {
        var state = TestScene.New().MyCharacter("OP14-093").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var info = Card(number).Info;
        var target = new CardInstance { Info = new CardInfo
        {
            Number = info.Number, Name = info.Name, Color = info.Color, Kind = info.Kind,
            Property = info.Property, Power = info.Power, Cost = printedCost, Keywords = info.Keywords,
        }};
        me.Trash.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await AtomicOps.KOByEffectAsync(state, 0, source, prompts, 1);

        Assert.Contains(source, me.Trash);
        Assert.Equal(eligible, me.Hand.Contains(target));
        Assert.Equal(!eligible, me.Trash.Contains(target));
        Assert.Equal(eligible, prompts.ChooseHistory.Any(prompt => prompt.choices.Contains(target.Id.ToString())));
    }

    [Theory]
    [InlineData("PRB02-001", true)]
    [InlineData("P-092", true)]
    [InlineData("P-014", true)]
    [InlineData("OP02-098", false)]
    public async Task 山治可赋予无登场效果的可比速攻而不绕过真实登场效果限制(string number, bool eligible)
    {
        var state = TestScene.New("PRB01-001").MyHandAdd(number).MyDeckTop("OP15-003").Build();
        state.TurnCount = 3;
        var me = state.Players[0];
        var target = me.Hand[0];
        await AtomicOps.PlayFromHandFree(state, 0, target);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService().QueueConfirm(false));
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(eligible, ActionValidator.HasKeyword(state, target, "速攻"));
        Assert.Equal(eligible, prompts.ChooseHistory.Any(prompt => prompt.choices.Contains(target.Id.ToString())));
        Assert.Equal(eligible, ActionValidator.CanAttack(state, 0, target.Id, true, null).Ok);
    }

    [Theory]
    [InlineData("PRB02-001", 5000, 6000)]
    [InlineData("P-092", 7000, 4000)]
    public async Task 可比静态力量在整卡无效结束后恢复且不声明登场效果(string number, int ownPower, int oppPower)
    {
        var state = TestScene.New("OP02-093").MyHandAdd(number).MyDeckTop("OP15-003").Build();
        var source = state.Players[0].Hand[0];
        source.IsEffectsNullified = true;
        await AtomicOps.PlayFromHandFree(state, 0, source);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());
        source.IsEffectsNullified = false;

        Assert.DoesNotContain("OnEnterField", source.Info.EffectTags);
        Assert.Equal(ownPower, state.CurrentPowerOf(0, source));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(oppPower, state.CurrentPowerOf(0, source));
    }

    [Theory]
    [InlineData("OP17-092", "OP17-085", false)]
    [InlineData("OP17-092", "OP17-085", true)]
    [InlineData("OP17-085", "OP17-092", false)]
    [InlineData("OP17-085", "OP17-092", true)]
    public async Task 东利布洛基从废弃区只登场同伴且选零张也施加后续登场限制(string sourceNumber, string targetNumber, bool skip)
    {
        var state = TestScene.New("OP17-079").MyCharacter(sourceNumber).MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var target = Card(targetNumber);
        var wrong = Card("OP17-089");
        me.Trash.AddRange([target, wrong]);
        var prompts = skip ? new MockPromptService().QueueChooseEmpty()
            : new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Characters[0], EffectTrigger.OnEnterField, prompts);

        Assert.Contains(target.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
        Assert.DoesNotContain(wrong.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.Equal(!skip, me.Characters.Contains(target));
        Assert.Contains(wrong, me.Trash);
        Assert.Contains(0, state.NoPlayCharacterThisTurn);
    }

    [Theory]
    [InlineData(5000, false)]
    [InlineData(6000, true)]
    public async Task 五费邦妮按当前力量门槛选择罗宾而非减攻后的显示值(int currentPower, bool eligible)
    {
        var state = TestScene.New().MyCharacter("EB05-001").OppCharacter("OP18-031").MyDeckTop("OP15-003").Build();
        var target = state.Players[1].Characters[0];
        target.PowerModThisTurn = currentPower - target.Info.Power;
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Characters[0], EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(eligible, prompts.ChooseHistory.Any(prompt => prompt.choices.Contains(target.Id.ToString())));
        Assert.Equal(currentPower - (eligible ? 1000 : 0), state.CurrentPowerOf(1, target));
    }

    [Fact]
    public async Task 两张同名反击事件分别支付休息成本后叠加而无成本的第三张不加攻()
    {
        var state = TestScene.New().MyCharacter("OP15-003").MyDeckTop("OP15-004").Build();
        var me = state.Players[0];
        var target = me.Leader;
        var cost = me.Characters[0];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString()).QueueChoose(target.Id.ToString())
            .QueueChoose(cost.Id.ToString()).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP17-037"), EffectTrigger.EventCounter, prompts);
        Assert.Equal(3000, target.PowerModThisBattle);
        await EffectRuntime.Resolve(state, 0, Card("OP17-037"), EffectTrigger.EventCounter, prompts);
        Assert.Equal(6000, target.PowerModThisBattle);
        Assert.True(target.IsTapped);
        Assert.True(cost.IsTapped);
        await EffectRuntime.Resolve(state, 0, Card("OP17-037"), EffectTrigger.EventCounter, prompts);
        Assert.Equal(6000, target.PowerModThisBattle);
        Assert.Equal(4, prompts.ChooseHistory.Count);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task 巴基要求五张当前费用至少五的角色才能留下(int highCostCount, bool remains)
    {
        var state = TestScene.New().MyCharacter("OP09-051").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        for (int i = 1; i < highCostCount; i++) me.Characters.Add(Card("OP17-085"));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(remains, me.Characters.Contains(source));
        Assert.Equal(!remains, me.Deck.Contains(source));
    }

    [Fact]
    public async Task 路飞可检索OP14018事件且奈美保留印刷反击值而斯图西不额外获得阻挡者()
    {
        var state = TestScene.New().MyDeckTop("OP14-018", "OP15-003", "OP15-004").Build();
        var target = state.Players[0].Deck[0];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-013"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(target, state.Players[0].Hand);
        Assert.Contains(target.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.Equal(2000, Card("EB05-061").Info.Counter);
        Assert.False(ActionValidator.HasKeyword(state, Card("OP17-054"), "阻挡者"));
    }

    [Theory]
    [InlineData("OP17-046")]
    [InlineData("OP17-054")]
    [InlineData("OP17-066")]
    [InlineData("OP17-105")]
    public async Task 四张误填智属性的卡牌按知属性参与罗宾领袖重置(string number)
    {
        var state = TestScene.New("EB05-010").MyCharacter(number).MyDeckTop("OP15-003").Build();
        var target = state.Players[0].Characters[0];
        target.IsTapped = true;
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal("知", target.Info.Property);
        Assert.False(target.IsTapped);
    }
}
