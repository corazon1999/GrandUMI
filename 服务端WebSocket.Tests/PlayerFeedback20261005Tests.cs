using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;
using System.Text.Json;

namespace GrandUMI.Tests;

/// <summary>最新玩家反馈的区域移动、筛选条件和次数限制回归。</summary>
public sealed class PlayerFeedback20261005Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 霍金斯检索的角色直接登场并结算登场效果()
    {
        var state = TestScene.New().MyDeckTop("OP14-013", "OP14-010", "OP15-003", "OP15-004", "OP15-005", "OP15-006").Build();
        var me = state.Players[0];
        var picked = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(picked.Id.ToString()).QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, Card("OP14-010"), EffectTrigger.OnKO, prompts);
        Assert.Contains(picked, me.Characters);
        Assert.DoesNotContain(picked, me.Hand);
        Assert.DoesNotContain(picked, me.Deck);
        Assert.Equal(2, prompts.ChooseHistory.Count(prompt => prompt.kind == "LookTopReveal"));
        Assert.DoesNotContain(Card("OP14-010").Info.Number, prompts.ChooseHistory[0].choices
            .Select(id => me.Deck.FirstOrDefault(card => card.Id.ToString() == id)?.Info.Number));
    }

    [Fact]
    public async Task 霍金斯可以放弃登场并自行排列剩余卡牌()
    {
        var state = TestScene.New().MyDeckTop("OP01-006", "OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007").Build();
        var original = state.Players[0].Deck.ToList();
        var prompts = new MockPromptService().QueueChooseEmpty()
            .QueueChoose(original.Take(5).Reverse().Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, Card("OP14-010"), EffectTrigger.OnKO, prompts);
        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Characters);
        Assert.Equal(new[] { original[5] }.Concat(original.Take(5).Reverse()), state.Players[0].Deck);
    }

    [Theory]
    [InlineData(8, 6000, true)]
    [InlineData(2, 8000, false)]
    [InlineData(7, 7000, true)]
    public async Task 绿事件按原本力量而非费用选择(int cost, int power, bool eligible)
    {
        var state = TestScene.New().MyActiveDon(2).MyDeckTop("OP15-003").Build();
        var target = new CardInstance { Info = new CardInfo
        {
            Number = "测试-001", Name = "筛选测试角色", Color = "黑", Kind = CardKind.Character,
            Property = "打", Cost = cost, Power = power,
        } };
        state.Players[1].Characters.Add(target);
        target.PowerModThisTurn = 5000;
        var prompts = new MockPromptService()
            .QueueChoose(state.Players[0].CostArea.Select(don => don.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP14-038"), EffectTrigger.EventMain, prompts);
        Assert.Equal(eligible, prompts.ChooseHistory.Last().choices.Contains(target.Id.ToString()));
        Assert.Equal(eligible, target.IsTapped);
    }

    [Fact]
    public async Task 郊游熊只需附着咚且能选择另一张同名角色()
    {
        var state = TestScene.New().MyCharacter("OP08-010").MyCharacter("OP08-010").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var target = me.Characters[1];
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(1000, target.PowerModThisTurn);
        Assert.Equal(0, source.PowerModThisTurn);
        Assert.Same(don, Assert.Single(me.CostArea));
        Assert.Empty(me.DonDeck);
        Assert.DoesNotContain(source.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
    }

    [Fact]
    public async Task 郊游熊未附着咚时不能发动()
    {
        var state = TestScene.New().MyCharacter("OP08-010").MyCharacter("OP08-010").MyActiveDon(1).Build();
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, state.Players[0].Characters[0], EffectTrigger.ActivatedMain, prompts);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(DonState.Active, Assert.Single(state.Players[0].CostArea).State);
    }

    [Theory]
    [InlineData("OP15-061", false)]
    [InlineData("ST10-010", false)]
    [InlineData("OP15-060", true)]
    public async Task 放电反击只能选择艾尼路(string number, bool eligible)
    {
        var state = TestScene.New("OP15-058").MyCharacter(number).Build();
        var target = Assert.Single(state.Players[0].Characters);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP15-074"), EffectTrigger.EventCounter, prompts);
        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(0, choice.min);
        Assert.DoesNotContain(state.Players[0].Leader.Id.ToString(), choice.choices);
        Assert.Equal(eligible, choice.choices.Contains(target.Id.ToString()));
        Assert.Equal(eligible ? 2000 : 0, target.PowerModThisBattle);
    }

    [Fact]
    public async Task 十字公会能够检索促销巴基()
    {
        var state = TestScene.New().MyDeckTop("P-098").Build();
        var buggy = Assert.Single(state.Players[0].Deck);
        await EffectRuntime.Resolve(state, 0, Card("OP09-057"), EffectTrigger.EventMain,
            new MockPromptService().QueueChoose(buggy.Id.ToString()));
        Assert.Same(buggy, Assert.Single(state.Players[0].Hand));
        Assert.True(buggy.Info.HasKeyword("十字公会"));
    }

    [Fact]
    public async Task 耶稣布能够活跃红发海盗团旗下弗戈()
    {
        var state = TestScene.New().MyCharacter("OP17-031").MyCharacter("OP17-026").Build();
        var target = state.Players[0].Characters[1];
        target.IsTapped = true;
        await EffectRuntime.Resolve(state, 0, state.Players[0].Characters[0], EffectTrigger.OnMyTurnEnd,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.False(target.IsTapped);
    }

    [Fact]
    public async Task 汉库克空生命触发也消耗次数()
    {
        var state = TestScene.New("OP14-041").AttachDonToMyLeader(1).Build();
        var ko = Card("OP14-114");
        state.Players[0].Trash.Add(ko);
        var payload = new Dictionary<string, object?> { ["owner"] = 0, ["cardId"] = ko.Id.ToString(), ["originalPower"] = 6000 };
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAnyCharKOd,
            new MockPromptService(), payload);
        var life = Card("OP15-003");
        state.Players[1].LifeArea.Add(life);
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAnyCharKOd,
            new MockPromptService(), payload);
        Assert.Same(life, Assert.Single(state.Players[1].LifeArea));
        Assert.Empty(state.Players[1].Hand);
        Assert.Contains($"OP14-041-Ability2:{state.Players[0].Leader.Id}", state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task 汉库克使用离场前原本力量快照()
    {
        var state = TestScene.New("OP14-041").AttachDonToMyLeader(1).Build();
        var ko = Card("OP14-114");
        state.Players[0].Trash.Add(ko);
        var life = Card("OP15-003");
        state.Players[1].LifeArea.Add(life);
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAnyCharKOd,
            new MockPromptService(), new Dictionary<string, object?>
            { ["owner"] = 0, ["cardId"] = ko.Id.ToString(), ["originalPower"] = 0 });
        Assert.Same(life, Assert.Single(state.Players[1].LifeArea));
        Assert.Empty(state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task 罗西南迪没有合法罗也应展示确认牌并允许排序()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var original = state.Players[0].Deck.ToList();
        var prompts = new MockPromptService().QueueChooseEmpty()
            .QueueChoose(original.AsEnumerable().Reverse().Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, Card("OP12-108"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.Empty(prompts.ChooseHistory[0].choices);
        Assert.NotNull(prompts.ChooseHistory[0].extra?["choiceCards"]);
        Assert.Equal(original.AsEnumerable().Reverse(), state.Players[0].Deck);
    }

    [Fact]
    public async Task 云雀离场重新登场后可再次发动启动效果()
    {
        var state = TestScene.New().MyCharacter("EB03-008").OppCharacter("OP15-003").Build();
        var source = Assert.Single(state.Players[0].Characters);
        var target = Assert.Single(state.Players[1].Characters);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        AtomicOps.BounceToHand(state, 0, source);
        await AtomicOps.PlayFromHandFree(state, 0, source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(-2000, target.PowerModThisTurn);
    }

    [Fact]
    public async Task 克比离场后领袖原本力量仍持续到对方结束()
    {
        var state = TestScene.New("OP02-093").MyCharacter("P-092").Build();
        var source = Assert.Single(state.Players[0].Characters);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, new MockPromptService());
        AtomicOps.BounceToHand(state, 0, source);
        Assert.Equal(7000, state.OriginalPowerOf(0, state.Players[0].Leader));
        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(7000, state.OriginalPowerOf(0, state.Players[0].Leader));
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(state.Players[0].Leader.Info.Power, state.OriginalPowerOf(0, state.Players[0].Leader));
    }

    [Theory]
    [InlineData("OP14-110")]
    [InlineData("EB03-055")]
    public async Task 被黑胡子无效的角色KO时不再发动自身效果(string number)
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP09-093").OppCharacter(number)
            .Build();
        var source = Assert.Single(state.Players[0].Characters);
        source.TurnPlayed = state.TurnCount;
        var target = Assert.Single(state.Players[1].Characters);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(state.IsContinuouslyNullified(target));
        var prompts = new MockPromptService();
        await AtomicOps.KOByEffectAsync(state, 1, target, prompts, 0);
        Assert.Contains(target, state.Players[1].Trash);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Empty(state.Players[1].Hand);
    }

    [Fact]
    public async Task 黑胡子离场或自身无效后已结算的目标无效仍保留()
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP09-093").OppCharacter("OP17-044").Build();
        var source = Assert.Single(state.Players[0].Characters);
        source.TurnPlayed = state.TurnCount;
        var target = Assert.Single(state.Players[1].Characters);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        source.IsEffectsNullified = true;
        Assert.True(state.IsContinuouslyNullified(target));
        AtomicOps.BounceToHand(state, 0, source);
        Assert.True(state.IsContinuouslyNullified(target));
        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.True(state.IsContinuouslyNullified(target));
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.False(state.IsContinuouslyNullified(target));
    }

    [Fact]
    public async Task 黑胡子无效不会跟随目标离场后重新登场()
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP09-093").OppCharacter("OP17-044").Build();
        var source = Assert.Single(state.Players[0].Characters);
        source.TurnPlayed = state.TurnCount;
        var target = Assert.Single(state.Players[1].Characters);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        AtomicOps.BounceToHand(state, 1, target);
        await AtomicOps.PlayFromHandFree(state, 1, target);
        Assert.False(state.IsContinuouslyNullified(target));
    }

    [Fact]
    public async Task 萨博已结算的防效果KO不因萨博被无效或离场而消失()
    {
        var state = TestScene.New().MyCharacter("OP04-083").MyCharacter("OP15-003").Build();
        var source = state.Players[0].Characters[0];
        var target = state.Players[0].Characters[1];
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        source.IsEffectsNullified = true;
        Assert.True(state.IsKoGuarded(source, "effect"));
        Assert.True(state.IsKoGuarded(target, "effect"));
        Assert.False(state.IsKoGuarded(target, "battle"));
        AtomicOps.BounceToHand(state, 0, source);
        Assert.True(state.IsKoGuarded(target, "effect"));
        state.CurrentTurnPlayer = 1;
        state.TurnCount++;
        TurnEngine.EnterEndPhase(state);
        Assert.True(state.IsKoGuarded(target, "effect"));
        state.CurrentTurnPlayer = 0;
        state.TurnCount++;
        TurnEngine.EnterResetPhase(state);
        Assert.False(state.IsKoGuarded(target, "effect"));
    }

    [Fact]
    public async Task 萨博保护只作用于结算时在场的角色()
    {
        var state = TestScene.New().MyCharacter("OP04-083").MyCharacter("OP15-003").Build();
        var source = state.Players[0].Characters[0];
        var target = state.Players[0].Characters[1];
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        AtomicOps.BounceToHand(state, 0, target);
        await AtomicOps.PlayFromHandFree(state, 0, target);
        Assert.False(state.IsKoGuarded(target, "effect"));
        var enteredLater = Card("OP15-004");
        state.Players[0].Characters.Add(enteredLater);
        Assert.False(state.IsKoGuarded(enteredLater, "effect"));
    }

    [Fact]
    public async Task 约翰被持续无效后不再限制攻击目标()
    {
        var state = TestScene.New("OP09-081", "OP17-039").MyCharacter("OP09-093").OppCharacter("OP17-044").Build();
        state.TurnCount = 3;
        var source = Assert.Single(state.Players[0].Characters);
        source.TurnPlayed = state.TurnCount;
        var target = Assert.Single(state.Players[1].Characters);
        target.IsTapped = true;
        Assert.False(ActionValidator.CanAttack(state, 0, state.Players[0].Leader.Id, true, null).Ok);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(ActionValidator.CanAttack(state, 0, state.Players[0].Leader.Id, true, null).Ok);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-12, true)]
    public async Task 龙马按当前费用判断萨乌罗能否KO(int modifier, bool eligible)
    {
        var state = TestScene.New().OppCharacter("OP17-089").Build();
        var target = Assert.Single(state.Players[1].Characters);
        await EffectRuntime.Resolve(state, 1, target, EffectTrigger.OnEnterField, new MockPromptService());
        target.IsTapped = true;
        target.CostModThisTurn = modifier;
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP15-036"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(eligible, Assert.Single(prompts.ChooseHistory).choices.Contains(target.Id.ToString()));
        Assert.Equal(eligible, state.Players[1].Trash.Contains(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 白胡子攻击税允许取消且只有完整支付后才宣言攻击(bool pay)
    {
        _ = TestScene.New();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("攻击税回归", ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);
        var state = engine.State;
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        state.Players[0].Characters.Clear();
        state.Players[1].Characters.Clear();
        state.Players[0].Hand.Clear();
        var hand = new[] { Card("OP15-003"), Card("OP15-004") };
        state.Players[0].Hand.AddRange(hand);
        var attacker = Card("OP01-004");
        state.Players[0].Characters.Add(attacker);
        state.AttackTaxDiscard[0] = 2;
        Assert.True(engine.HandleAction(0, "Attack", JsonSerializer.SerializeToElement(new
        { attackerId = attacker.Id.ToString(), targetIsLeader = true })));
        var deadline = Environment.TickCount64 + 3000;
        while (state.PendingPrompt is null && Environment.TickCount64 < deadline) await Task.Delay(10);
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("AttackTaxDiscard", prompt.Kind);
        Assert.Equal(0, prompt.MinChoose);
        engine.Prompts.Resolve(prompt.PromptId, pay ? hand.Select(card => card.Id.ToString()).ToArray() : []);
        await engine.WaitSettledAsync();
        Assert.Equal(pay, attacker.IsTapped);
        Assert.Equal(pay ? 0 : 2, state.Players[0].Hand.Count);
        if (pay) Assert.NotNull(state.CurrentBattle);
        else { Assert.Null(state.CurrentBattle); Assert.Equal(Phase.Main, state.Phase); }
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 4000)]
    public async Task 浸食轮回反击仅在废弃区至少十张时加攻(int trashCount, int bonus)
    {
        var state = TestScene.New().Build();
        for (int index = 0; index < trashCount; index++) state.Players[0].Trash.Add(Card("OP15-003"));
        await EffectRuntime.Resolve(state, 0, Card("OP14-096"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(state.Players[0].Leader.Id.ToString()));
        Assert.Equal(bonus, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task KO后回收到手牌再登场时清除旧减攻()
    {
        var state = TestScene.New().MyCharacter("OP14-013").Build();
        var source = Assert.Single(state.Players[0].Characters);
        source.PowerModThisTurn = -2000;
        source.PowerModPersistent = -1000;
        BattleEngine.KOCard(state, 0, source);
        Assert.True(state.Players[0].Trash.Remove(source));
        state.Players[0].Hand.Add(source);
        await AtomicOps.PlayFromHandFree(state, 0, source);
        Assert.Equal(0, source.PowerModThisTurn);
        Assert.Equal(0, source.PowerModPersistent);
        Assert.Equal(source.Info.Power, state.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task 罗领袖从生命区登场的角色能发动登场效果()
    {
        var state = TestScene.New("OP10-022").AttachDonToMyLeader(1).MyCharacter("OP04-083")
            .MyDeckTop("OP14-011", "OP15-003").Build();
        var me = state.Players[0];
        var returned = Assert.Single(me.Characters);
        var entered = Card("OP14-013");
        me.LifeArea.Add(entered);
        var prompts = new MockPromptService().QueueConfirm(true).QueueConfirm(true)
            .QueueChoose(returned.Id.ToString()).QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.Contains(entered, me.Characters);
        Assert.Contains(returned, me.Hand);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "LookTopReveal");
        Assert.Empty(me.LifeArea);
    }

    [Fact]
    public async Task 人妖之道完整支付返咚后能改变攻击对象()
    {
        var state = TestScene.New("OP14-079").MyCharacter("OP15-003").MyActiveDon(1).Build();
        var me = state.Players[0];
        var target = Assert.Single(me.Characters);
        var don = Assert.Single(me.CostArea);
        state.CurrentTurnPlayer = 1;
        state.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1,
            AttackerCardId = state.Players[1].Leader.Id, DefenderPlayerIndex = 0, TargetIsLeader = true };
        await EffectRuntime.Resolve(state, 0, Card("EB01-038"), EffectTrigger.EventCounter,
            new MockPromptService().QueueConfirm(true).QueueChoose(target.Id.ToString()).QueueChoose(don.Id.ToString()));
        Assert.False(state.CurrentBattle.TargetIsLeader);
        Assert.Equal(target.Id, state.CurrentBattle.TargetCardId);
        Assert.Contains(don, me.DonDeck);
        Assert.Empty(me.CostArea);
    }

    [Fact]
    public async Task 巴基领袖能够从手牌登场促销巴基()
    {
        var state = TestScene.New("OP09-042").MyActiveDon(5).MyHandAdd("OP15-003").MyHandAdd("P-098")
            .MyCharacter("P-098").MyCharacter("P-098").MyCharacter("P-098").MyCharacter("P-098").Build();
        var me = state.Players[0];
        var discard = me.Hand[0];
        var entered = me.Hand[1];
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(discard.Id.ToString()).QueueChoose(entered.Id.ToString()));
        Assert.Contains(entered, me.Characters);
        Assert.DoesNotContain(entered, me.Hand);
        Assert.Equal(5, me.Characters.Count);
        Assert.Contains(discard, me.Trash);
    }
}
