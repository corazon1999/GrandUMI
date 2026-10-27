using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18October27EffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(int power = 3000, int cost = 3, string feature = "阿拉巴斯坦王国",
        CardKind kind = CardKind.Character) => new() { Info = new CardInfo
        { Number = "TEST-OCT27", Name = "测试卡", Color = "红", Kind = kind,
            Property = "打", Cost = cost, Power = power, Keywords = [feature] } };
    private static DonCard Don(PlayerState player, DonState state = DonState.Active, CardInstance? target = null)
    {
        var don = new DonCard { State = state, AttachedToCardId = target?.Id };
        player.CostArea.Add(don); return don;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 佩鲁条件速攻与登场收益分别结算且送墓不触发KO(int owner)
    {
        var state = TestScene.New("OP18-001", "OP18-001").Build();
        var me = state.Players[owner]; var opponent = state.Players[1 - owner];
        var source = Card("OP18-013"); me.Characters.Add(source);
        var cost = Custom(5000); var eventCard = Custom(kind: CardKind.Event); var high = Custom(6000);
        me.Hand.AddRange([cost, eventCard, high]); me.Deck.AddRange([Custom(), Custom(), Custom()]);
        var victim = Card("OP18-064"); victim.OriginalPowerOverride = 3000;
        victim.PowerModThisTurn = 9000; opponent.Characters.Add(victim);
        Don(opponent); var attached = Don(opponent, DonState.Attached, victim);
        var prompts = new MockPromptService().QueueChoose(cost.Id.ToString()).QueueChoose(victim.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Contains(cost, me.Trash); Assert.Equal(4, me.Hand.Count); Assert.Single(me.Deck);
        Assert.Contains(victim, opponent.Trash); Assert.DoesNotContain(victim, opponent.Characters);
        Assert.Equal(DonState.Rest, attached.State); Assert.Equal(2, opponent.CostArea.Count);
        Assert.DoesNotContain(eventCard.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.DoesNotContain(high.Id.ToString(), prompts.ChooseHistory[0].choices);
        source.IsEffectsNullified = true;
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        source.IsEffectsNullified = false;
        state.Players[owner] = new PlayerState { SessionId = "换领袖", AccountName = "换领袖",
            Leader = Custom(kind: CardKind.Leader, feature: "海军") };
        state.Players[owner].Characters.Add(source);
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    public async Task 佩鲁成本无效不弃牌不抽牌(string reason)
    {
        var state = TestScene.New().Build(); var me = state.Players[0];
        var source = Card("OP18-013"); me.Characters.Add(source);
        var cost = Custom(); me.Hand.Add(cost); me.Deck.AddRange([Custom(), Custom(), Custom()]);
        var prompts = reason == "cancel" ? new MockPromptService().QueueChooseEmpty()
            : reason == "duplicate" ? new MockPromptService().QueueChoose(cost.Id.ToString(), cost.Id.ToString())
            : new MockPromptService().QueueChoose(cost.Id.ToString());
        if (reason == "stale") prompts.OnChooseResponse = _ => me.Hand.Remove(cost);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.Trash); Assert.Equal(3, me.Deck.Count); Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 卡鲁事件只强化当前我方同特征角色且触发可强化领袖(int owner)
    {
        var state = TestScene.New("OP18-001", "OP18-001").Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var ally = Custom(); var wrong = Custom(feature: "海军");
        var enemy = Custom(); me.Characters.AddRange([ally, wrong]); state.Players[1 - owner].Characters.Add(enemy);
        var source = Card("OP18-018");
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.EventMain, new MockPromptService());
        Assert.Equal(1000, ally.PowerModThisTurn); Assert.Equal(0, wrong.PowerModThisTurn);
        Assert.Equal(0, enemy.PowerModThisTurn); Assert.Equal(0, me.Leader.PowerModThisTurn);
        var later = Custom(); me.Characters.Add(later); Assert.Equal(0, later.PowerModThisTurn);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()));
        Assert.Equal(2000, me.Leader.PowerModThisTurn); Assert.Equal(0, me.Leader.PowerModThisBattle);
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(0, ally.PowerModThisTurn); Assert.Equal(0, me.Leader.PowerModThisTurn);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    public async Task 乔巴赋予可选数量休息咚且每回合只重置一次并跳过下次准备(int owner, int count)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var source = Card("OP18-029");
        source.IsTapped = true; me.Characters.Add(source);
        for (int i = 0; i < 4; i++) Don(me, DonState.Rest);
        var active = Don(me); var attached = Don(me, DonState.Attached, source);
        var prompts = new MockPromptService().QueueOption(count);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Equal(count, me.AttachedDonCount(me.Leader.Id)); Assert.False(source.IsTapped);
        Assert.True(source.CannotActivateNextReset); Assert.Equal(DonState.Active, active.State);
        Assert.Equal(source.Id, attached.AttachedToCardId);
        source.IsTapped = true;
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.True(source.IsTapped); Assert.Single(prompts.ConfirmHistory);
        TurnEngine.EnterEndPhase(state); state.TurnCount += 2;
        TurnEngine.EnterResetPhase(state); Assert.True(source.IsTapped); Assert.False(source.CannotActivateNextReset);
        TurnEngine.EnterResetPhase(state); Assert.False(source.IsTapped);
    }

    [Fact]
    public async Task 乔巴没有休息咚也能重置且放弃发动不消耗次数()
    {
        var state = TestScene.New().Build(); var source = Card("OP18-029");
        source.IsTapped = true; state.Players[0].Characters.Add(source); Don(state.Players[0]);
        var skip = new MockPromptService().QueueConfirm(false);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, skip);
        Assert.True(source.IsTapped); Assert.Empty(state.Players[0].TurnOnceUsed);
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.False(source.IsTapped); Assert.Empty(prompts.OptionHistory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Mr4休息五咚可选择双方两档不同角色并归还附着咚(int owner)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = owner; var me = state.Players[owner];
        var source = Card("OP18-051"); me.Characters.Add(source);
        var first = Custom(cost: 7); first.CostModThisTurn = -1;
        var second = Custom(cost: 5); var tooHigh = Custom(cost: 7);
        state.Players[1 - owner].Characters.AddRange([first, tooHigh]); me.Characters.Add(second);
        for (int i = 0; i < 6; i++) Don(me);
        var attached = Don(me, DonState.Attached, source);
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString()).QueueChoose(second.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(5, me.CostArea.Count(d => d.State == DonState.Rest));
        Assert.Contains(first, state.Players[1 - owner].Deck); Assert.Contains(second, me.Deck);
        Assert.DoesNotContain(first.Id.ToString(), prompts.ChooseHistory[1].choices);
        Assert.DoesNotContain(tooHigh.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.Equal(5000, state.CurrentPowerOf(owner, source));
        attached.State = DonState.Rest; attached.AttachedToCardId = null;
        Assert.Equal(3000, state.CurrentPowerOf(owner, source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mr4取消或不足五张活跃咚不支付成本(bool insufficient)
    {
        var state = TestScene.New().Build(); var me = state.Players[0]; var source = Card("OP18-051");
        me.Characters.Add(source);
        for (int i = 0; i < (insufficient ? 4 : 5); i++) Don(me);
        Don(me, DonState.Rest); Don(me, DonState.Attached, source);
        var prompts = new MockPromptService().QueueConfirm(false);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Single(me.CostArea.Where(d => d.State == DonState.Rest)); Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task Mr4双目标同时离场可由一张佐罗支付一次保护整批()
    {
        var state = TestScene.New("OP18-001", "OP18-001").Build();
        var me = state.Players[0]; var opponent = state.Players[1];
        var source = Card("OP18-051"); me.Characters.Add(source);
        for (int i = 0; i < 5; i++) Don(me);
        var guardian = Card("OP18-017"); var victim = Custom(cost: 5);
        opponent.Characters.AddRange([guardian, victim]);
        var prompts = new MockPromptService().QueueChoose(guardian.Id.ToString()).QueueChoose(victim.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(guardian, opponent.Characters); Assert.Contains(victim, opponent.Characters);
        Assert.Empty(opponent.Deck); Assert.Equal(-2000, opponent.Leader.PowerModThisTurn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 麒麟原本力量两档封攻保持到下个对方结束阶段(int owner)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var source = Card("OP18-064"); me.Characters.Add(source);
        var first = Custom(6000); first.PowerModThisTurn = 10000;
        var second = Custom(8000); second.OriginalPowerOverride = 5000;
        var wrong = Custom(8000); wrong.PowerModThisTurn = -7000;
        state.Players[1 - owner].Characters.AddRange([first, second, wrong]);
        var don = Don(me, DonState.Attached, source);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString())
            .QueueChoose(first.Id.ToString()).QueueChoose(second.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(don, me.DonDeck); Assert.Null(don.AttachedToCardId);
        Assert.Single(first.Restrictions); Assert.Single(second.Restrictions); Assert.Empty(wrong.Restrictions);
        Assert.DoesNotContain(wrong.Id.ToString(), prompts.ChooseHistory[1].choices);
        Assert.DoesNotContain(first.Id.ToString(), prompts.ChooseHistory[2].choices);
        TurnEngine.EnterEndPhase(state); Assert.Single(first.Restrictions);
        state.CurrentTurnPlayer = 1 - owner; TurnEngine.EnterEndPhase(state);
        Assert.Empty(first.Restrictions); Assert.Empty(second.Restrictions);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task 麒麟真实KO后可复活且仅我方回合继续结算登场(int owner, bool ownTurn)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = ownTurn ? owner : 1 - owner;
        var me = state.Players[owner]; var source = Card("OP18-064"); me.Characters.Add(source);
        var d1 = Don(me); var d2 = Don(me); var victim = Custom(5000);
        state.Players[1 - owner].Characters.Add(victim);
        var prompts = new MockPromptService().QueueChoose(d1.Id.ToString()).QueueChoose(d2.Id.ToString())
            .QueueChoose(victim.Id.ToString()).QueueChooseEmpty();
        await AtomicOps.KOByEffectAsync(state, owner, source, prompts, actingSide: 1 - owner);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Contains(source, me.Characters); Assert.DoesNotContain(source, me.Trash);
        Assert.Equal(ownTurn ? 2 : 1, me.DonDeck.Count);
        Assert.Equal(ownTurn ? 1 : 0, victim.Restrictions.Count);
    }

    [Fact]
    public async Task 麒麟对方回合登场不返咚且取消复活保留废弃区()
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = 1;
        var me = state.Players[0]; var source = Card("OP18-064"); me.Characters.Add(source); Don(me);
        var prompts = new MockPromptService().QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(prompts.ChooseHistory); Assert.Single(me.CostArea);
        await AtomicOps.KOByEffectAsync(state, 0, source, prompts, actingSide: 1);
        Assert.Contains(source, me.Trash); Assert.Single(me.CostArea);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 汤姆复合成本后可回收刚弃掉的舞台以及已有舞台(int owner)
    {
        var state = TestScene.New().Build(); var me = state.Players[owner];
        var source = Card("OP18-070"); me.Characters.Add(source);
        var discarded = Custom(kind: CardKind.Stage); var oldStage = Custom(kind: CardKind.Stage);
        var wrong = Custom(); me.Hand.Add(discarded); me.Trash.AddRange([oldStage, wrong]);
        var don = Don(me, DonState.Attached, me.Leader);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString()).QueueChoose(discarded.Id.ToString())
            .QueueChoose(discarded.Id.ToString(), oldStage.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(don, me.DonDeck); Assert.Null(don.AttachedToCardId);
        Assert.Contains(discarded, me.Hand); Assert.Contains(oldStage, me.Hand); Assert.Single(me.Trash);
        Assert.DoesNotContain(wrong.Id.ToString(), prompts.ChooseHistory[2].choices);
        Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData("cancel-don")]
    [InlineData("cancel-hand")]
    [InlineData("stale-hand")]
    [InlineData("stale-don")]
    [InlineData("duplicate-hand")]
    public async Task 汤姆复合成本不完整时不返咚也不弃牌(string reason)
    {
        var state = TestScene.New().Build(); var me = state.Players[0]; var source = Card("OP18-070");
        me.Characters.Add(source); var hand = Custom(); me.Hand.Add(hand);
        var don = Don(me); var stage = Custom(kind: CardKind.Stage); me.Trash.Add(stage);
        var prompts = new MockPromptService();
        if (reason == "cancel-don") prompts.QueueChooseEmpty();
        else
        {
            prompts.QueueChoose(don.Id.ToString());
            if (reason == "cancel-hand") prompts.QueueChooseEmpty();
            else if (reason == "duplicate-hand") prompts.QueueChoose(hand.Id.ToString(), hand.Id.ToString());
            else prompts.QueueChoose(hand.Id.ToString());
        }
        prompts.OnChooseResponse = kind =>
        {
            if (kind != "OwnHandDiscard") return;
            if (reason == "stale-hand") me.Hand.Remove(hand);
            if (reason == "stale-don") me.CostArea.Remove(don);
        };
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.DonDeck); Assert.Single(me.Trash); Assert.Contains(stage, me.Trash);
        Assert.DoesNotContain(stage, me.Hand); Assert.False(EffectRuntime.PayingCost);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task 杰布拉KO两步可独立选择且弃生命不按伤害发动触发(int owner, bool heal)
    {
        var state = TestScene.New().Build(); var me = state.Players[owner]; var opponent = state.Players[1 - owner];
        var source = Card("OP18-103"); me.Trash.Add(source);
        var deckTop = Custom(); me.Deck.AddRange([deckTop, Custom()]);
        var lifeTop = Card("OP18-018"); lifeTop.IsLifeFaceUp = true; opponent.LifeArea.Add(lifeTop);
        var prompts = new MockPromptService().QueueConfirm(heal).QueueConfirm(true).QueueLifeTrigger(true);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnKO, prompts);
        Assert.Equal(heal ? 1 : 0, me.LifeArea.Count); Assert.Empty(opponent.LifeArea);
        if (heal) Assert.Same(deckTop, me.LifeArea[0]);
        Assert.Contains(lifeTop, opponent.Trash); Assert.False(lifeTop.IsLifeFaceUp);
        Assert.False(state.IsGameOver); Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 杰布拉无卡组仍可弃对方生命且对方无生命仍可补命()
    {
        var state = TestScene.New().Build(); var source = Card("OP18-103");
        state.Players[1].LifeArea.Add(Custom());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, new MockPromptService());
        Assert.Empty(state.Players[1].LifeArea);
        state.Players[0].Deck.Add(Custom());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, new MockPromptService());
        Assert.Single(state.Players[0].LifeArea); Assert.False(state.IsGameOver);
    }

    [Theory]
    [InlineData(0, "CP9")]
    [InlineData(1, "CP0")]
    [InlineData(0, "原CP9")]
    [InlineData(1, "海军")]
    public async Task 杰布拉生命触发仅限CP领袖并可回双方当前八费角色(int owner, string feature)
    {
        var state = TestScene.New().Build();
        state.Players[owner] = new PlayerState { SessionId = "CP测试", AccountName = "CP测试",
            Leader = Custom(feature: feature, kind: CardKind.Leader) };
        var me = state.Players[owner];
        me.Deck.AddRange([Custom(), Custom()]); var victim = Custom(cost: 9); victim.CostModThisTurn = -1;
        me.Characters.Add(victim); var source = Card("OP18-103");
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(victim.Id.ToString()));
        bool cp = feature.Contains("CP");
        Assert.Equal(cp ? 1 : 0, me.Hand.Count);
        Assert.Equal(!cp, me.Characters.Contains(victim));
        if (cp) Assert.Same(victim, me.Deck[^1]);
    }

    [Theory]
    [InlineData("OP18-013")]
    [InlineData("OP18-051")]
    [InlineData("OP18-064")]
    [InlineData("OP18-070")]
    public async Task 黑胡子封锁新卡登场成本但持续能力照常生效(string number)
    {
        var state = TestScene.New("OP18-001", "OP09-081").Build(); var me = state.Players[0];
        var opponent = state.Players[1]; opponent.Hand.Add(Custom());
        await EffectRuntime.Resolve(state, 1, opponent.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 1, opponent.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        var source = Card(number); me.Hand.AddRange([source, Custom()]);
        for (int i = 0; i < 5; i++) Don(me);
        var attached = Don(me, DonState.Attached, source);
        await AtomicOps.PlayFromHandFree(state, 0, source);
        var prompts = new MockPromptService();
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Empty(prompts.ChooseHistory); Assert.Empty(prompts.ConfirmHistory);
        Assert.Single(me.Hand); Assert.Empty(me.Trash); Assert.Empty(me.DonDeck);
        Assert.Equal(5, me.CostArea.Count(don => don.State == DonState.Active));
        if (number == "OP18-013") Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        if (number == "OP18-051") Assert.Equal(5000, state.CurrentPowerOf(0, source));
    }

    [Theory]
    [InlineData("OP18-051")]
    [InlineData("OP18-064")]
    public async Task 两档目标允许各选零张且第一档过期时不能继续处理旧目标(string number)
    {
        var state = TestScene.New().Build(); var me = state.Players[0]; var opponent = state.Players[1];
        var source = Card(number); me.Characters.Add(source);
        var first = Custom(5000, 5); var second = Custom(3000, 3); opponent.Characters.AddRange([first, second]);
        var prompts = new MockPromptService();
        if (number == "OP18-064") prompts.QueueChoose(Don(me).Id.ToString());
        else for (int i = 0; i < 5; i++) Don(me);
        prompts.QueueChoose(first.Id.ToString()).QueueChoose(second.Id.ToString());
        int count = 0;
        prompts.OnChooseResponse = kind =>
        {
            if (kind is not ("AnyCharacter" or "OppCharacter") || ++count != 2) return;
            first.CostModThisTurn = 10; first.OriginalPowerOverride = 10000;
        };
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(first, opponent.Characters); Assert.Empty(first.Restrictions);
        if (number == "OP18-051") Assert.Contains(second, opponent.Deck);
        else Assert.Single(second.Restrictions);
    }

    [Fact]
    public async Task Mr4同方双目标由持有者决定卡组底顺序()
    {
        var state = TestScene.New().Build(); var me = state.Players[0]; var opponent = state.Players[1];
        var source = Card("OP18-051"); me.Characters.Add(source);
        for (int i = 0; i < 5; i++) Don(me);
        var a = Custom(cost: 6); var b = Custom(cost: 5); opponent.Characters.AddRange([a, b]);
        var prompts = new MockPromptService().QueueChoose(a.Id.ToString()).QueueChoose(b.Id.ToString())
            .QueueChoose(b.Id.ToString(), a.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(new[] { b, a }, opponent.Deck);
        Assert.Equal("OrderOwnCharacters", prompts.ChooseHistory[2].kind);
    }

    [Fact]
    public async Task 佩鲁非KO送墓也尊重佐罗离场保护()
    {
        var state = TestScene.New("OP18-001", "OP18-001").Build();
        var me = state.Players[0]; var opponent = state.Players[1];
        var source = Card("OP18-013"); me.Characters.Add(source);
        var hand = Custom(); me.Hand.Add(hand); me.Deck.AddRange([Custom(), Custom(), Custom()]);
        var guardian = Card("OP18-017"); var victim = Custom(); opponent.Characters.AddRange([guardian, victim]);
        var prompts = new MockPromptService().QueueChoose(hand.Id.ToString()).QueueChoose(victim.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(victim, opponent.Characters); Assert.Empty(opponent.Trash);
        Assert.Equal(-2000, opponent.Leader.PowerModThisTurn);
    }

    [Theory]
    [InlineData("OP18-013")]
    [InlineData("OP18-103")]
    public async Task 抽牌导致牌组耗尽结束后不再弹出移区收益(string number)
    {
        var state = TestScene.New("OP18-079").Build(); var me = state.Players[0];
        var source = Card(number); var victim = Custom(); state.Players[1].Characters.Add(victim);
        var prompts = new MockPromptService();
        if (number == "OP18-013")
        {
            var cost = Custom(); me.Hand.Add(cost); me.Characters.Add(source);
            prompts.QueueChoose(cost.Id.ToString());
        }
        DeckOutRules.Arm(state);
        await EffectRuntime.Resolve(state, 0, source,
            number == "OP18-013" ? EffectTrigger.OnEnterField : EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.True(state.IsGameOver); Assert.Contains(victim, state.Players[1].Characters);
        Assert.Equal(number == "OP18-013" ? 1 : 0, prompts.ChooseHistory.Count);
    }
}
