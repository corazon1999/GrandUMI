using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月20日玩家反馈的真实结算链与候选边界回归。</summary>
public sealed class PlayerFeedback20261020Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 乔兹回手凶前可以支付或拒绝离场保护且不退还乔兹成本(bool accept)
    {
        var s = TestScene.New().MyCharacter("OP17-045").MyHandAdd("OP15-003").MyHandAdd("OP15-004")
            .OppCharacter("OP08-047").OppCharacter("OP15-003").Build();
        var me = s.Players[0]; var opp = s.Players[1];
        var victim = me.Characters[0]; var source = opp.Characters[0]; var cost = opp.Characters[1];
        var p = new MockPromptService().QueueConfirm(true).QueueConfirm(accept)
            .QueueChoose(cost.Id.ToString()).QueueChoose(victim.Id.ToString())
            .QueueChoose(me.Hand.Select(x => x.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(s, 1, source, EffectTrigger.OnEnterField, p);
        Assert.Contains(cost, opp.Hand);
        Assert.Equal(accept, me.Characters.Contains(victim));
        Assert.Equal(!accept, me.Hand.Contains(victim));
        Assert.Equal(accept ? 2 : 0, me.Trash.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 乔兹回手空岛角色经过路飞领袖的生命保护(bool accept)
    {
        var s = TestScene.New("OP15-098").MyCharacter("OP05-102")
            .OppCharacter("OP08-047").OppCharacter("OP15-003").Build();
        var me = s.Players[0]; var opp = s.Players[1];
        var life = Card("OP15-004"); me.LifeArea.Add(life);
        var victim = me.Characters[0]; var source = opp.Characters[0]; var cost = opp.Characters[1];
        var p = new MockPromptService().QueueConfirm(true).QueueConfirm(accept)
            .QueueChoose(cost.Id.ToString()).QueueChoose(victim.Id.ToString());
        await EffectRuntime.Resolve(s, 1, source, EffectTrigger.OnEnterField, p);
        Assert.Equal(accept, me.Characters.Contains(victim));
        Assert.Equal(!accept, me.LifeArea.Contains(life));
        Assert.Equal(accept, me.Hand.Contains(life));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 瓦斯蒂诺把凶放入生命前先询问保护(bool accept)
    {
        var s = TestScene.New().MyCharacter("OP17-045").MyHandAdd("OP15-003").MyHandAdd("OP15-004")
            .OppCharacter("EB01-053").Build();
        var me = s.Players[0]; var victim = me.Characters[0];
        var p = new MockPromptService().QueueChoose(victim.Id.ToString())
            .QueueChoose(me.Hand.Select(x => x.Id.ToString()).ToArray()).QueueConfirm(accept).QueueOption(1);
        await EffectRuntime.Resolve(s, 1, s.Players[1].Characters[0], EffectTrigger.OnEnterField, p);
        Assert.Equal(accept, me.Characters.Contains(victim));
        Assert.Equal(!accept, me.LifeArea.Contains(victim));
        Assert.Equal(accept ? 2 : 0, me.Trash.Count);
    }

    [Theory]
    [InlineData("P-102", 2000)]
    [InlineData("OP08-028", 1000)]
    public void 反击值与原始卡面一致(string number, int expected)
        => Assert.Equal(expected, CardDatabase.Get(number)!.Counter);

    [Fact]
    public void 莉贝卡普通对局不能宣言攻击()
    {
        var s = TestScene.New("OP15-039").Build();
        s.TurnCount = 3; s.Phase = Phase.Main; s.CurrentTurnPlayer = 0;
        Assert.False(ActionValidator.CanAttack(s, 0, s.Players[0].Leader.Id, true, null).Ok);
        Assert.True(ActionValidator.HasCannotAttackStatus(s, s.Players[0].Leader));
    }

    [Fact]
    public async Task 拉琪真正登场卡尔加拉并继续解决其登场检索()
    {
        var s = TestScene.New().MyHandAdd("OP15-112").MyHandAdd("OP15-101").MyHandAdd("OP15-003")
            .MyDeckTop("OP15-101", "OP15-004", "OP15-005", "OP15-006", "OP15-007").Build();
        var me = s.Players[0]; var source = me.Hand[0]; var target = me.Hand[1]; var cost = me.Hand[2];
        var searched = me.Deck[0];
        var p = new MockPromptService().QueueChoose(target.Id.ToString()).QueueConfirm(true)
            .QueueChoose(cost.Id.ToString()).QueueChoose(searched.Id.ToString());
        await AtomicOps.PlayFromHandFree(s, 0, source);
        await EffectRuntime.DrainPendingEnterFields(s, p);
        Assert.Contains(target, me.Characters);
        Assert.Contains(cost, me.Trash);
        Assert.Contains(searched, me.Hand);
    }

    [Theory]
    [InlineData("OP15-101", true)]
    [InlineData("OP15-003", false)]
    [InlineData("OP12-099", false)]
    public async Task 拉琪只接受三费以下山迪亚战士角色(string number, bool eligible)
    {
        var s = TestScene.New().MyHandAdd(number).Build();
        var target = s.Players[0].Hand[0];
        await EffectRuntime.Resolve(s, 0, Card("OP15-112"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(eligible, s.Players[0].Characters.Contains(target));
    }

    [Theory]
    [InlineData("OP16-084", false)]
    [InlineData("OP16-085", false)]
    [InlineData("OP15-003", false)]
    [InlineData("OP06-036", true)]
    public async Task 桃之助复活严格排除同名与非和之国(string number, bool eligible)
    {
        var s = TestScene.New().Build(); var me = s.Players[0]; var target = Card(number); me.Trash.Add(target);
        await EffectRuntime.Resolve(s, 0, Card("OP16-085"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(eligible, me.Characters.Contains(target));
        Assert.Equal(!eligible, me.Trash.Contains(target));
    }

    [Fact]
    public async Task 莫奈允许检索同名的一千力量莫奈()
    {
        var s = TestScene.New().MyDeckTop("EB05-007", "OP15-003", "OP15-004", "OP15-005", "OP15-006").Build();
        var me = s.Players[0]; var target = me.Deck[0];
        await EffectRuntime.Resolve(s, 0, Card("EB03-010"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, me.Hand);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public async Task 黑胡子仅封锁修罗登场时而条件速攻独立生效(int don)
    {
        var s = TestScene.New("OP09-081").MyCharacter("OP15-067").MyActiveDon(don)
            .MyDeckTop("OP15-003").Build();
        var me = s.Players[0]; var source = me.Characters[0];
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(s, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(don <= 6, ActionValidator.HasKeyword(s, source, "速攻"));
        Assert.Equal(don, me.TotalDonInCostArea);
        Assert.Empty(me.Hand);
        source.IsEffectsNullified = true;
        Assert.False(ActionValidator.HasKeyword(s, source, "速攻"));
    }

    [Fact]
    public async Task 地狱审判一次选择不同目标而不对同一目标减六千()
    {
        var s = TestScene.New().MyActiveDon(1).OppCharacter("OP15-003").Build();
        var me = s.Players[0]; var opp = s.Players[1];
        var p = new MockPromptService().QueueChoose(opp.Leader.Id.ToString(), opp.Characters[0].Id.ToString());
        await EffectRuntime.Resolve(s, 0, Card("OP02-089"), EffectTrigger.EventCounter, p);
        Assert.Equal(opp.Leader.Info.Power - 3000, s.CurrentPowerOf(1, opp.Leader));
        Assert.Equal(opp.Characters[0].Info.Power - 3000, s.CurrentPowerOf(1, opp.Characters[0]));
        Assert.Single(p.ChooseHistory.Where(x => x.kind == "OpponentLeaderOrCharacter"));
    }

    [Fact]
    public async Task 布玲只保护发动时的角色且离场重登不继承旧保护()
    {
        var s = TestScene.New().MyCharacter("OP17-107").MyDeckTop("OP15-003").Build();
        var me = s.Players[0]; var initial = me.Characters[0];
        Assert.Equal(4000, s.OriginalPowerOf(0, initial));
        await EffectRuntime.Resolve(s, 0, Card("EB05-059"), EffectTrigger.EventMain, new MockPromptService());
        Assert.True(s.IsKoGuarded(initial, "battle"));
        var later = Card("OP17-107"); me.Characters.Add(later);
        await BattleEngine.KOCardAsync(s, 0, later, new MockPromptService());
        Assert.Contains(later, me.Trash);
        AtomicOps.BounceToHand(s, 0, initial);
        await AtomicOps.PlayFromHandFree(s, 0, initial);
        Assert.False(s.IsKoGuarded(initial, "battle"));
    }

    [Theory]
    [InlineData("OP13-004", 2)]
    [InlineData("OP17-079", 14)]
    public async Task 葛尔兹费用门槛按实际领袖动态判定(string leader, int expected)
    {
        var s = TestScene.New(leader).MyCharacter("OP17-081").Build(); var card = s.Players[0].Characters[0];
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(expected, s.CurrentCostOf(card));
    }

    [Fact]
    public async Task 邦妮对六千领袖只减一千且消费一次机会()
    {
        var s = TestScene.New(oppLeaderNumber: "ST10-002").MyCharacter("EB05-001").Build();
        var target = s.Players[1].Leader;
        var p = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(s, 0, s.Players[0].Characters[0], EffectTrigger.ActivatedMain, p);
        Assert.Equal(5000, s.CurrentPowerOf(1, target));
        await EffectRuntime.Resolve(s, 0, s.Players[0].Characters[0], EffectTrigger.ActivatedMain, p);
        Assert.Equal(5000, s.CurrentPowerOf(1, target));
        Assert.Single(p.ChooseHistory);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task 六费紫烬追加活跃咚与休息咚分别可选(bool active, bool rest)
    {
        var s = TestScene.New("OP01-091").MyCharacter("EB04-031").Build();
        var me = s.Players[0];
        me.DonDeck.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Rest }));
        var p = new MockPromptService().QueueConfirm(active).QueueConfirm(rest);
        await EffectRuntime.Resolve(s, 0, me.Characters[0], EffectTrigger.ActivatedMain, p);
        Assert.Equal(active ? 1 : 0, me.CostArea.Count(x => x.State == DonState.Active));
        Assert.Equal(rest ? 1 : 0, me.CostArea.Count(x => x.State == DonState.Rest));
        Assert.Equal(10 - (active ? 1 : 0) - (rest ? 1 : 0), me.DonDeck.Count);
        Assert.Equal(2, p.ConfirmHistory.Count);
    }

    [Fact]
    public async Task SMILE没有命中也能确认顶五并自选余牌顺序()
    {
        var s = TestScene.New().MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007", "OP15-008").Build();
        var me = s.Players[0]; var top = me.Deck.Take(5).ToArray(); var untouched = me.Deck[5];
        var p = new MockPromptService().QueueChooseEmpty().QueueChoose(top.Reverse().Select(x => x.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(s, 0, Card("OP01-116"), EffectTrigger.EventMain, p);
        Assert.Contains(p.ChooseHistory, x => x.kind == "LookTopReveal" && x.extra is not null);
        Assert.Contains(p.ChooseHistory, x => x.kind == "ReorderToDeckBottom");
        Assert.Equal(new[] { untouched }.Concat(top.Reverse()), me.Deck);
    }
    [Fact]
    public async Task 白胡子事件允许特征中包含白胡子海盗团的领袖()
    {
        var s = TestScene.New("OP13-002").MyHandAdd("OP02-004").Build();
        var me = s.Players[0]; var target = me.Hand[0];
        await EffectRuntime.Resolve(s, 0, Card("ST22-015"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, me.Characters);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task 克里克空场时不再使对方角色减两千(int count, bool reduced)
    {
        var s = TestScene.New("OP15-001").OppCharacter("OP15-003").Build();
        var me = s.Players[0]; var target = s.Players[1].Characters[0];
        for (int i = 0; i < count; i++) me.Characters.Add(Card("OP15-003"));
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        s.CurrentTurnPlayer = 1;
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        Assert.Equal(target.Info.Power - (reduced ? 2000 : 0), s.CurrentPowerOf(1, target));
    }

    [Fact]
    public async Task 罗宾先支付生命再登场卡尔加拉不能补触发过去的生命离场()
    {
        var s = TestScene.New("ST30-001").MyHandAdd("OP12-099")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-008").Build();
        var me = s.Players[0]; var target = me.Hand[0]; var life = Card("OP15-006"); me.LifeArea.Add(life);
        var p = new MockPromptService().QueueConfirm(true).QueueConfirm(true).QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(s, 0, Card("OP15-109"), EffectTrigger.OnEnterField, p);
        Assert.Contains(target, me.Characters);
        Assert.Equal(new[] { life }, me.Hand);
        Assert.Equal(3, me.Deck.Count);
        // 之后新发生的生命离场仍可触发，不能把角色本回合的能力整体禁掉。
        await EffectRuntime.Resolve(s, 0, Card("OP15-109"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true).QueueConfirm(true).QueueChooseEmpty());
        Assert.Equal(3, me.Hand.Count);
        Assert.Single(me.Deck);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 奈美两张手牌放同一端并按选择顺序排列(int placement)
    {
        var s = TestScene.New("OP06-001").MyHandAdd("OP15-006").MyHandAdd("OP15-007")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-008").Build();
        var me = s.Players[0]; var first = me.Hand[0]; var second = me.Hand[1]; var rest = me.Deck[3];
        var p = new MockPromptService().QueueChoose(first.Id.ToString()).QueueChoose(second.Id.ToString())
            .QueueOption(placement).QueueOption(1 - placement);
        await EffectRuntime.Resolve(s, 0, Card("OP11-054"), EffectTrigger.OnEnterField, p);
        Assert.Single(p.OptionHistory);
        Assert.Equal(placement == 0 ? new[] { first, second, rest } : new[] { rest, first, second }, me.Deck);
    }

    [Fact]
    public async Task 罗宾支付生命时已经在场的卡尔加拉仍然正常抽牌且只触发一次()
    {
        var s = TestScene.New("ST30-001").MyCharacter("OP12-099")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var me = s.Players[0]; var life = Card("OP15-006"); me.LifeArea.Add(life);
        await EffectRuntime.Resolve(s, 0, Card("OP15-109"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true).QueueConfirm(true).QueueChooseEmpty());
        Assert.Equal(2, me.Hand.Count);
        Assert.Single(me.Deck);
    }

    [Theory]
    [InlineData(EffectTrigger.OnEnterField, 0, true, false)]
    [InlineData(EffectTrigger.OnKO, 0, true, false)]
    [InlineData(EffectTrigger.OnEnterField, -2, true, true)]
    [InlineData(EffectTrigger.OnKO, -2, true, true)]
    [InlineData(EffectTrigger.OnEnterField, -2, false, false)]
    public async Task 龙马两个时机严格限制休息且当前费用不高于四(EffectTrigger trigger, int reduction, bool tapped, bool eligible)
    {
        var s = TestScene.New().OppCharacter("PRB02-013").Build();
        var target = s.Players[1].Characters[0]; target.CostModThisTurn = reduction; target.IsTapped = tapped;
        await EffectRuntime.Resolve(s, 0, Card("OP06-036"), trigger,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(eligible, s.Players[1].Trash.Contains(target));
        Assert.Equal(!eligible, s.Players[1].Characters.Contains(target));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-4, true)]
    public async Task 马幻刃费用上限同样受DSL当前费用过滤约束(int reduction, bool eligible)
    {
        var s = TestScene.New().MyDeckTop("OP15-003", "OP15-004").OppCharacter("PRB02-013").Build();
        var me = s.Players[0]; me.Trash.AddRange(Enumerable.Range(0, 10).Select(_ => Card("OP15-003")));
        var target = s.Players[1].Characters[0]; target.CostModThisTurn = reduction;
        await EffectRuntime.Resolve(s, 0, Card("OP16-101"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()).QueueChoose(target.Id.ToString()));
        Assert.Equal(eligible, s.Players[1].Trash.Contains(target));
    }
}
