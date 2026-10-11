using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class October2026CardEffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(string number, int cost = 3, int power = 5000,
        CardKind kind = CardKind.Character, params string[] keywords) => new()
    {
        Info = new CardInfo { Number = number, Name = number, Color = "黑", Kind = kind,
            Property = "知", Cost = cost, Power = power, Keywords = keywords },
    };
    private static void FillDeck(PlayerState player)
        => player.Deck.AddRange(Enumerable.Range(0, 10).Select(index => Custom($"DECK-{index}")));
    private static GameState State()
    {
        var state = TestScene.New("OP18-060", "OP18-022").Build();
        FillDeck(state.Players[0]);
        FillDeck(state.Players[1]);
        return state;
    }
    private static CardInstance Source(GameState state, string number)
    {
        var card = Card(number);
        state.Players[0].Characters.Add(card);
        return card;
    }
    private static void SetLeader(GameState state, string name, params string[] keywords)
    {
        var previous = state.Players[0];
        var player = new PlayerState
        {
            SessionId = previous.SessionId, AccountName = previous.AccountName,
            Leader = new CardInstance
            {
                Info = new CardInfo { Number = "TEST-LEADER", Name = name, Color = "蓝", Property = "知",
                    Kind = CardKind.Leader, Cost = 4, Power = 5000, Keywords = keywords },
            },
        };
        player.Deck.AddRange(previous.Deck);
        player.Hand.AddRange(previous.Hand);
        player.Characters.AddRange(previous.Characters);
        state.Players[0] = player;
    }

    [Theory]
    [InlineData("OP18-011")]
    [InlineData("OP18-024")]
    [InlineData("OP18-034")]
    [InlineData("OP18-046")]
    [InlineData("OP18-048")]
    [InlineData("OP18-061")]
    [InlineData("OP18-084")]
    [InlineData("OP18-100")]
    [InlineData("OP18-113")]
    [InlineData("EB05-030")]
    public void 新卡有真实脚本及相应结构化触发登记(string number)
    {
        _ = State();
        Assert.NotNull(CardRulesetManager.Current.TryGetScriptedEffect(number));
        Assert.NotEmpty(CardDatabase.Get(number)!.EffectTags);
    }

    [Fact]
    public async Task 薇薇光环仅加双特征我方角色且阻挡与六千力量随回合无效离场变化()
    {
        var state = State();
        var vivi = Source(state, "OP18-011");
        var animal = Custom("ANIMAL", keywords: ["阿拉巴斯坦王国", "动物"]);
        var onlyOne = Custom("ONE", keywords: ["阿拉巴斯坦王国"]);
        var enemy = Custom("ENEMY", keywords: ["阿拉巴斯坦王国", "动物"]);
        state.Players[0].Characters.AddRange([animal, onlyOne]);
        state.Players[1].Characters.Add(enemy);
        await EffectRuntime.Resolve(state, 0, vivi, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(0, state.CurrentPowerOf(0, vivi));
        Assert.Equal(6000, state.CurrentPowerOf(0, animal));
        Assert.Equal(5000, state.CurrentPowerOf(0, onlyOne));
        Assert.Equal(5000, state.CurrentPowerOf(1, enemy));
        Assert.False(ActionValidator.HasKeyword(state, vivi, "阻挡者"));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(6000, state.CurrentPowerOf(0, vivi));
        Assert.True(ActionValidator.HasKeyword(state, vivi, "阻挡者"));
        vivi.IsEffectsNullified = true;
        Assert.Equal(0, state.CurrentPowerOf(0, vivi));
        Assert.False(ActionValidator.HasKeyword(state, vivi, "阻挡者"));
        vivi.IsEffectsNullified = false;
        AtomicOps.TrashFieldCard(state, 0, vivi);
        Assert.Equal(5000, state.CurrentPowerOf(0, animal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 可可罗可取消或作为非KO成本废弃并返还附着咚且重置领袖(bool pay)
    {
        var state = State();
        var source = Source(state, "OP18-024");
        var me = state.Players[0];
        me.Leader.IsTapped = true;
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = source.Id });
        // 自身作为成本离场不受持续离场保护阻止。
        state.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = source.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, LeaveGuard = "any",
            Predicate = (_, _, card) => card == source });
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, new MockPromptService().QueueConfirm(pay));
        Assert.Equal(pay, me.Trash.Contains(source));
        Assert.Equal(!pay, me.Leader.IsTapped);
        Assert.Equal(pay ? DonState.Rest : DonState.Attached, me.CostArea[0].State);
        Assert.Empty(state.PendingKOEffects);
    }

    [Fact]
    public async Task 弗兰奇可登场舞台并依据登场后舞台及对方当前费用休息角色()
    {
        var state = State();
        var source = Source(state, "OP18-034");
        var stage = Custom("NEW-STAGE", cost: 5, kind: CardKind.Stage, keywords: ["弗兰奇一家"]);
        var eventCard = Custom("EVENT", kind: CardKind.Event, keywords: ["草帽一伙"]);
        state.Players[0].Hand.AddRange([stage, eventCard]);
        var target = Custom("TARGET", cost: 7);
        target.CostModThisTurn = -1;
        var over = Custom("OVER", cost: 7);
        state.Players[1].Characters.AddRange([target, over]);
        var prompts = new MockPromptService().QueueChoose(stage.Id.ToString()).QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Same(stage, state.Players[0].StageCard);
        Assert.True(target.IsTapped);
        Assert.False(over.IsTapped);
        Assert.Contains(eventCard, state.Players[0].Hand);
        Assert.DoesNotContain(eventCard.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.DoesNotContain(over.Id.ToString(), prompts.ChooseHistory[1].choices);
    }

    [Fact]
    public async Task 弗兰奇选择零张登场仍可由既有五费舞台发动后半效果()
    {
        var state = State();
        var source = Source(state, "OP18-034");
        state.Players[0].StageCard = Custom("STAGE", cost: 5, kind: CardKind.Stage);
        var hand = Custom("HAND", keywords: ["草帽一伙"]);
        state.Players[0].Hand.Add(hand);
        var target = Custom("TARGET", cost: 6);
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty().QueueChoose(target.Id.ToString()));
        Assert.Contains(hand, state.Players[0].Hand);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task 九费组合抽二并可给领袖和每个角色分别赋予零至二张休息咚()
    {
        var state = State();
        var source = Source(state, "OP18-046");
        var second = Custom("SECOND");
        var me = state.Players[0];
        me.Characters.Add(second);
        me.CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Rest }));
        me.CostArea.Add(new DonCard { State = DonState.Active });
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueOption(2).QueueOption(0).QueueOption(2));
        Assert.Equal(2, me.Hand.Count);
        Assert.Equal(2, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(0, me.AttachedDonCount(source.Id));
        Assert.Equal(2, me.AttachedDonCount(second.Id));
        Assert.Single(me.CostArea.Where(don => don.State == DonState.Rest));
        Assert.Single(me.CostArea.Where(don => don.State == DonState.Active));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 九费组合攻击时成本可拒绝且原本力量变七千保留其他加成并回合末清除(bool pay)
    {
        var state = State();
        state.CurrentTurnPlayer = 1;
        SetLeader(state, "克洛克达尔", "新巴洛克工作室");
        var source = Source(state, "OP18-046");
        var me = state.Players[0];
        var cost = Custom("DISCARD");
        me.Hand.Add(cost);
        me.Leader.PowerModThisTurn = 1000;
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueConfirm(pay).QueueChoose(cost.Id.ToString()));
        Assert.Equal(pay, me.Trash.Contains(cost));
        Assert.Equal(pay ? 7000 : 5000, state.OriginalPowerOf(0, me.Leader));
        Assert.Equal(pay ? 8000 : 6000, state.CurrentPowerOf(0, me.Leader));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5000, state.OriginalPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task 九费组合不从失效手牌响应支付成本且错误领袖无可用触发()
    {
        var state = State();
        var source = Source(state, "OP18-046");
        state.CurrentTurnPlayer = 1;
        SetLeader(state, "克洛克达尔", "巴洛克工作室");
        var cost = Custom("STALE");
        state.Players[0].Hand.Add(cost);
        var prompts = new MockPromptService().QueueChoose(cost.Id.ToString());
        prompts.OnChooseResponse = _ => state.Players[0].Hand.Remove(cost);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare, prompts);
        Assert.DoesNotContain(cost, state.Players[0].Trash);
        Assert.Null(state.Players[0].Leader.OriginalPowerOverride);
        SetLeader(state, "路飞", "草帽一伙");
        Assert.False(((ITriggeredEffectAvailability)new GrandUMI.Effects.Scripted.OP18_046_Mr0AllSunday())
            .IsTriggerAvailable(state, 0, source, EffectTrigger.OnOppAttackDeclare, null));
    }

    [Fact]
    public async Task 双指获得速攻并仅登场当前费用合格的特征包含巴洛克工作室角色()
    {
        var state = State();
        var source = Source(state, "OP18-048");
        var target = Custom("VALID", cost: 6, keywords: ["新巴洛克工作室"]);
        target.CostModThisTurn = -1;
        var stage = Custom("STAGE", kind: CardKind.Stage, keywords: ["巴洛克工作室"]);
        var wrong = Custom("WRONG", keywords: ["草帽一伙"]);
        state.Players[0].Hand.AddRange([target, stage, wrong]);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(target, state.Players[0].Characters);
        Assert.Contains(stage, state.Players[0].Hand);
        Assert.Contains(wrong, state.Players[0].Hand);
        Assert.Single(prompts.ChooseHistory[0].choices);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
    }

    [Fact]
    public async Task 阿斯巴古合计检索两张舞台或七水之城卡且其余按自选顺序沉底()
    {
        var state = State();
        var source = Source(state, "OP18-061");
        var me = state.Players[0];
        var stage = Custom("STAGE", kind: CardKind.Stage);
        var dual = Custom("DUAL", kind: CardKind.Stage, keywords: ["七水之城"]);
        var water = Custom("WATER", keywords: ["七水之城"]);
        var other = Custom("OTHER");
        var fifth = Custom("FIFTH");
        var untouched = me.Deck[0];
        me.Deck.InsertRange(0, [stage, dual, water, other, fifth]);
        var prompts = new MockPromptService().QueueChoose(stage.Id.ToString(), dual.Id.ToString())
            .QueueChoose(fifth.Id.ToString(), water.Id.ToString(), other.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(new[] { stage, dual }, me.Hand);
        Assert.Same(untouched, me.Deck[0]);
        Assert.Equal(new[] { fifth, water, other }, me.Deck.TakeLast(3));
        Assert.Equal(3, prompts.ChooseHistory[0].choices.Count);
        Assert.Equal(2, prompts.ChooseHistory[0].max);
        Assert.Equal(5, ((System.Collections.IEnumerable)prompts.ChooseHistory[0].extra!["choiceCards"]!).Cast<object>().Count());
    }

    [Fact]
    public async Task 阿斯巴古检索期间卡组顶变化时旧响应不会搬走新的牌()
    {
        var state = State();
        var source = Source(state, "OP18-061");
        var me = state.Players[0];
        var target = Custom("WATER", keywords: ["七水之城"]);
        me.Deck.Insert(0, target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        prompts.OnChooseResponse = kind => { if (kind == "ReorderToDeckBottom") me.Deck.Insert(0, Custom("NEW-TOP")); };
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.Hand);
        Assert.Contains(target, me.Deck);
        Assert.Equal("NEW-TOP", me.Deck[0].Info.Number);
    }

    [Fact]
    public async Task 军子宫检索零张也会将看过的四张放置废弃区()
    {
        var state = State();
        var source = Source(state, "OP18-084");
        var me = state.Players[0];
        var top = me.Deck.Take(4).ToList();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService().QueueChooseEmpty());
        Assert.Equal(top, me.Trash);
        Assert.Equal(6, me.Deck.Count);
        Assert.Empty(me.Hand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task 军子宫的四咚成本必须完整支付而且没有每回合一次限制(int selectedCount)
    {
        var state = State();
        var source = Source(state, "OP18-084");
        var me = state.Players[0];
        me.CostArea.AddRange(Enumerable.Range(0, 8).Select(_ => new DonCard { State = DonState.Active }));
        var target = Custom("KNIGHT", cost: 6, keywords: ["神之骑士团"]);
        me.Trash.Add(target);
        var prompts = new MockPromptService().QueueChoose(me.CostArea.Take(selectedCount).Select(don => don.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(selectedCount == 4 ? 4 : 0, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.Equal(selectedCount == 4, me.Characters.Contains(target));
        if (selectedCount == 4)
        {
            var second = Custom("KNIGHT-2", keywords: ["神之骑士团"]);
            me.Trash.Add(second);
            await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
                new MockPromptService().QueueChoose(me.CostArea.Where(don => don.State == DonState.Active)
                    .Select(don => don.Id.ToString()).ToArray()).QueueChoose(second.Id.ToString()));
            Assert.Contains(second, me.Characters);
            Assert.Equal(8, me.CostArea.Count(don => don.State == DonState.Rest));
            Assert.DoesNotContain(me.TurnOnceUsed, key => key.StartsWith("OP18-084", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task 军子宫不支付包含重复或失效咚的成本()
    {
        var state = State();
        var source = Source(state, "OP18-084");
        var me = state.Players[0];
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Active }));
        var ids = me.CostArea.Select(don => don.Id.ToString()).ToArray();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(ids[0], ids[0], ids[1], ids[2]));
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
        var stale = new MockPromptService().QueueChoose(ids);
        stale.OnChooseResponse = _ => me.CostArea.RemoveAt(3);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, stale);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
    }

    [Fact]
    public void MMA允许完整五十张同卡并开放标准排位但其他角色仍限制四张()
    {
        _ = State();
        string deck = "OP18-060\n" + string.Join('\n', Enumerable.Repeat("OP18-093", 50));
        Assert.True(DeckValidator.Validate(deck, DeckValidator.FormatUnrestricted).Ok);
        Assert.True(DeckValidator.Validate(deck, DeckValidator.FormatPublicUnrestricted).Ok);
        var ranked = DeckValidator.Validate(deck, DeckValidator.FormatStandardRanked);
        Assert.True(ranked.Ok, ranked.Reason);
        string ordinary = "OP18-060\n" + string.Join('\n', Enumerable.Repeat("OP18-084", 50));
        var result = DeckValidator.Validate(ordinary, DeckValidator.FormatUnrestricted);
        Assert.False(result.Ok);
        Assert.Contains("超过 4 张", result.Reason);
    }

    [Fact]
    public void MMA在真实阻挡窗口能阻挡且休息后不能再次阻挡()
    {
        var state = State();
        var mma = Source(state, "OP18-093");
        state.CurrentTurnPlayer = 1;
        state.Phase = Phase.BattleBlock;
        state.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id, TargetIsLeader = true };
        Assert.True(ActionValidator.CanDeclareBlocker(state, 0, mma.Id).Ok);
        BattleEngine.DeclareBlocker(state, mma.Id);
        Assert.True(mma.IsTapped);
        Assert.Equal(mma.Id, state.CurrentBattle.TargetCardId);
        Assert.False(ActionValidator.CanDeclareBlocker(state, 0, mma.Id).Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 佳妮法可将刚抽到的CP卡正面放在生命最上方或最下方(int position)
    {
        var state = State();
        var source = Source(state, "OP18-100");
        var me = state.Players[0];
        var cp = Custom("DRAWN-CP", keywords: ["CP0"]);
        var life = Custom("OLD-LIFE");
        me.Deck.Insert(0, cp);
        me.LifeArea.Add(life);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(cp.Id.ToString()).QueueOption(position));
        Assert.True(cp.IsLifeFaceUp);
        Assert.Same(cp, position == 0 ? me.LifeArea[0] : me.LifeArea[^1]);
        Assert.DoesNotContain(cp, me.Hand);
        Assert.Contains(life, me.LifeArea);
    }

    [Fact]
    public async Task 佳妮法可不加生命且位置选择期间已移走的手牌不会重复进入生命()
    {
        var state = State();
        var source = Source(state, "OP18-100");
        var me = state.Players[0];
        var cp = Custom("CP", keywords: ["CP9"]);
        me.Hand.Add(cp);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService().QueueChooseEmpty());
        Assert.Empty(me.LifeArea);
        var prompts = new MockPromptService().QueueChoose(cp.Id.ToString()).QueueOption(1);
        prompts.OnOptionResponse = () => me.Hand.Remove(cp);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.LifeArea);
    }

    [Theory]
    [InlineData("OP18-100")]
    [InlineData("OP18-113")]
    public async Task 非CP领袖不能发动两张新卡的条件生命效果(string number)
    {
        var state = State();
        SetLeader(state, "路飞", "草帽一伙");
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, Card(number), EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 佳妮法生命效果先抽一且可休息对方领袖并服从休息限制()
    {
        var state = State();
        SetLeader(state, "CP领袖", "CP9");
        var opp = state.Players[1];
        var immune = Custom("IMMUNE");
        immune.Restrictions.Add(new CardRestriction { Kind = RestrictionKind.CannotBeRested, Duration = KeywordDuration.ThisTurn });
        opp.Characters.Add(immune);
        var prompts = new MockPromptService().QueueChoose(opp.Leader.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP18-100"), EffectTrigger.OnLifeRevealTrigger, prompts);
        Assert.Single(state.Players[0].Hand);
        Assert.True(opp.Leader.IsTapped);
        Assert.False(immune.IsTapped);
        Assert.DoesNotContain(immune.Id.ToString(), prompts.ChooseHistory[0].choices);
    }

    [Theory]
    [InlineData(EffectTrigger.OnEnterField)]
    [InlineData(EffectTrigger.OnKO)]
    public async Task 鲁兹登场或KO只废弃对方顶部生命不发动生命触发且零生命不会造成败北(EffectTrigger trigger)
    {
        var state = State();
        var opp = state.Players[1];
        var life = Card("OP18-100");
        life.IsLifeFaceUp = true;
        var lower = Custom("LOWER");
        opp.LifeArea.AddRange([life, lower]);
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, Card("OP18-113"), trigger, prompts);
        Assert.Contains(life, opp.Trash);
        Assert.False(life.IsLifeFaceUp);
        Assert.Equal(new[] { lower }, opp.LifeArea);
        Assert.Empty(prompts.ChooseHistory);
        opp.LifeArea.Clear();
        await EffectRuntime.Resolve(state, 0, Card("OP18-113"), trigger, prompts);
        Assert.False(state.IsGameOver);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 鲁兹生命效果可将任一方当前费用不高于八的角色沉底并归还附着咚(int side)
    {
        var state = State();
        SetLeader(state, "CP领袖", "CP0");
        var target = Custom("TARGET", cost: 9);
        target.CostModThisTurn = -1;
        state.Players[side].Characters.Add(target);
        state.Players[side].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = target.Id });
        await EffectRuntime.Resolve(state, 0, Card("OP18-113"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Single(state.Players[0].Hand);
        Assert.DoesNotContain(target, state.Players[side].Characters);
        Assert.Same(target, state.Players[side].Deck.Last());
        Assert.Equal(DonState.Rest, state.Players[side].CostArea[0].State);
        Assert.Equal(9, target.CurrentCost());
    }

    [Fact]
    public async Task 鲁兹沉底对方角色会询问奈美离场置换并允许支付生命保留角色()
    {
        var state = State();
        SetLeader(state, "CP领袖", "CP9");
        var target = Custom("TARGET");
        var nami = Card("EB05-061");
        var life = Custom("LIFE");
        var opp = state.Players[1];
        opp.Characters.AddRange([target, nami]);
        opp.LifeArea.Add(life);
        await EffectRuntime.Resolve(state, 0, Card("OP18-113"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(target.Id.ToString()).QueueConfirm(true));
        Assert.Contains(target, opp.Characters);
        Assert.Contains(life, opp.Hand);
        Assert.Empty(opp.LifeArea);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 保重事件在奈美领袖下可退回任一方角色且不符合费用的卡不能选择(int side)
    {
        var state = State();
        SetLeader(state, "奈美", "东海");
        var target = Custom("TARGET", cost: 5);
        var over = Custom("OVER", cost: 6);
        state.Players[side].Characters.AddRange([target, over]);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("EB05-030"), EffectTrigger.EventMain, prompts);
        Assert.Contains(target, state.Players[side].Hand);
        Assert.Contains(over, state.Players[side].Characters);
        Assert.DoesNotContain(over.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.Equal(CardKind.Event, CardDatabase.Get("EB05-030")!.Kind);
        Assert.Equal("蓝", CardDatabase.Get("EB05-030")!.Color);
    }

    [Fact]
    public async Task 保重事件错误领袖主要效果无效但反击抽一与本回合加一千独立可用()
    {
        var state = State();
        SetLeader(state, "路飞", "草帽一伙");
        var source = Card("EB05-030");
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, prompts);
        Assert.Empty(prompts.ChooseHistory);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(state.Players[0].Leader.Id.ToString()));
        Assert.Single(state.Players[0].Hand);
        Assert.Equal(6000, state.CurrentPowerOf(0, state.Players[0].Leader));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5000, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task 保重反击事件通过真实出牌入口扣二费入废弃并显示合法目标()
    {
        _ = State();
        string deck = "OP18-022\n" + string.Join('\n', Enumerable.Repeat("OP18-003", 20));
        var engine = new GameEngine("october-counter", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 10);
        var state = engine.State;
        state.Phase = Phase.BattleCounter;
        state.CurrentTurnPlayer = 1;
        state.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id, TargetIsLeader = true };
        var me = state.Players[0];
        me.Hand.Clear();
        me.Hand.Add(Card("EB05-030"));
        me.CostArea.Clear();
        me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ => new DonCard { State = DonState.Active }));
        Assert.True(engine.HandleAction(0, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        for (int attempt = 0; attempt < 300 && state.PendingPrompt?.Kind != "OwnPowerTarget"; attempt++) await Task.Delay(10);
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("OwnPowerTarget", prompt.Kind);
        engine.Prompts.Resolve(prompt.PromptId, [me.Leader.Id.ToString()]);
        await engine.WaitSettledAsync();
        Assert.Equal(2, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.Contains(me.Trash, card => card.Info.Number == "EB05-030");
        Assert.Single(me.Hand);
        Assert.Equal(6000, state.CurrentPowerOf(0, me.Leader));
        Assert.Equal(Phase.BattleCounter, state.Phase);
    }

    [Theory]
    [InlineData("OP18-100", "OppRestTarget")]
    [InlineData("OP18-113", "AnyCharacter")]
    public async Task 两张CP新卡通过真实生命伤害入口进入废弃并完整发动触发(string number, string targetKind)
    {
        _ = State();
        string deck = "OP18-079\n" + string.Join('\n', Enumerable.Repeat("OP18-003", 20));
        var engine = new GameEngine("october-life-" + number, ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 12);
        var state = engine.State;
        var me = state.Players[0];
        var life = Card(number);
        me.LifeArea.Clear();
        me.LifeArea.Add(life);
        me.Hand.Clear();
        var target = Custom("TARGET", cost: 8);
        state.Players[1].Characters.Add(target);
        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        async Task<PendingPrompt> Wait(string kind)
        {
            for (int attempt = 0; attempt < 300; attempt++)
            {
                if (state.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
                await Task.Delay(10);
            }
            throw new TimeoutException("等待生命触发交互超时：" + kind);
        }
        var activate = await Wait("LifeTrigger");
        engine.Prompts.Resolve(activate.PromptId, ["trigger"]);
        var choose = await Wait(targetKind);
        engine.Prompts.Resolve(choose.PromptId, [target.Id.ToString()]);
        await damage;
        Assert.Contains(life, me.Trash);
        Assert.Single(me.Hand);
        Assert.Empty(me.LifeArea);
        if (number == "OP18-100") Assert.True(target.IsTapped);
        else Assert.Same(target, state.Players[1].Deck.Last());
        Assert.False(state.IsGameOver);
    }
}
