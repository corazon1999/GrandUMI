using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Scripted;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18October20EffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(int power = 6000, string keyword = "草帽一伙", CardKind kind = CardKind.Character)
        => new() { Info = new CardInfo { Number = "TEST-OCT20", Name = "测试卡", Color = "红", Kind = kind,
            Property = "打", Cost = 3, Power = power, Keywords = [keyword] } };
    private static CardInstance Stage(GameState state, string number, bool extra = false)
    {
        var card = Card(number);
        if (extra) state.Players[0].ExtraStageCard = card;
        else state.Players[0].StageCard = card;
        return card;
    }
    private static DonCard Don(PlayerState player, DonState state, CardInstance? target = null)
    {
        var don = new DonCard { State = state, AttachedToCardId = target?.Id };
        if (state == DonState.InDeck) player.DonDeck.Add(don);
        else player.CostArea.Add(don);
        return don;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 睫毛以非KO成本离场并对领袖或角色减攻且回合末清理(bool leaderTarget)
    {
        var state = TestScene.New("OP18-001").Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var source = Card("OP18-015");
        me.Characters.Add(source);
        var attached = Don(me, DonState.Attached, source);
        var target = leaderTarget ? state.Players[1].Leader : Custom();
        if (!leaderTarget) state.Players[1].Characters.Add(target);
        var before = state.CurrentPowerOf(1, target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare, prompts);
        Assert.Contains(source, me.Trash);
        Assert.DoesNotContain(source, me.Characters);
        Assert.Equal(DonState.Rest, attached.State);
        Assert.Null(attached.AttachedToCardId);
        Assert.Equal(before - 3000, state.CurrentPowerOf(1, target));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(before, state.CurrentPowerOf(1, target));
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("wrongturn")]
    [InlineData("missing")]
    public async Task 睫毛未发动或不在场时不支付成本(string reason)
    {
        var state = TestScene.New("OP18-001").Build();
        state.CurrentTurnPlayer = reason == "wrongturn" ? 0 : 1;
        var source = Card("OP18-015");
        if (reason != "missing") state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService().QueueConfirm(false);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare, prompts);
        Assert.Empty(state.Players[0].Trash);
        Assert.Equal(0, state.Players[1].Leader.PowerModThisTurn);
    }

    [Theory]
    [InlineData("feature")]
    [InlineData("skip")]
    [InlineData("stale")]
    public async Task 睫毛成本已支付但不满足后续条件或不选目标时不减攻(string reason)
    {
        var state = TestScene.New(reason == "feature" ? "OP18-022" : "OP18-001").Build();
        state.CurrentTurnPlayer = 1;
        var source = Card("OP18-015");
        var target = Custom();
        state.Players[0].Characters.Add(source);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        if (reason == "skip") prompts = new MockPromptService().QueueChooseEmpty();
        if (reason == "stale") prompts.OnChooseResponse = _ => state.Players[1].Characters.Remove(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare, prompts);
        Assert.Contains(source, state.Players[0].Trash);
        Assert.Equal(0, target.PowerModThisTurn);
        if (reason == "feature") Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 睫毛作为成本废弃不会触发卡鲁的KO抽牌()
    {
        var state = TestScene.New("OP18-001").Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var source = Card("OP18-015");
        me.Characters.Add(source);
        me.Deck.AddRange([Custom(), Custom()]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare, new MockPromptService().QueueChooseEmpty());
        Assert.Empty(me.Hand);
        Assert.Equal(2, me.Deck.Count);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task 路飞回合结束时非KO废弃后抽牌重置领袖()
    {
        var state = TestScene.New("OP18-001").Build();
        var me = state.Players[0];
        me.Leader.IsTapped = true;
        var source = Card("OP18-036");
        var draw = Custom();
        me.Characters.Add(source);
        me.Deck.AddRange([draw, Custom()]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, new MockPromptService());
        Assert.Contains(source, me.Trash);
        Assert.Same(draw, Assert.Single(me.Hand));
        Assert.False(me.Leader.IsTapped);
        Assert.Single(me.Deck);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("wrongturn")]
    [InlineData("missing")]
    public async Task 路飞取消或不满足回合与区域条件时不抽牌也不重置(string reason)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP18-036");
        if (reason != "missing") me.Characters.Add(source);
        if (reason == "wrongturn") state.CurrentTurnPlayer = 1;
        me.Leader.IsTapped = true;
        me.Deck.Add(Custom());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, new MockPromptService().QueueConfirm(false));
        Assert.Empty(me.Hand);
        Assert.Empty(me.Trash);
        Assert.True(me.Leader.IsTapped);
    }

    [Theory]
    [InlineData("十字公会", true)]
    [InlineData("巴洛克工作室", true)]
    [InlineData("原巴洛克工作室", true)]
    [InlineData("草帽一伙", false)]
    public async Task Mr1仅赋予符合特征的我方领袖且不作用角色(string feature, bool granted)
    {
        var state = TestScene.New().Build();
        var me = new PlayerState { SessionId = "测试会话", AccountName = "测试玩家", Leader = Custom(keyword: feature, kind: CardKind.Leader) };
        state.Players[0] = me;
        var character = Custom(keyword: "十字公会");
        me.Characters.Add(character);
        await EffectRuntime.Resolve(state, 0, Card("OP18-047"), EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(granted, ActionValidator.HasKeyword(state, me.Leader, "不可阻挡"));
        Assert.Empty(character.GainedKeywords);
        TurnEngine.EnterEndPhase(state);
        Assert.False(ActionValidator.HasKeyword(state, me.Leader, "不可阻挡"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mr1允许零目标且拒绝已更换的领袖(bool stale)
    {
        var state = TestScene.New("OP18-041").Build();
        var old = state.Players[0].Leader;
        var prompts = new MockPromptService();
        if (stale) prompts.OnChooseResponse = _ => state.Players[0] = new PlayerState {
            SessionId = "替换会话", AccountName = "替换玩家", Leader = Custom(kind: CardKind.Leader) };
        else prompts.QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, Card("OP18-047"), EffectTrigger.OnEnterField, prompts);
        Assert.Empty(old.GainedKeywords);
        Assert.Empty(state.Players[0].Leader.GainedKeywords);
    }

    [Theory]
    [InlineData("OP18-075", true, true)]
    [InlineData("OP18-075", false, true)]
    [InlineData("OP18-075", true, false)]
    [InlineData("OP18-077", true, true)]
    [InlineData("OP18-077", false, true)]
    [InlineData("OP18-077", true, false)]
    public async Task 两张舞台登场先强制抽牌再可选追加一张休息咚(string number, bool addDon, bool deckHasDon)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Stage(state, number);
        var draw = Custom();
        me.Deck.AddRange([draw, Custom()]);
        if (deckHasDon) Don(me, DonState.InDeck);
        var prompts = new MockPromptService().QueueConfirm(addDon);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Same(draw, Assert.Single(me.Hand));
        Assert.Equal(addDon && deckHasDon ? 1 : 0, me.CostArea.Count);
        if (addDon && deckHasDon) Assert.Equal(DonState.Rest, Assert.Single(me.CostArea).State);
    }

    [Theory]
    [InlineData(DonState.Active, false)]
    [InlineData(DonState.Rest, false)]
    [InlineData(DonState.Attached, true)]
    public async Task 阳光号可返还任意状态的两咚并按付款后当前力量筛选角色(DonState donState, bool extraStage)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Stage(state, "OP18-075", extraStage);
        var target = Custom(power: 7000);
        target.IsTapped = true;
        me.Characters.Add(target);
        var first = Don(me, donState, donState == DonState.Attached ? target : null);
        var second = Don(me, donState, donState == DonState.Attached ? target : null);
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString()).QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.True(source.IsTapped);
        Assert.Empty(me.CostArea);
        Assert.Equal(2, me.DonDeck.Count);
        Assert.All(me.DonDeck, don => { Assert.Equal(DonState.InDeck, don.State); Assert.Null(don.AttachedToCardId); });
        Assert.False(target.IsTapped);
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("partial")]
    [InlineData("duplicate")]
    [InlineData("excess")]
    [InlineData("donleave")]
    [InlineData("donstate")]
    [InlineData("stageleave")]
    [InlineData("stagerest")]
    public async Task 阳光号无效选择不产生部分成本支付(string reason)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Stage(state, "OP18-075");
        var first = Don(me, DonState.Active);
        var second = Don(me, DonState.Rest);
        var third = Don(me, DonState.Active);
        var prompts = new MockPromptService();
        if (reason == "cancel") prompts.QueueChooseEmpty();
        else if (reason == "partial") prompts.QueueChoose(first.Id.ToString());
        else if (reason == "duplicate") prompts.QueueChoose(first.Id.ToString(), first.Id.ToString());
        else if (reason == "excess") prompts.QueueChoose(first.Id.ToString(), second.Id.ToString(), third.Id.ToString());
        else prompts.QueueChoose(first.Id.ToString(), second.Id.ToString());
        prompts.OnChooseResponse = _ => {
            if (reason == "donleave") me.CostArea.Remove(second);
            if (reason == "donstate") second.State = DonState.InDeck;
            if (reason == "stageleave") me.StageCard = null;
            if (reason == "stagerest") source.IsTapped = true;
        };
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Empty(me.DonDeck);
        Assert.Equal(DonState.Active, first.State);
        Assert.Equal(reason == "stagerest", source.IsTapped);
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rested")]
    [InlineData("onedon")]
    [InlineData("wrongturn")]
    public async Task 阳光号发动入口要求舞台可休息且能完整返还两咚(string reason)
    {
        var state = TestScene.New().Build();
        var source = Stage(state, "OP18-075");
        Don(state.Players[0], DonState.Active);
        if (reason != "onedon") Don(state.Players[0], DonState.Active);
        if (reason == "missing") state.Players[0].StageCard = null;
        if (reason == "rested") source.IsTapped = true;
        if (reason == "wrongturn") state.CurrentTurnPlayer = 1;
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Empty(state.Players[0].DonDeck);
        if (reason != "wrongturn") Assert.NotNull(new OP18_075_ThousandSunny().GetActivatedMainUnavailableReason(state, 0, source));
    }

    [Theory]
    [InlineData("highpower")]
    [InlineData("feature")]
    [InlineData("stale")]
    [InlineData("powerchanged")]
    [InlineData("skip")]
    public async Task 阳光号不会重置不合格或失效角色且已支付成本不退回(string reason)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Stage(state, "OP18-075");
        var target = Custom(power: reason == "highpower" ? 7001 : 7000, keyword: reason == "feature" ? "海军" : "草帽一伙");
        target.IsTapped = true;
        me.Characters.Add(target);
        var first = Don(me, DonState.Active);
        var second = Don(me, DonState.Rest);
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString());
        if (reason == "skip") prompts.QueueChooseEmpty();
        else prompts.QueueChoose(target.Id.ToString());
        prompts.OnChooseResponse = kind => {
            if (kind != "OwnCharacter") return;
            if (reason == "stale") me.Characters.Remove(target);
            if (reason == "powerchanged") target.PowerModThisTurn = 1;
        };
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.True(target.IsTapped);
        Assert.True(source.IsTapped);
        Assert.Equal(2, me.DonDeck.Count);
    }

    [Fact]
    public async Task 白木马赋予角色速攻只能攻击角色且回合结束清理()
    {
        var state = TestScene.New().Build();
        state.TurnCount = 3;
        var me = state.Players[0];
        var source = Stage(state, "OP18-077", true);
        var target = Custom();
        target.TurnPlayed = state.TurnCount;
        me.Characters.Add(target);
        var opponent = Custom();
        opponent.IsTapped = true;
        state.Players[1].Characters.Add(opponent);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(source.IsTapped);
        Assert.True(ActionValidator.HasKeyword(state, target, "速攻：角色"));
        Assert.False(ActionValidator.HasKeyword(state, target, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, target.Id, false, opponent.Id).Ok);
        Assert.False(ActionValidator.CanAttack(state, 0, target.Id, true, null).Ok);
        TurnEngine.EnterEndPhase(state);
        Assert.False(ActionValidator.HasKeyword(state, target, "速攻：角色"));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("rested")]
    [InlineData("missing")]
    [InlineData("wrongturn")]
    public async Task 白木马取消或发动条件不满足时不休息舞台(string reason)
    {
        var state = TestScene.New().Build();
        var source = Stage(state, "OP18-077");
        if (reason == "rested") source.IsTapped = true;
        if (reason == "missing") state.Players[0].StageCard = null;
        if (reason == "wrongturn") state.CurrentTurnPlayer = 1;
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService().QueueConfirm(false));
        Assert.Equal(reason == "rested", source.IsTapped);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("feature")]
    [InlineData("stale")]
    public async Task 白木马不对错误或过期目标赋予能力但保留舞台成本(string reason)
    {
        var state = TestScene.New().Build();
        var source = Stage(state, "OP18-077");
        var target = Custom(keyword: reason == "feature" ? "海军" : "草帽一伙");
        state.Players[0].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        if (reason == "skip") prompts = new MockPromptService().QueueChooseEmpty();
        if (reason == "stale") prompts.OnChooseResponse = _ => state.Players[0].Characters.Remove(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.True(source.IsTapped);
        Assert.Empty(target.GainedKeywords);
    }
}
