using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class October9CardEffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(string number, int cost = 3, int power = 5000,
        CardKind kind = CardKind.Character, string color = "黑", string trigger = "", params string[] keywords) => new()
    {
        Info = new CardInfo { Number = number, Name = number, Cost = cost, Power = power,
            Kind = kind, Color = color, Property = "打", Trigger = trigger, Keywords = keywords },
    };
    private static GameState State()
    {
        var state = TestScene.New("OP18-060", "OP18-022").Build();
        foreach (var player in state.Players)
            player.Deck.AddRange(Enumerable.Range(0, 12).Select(index => Custom($"DECK-{index}")));
        return state;
    }
    private static CardInstance Source(GameState state, string number)
    {
        var card = Card(number);
        state.Players[0].Characters.Add(card);
        return card;
    }
    private static void Leader(GameState state, params string[] keywords)
    {
        var old = state.Players[0];
        var player = new PlayerState { SessionId = old.SessionId, AccountName = old.AccountName,
            Leader = Custom("LEADER", kind: CardKind.Leader, keywords: keywords) };
        player.Deck.AddRange(old.Deck);
        state.Players[0] = player;
    }

    [Theory]
    [InlineData("OP18-017")]
    [InlineData("OP18-055")]
    [InlineData("OP18-069")]
    [InlineData("OP18-089")]
    [InlineData("OP18-091")]
    [InlineData("EB05-008")]
    [InlineData("EB05-019")]
    [InlineData("EB05-033")]
    [InlineData("EB05-035")]
    [InlineData("EB05-040")]
    [InlineData("EB05-049")]
    [InlineData("EB05-059")]
    public void 十二张受影响卡均有真实效果及对应触发登记(string number)
    {
        _ = State();
        Assert.NotNull(CardRulesetManager.Current.TryGetScriptedEffect(number));
        Assert.NotEmpty(CardDatabase.Get(number)!.EffectTags);
    }

    [Theory]
    [InlineData("EB05-003")]
    [InlineData("EB05-015")]
    [InlineData("EB05-026")]
    [InlineData("EB05-032")]
    [InlineData("EB05-041")]
    [InlineData("EB05-058")]
    public async Task 六张无效果角色通过基础规则可登场且没有空脚本(string number)
    {
        var state = State();
        var card = Card(number);
        Assert.Null(CardRulesetManager.Current.TryGetScriptedEffect(number));
        Assert.Empty(card.Info.EffectTags);
        Assert.True(card.Info.Power > 0);
        state.Players[0].Hand.Add(card);
        await AtomicOps.PlayFromHandFree(state, 0, card);
        Assert.Contains(card, state.Players[0].Characters);
        Assert.DoesNotContain(card, state.Players[0].Hand);
    }

    [Fact]
    public async Task 道伯曼的基础双重攻击在真实战斗结算产生两点伤害()
    {
        var state = State();
        var card = Source(state, "OP18-106");
        state.CurrentBattle = new BattleContext { AttackerPlayerIndex = 0, DefenderPlayerIndex = 1,
            AttackerCardId = card.Id, TargetIsLeader = true };
        Assert.Null(CardRulesetManager.Current.TryGetScriptedEffect(card.Info.Number));
        Assert.True(ActionValidator.HasKeyword(state, card, "双重攻击"));
        Assert.Equal(2, await BattleEngine.ResolveDamageAsync(state, new MockPromptService()));
    }

    [Fact]
    public async Task 佐罗阻止对方效果KO自身并减领袖两千且本回合不能再次保护()
    {
        var state = State();
        var zoro = Source(state, "OP18-017");
        Assert.False(await AtomicOps.KOByEffectAsync(state, 0, zoro, new MockPromptService(), 1));
        Assert.Contains(zoro, state.Players[0].Characters);
        Assert.Equal(-2000, state.Players[0].Leader.PowerModThisTurn);
        Assert.True(await AtomicOps.KOByEffectAsync(state, 0, zoro, new MockPromptService(), 1));
        Assert.Contains(zoro, state.Players[0].Trash);
    }

    [Theory]
    [InlineData("battle")]
    [InlineData("own")]
    [InlineData("wrongTrait")]
    [InlineData("cancel")]
    public async Task 佐罗不会保护战斗己方效果或非阿拉巴斯坦且可以取消(string mode)
    {
        var state = State();
        var zoro = Source(state, "OP18-017");
        var victim = mode == "wrongTrait" ? Custom("OTHER") : zoro;
        if (victim != zoro) state.Players[0].Characters.Add(victim);
        var prompts = new MockPromptService().QueueConfirm(false);
        bool ko = mode == "battle"
            ? await BattleEngine.KOCardAsync(state, 0, victim, prompts)
            : await AtomicOps.KOByEffectAsync(state, 0, victim, prompts, mode == "own" ? 0 : 1);
        Assert.True(ko);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisTurn);
        Assert.Empty(state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task 佐罗同时保护整批合格角色只减攻一次()
    {
        var state = State();
        Source(state, "OP18-017");
        var first = Custom("FIRST", keywords: ["阿拉巴斯坦王国"]);
        var second = Custom("SECOND", keywords: ["阿拉巴斯坦王国"]);
        state.Players[0].Characters.AddRange([first, second]);
        Assert.Equal(0, await AtomicOps.KOCardsByEffectAsync(state, 0, [first, second], new MockPromptService(), 1));
        Assert.Contains(first, state.Players[0].Characters);
        Assert.Contains(second, state.Players[0].Characters);
        Assert.Equal(-2000, state.Players[0].Leader.PowerModThisTurn);
    }

    [Fact]
    public async Task 佐罗能保护对方退回手牌并给我方角色速攻至回合结束()
    {
        var state = State();
        var zoro = Source(state, "OP18-017");
        var target = Custom("TARGET", cost: 2, keywords: ["阿拉巴斯坦王国"]);
        state.Players[0].Characters.Add(target);
        var enemy = Card("OP18-055");
        state.Players[1].Characters.Add(enemy);
        await EffectRuntime.Resolve(state, 1, enemy, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, state.Players[0].Characters);
        Assert.DoesNotContain(target, state.Players[0].Hand);
        await EffectRuntime.Resolve(state, 0, zoro, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(ActionValidator.HasKeyword(state, target, "速攻"));
        Assert.False(ActionValidator.HasKeyword(state, state.Players[0].Leader, "速攻"));
        TurnEngine.EnterEndPhase(state);
        Assert.False(ActionValidator.HasKeyword(state, target, "速攻"));
        Assert.Equal(0, state.Players[0].Leader.PowerModThisTurn);
    }

    [Fact]
    public async Task 星期三按当前费用退回角色且咚加攻会随附着无效及离场变化()
    {
        var state = State();
        var source = Source(state, "OP18-055");
        var target = Custom("TARGET", cost: 3);
        target.CostModThisTurn = -1;
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, state.Players[1].Hand);
        Assert.Equal(4000, state.CurrentPowerOf(0, source));
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        state.Players[0].CostArea.Add(don);
        Assert.Equal(6000, state.CurrentPowerOf(0, source));
        source.IsEffectsNullified = true;
        Assert.Equal(5000, state.CurrentPowerOf(0, source));
        source.IsEffectsNullified = false;
        don.State = DonState.Rest;
        don.AttachedToCardId = null;
        Assert.Equal(4000, state.CurrentPowerOf(0, source));
    }

    [Theory]
    [InlineData(DonState.Active)]
    [InlineData(DonState.Rest)]
    [InlineData(DonState.Attached)]
    public async Task 索德姆可以放回任意状态咚并休息自身保护战斗KO(DonState donState)
    {
        var state = State();
        var source = Source(state, "OP18-069");
        var target = Custom("FRANKY", keywords: ["弗兰奇一家"]);
        state.Players[0].Characters.Add(target);
        var don = new DonCard { State = donState, AttachedToCardId = donState == DonState.Attached ? source.Id : null };
        state.Players[0].CostArea.Add(don);
        Assert.False(await BattleEngine.KOCardAsync(state, 0, target,
            new MockPromptService().QueueChoose(don.Id.ToString())));
        Assert.True(source.IsTapped);
        Assert.Contains(don, state.Players[0].DonDeck);
        Assert.Null(don.AttachedToCardId);
        Assert.Contains(target, state.Players[0].Characters);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("duplicate")]
    [InlineData("sourceRested")]
    [InlineData("donRemoved")]
    public async Task 索德姆复合成本取消重复或过期回答都不部分支付(string mode)
    {
        var state = State();
        var source = Source(state, "OP18-069");
        var don = new DonCard { State = DonState.Active };
        var me = state.Players[0];
        me.CostArea.Add(don);
        var prompts = new MockPromptService();
        if (mode == "cancel") prompts.QueueChooseEmpty();
        else if (mode == "duplicate") prompts.QueueChoose(don.Id.ToString(), don.Id.ToString());
        else prompts.QueueChoose(don.Id.ToString());
        prompts.OnChooseResponse = _ => { if (mode == "sourceRested") source.IsTapped = true;
            if (mode == "donRemoved") me.CostArea.Remove(don); };
        Assert.True(await AtomicOps.KOByEffectAsync(state, 0, source, prompts, 1));
        Assert.DoesNotContain(don, me.DonDeck);
        Assert.Equal(mode == "sourceRested", source.IsTapped);
    }

    [Theory]
    [InlineData("OP18-089", EffectTrigger.OnEnterField)]
    [InlineData("OP18-091", EffectTrigger.OnAttackDeclare)]
    public async Task 两张巨人费用只在场上加十二且被无效时恢复(string number, EffectTrigger trigger)
    {
        var state = State();
        var source = Source(state, number);
        var prompts = new MockPromptService().QueueChooseEmpty();
        // 真实登场流程也会为没有【登场时】文本的角色登记场上持续效果。
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        if (trigger == EffectTrigger.OnEnterField) prompts.QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, source, trigger, prompts);
        Assert.Equal(16, state.CurrentCostOf(0, source));
        source.IsEffectsNullified = true;
        Assert.Equal(4, state.CurrentCostOf(0, source));
        source.IsEffectsNullified = false;
        AtomicOps.BounceToHand(state, 0, source);
        Assert.Equal(4, state.CurrentCostOf(0, source));
    }

    [Fact]
    public async Task 索德姆放回咚导致第二项休息成本不可支付时恢复咚且不保护KO()
    {
        var state = State();
        var source = Source(state, "OP18-069");
        var me = state.Players[0];
        var victim = Custom("FRANKY", keywords: ["弗兰奇一家"]);
        me.Characters.Add(victim);
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        state.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = me.Leader.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, GrantRestriction = RestrictionKind.CannotBeRested,
            Predicate = (current, _, card) => card == source && current.Players[0].CostArea.Count == 0 });
        Assert.True(await AtomicOps.KOByEffectAsync(state, 0, victim,
            new MockPromptService().QueueChoose(don.Id.ToString()), 1));
        Assert.Contains(don, me.CostArea);
        Assert.DoesNotContain(don, me.DonDeck);
        Assert.Equal(DonState.Active, don.State);
        Assert.False(source.IsTapped);
        Assert.Contains(victim, me.Trash);
    }

    [Fact]
    public async Task 多利弃牌成本后由对方自行选择必须丢弃的手牌()
    {
        var state = State();
        var source = Source(state, "OP18-089");
        var mine = Custom("MINE");
        var theirs = Custom("THEIRS");
        state.Players[0].Hand.Add(mine);
        state.Players[1].Hand.Add(theirs);
        var recorder = new RecordingPrompts(new MockPromptService().QueueChoose(mine.Id.ToString()).QueueChoose(theirs.Id.ToString()));
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, recorder);
        Assert.Equal(new[] { 0, 1 }, recorder.Owners);
        Assert.Contains(mine, state.Players[0].Trash);
        Assert.Contains(theirs, state.Players[1].Trash);
    }

    [Fact]
    public async Task 布洛基可以回收刚丢弃的低费角色但不能回收低费事件()
    {
        var state = State();
        var source = Source(state, "OP18-091");
        var card = Custom("LOW", cost: 2);
        var eventCard = Custom("EVENT", cost: 0, kind: CardKind.Event);
        state.Players[0].Hand.Add(card);
        state.Players[0].Trash.Add(eventCard);
        var prompts = new MockPromptService().QueueChoose(card.Id.ToString()).QueueChoose(card.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Contains(card, state.Players[0].Hand);
        Assert.DoesNotContain(eventCard.Id.ToString(), prompts.ChooseHistory[1].choices);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("cancel")]
    [InlineData("duplicate")]
    [InlineData("donRemoved")]
    [InlineData("handRemoved")]
    public async Task 路飞事件赋咚与公开两张红色事件必须完整支付且公开不会弃牌(string mode)
    {
        var state = State();
        var me = state.Players[0];
        var red = Custom("RED", kind: CardKind.Event, color: "红");
        var dual = Custom("DUAL", kind: CardKind.Event, color: "红/紫");
        me.Hand.AddRange([red, dual]);
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var target = Custom("RESTED");
        target.IsTapped = true;
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString());
        if (mode == "cancel") prompts.QueueChooseEmpty();
        else if (mode == "duplicate") prompts.QueueChoose(red.Id.ToString(), red.Id.ToString());
        else prompts.QueueChoose(red.Id.ToString(), dual.Id.ToString());
        prompts.QueueChoose(target.Id.ToString());
        prompts.OnChooseResponse = kind => { if (kind != "RevealOwnHand") return;
            if (mode == "donRemoved") me.CostArea.Remove(don);
            if (mode == "handRemoved") me.Hand.Remove(red); };
        await EffectRuntime.Resolve(state, 0, Card("EB05-008"), EffectTrigger.EventMain, prompts);
        Assert.Equal(mode == "valid" ? DonState.Attached : DonState.Active, don.State);
        Assert.Equal(mode == "valid" ? -7000 : 0, target.PowerModThisTurn);
        Assert.Contains(dual, me.Hand);
        Assert.Empty(me.Trash);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task 路飞反击独立于主要成本且按当前角色费用判断(int cost, bool applies)
    {
        var state = State();
        var big = Custom("BIG", cost: 6);
        big.CostModThisTurn = cost - 6;
        state.Players[0].Characters.Add(big);
        await EffectRuntime.Resolve(state, 0, Card("EB05-008"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(state.Players[0].Leader.Id.ToString()));
        Assert.Equal(applies ? 4000 : 0, state.Players[0].Leader.PowerModThisBattle);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisTurn);
        BattleEngine.EndBattle(state);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Theory]
    [InlineData("don")]
    [InlineData("leader")]
    [InlineData("stage")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    public async Task 开国反击允许休息咚领袖舞台且拒绝重复和离场成本(string mode)
    {
        var state = State();
        Leader(state, "和之国");
        var me = state.Players[0];
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var stage = Custom("STAGE", kind: CardKind.Stage);
        me.StageCard = stage;
        string id = mode == "leader" ? me.Leader.Id.ToString() : mode == "stage" ? stage.Id.ToString() : don.Id.ToString();
        var prompts = new MockPromptService().QueueChoose(mode == "duplicate" ? [id, id] : [id]).QueueChoose(me.Leader.Id.ToString());
        if (mode == "stale") prompts.OnChooseResponse = kind => { if (kind == "RestOwnCardsOrDon") me.CostArea.Remove(don); };
        await EffectRuntime.Resolve(state, 0, Card("EB05-019"), EffectTrigger.EventCounter, prompts);
        bool paid = mode is "don" or "leader" or "stage";
        Assert.Equal(paid ? 4000 : 0, me.Leader.PowerModThisBattle);
        Assert.Equal(mode == "don" ? DonState.Rest : DonState.Active, don.State);
        Assert.Equal(mode == "leader", me.Leader.IsTapped);
        Assert.Equal(mode == "stage", stage.IsTapped);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task 开国触发检查对方生命和我方手牌力量特征(int lives, bool applies)
    {
        var state = State();
        var eligible = Custom("WANO", power: 6000, keywords: ["和之国"]);
        var over = Custom("OVER", power: 7000, keywords: ["和之国"]);
        state.Players[0].Hand.AddRange([eligible, over]);
        state.Players[1].LifeArea.AddRange(Enumerable.Range(0, lives).Select(i => Custom($"LIFE-{i}")));
        var prompts = new MockPromptService().QueueChoose(eligible.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("EB05-019"), EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.Equal(applies, state.Players[0].Characters.Contains(eligible));
        Assert.Contains(over, state.Players[0].Hand);
        if (applies) Assert.DoesNotContain(over.Id.ToString(), prompts.ChooseHistory[0].choices);
    }

    [Fact]
    public async Task 斯皮德公开两张任意类型SMILE按自选顺序放底并能登场刚检索的角色()
    {
        var state = State();
        var source = Source(state, "EB05-035");
        var me = state.Players[0];
        me.Deck.Clear();
        var smile = Custom("SMILE-CHAR", power: 5000, keywords: ["SMILE"]);
        var smileEvent = Custom("SMILE-EVENT", kind: CardKind.Event, keywords: ["SMILE"]);
        var other = Enumerable.Range(0, 3).Select(i => Custom($"OTHER-{i}")).ToArray();
        var tail = Custom("TAIL");
        me.Deck.AddRange([smile, smileEvent, ..other, tail]);
        var handSmile = Custom("HAND-SMILE", power: 4000, keywords: ["SMILE"]);
        me.Hand.Add(handSmile);
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString())
            .QueueChoose(smile.Id.ToString(), smileEvent.Id.ToString())
            .QueueChoose(other[2].Id.ToString(), other[0].Id.ToString(), other[1].Id.ToString())
            .QueueChoose(smile.Id.ToString(), handSmile.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(smile, me.Characters);
        Assert.Contains(handSmile, me.Characters);
        Assert.Contains(smileEvent, me.Hand);
        Assert.Equal(new[] { tail, other[2], other[0], other[1] }, me.Deck);
        Assert.Contains(don, me.DonDeck);
    }

    [Fact]
    public async Task 斯皮德可以取消咚成本且检索选零仍会放底并能登场原有手牌()
    {
        var state = State();
        var source = Source(state, "EB05-035");
        var me = state.Players[0];
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var before = me.Deck.ToArray();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService().QueueChooseEmpty());
        Assert.Equal(before, me.Deck);
        Assert.Contains(don, me.CostArea);
        var hand = Custom("SMILE", power: 5000, keywords: ["SMILE"]);
        me.Hand.Add(hand);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService()
            .QueueChoose(don.Id.ToString()).QueueChooseEmpty().QueueChooseEmpty().QueueChoose(hand.Id.ToString()));
        Assert.Contains(hand, me.Characters);
        Assert.Equal(before.Skip(5).Concat(before.Take(5)), me.Deck);
    }

    [Theory]
    [InlineData(DonState.Active, true)]
    [InlineData(DonState.Rest, false)]
    [InlineData(DonState.Attached, false)]
    public async Task 你需要我主要只接受活跃咚并抽三弃一(DonState donState, bool applies)
    {
        var state = State();
        Leader(state, "堂吉诃德海盗团");
        var me = state.Players[0];
        var don = new DonCard { State = donState, AttachedToCardId = donState == DonState.Attached ? me.Leader.Id : null };
        me.CostArea.Add(don);
        var discarded = me.Deck[0];
        await EffectRuntime.Resolve(state, 0, Card("EB05-040"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(don.Id.ToString()).QueueChoose(discarded.Id.ToString()));
        Assert.Equal(applies ? 2 : 0, me.Hand.Count);
        Assert.Equal(applies, me.Trash.Contains(discarded));
        Assert.Equal(applies, me.DonDeck.Contains(don));
    }

    [Fact]
    public async Task 零费你需要我通过真实反击出牌入口入废弃并只给领袖加攻()
    {
        _ = State();
        string deck = "OP18-022\n" + string.Join('\n', Enumerable.Repeat("OP18-003", 20));
        var engine = new GameEngine("october9-counter", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 10);
        var state = engine.State;
        state.Phase = Phase.BattleCounter;
        state.CurrentTurnPlayer = 1;
        state.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id, TargetIsLeader = true };
        var me = state.Players[0];
        me.Hand.Clear(); me.Hand.Add(Card("EB05-040")); me.CostArea.Clear();
        Assert.True(engine.HandleAction(0, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        Assert.Contains(me.Trash, card => card.Info.Number == "EB05-040");
        Assert.Empty(me.CostArea);
        Assert.Equal(2000, me.Leader.PowerModThisBattle);
        BattleEngine.EndBattle(state);
        Assert.Equal(0, me.Leader.PowerModThisBattle);
    }

    [Theory]
    [InlineData(EffectTrigger.EventMain)]
    [InlineData(EffectTrigger.EventCounter)]
    public async Task 特大幽灵实际KO成本返还附着咚并结算正确的主要或反击效果(EffectTrigger trigger)
    {
        var state = State();
        var victim = Custom("THRILLER", keywords: ["恐怖之船海盗团"]);
        state.Players[0].Characters.Add(victim);
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = victim.Id };
        state.Players[0].CostArea.Add(don);
        var enemy = Custom("ENEMY", cost: 6);
        state.Players[1].Characters.Add(enemy);
        var target = trigger == EffectTrigger.EventMain ? enemy : state.Players[0].Leader;
        await EffectRuntime.Resolve(state, 0, Card("EB05-049"), trigger,
            new MockPromptService().QueueChoose(victim.Id.ToString()).QueueChoose(target.Id.ToString()));
        Assert.Contains(victim, state.Players[0].Trash);
        Assert.Equal(DonState.Rest, don.State);
        Assert.Null(don.AttachedToCardId);
        Assert.Equal(trigger == EffectTrigger.EventMain ? -3 : 0, enemy.CostModThisTurn);
        Assert.Equal(trigger == EffectTrigger.EventCounter ? 4000 : 0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task 特大幽灵成本若被KO保护置换则不产生减费或反击加攻()
    {
        var state = State();
        var victim = Custom("THRILLER", keywords: ["恐怖之船海盗团"]);
        state.Players[0].Characters.Add(victim);
        state.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = victim.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, KoGuard = "any", Predicate = (_, _, card) => card == victim });
        var prompts = new MockPromptService().QueueChoose(victim.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("EB05-049"), EffectTrigger.EventCounter, prompts);
        Assert.Contains(victim, state.Players[0].Characters);
        Assert.Single(prompts.ChooseHistory);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task 布玲动态保护后续登场的原本四千大妈角色且持续到下个对方结束阶段()
    {
        var state = State();
        var source = Card("EB05-059");
        var me = state.Players[0];
        me.Trash.Add(source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, new MockPromptService());
        Assert.Single(me.Hand);
        var eligible = Custom("BIGMOM", power: 4000, keywords: ["大妈海盗团"]);
        eligible.PowerModThisTurn = 3000;
        var wrongTrait = Custom("OTHER", power: 4000);
        var wrongOriginal = Custom("OTHERPOWER", power: 5000, keywords: ["大妈海盗团"]);
        wrongOriginal.PowerModThisTurn = -1000;
        me.Characters.AddRange([eligible, wrongTrait, wrongOriginal]);
        source.IsEffectsNullified = true;
        Assert.True(state.IsKoGuarded(eligible, "battle"));
        Assert.False(state.IsKoGuarded(eligible, "effect"));
        Assert.False(state.IsKoGuarded(wrongTrait, "battle"));
        Assert.False(state.IsKoGuarded(wrongOriginal, "battle"));
        Assert.False(await BattleEngine.KOCardAsync(state, 0, eligible, new MockPromptService()));
        TurnEngine.EnterEndPhase(state);
        Assert.True(state.IsKoGuarded(eligible, "battle"));
        state.CurrentTurnPlayer = 1;
        Assert.False(await BattleEngine.KOCardAsync(state, 0, eligible, new MockPromptService()));
        TurnEngine.EnterEndPhase(state);
        Assert.False(state.IsKoGuarded(eligible, "battle"));
        Assert.True(await BattleEngine.KOCardAsync(state, 0, eligible, new MockPromptService()));
    }

    [Fact]
    public async Task 布玲触发能登场刚抽到的非大妈四千力量触发角色并排除无触发角色()
    {
        var state = State();
        var me = state.Players[0];
        var eligible = Custom("TRIGGER", power: 4000, trigger: "【触发】抽取1张卡牌。");
        var vanilla = Custom("VANILLA", power: 4000);
        me.Deck.Insert(0, eligible);
        me.Hand.Add(vanilla);
        var prompts = new MockPromptService().QueueChoose(eligible.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("EB05-059"), EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.Contains(eligible, me.Characters);
        Assert.Contains(vanilla, me.Hand);
        Assert.DoesNotContain(vanilla.Id.ToString(), prompts.ChooseHistory[0].choices);
    }

    private sealed class RecordingPrompts(MockPromptService inner) : IPromptService
    {
        public List<int> Owners { get; } = [];
        public Task<List<string>> ChooseCards(int playerIdx, string kind, string text, IReadOnlyList<string> validChoices,
            int min, int max, Dictionary<string, object?>? extra = null)
        { Owners.Add(playerIdx); return inner.ChooseCards(playerIdx, kind, text, validChoices, min, max, extra); }
        public Task<bool> ConfirmOptional(int playerIdx, string text) => inner.ConfirmOptional(playerIdx, text);
        public Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options,
            Dictionary<string, object?>? extra = null) => inner.ChooseOption(playerIdx, text, options, extra);
        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
            => inner.AskLifeTrigger(playerIdx, lifeCard, hasRealTrigger);
    }
}
