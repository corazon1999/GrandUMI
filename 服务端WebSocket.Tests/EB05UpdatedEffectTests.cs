using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class EB05UpdatedEffectTests
{
    private static readonly string[] NewCards =
    [
        "EB05-001", "EB05-002", "EB05-004", "EB05-005", "EB05-006", "EB05-007", "EB05-009",
        "EB05-011", "EB05-012", "EB05-013", "EB05-014", "EB05-017", "EB05-018", "EB05-020",
        "EB05-021", "EB05-022", "EB05-023", "EB05-024", "EB05-025", "EB05-027", "EB05-028",
        "EB05-029", "EB05-031", "EB05-034", "EB05-035", "EB05-036", "EB05-037", "EB05-038",
        "EB05-039", "EB05-042", "EB05-043", "EB05-044", "EB05-045", "EB05-046", "EB05-047",
        "EB05-048", "EB05-050", "EB05-051", "EB05-052", "EB05-053", "EB05-054", "EB05-055",
        "EB05-056", "EB05-057", "EB05-060", "EB05-061",
    ];

    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance Custom(
        string number,
        int power = 1000,
        int cost = 1,
        CardKind kind = CardKind.Character,
        string name = "测试卡",
        string color = "红",
        string property = "知",
        string[]? keywords = null,
        string trigger = "",
        string[]? effectTags = null)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = name,
                Color = color,
                Kind = kind,
                Property = property,
                Power = power,
                Cost = cost,
                Keywords = keywords ?? [],
                Trigger = trigger,
                EffectTags = effectTags ?? [],
            },
        };

    private static void FillDeck(PlayerState player, int count = 12)
        => player.Deck.AddRange(Enumerable.Range(0, count).Select(index =>
            Custom($"TEST-DECK-{index}", power: 3000, cost: 3)));

    private static void AddDon(PlayerState player, int active, int deck = 0)
    {
        player.CostArea.AddRange(Enumerable.Range(0, active)
            .Select(_ => new DonCard { State = DonState.Active }));
        player.DonDeck.AddRange(Enumerable.Range(0, deck)
            .Select(_ => new DonCard { State = DonState.InDeck }));
    }

    private static GameEngine NewEngine(string leader = "OP15-001")
    {
        _ = TestScene.New().Build();
        string deck = leader + "\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 12));
        return new GameEngine(
            $"eb05-{Guid.NewGuid():N}",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 17);
    }

    private static async Task<PendingPrompt> WaitForPrompt(GameEngine engine, string kind)
    {
        for (int i = 0; i < 300; i++)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
            await Task.Delay(10);
        }
        throw new TimeoutException($"等待 {kind} 交互超时");
    }

    private static void Answer(GameEngine engine, PendingPrompt prompt, params string[] choices)
        => engine.Prompts.Resolve(prompt.PromptId, choices);

    public static IEnumerable<object[]> AllNewCards()
        => NewCards.Select(number => new object[] { number });

    [Theory]
    [MemberData(nameof(AllNewCards))]
    public void 四十六张新卡均有脚本登记(string number)
    {
        _ = TestScene.New().Build();
        var script = CardRulesetManager.Current.TryGetScriptedEffect(number);
        Assert.NotNull(script);
        Assert.Equal(number, script!.CardNumber);
    }

    [Fact]
    public async Task EB05001登场抽牌铺场且启动主要按当前力量减攻并限每回合一次()
    {
        var state = TestScene.New("OP01-001").Build();
        var me = state.Players[0];
        var source = Card("EB05-001");
        me.Characters.Add(source);
        var low1 = Custom("LOW-1", power: 2000);
        var low2 = Custom("LOW-2", power: 1000);
        me.Deck.AddRange([low1, low2]);
        var prompts = new MockPromptService().QueueChoose(low1.Id.ToString(), low2.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(low1, me.Characters);
        Assert.Contains(low2, me.Characters);
        var opponentTarget = Custom("OPP-6000", power: 5000);
        opponentTarget.PowerModThisTurn = 1000;
        state.Players[1].Characters.Add(opponentTarget);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(opponentTarget.Id.ToString()));
        Assert.Equal(5000, state.CurrentPowerOf(1, opponentTarget));
        Assert.Contains($"EB05-001-main:{source.Id}", me.TurnOnceUsed);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Equal(5000, state.CurrentPowerOf(1, opponentTarget));
    }

    [Fact]
    public async Task EB05002按公开候选取两张海军并自选余牌顺序后弃牌()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var navy2 = Custom("NAVY-2", cost: 2, keywords: ["海军"]);
        var navy5 = Custom("NAVY-5", cost: 5, kind: CardKind.Event, keywords: ["海军"]);
        var navy1 = Custom("NAVY-1", cost: 1, keywords: ["海军"]);
        var other1 = Custom("OTHER-1");
        var other2 = Custom("OTHER-2");
        var oldHand = Custom("OLD-HAND");
        me.Deck.AddRange([navy2, other1, navy5, navy1, other2]);
        me.Hand.Add(oldHand);
        var prompts = new MockPromptService()
            .QueueChoose(navy5.Id.ToString(), navy2.Id.ToString())
            .QueueChoose(other2.Id.ToString(), navy1.Id.ToString(), other1.Id.ToString())
            .QueueChoose(oldHand.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("EB05-002"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(navy2, me.Hand);
        Assert.Contains(navy5, me.Hand);
        Assert.Contains(oldHand, me.Trash);
        Assert.True(new[] { other2.Id, navy1.Id, other1.Id }
            .SequenceEqual(me.Deck.Select(card => card.Id)));
        Assert.Equal(2, prompts.ChooseHistory[0].choices.Count);
        Assert.DoesNotContain(navy1.Id.ToString(), prompts.ChooseHistory[0].choices);
    }

    [Fact]
    public async Task EB05004公开两事件抽牌且KO时从手牌休息登场()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-004");
        me.Characters.Add(source);
        var e1 = Custom("EVENT-1", kind: CardKind.Event);
        var e2 = Custom("EVENT-2", kind: CardKind.Event);
        me.Hand.AddRange([e1, e2]);
        FillDeck(me, 3);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(e1.Id.ToString(), e2.Id.ToString()));
        Assert.Equal(4, me.Hand.Count);

        var played = Custom("PLAYED", power: 6000);
        me.Hand.Add(played);
        Assert.True(await BattleEngine.KOCardAsync(state, 0, source,
            new MockPromptService().QueueChoose(played.Id.ToString())));
        Assert.Contains(source, me.Trash);
        Assert.Contains(played, me.Characters);
        Assert.True(played.IsTapped);
    }

    [Fact]
    public async Task EB05005登场只加成革命军触发角色且启动主要送废弃后减攻()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-005");
        var eligible = Custom("REV-TRIGGER", power: 3000, keywords: ["革命军"], trigger: "【触发】此卡牌登场。");
        var noTrigger = Custom("REV", power: 3000, keywords: ["革命军"]);
        me.Characters.AddRange([source, eligible, noTrigger]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(eligible.Id.ToString()));
        Assert.Equal(5000, state.CurrentPowerOf(0, eligible));
        Assert.Equal(3000, state.CurrentPowerOf(0, noTrigger));

        var opponent = Custom("OPP", power: 5000);
        state.Players[1].Characters.Add(opponent);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(opponent.Id.ToString()));
        Assert.Contains(source, me.Trash);
        Assert.Equal(3000, state.CurrentPowerOf(1, opponent));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task EB05006生命入手限制决定能否支付且拒绝时不消费每回合一次(bool prohibited, bool succeeds)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-006");
        me.Characters.Add(source);
        var life = Custom("LIFE");
        me.LifeArea.Add(life);
        var target = Custom("TARGET", power: 5000);
        state.Players[1].Characters.Add(target);
        if (prohibited) state.NoEffectLifeToHandThisTurn.Add(0);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueOption(1).QueueChoose(target.Id.ToString()));
        Assert.Equal(succeeds, me.Hand.Contains(life));
        Assert.Equal(succeeds ? 1000 : 5000, state.CurrentPowerOf(1, target));
        Assert.Equal(succeeds, me.TurnOnceUsed.Contains($"EB05-006-main:{source.Id}"));
    }

    [Fact]
    public async Task EB05007公开三张合格手牌抽一且回合结束获得跨对方回合力量()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-007");
        me.Characters.Add(source);
        var eventCard = Custom("EVENT", kind: CardKind.Event);
        var punk1 = Custom("PUNK-1", keywords: ["班克禁区"]);
        var punk2 = Custom("PUNK-2", keywords: ["班克禁区"]);
        me.Hand.AddRange([eventCard, punk1, punk2]);
        FillDeck(me, 2);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(eventCard.Id.ToString(), punk1.Id.ToString(), punk2.Id.ToString()));
        Assert.Equal(4, me.Hand.Count);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, new MockPromptService());
        Assert.Equal(source.Info.Power + 5000, state.CurrentPowerOf(0, source));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(source.Info.Power + 5000, state.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task EB05009主要按原本力量群体加攻且反击只加领袖本次战斗()
    {
        var state = TestScene.New().Build();
        var low = Custom("LOW", power: 4000);
        var modifiedHigh = Custom("HIGH", power: 5000);
        modifiedHigh.PowerModThisTurn = -2000;
        state.Players[0].Characters.AddRange([low, modifiedHigh]);
        var source = Card("EB05-009");
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, new MockPromptService());
        Assert.Equal(5000, state.CurrentPowerOf(0, low));
        Assert.Equal(3000, state.CurrentPowerOf(0, modifiedHigh));
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, new MockPromptService());
        Assert.Equal(state.Players[0].Leader.Info.Power + 3000,
            state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public async Task EB05011与012按当前费用休息目标且011支付翻面生命成本()
    {
        var state = TestScene.New("OP11-022").Build();
        var me = state.Players[0];
        var faceUp = Custom("FACE-UP");
        faceUp.IsLifeFaceUp = true;
        me.LifeArea.Add(faceUp);
        var five = Custom("COST-6-AS-5", cost: 6);
        five.CostModThisTurn = -1;
        var six = Custom("COST-6", cost: 6);
        state.Players[1].Characters.AddRange([five, six]);
        await EffectRuntime.Resolve(state, 0, Card("EB05-011"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(five.Id.ToString()));
        Assert.False(faceUp.IsLifeFaceUp);
        Assert.True(five.IsTapped);
        await EffectRuntime.Resolve(state, 0, Card("EB05-012"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(six.Id.ToString()));
        Assert.True(six.IsTapped);
    }

    [Fact]
    public async Task EB05013和017在对方攻击窗口支付各自成本并仅强化鱼人领袖()
    {
        var state = TestScene.New("OP03-022").Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var shirley = Card("EB05-013");
        var dancer = Card("EB05-017");
        me.Characters.AddRange([shirley, dancer]);
        AddDon(me, 2);
        await EffectRuntime.Resolve(state, 0, shirley, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(me.CostArea[0].Id.ToString()));
        Assert.Equal(DonState.Rest, me.CostArea[0].State);
        Assert.Equal(me.Leader.Info.Power + 2000, state.CurrentPowerOf(0, me.Leader));
        await EffectRuntime.Resolve(state, 0, dancer, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(me.CostArea[1].Id.ToString()));
        Assert.Contains(dancer, me.Trash);
        Assert.Equal(me.Leader.Info.Power + 6000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task EB05014搜索后废弃余牌且休息自身赋海王类速攻()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-014");
        me.Characters.Add(source);
        var megalo = Custom("MEGALO", name: "梅迦罗");
        var seaKing = Custom("SEA-KING", keywords: ["海王类"]);
        var remainder = Custom("REMAINDER");
        me.Deck.AddRange([megalo, seaKing, remainder]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(megalo.Id.ToString(), seaKing.Id.ToString()));
        Assert.Contains(megalo, me.Hand);
        Assert.Contains(seaKing, me.Hand);
        Assert.Contains(remainder, me.Trash);
        me.Hand.Remove(seaKing);
        me.Characters.Add(seaKing);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(seaKing.Id.ToString()));
        Assert.True(source.IsTapped);
        Assert.True(ActionValidator.HasKeyword(state, seaKing, "速攻"));
    }

    [Fact]
    public async Task EB05018阻止目标下个重置阶段转活跃()
    {
        var state = TestScene.New().Build();
        var target = Custom("RESTED", cost: 6);
        target.IsTapped = true;
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, Card("EB05-018"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(target.CannotActivateNextReset);
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterResetPhase(state);
        Assert.True(target.IsTapped);
        Assert.False(target.CannotActivateNextReset);
    }

    [Fact]
    public async Task EB05020休息白星领袖作为事件成本后抽二()
    {
        var state = TestScene.New("OP11-022").Build();
        FillDeck(state.Players[0], 2);
        await EffectRuntime.Resolve(state, 0, Card("EB05-020"), EffectTrigger.EventMain,
            new MockPromptService());
        Assert.True(state.Players[0].Leader.IsTapped);
        Assert.Equal(2, state.Players[0].Hand.Count);
    }

    [Fact]
    public async Task EB05021先抽牌登场十字公会再建立全回合登场限制且攻击时无效目标()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-021");
        me.Characters.Add(source);
        var crossGuild = Custom("CROSS-GUILD", cost: 8, keywords: ["十字公会"]);
        var drawn = Custom("DRAWN");
        me.Deck.AddRange([crossGuild, drawn]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(crossGuild.Id.ToString()));
        Assert.Contains(crossGuild, me.Characters);
        Assert.Contains(0, state.NoPlayCharacterThisTurn);
        var blockedPlay = Custom("BLOCKED-PLAY");
        me.Hand.Add(blockedPlay);
        await AtomicOps.PlayFromHandFree(state, 0, blockedPlay);
        Assert.Contains(blockedPlay, me.Hand);
        var opponent = Custom("NULLIFY");
        state.Players[1].Characters.Add(opponent);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(opponent.Id.ToString()));
        Assert.True(opponent.IsEffectsNullified);
    }

    [Fact]
    public async Task EB05022通过真实KO入口发动而不会在登场时误抽()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-022");
        me.Characters.Add(source);
        FillDeck(me, 4);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Empty(me.Hand);
        Assert.True(await BattleEngine.KOCardAsync(state, 0, source, new MockPromptService()));
        Assert.Equal(2, me.Hand.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EB05023丢两事件抽三可以明确取消(bool accept)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var first = Custom("EVENT-A", kind: CardKind.Event);
        var second = Custom("EVENT-B", kind: CardKind.Event);
        me.Hand.AddRange([first, second]);
        FillDeck(me, 3);
        var prompts = new MockPromptService().QueueConfirm(accept);
        if (accept) prompts.QueueChoose(first.Id.ToString(), second.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("EB05-023"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(accept ? 3 : 2, me.Hand.Count);
        Assert.Equal(accept ? 2 : 0, me.Trash.Count);
    }

    [Fact]
    public async Task EB05024仅因佩尔地狱领袖下获得动态阻挡者和费用加二()
    {
        var state = TestScene.New("OP02-071").Build();
        var source = Card("EB05-024");
        state.Players[0].Characters.Add(source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(source.Info.Cost + 2, state.CurrentCostOf(0, source));
        Assert.True(ActionValidator.HasKeyword(state, source, "阻挡者"));
        source.IsEffectsNullified = true;
        Assert.Equal(source.Info.Cost, state.CurrentCostOf(0, source));
        Assert.False(ActionValidator.HasKeyword(state, source, "阻挡者"));
    }

    [Fact]
    public async Task EB05025生命触发真实伤害路径能识别省略的并复用登场时效果()
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Deck.Clear();
        var trigger = Card("EB05-025");
        var impel = Custom("IMPEL", keywords: ["因佩尔地狱"]);
        var other1 = Custom("OTHER-A");
        var other2 = Custom("OTHER-B");
        me.LifeArea.Add(trigger);
        me.Deck.AddRange([impel, other1, other2]);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        Answer(engine, lifePrompt, "trigger");
        var search = await WaitForPrompt(engine, "LookTop");
        Assert.Single(search.ValidChoices);
        Assert.Contains(impel.Id.ToString(), search.ValidChoices);
        Answer(engine, search, impel.Id.ToString());
        var reorder = await WaitForPrompt(engine, "ReorderToDeckBottom");
        Answer(engine, reorder, other2.Id.ToString(), other1.Id.ToString());
        await damage;

        Assert.Contains(trigger, me.Trash);
        Assert.Contains(impel, me.Hand);
        Assert.True(new[] { other2.Id, other1.Id }.SequenceEqual(me.Deck.Select(card => card.Id)));
    }

    [Fact]
    public async Task EB05027完成抽三弃二后可把任意一方当前二费角色放回持有者卡组底()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        FillDeck(me, 3);
        var old1 = Custom("OLD-1");
        var old2 = Custom("OLD-2");
        me.Hand.AddRange([old1, old2]);
        var target = Custom("TARGET", cost: 3);
        target.CostModThisTurn = -1;
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService()
            .QueueChoose(old1.Id.ToString(), old2.Id.ToString())
            .QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("EB05-027"), EffectTrigger.OnEnterField, prompts);
        Assert.Contains(old1, me.Trash);
        Assert.Contains(old2, me.Trash);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Same(target, state.Players[1].Deck.Last());
    }

    [Fact]
    public async Task EB05028仅在对方九手牌时由对方选择弃四()
    {
        var state = TestScene.New().Build();
        var opponent = state.Players[1];
        var cards = Enumerable.Range(0, 9).Select(index => Custom($"HAND-{index}")).ToList();
        opponent.Hand.AddRange(cards);
        await EffectRuntime.Resolve(state, 0, Card("EB05-028"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(cards.Take(4).Select(card => card.Id.ToString()).ToArray()));
        Assert.Equal(5, opponent.Hand.Count);
        Assert.Equal(cards.Take(4), opponent.Trash);
    }

    [Fact]
    public async Task EB05029主要支付弃牌后无效并弹回当前六费目标且触发抽二弃一()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var cost = Custom("COST");
        me.Hand.Add(cost);
        var target = Custom("TARGET", cost: 7);
        target.CostModThisTurn = -1;
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, Card("EB05-029"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(cost.Id.ToString()).QueueChoose(target.Id.ToString()));
        Assert.Contains(cost, me.Trash);
        Assert.Contains(target, state.Players[1].Hand);

        var guardSource = Custom("GUARD-SOURCE");
        var guarded = Custom("GUARDED", cost: 6);
        state.Players[1].Characters.AddRange([guardSource, guarded]);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = guardSource.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
            LeaveGuard = "effect",
            Predicate = (_, side, card) => side == 1 && card.Id == guarded.Id,
        });
        var secondCost = Custom("SECOND-COST");
        me.Hand.Add(secondCost);
        await EffectRuntime.Resolve(state, 0, Card("EB05-029"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(secondCost.Id.ToString()).QueueChoose(guarded.Id.ToString()));
        Assert.Contains(guarded, state.Players[1].Characters);
        Assert.True(guarded.IsEffectsNullified);

        var old = Custom("OLD");
        me.Hand.Add(old);
        FillDeck(me, 2);
        await EffectRuntime.Resolve(state, 0, Card("EB05-029"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(old.Id.ToString()));
        Assert.Contains(old, me.Trash);
        Assert.Equal(2, me.Hand.Count);
    }

    [Fact]
    public async Task EB05031温思默克登场咚减一磨十且启动主要送废弃后追加四活跃咚()
    {
        var state = TestScene.New("OP12-041").Build();
        var me = state.Players[0];
        var source = Card("EB05-031");
        me.Characters.Add(source);
        AddDon(me, active: 1, deck: 4);
        FillDeck(me, 12);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(me.CostArea[0].Id.ToString()));
        Assert.Empty(me.CostArea);
        Assert.Equal(10, me.Trash.Count);
        Assert.Equal(2, me.Deck.Count);

        state.Players[1].Characters.Add(Custom("BIG", power: 8000));
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueOption(4));
        Assert.Contains(source, me.Trash);
        Assert.Equal(4, me.CostArea.Count);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
    }

    [Fact]
    public async Task EB05034咚减二后仍有七咚才减攻()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        AddDon(me, 9);
        var target = Custom("TARGET", power: 6000);
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, Card("EB05-034"), EffectTrigger.OnEnterField,
            new MockPromptService()
                .QueueChoose(me.CostArea[0].Id.ToString(), me.CostArea[1].Id.ToString())
                .QueueChoose(target.Id.ToString()));
        Assert.Equal(7, me.CostArea.Count);
        Assert.Equal(2000, state.CurrentPowerOf(1, target));
    }

    [Fact]
    public async Task EB05035草帽领袖咚差六时抽三弃二并追加四休息咚()
    {
        var state = TestScene.New("EB02-010").Build();
        var me = state.Players[0];
        AddDon(state.Players[1], 6);
        AddDon(me, 0, deck: 4);
        var old1 = Custom("OLD-1");
        var old2 = Custom("OLD-2");
        me.Hand.AddRange([old1, old2]);
        FillDeck(me, 3);
        await EffectRuntime.Resolve(state, 0, Card("EB05-035"), EffectTrigger.OnEnterField,
            new MockPromptService()
                .QueueChoose(old1.Id.ToString(), old2.Id.ToString())
                .QueueOption(4));
        Assert.Equal(3, me.Hand.Count);
        Assert.Equal(4, me.CostArea.Count);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Rest, don.State));
    }

    [Fact]
    public async Task EB05036海军领袖登场抽一并可追加一张休息咚()
    {
        var state = TestScene.New("OP02-002").Build();
        var me = state.Players[0];
        FillDeck(me, 1);
        AddDon(me, 0, deck: 1);
        await EffectRuntime.Resolve(state, 0, Card("EB05-036"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueOption(1));
        Assert.Single(me.Hand);
        Assert.Single(me.CostArea);
        Assert.Equal(DonState.Rest, me.CostArea[0].State);
    }

    [Fact]
    public async Task EB05037搜索百兽海盗团并把余牌自选顺序放回卡组底()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var beast = Custom("BEAST", keywords: ["百兽海盗团"]);
        var first = Custom("FIRST");
        var second = Custom("SECOND");
        me.Deck.AddRange([first, beast, second]);
        await EffectRuntime.Resolve(state, 0, Card("EB05-037"), EffectTrigger.OnEnterField,
            new MockPromptService()
                .QueueChoose(beast.Id.ToString())
                .QueueChoose(second.Id.ToString(), first.Id.ToString()));
        Assert.Contains(beast, me.Hand);
        Assert.True(new[] { second.Id, first.Id }.SequenceEqual(me.Deck.Select(card => card.Id)));
    }

    [Fact]
    public async Task EB05038只强化赛诺尔平克()
    {
        var state = TestScene.New().Build();
        var target = Custom("SENOR", power: 5000, name: "赛诺尔·平克");
        var other = Custom("OTHER", power: 5000);
        state.Players[0].Characters.AddRange([target, other]);
        await EffectRuntime.Resolve(state, 0, Card("EB05-038"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(8000, state.CurrentPowerOf(0, target));
        Assert.Equal(5000, state.CurrentPowerOf(0, other));
    }

    [Fact]
    public async Task EB05039主要和反击分别支付咚减一后结算对应力量修正()
    {
        var state = TestScene.New("OP12-041").Build();
        var me = state.Players[0];
        AddDon(me, 2);
        var target = Custom("TARGET", power: 6000);
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, Card("EB05-039"), EffectTrigger.EventMain,
            new MockPromptService()
                .QueueChoose(me.CostArea[0].Id.ToString())
                .QueueChoose(target.Id.ToString()));
        Assert.Equal(2000, state.CurrentPowerOf(1, target));
        await EffectRuntime.Resolve(state, 0, Card("EB05-039"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(me.CostArea[0].Id.ToString()));
        Assert.Empty(me.CostArea);
        Assert.Equal(me.Leader.Info.Power + 4000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task EB05042抽二后强制弃二()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var old1 = Custom("OLD-1");
        var old2 = Custom("OLD-2");
        me.Hand.AddRange([old1, old2]);
        FillDeck(me, 2);
        await EffectRuntime.Resolve(state, 0, Card("EB05-042"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(old1.Id.ToString(), old2.Id.ToString()));
        Assert.Equal(2, me.Hand.Count);
        Assert.Contains(old1, me.Trash);
        Assert.Contains(old2, me.Trash);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task EB05043KO时可选择KO或休息当前六费目标(int option, bool rests)
    {
        var state = TestScene.New().Build();
        var source = Card("EB05-043");
        state.Players[0].Characters.Add(source);
        var target = Custom("TARGET", cost: 6);
        state.Players[1].Characters.Add(target);
        Assert.True(await BattleEngine.KOCardAsync(state, 0, source,
            new MockPromptService().QueueChoose(target.Id.ToString()).QueueOption(option)));
        Assert.Equal(rests, state.Players[1].Characters.Contains(target));
        Assert.Equal(rests, target.IsTapped);
        Assert.Equal(!rests, state.Players[1].Trash.Contains(target));
    }

    [Fact]
    public async Task EB05044巴洛克领袖面对零费角色时降低对方领袖力量()
    {
        var state = TestScene.New("OP01-062").Build();
        state.Players[1].Characters.Add(Custom("ZERO", cost: 0));
        await EffectRuntime.Resolve(state, 0, Card("EB05-044"), EffectTrigger.OnEnterField,
            new MockPromptService());
        Assert.Equal(state.Players[1].Leader.Info.Power - 1000,
            state.CurrentPowerOf(1, state.Players[1].Leader));
    }

    [Fact]
    public async Task EB05045登场KO巴洛克角色且自身KO时搜索后废弃余牌()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-045");
        var cost = Custom("BAROQUE-COST", keywords: ["巴洛克工作室"]);
        me.Characters.AddRange([source, cost]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(cost.Id.ToString()));
        Assert.Contains(cost, me.Trash);

        var picked = Custom("BAROQUE-PICK", keywords: ["巴洛克工作室"]);
        var rest1 = Custom("REST-1");
        var rest2 = Custom("REST-2");
        me.Deck.AddRange([rest1, picked, rest2]);
        Assert.True(await BattleEngine.KOCardAsync(state, 0, source,
            new MockPromptService().QueueChoose(picked.Id.ToString())));
        Assert.Contains(picked, me.Hand);
        Assert.Contains(rest1, me.Trash);
        Assert.Contains(rest2, me.Trash);
    }

    [Fact]
    public async Task EB05046动态阻挡与加攻依赖对方八千原本力量且九废弃支付手牌后达十生效()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var source = Card("EB05-046");
        me.Characters.Add(source);
        var big = Custom("BIG", power: 8000);
        state.Players[1].Characters.Add(big);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(ActionValidator.HasKeyword(state, source, "阻挡者"));
        Assert.Equal(source.Info.Power + 3000, state.CurrentPowerOf(0, source));
        me.Trash.AddRange(Enumerable.Range(0, 9).Select(index => Custom($"TRASH-{index}")));
        var discard = Custom("DISCARD");
        me.Hand.Add(discard);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(discard.Id.ToString()));
        Assert.Equal(10, me.Trash.Count);
        Assert.Equal(7000, me.Leader.OriginalPowerOverride);
    }

    [Fact]
    public async Task EB05047持续费用加十二且可丢低费角色后立即从废弃区登场该卡()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-047");
        me.Characters.Add(source);
        var discardAndPlay = Custom("LOW", cost: 2);
        me.Hand.Add(discardAndPlay);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService()
                .QueueChoose(discardAndPlay.Id.ToString())
                .QueueChoose(discardAndPlay.Id.ToString()));
        Assert.Equal(source.Info.Cost + 12, state.CurrentCostOf(0, source));
        Assert.Contains(discardAndPlay, me.Characters);
        Assert.DoesNotContain(discardAndPlay, me.Trash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EB05048复合成本仅在全部权威复验成功后提交并限制当时零费角色(bool stale)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var cost = Custom("BAROQUE", keywords: ["巴洛克工作室"]);
        me.Characters.Add(cost);
        var zero = Custom("ZERO", cost: 0);
        state.Players[1].Characters.Add(zero);
        var prompts = new MockPromptService()
            .QueueChoose(don.Id.ToString())
            .QueueChoose(cost.Id.ToString());
        if (stale)
            prompts.OnChooseResponse = kind =>
            {
                if (kind == "OwnCharacter") me.Characters.Remove(cost);
            };
        await EffectRuntime.Resolve(state, 0, Card("EB05-048"), EffectTrigger.EventMain, prompts);
        Assert.Equal(stale ? DonState.Active : DonState.Rest, don.State);
        Assert.Equal(!stale, me.Trash.Contains(cost));
        Assert.Equal(!stale, zero.HasRestriction(RestrictionKind.CannotBeBlocker));

        await EffectRuntime.Resolve(state, 0, Card("EB05-048"), EffectTrigger.EventCounter,
            new MockPromptService());
        Assert.Equal(me.Leader.Info.Power + 3000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task EB05050生命触发真实伤害路径能复用登场时检索属性知()
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Deck.Clear();
        var trigger = Card("EB05-050");
        var knowledge = Custom("KNOWLEDGE", property: "知");
        var other1 = Custom("OTHER-A", property: "斩");
        var other2 = Custom("OTHER-B", property: "打");
        var other3 = Custom("OTHER-C", property: "射");
        me.LifeArea.Add(trigger);
        me.Deck.AddRange([other1, knowledge, other2, other3]);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        Answer(engine, lifePrompt, "trigger");
        var search = await WaitForPrompt(engine, "LookTop");
        Assert.Equal([knowledge.Id.ToString()], search.ValidChoices);
        Answer(engine, search, knowledge.Id.ToString());
        var reorder = await WaitForPrompt(engine, "ReorderToDeckBottom");
        Answer(engine, reorder, other3.Id.ToString(), other2.Id.ToString(), other1.Id.ToString());
        await damage;

        Assert.Contains(trigger, me.Trash);
        Assert.Contains(knowledge, me.Hand);
        Assert.True(new[] { other3.Id, other2.Id, other1.Id }.SequenceEqual(me.Deck.Select(card => card.Id)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task EB05051按响应后的双方生命合计重新校验当前费用(bool lifeChanges, bool rests)
    {
        var state = TestScene.New().Build();
        var myLife = Custom("MY-LIFE");
        var oppLife1 = Custom("OPP-LIFE-1");
        var oppLife2 = Custom("OPP-LIFE-2");
        state.Players[0].LifeArea.Add(myLife);
        state.Players[1].LifeArea.AddRange([oppLife1, oppLife2]);
        var target = Custom("TARGET", cost: 3);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        if (lifeChanges)
            prompts.OnChooseResponse = _ => state.Players[1].LifeArea.Remove(oppLife2);
        await EffectRuntime.Resolve(state, 0, Card("EB05-051"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(rests, target.IsTapped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EB05052拒绝或接受伤害置换决定生命是否受伤(bool accept)
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        var source = Card("EB05-052");
        var life = Custom("SAFE-LIFE");
        me.Characters.Add(source);
        me.LifeArea.Add(life);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var confirm = await WaitForPrompt(engine, "Option");
        Answer(engine, confirm, accept ? "0" : "1");
        await damage;

        Assert.Equal(accept, me.LifeArea.Contains(life));
        Assert.Equal(accept, me.Trash.Contains(source));
        Assert.Equal(!accept, me.Hand.Contains(life));
    }

    [Fact]
    public async Task EB05052零生命仍可支付置换并防止败北()
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Characters.Clear();
        var source = Card("EB05-052");
        me.Characters.Add(source);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var confirm = await WaitForPrompt(engine, "Option");
        Answer(engine, confirm, "0");
        await damage;

        Assert.False(engine.State.IsGameOver);
        Assert.Contains(source, me.Trash);
    }

    [Fact]
    public async Task EB05052一次置换防止整次双重伤害()
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        var source = Card("EB05-052");
        me.Characters.Add(source);
        me.LifeArea.AddRange([Custom("LIFE-A"), Custom("LIFE-B")]);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 2);
        var confirm = await WaitForPrompt(engine, "Option");
        Answer(engine, confirm, "0");
        await damage;

        Assert.Equal(2, me.LifeArea.Count);
        Assert.Empty(me.Hand);
        Assert.Contains(source, me.Trash);
    }

    [Fact]
    public async Task EB05052多个候选第一张拒绝后可由第二张成功置换()
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        var first = Card("EB05-052");
        var second = Card("EB05-052");
        var life = Custom("SAFE-LIFE");
        me.Characters.AddRange([first, second]);
        me.LifeArea.Add(life);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var order = await WaitForPrompt(engine, "EffectOrder");
        Answer(engine, order, first.Id.ToString());
        var decline = await WaitForPrompt(engine, "Option");
        Answer(engine, decline, "1");
        var accept = await WaitForPrompt(engine, "Option");
        Answer(engine, accept, "0");
        await damage;

        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Trash);
        Assert.Contains(life, me.LifeArea);
    }

    [Fact]
    public async Task EB05052确认等待期间来源离场不得无成本吞掉伤害()
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        var source = Card("EB05-052");
        var life = Custom("SAFE-LIFE");
        me.Characters.Add(source);
        me.LifeArea.Add(life);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var confirm = await WaitForPrompt(engine, "Option");
        me.Characters.Remove(source);
        me.Trash.Add(source);
        Answer(engine, confirm, "0");
        await damage;

        Assert.Empty(me.LifeArea);
        Assert.Contains(life, me.Hand);
        Assert.Contains(source, me.Trash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EB05052多候选排序空响应或伪造响应均继续原伤害(bool forged)
    {
        var engine = NewEngine();
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        var first = Card("EB05-052");
        var second = Card("EB05-052");
        var life = Custom("SAFE-LIFE");
        me.Characters.AddRange([first, second]);
        me.LifeArea.Add(life);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var order = await WaitForPrompt(engine, "EffectOrder");
        if (forged) Answer(engine, order, Guid.NewGuid().ToString());
        else Answer(engine, order);
        await damage;

        Assert.Empty(me.LifeArea);
        Assert.Contains(life, me.Hand);
        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
    }

    [Fact]
    public async Task EB05052生命触发抽二弃一()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var old = Custom("OLD");
        me.Hand.Add(old);
        FillDeck(me, 2);
        await EffectRuntime.Resolve(state, 0, Card("EB05-052"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(old.Id.ToString()));
        Assert.Contains(old, me.Trash);
        Assert.Equal(2, me.Hand.Count);
    }

    [Fact]
    public async Task EB05053攻击时按九手牌条件把己方手牌置生命且每回合一次()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-053");
        me.Characters.Add(source);
        state.Players[1].Hand.AddRange(Enumerable.Range(0, 9).Select(index => Custom($"OPP-{index}")));
        var life = Custom("HAND-TO-LIFE");
        me.Hand.Add(life);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(life.Id.ToString()));
        Assert.Same(life, me.LifeArea[0]);
        Assert.Contains($"EB05-053-attack:{source.Id}", me.TurnOnceUsed);
        var second = Custom("SECOND");
        me.Hand.Add(second);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, new MockPromptService());
        Assert.Contains(second, me.Hand);
    }

    [Fact]
    public async Task EB05053生命不多于二时支付弃牌触发成本后从废弃区登场()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-053");
        var cost = Custom("COST");
        me.Trash.Add(source);
        me.Hand.Add(cost);
        me.LifeArea.AddRange([Custom("LIFE-A"), Custom("LIFE-B")]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(cost.Id.ToString()));
        Assert.Contains(cost, me.Trash);
        Assert.Contains(source, me.Characters);
    }

    [Fact]
    public async Task EB05054触发只登场最多两张原本力量四千的大妈角色()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var first = Custom("MOM-A", power: 4000, keywords: ["大妈海盗团"]);
        var second = Custom("MOM-B", power: 4000, keywords: ["大妈海盗团"]);
        var wrongPower = Custom("MOM-C", power: 5000, keywords: ["大妈海盗团"]);
        me.Hand.AddRange([first, second, wrongPower]);
        await EffectRuntime.Resolve(state, 0, Card("EB05-054"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString()));
        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
        Assert.Contains(wrongPower, me.Hand);
    }

    [Fact]
    public async Task EB05055我方回合知属性领袖登场加生命且生命触发付费自登场()
    {
        var state = TestScene.New("EB05-010").Build();
        var me = state.Players[0];
        var source = Card("EB05-055");
        var deckTop = Custom("DECK-TOP");
        me.Deck.Add(deckTop);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService());
        Assert.Same(deckTop, me.LifeArea[0]);

        var triggerCopy = Card("EB05-055");
        var cost = Custom("COST");
        me.Trash.Add(triggerCopy);
        me.Hand.Add(cost);
        await EffectRuntime.Resolve(state, 0, triggerCopy, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(cost.Id.ToString()));
        Assert.Contains(triggerCopy, me.Characters);
        Assert.Contains(cost, me.Trash);
    }

    [Fact]
    public async Task EB05056纯自登场生命触发通过真实路径继续发动登场时弃触发抽二()
    {
        var engine = NewEngine("EB05-010");
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        me.Deck.Clear();
        var source = Card("EB05-056");
        var cost = Custom("TRIGGER-COST", trigger: "【触发】抽取1张卡牌。");
        me.LifeArea.Add(source);
        me.Hand.Add(cost);
        FillDeck(me, 2);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        Answer(engine, lifePrompt, "trigger");
        var confirm = await WaitForPrompt(engine, "Option");
        Answer(engine, confirm, "0");
        var discard = await WaitForPrompt(engine, "OwnHandDiscard");
        Answer(engine, discard, cost.Id.ToString());
        await damage;

        Assert.Contains(source, me.Characters);
        Assert.Contains(cost, me.Trash);
        Assert.Equal(2, me.Hand.Count);
    }

    [Fact]
    public async Task EB05057启动主要选择赋予休息咚且触发铺低力量触发角色()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-057");
        me.Characters.Add(source);
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(source.Id.ToString()).QueueOption(1));
        Assert.Equal(DonState.Attached, don.State);
        Assert.Equal(source.Id, don.AttachedToCardId);
        Assert.Contains($"EB05-057-main:{source.Id}", me.TurnOnceUsed);

        var target = Custom("TRIGGER-CHAR", power: 6000, trigger: "【触发】抽取1张卡牌。");
        me.Hand.Add(target);
        me.LifeArea.AddRange([Custom("LIFE-A"), Custom("LIFE-B")]);
        await EffectRuntime.Resolve(state, 0, Card("EB05-057"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, me.Characters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EB05057合法选择零张仍消费次数但旧目标失效不消费(bool staleTarget)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-057");
        var target = Custom("TEST-KNOWLEDGE-TARGET", property: "知");
        me.Characters.AddRange([source, target]);
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueOption(0);
        if (staleTarget) prompts.OnChooseResponse = _ => me.Characters.Remove(target);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        string onceKey = $"EB05-057-main:{source.Id}";
        Assert.Equal(!staleTarget, me.TurnOnceUsed.Contains(onceKey));
        Assert.Empty(me.CostArea);
        Assert.Equal(staleTarget ? 0 : 1, prompts.OptionHistory.Count);

        if (!staleTarget)
        {
            var second = new MockPromptService();
            await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, second);
            Assert.Empty(second.ChooseHistory);
            Assert.Empty(second.OptionHistory);
        }
    }

    [Fact]
    public async Task EB05060主要以场上当前费用判定艾格赫德成本且反击翻正面生命加战斗力量()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var qualifies = Custom("QUALIFIES", cost: 4, keywords: ["艾格赫德"]);
        qualifies.CostModThisTurn = 1;
        var noLongerQualifies = Custom("NO-LONGER", cost: 5, keywords: ["艾格赫德"]);
        noLongerQualifies.CostModThisTurn = -1;
        me.Characters.AddRange([qualifies, noLongerQualifies]);
        var top = Custom("TOP");
        me.Deck.Add(top);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(qualifies.Id.ToString())
            .QueueConfirm(true);
        await EffectRuntime.Resolve(state, 0, Card("EB05-060"), EffectTrigger.EventMain, prompts);
        Assert.Contains(qualifies, me.Trash);
        Assert.Contains(noLongerQualifies, me.Characters);
        Assert.Same(top, me.LifeArea[0]);
        Assert.DoesNotContain(noLongerQualifies.Id.ToString(), prompts.ChooseHistory[0].choices);

        var faceDown = Custom("FACE-DOWN");
        me.LifeArea.Insert(0, faceDown);
        await EffectRuntime.Resolve(state, 0, Card("EB05-060"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()));
        Assert.True(faceDown.IsLifeFaceUp);
        Assert.Equal(me.Leader.Info.Power + 4000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task EB05061登场只铺二费以下红色角色()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var red = Custom("RED", cost: 2, color: "红");
        var blue = Custom("BLUE", cost: 2, color: "蓝");
        me.Hand.AddRange([red, blue]);
        await EffectRuntime.Resolve(state, 0, Card("EB05-061"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(red.Id.ToString()));
        Assert.Contains(red, me.Characters);
        Assert.Contains(blue, me.Hand);
    }

    [Fact]
    public async Task EB05061可在战斗KO自身前支付生命并阻止离场()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var source = Card("EB05-061");
        var life = Custom("LIFE");
        me.Characters.Add(source);
        me.LifeArea.Add(life);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetCardId = source.Id,
            TargetIsLeader = false,
        };

        Assert.False(await BattleEngine.KOCardAsync(state, 0, source, new MockPromptService()));
        Assert.Contains(source, me.Characters);
        Assert.Contains(life, me.Hand);
        Assert.Contains($"EB05-061-leave:{source.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task EB05061对方效果KO自身只询问一次并成功保护()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-061");
        var life = Custom("LIFE");
        me.Characters.Add(source);
        me.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);

        Assert.False(await AtomicOps.KOByEffectAsync(
            state, 0, source, prompts, actingSide: 1));

        Assert.Contains(source, me.Characters);
        Assert.Contains(life, me.Hand);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Contains($"EB05-061-leave:{source.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task EB05061被己方效果KO时不可发动保护()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB05-061");
        var life = Custom("LIFE");
        me.Characters.Add(source);
        me.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);

        Assert.True(await AtomicOps.KOByEffectAsync(
            state, 0, source, prompts, actingSide: 0));

        Assert.Contains(source, me.Trash);
        Assert.Contains(life, me.LifeArea);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.DoesNotContain($"EB05-061-leave:{source.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task EB05061生命入手限制使置换不可用且不会消费每回合一次()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var source = Card("EB05-061");
        var victim = Custom("VICTIM", power: 6000);
        var life = Custom("LIFE");
        me.Characters.AddRange([source, victim]);
        me.LifeArea.Add(life);
        state.NoEffectLifeToHandThisTurn.Add(0);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetCardId = victim.Id,
            TargetIsLeader = false,
        };

        Assert.True(await BattleEngine.KOCardAsync(state, 0, victim, new MockPromptService()));
        Assert.Contains(victim, me.Trash);
        Assert.Contains(life, me.LifeArea);
        Assert.DoesNotContain($"EB05-061-leave:{source.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task EB05061可阻止对方效果弹回且作用方在嵌套解析中保持正确()
    {
        var state = TestScene.New().Build();
        var defender = state.Players[0];
        var attacker = state.Players[1];
        var nami = Card("EB05-061");
        var victim = Custom("VICTIM", power: 6000);
        var life = Custom("LIFE");
        defender.Characters.AddRange([nami, victim]);
        defender.LifeArea.Add(life);
        var discard = Custom("DISCARD");
        attacker.Hand.Add(discard);

        await EffectRuntime.Resolve(state, 1, Card("EB05-029"), EffectTrigger.EventMain,
            new MockPromptService()
                .QueueConfirm(true)
                .QueueConfirm(true)
                .QueueChoose(discard.Id.ToString())
                .QueueChoose(victim.Id.ToString()));

        Assert.Contains(victim, defender.Characters);
        Assert.Contains(life, defender.Hand);
        Assert.Contains(discard, attacker.Trash);
    }

    [Fact]
    public async Task EB05061同时效果KO多张合格角色只支付一次生命并覆盖整批()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var nami = Card("EB05-061");
        var first = Custom("VICTIM-A", power: 5000);
        var second = Custom("VICTIM-B", power: 6000);
        var life = Custom("LIFE");
        me.Characters.AddRange([nami, first, second]);
        me.LifeArea.Add(life);

        int count = await AtomicOps.KOCardsByEffectAsync(
            state, 0, [first, second], new MockPromptService(), actingSide: 1);

        Assert.Equal(0, count);
        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
        Assert.Contains(life, me.Hand);
        Assert.Single(me.TurnOnceUsed.Where(key => key == $"EB05-061-leave:{nami.Id}"));
    }

    [Fact]
    public async Task 延迟KO冻结监听者集合并进入确定性检查点()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var victim = Custom("ALABASTA-VICTIM", power: 3000, keywords: ["阿拉巴斯坦王国"]);
        var oldListener = Card("OP18-001");
        var newListener = Card("OP18-001");
        me.Characters.AddRange([victim, oldListener]);
        FillDeck(me, 2);

        Assert.True(await AtomicOps.KOByEffectAsync(
            state, 0, victim, new MockPromptService(), actingSide: 1, deferOnKO: true));
        me.Characters.Remove(oldListener);
        me.Trash.Add(oldListener);
        me.Characters.Add(newListener);

        var checkpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        var pending = Assert.Single(checkpoint.GetProperty("pendingKoEffects").EnumerateArray());
        var listener = Assert.Single(pending.GetProperty("listenerSnapshot").EnumerateArray());
        Assert.Equal(oldListener.Id.ToString("D"),
            listener.GetProperty("source").GetProperty("id").GetString());

        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());
        Assert.Single(me.Hand);
        Assert.Contains($"OP18-001-ko:{oldListener.Id}", me.TurnOnceUsed);
        Assert.DoesNotContain($"OP18-001-ko:{newListener.Id}", me.TurnOnceUsed);
        Assert.Empty(state.PendingKOEffects);
    }

    [Fact]
    public async Task 延迟KO在触发时已成立的监听者离场后仍会结算()
    {
        const string listenerNumber = "TEST-FROZEN-KO-LISTENER";
        var state = TestScene.New().Build();
        var effect = new FieldBoundKOListenerEffect(listenerNumber);
        state.Ruleset = new CardRuleset(
            "test-frozen-ko-listener",
            baseRulesetId: null,
            description: "测试延迟 KO 已触发监听者",
            new Dictionary<string, IScriptedEffect>(StringComparer.OrdinalIgnoreCase)
            {
                [listenerNumber] = effect,
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            changedCards: [listenerNumber]);
        state.RulesetId = state.Ruleset.Id;
        var me = state.Players[0];
        var victim = Custom("TEST-KO-VICTIM");
        var listener = Custom(listenerNumber, effectTags: [EffectTrigger.OnAnyCharKOd.ToString()]);
        me.Characters.AddRange([victim, listener]);

        Assert.True(await AtomicOps.KOByEffectAsync(
            state, 0, victim, new MockPromptService(), actingSide: 1, deferOnKO: true));
        me.Characters.Remove(listener);
        me.Trash.Add(listener);

        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());

        Assert.Contains($"frozen-ko:{listener.Id}", me.TurnOnceUsed);
        Assert.Empty(state.PendingKOEffects);
    }

    private sealed class FieldBoundKOListenerEffect(string cardNumber)
        : IScriptedEffect, ITriggeredEffectAvailability
    {
        public string CardNumber => cardNumber;

        public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAnyCharKOd;

        public bool IsTriggerAvailable(
            GameState state,
            int ownerIndex,
            CardInstance source,
            EffectTrigger trigger,
            IReadOnlyDictionary<string, object?>? payload)
            => trigger == EffectTrigger.OnAnyCharKOd
               && state.Players[ownerIndex].Characters.Contains(source);

        public Task Resolve(EffectContext ctx)
        {
            ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Add($"frozen-ko:{ctx.Source.Id}");
            return Task.CompletedTask;
        }
    }
}
