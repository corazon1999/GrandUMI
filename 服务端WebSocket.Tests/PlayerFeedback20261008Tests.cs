using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using Xunit;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GrandUMI.Tests;

/// <summary>QQ 玩家反馈的检索、费用、反击和领袖目标回归。</summary>
public sealed class PlayerFeedback20261008Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData("OP18-022")]
    [InlineData("OP01-003")]
    public async Task JET枪能够使路飞领袖获得双重攻击(string leader)
    {
        var state = TestScene.New(leader).MyCharacter("OP14-013").Build();
        var me = state.Players[0];
        var prompts = new MockPromptService().QueueChoose(me.Leader.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP16-039"), EffectTrigger.EventMain, prompts);
        Assert.Contains(me.Leader.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
        Assert.Contains(me.Leader.GainedKeywords, keyword => keyword.Keyword == "双重攻击");
        Assert.DoesNotContain(me.Characters[0].GainedKeywords, keyword => keyword.Keyword == "双重攻击");
    }

    [Fact]
    public async Task JET枪不能选中其他名称的领袖()
    {
        var state = TestScene.New("OP14-020").MyCharacter("OP14-013").Build();
        var prompts = new MockPromptService().QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, Card("OP16-039"), EffectTrigger.EventMain, prompts);
        Assert.DoesNotContain(state.Players[0].Leader.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
    }

    [Fact]
    public async Task OP14013能够检索超新星事件并排除同名路飞()
    {
        var state = TestScene.New().MyDeckTop("OP13-040", "OP14-013", "OP14-010").Build();
        var me = state.Players[0];
        var target = me.Deck[0];
        var sameName = me.Deck[1];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP14-013"), EffectTrigger.OnEnterField, prompts);
        var selection = prompts.ChooseHistory[0];
        Assert.Contains(target.Id.ToString(), selection.choices);
        Assert.DoesNotContain(sameName.Id.ToString(), selection.choices);
        Assert.Contains(target, me.Hand);
        Assert.DoesNotContain(target, me.Deck);
    }

    [Theory]
    [InlineData("OP14-087", "OP14-088", EffectTrigger.OnEnterField)]
    [InlineData("OP14-087", "OP14-098", EffectTrigger.OnEnterField)]
    [InlineData("OP14-099", "OP14-088", EffectTrigger.EventMain)]
    [InlineData("OP14-099", "OP14-098", EffectTrigger.OnLifeRevealTrigger)]
    public async Task 巴洛克检索包含原巴洛克工作室(string source, string targetNumber, EffectTrigger trigger)
    {
        var state = TestScene.New("OP14-079").MyDeckTop(targetNumber, source, "OP01-016").Build();
        var me = state.Players[0];
        var target = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card(source), trigger, prompts);
        Assert.Contains(target, me.Hand);
        Assert.Contains(target.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
        Assert.Equal(2, me.Trash.Count);
    }

    [Fact]
    public async Task 巴洛克检索也接受特征中包含原巴洛克工作室的领袖()
    {
        var state = TestScene.New("OP14-079").MyDeckTop("OP14-088").Build();
        var deck = state.Players[0].Deck.ToArray();
        state.Players[0] = new PlayerState { SessionId = "s0", AccountName = "测试", Leader = new CardInstance { Info = new CardInfo
        { Number = "TEST-LEADER", Name = "测试领袖", Color = "黑", Property = "特", Kind = CardKind.Leader, Keywords = ["原巴洛克工作室"] } } };
        state.Players[0].Deck.AddRange(deck);
        var target = state.Players[0].Deck[0];
        await EffectRuntime.Resolve(state, 0, Card("OP14-087"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(target, state.Players[0].Hand);
    }

    [Fact]
    public async Task 红绿路飞横置四咚而不是返回咚卡组()
    {
        var state = TestScene.New("OP01-003").MyActiveDon(4).MyCharacter("OP14-013").Build();
        var me = state.Players[0];
        var target = me.Characters[0];
        target.IsTapped = true;
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(4, me.CostArea.Count);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        Assert.Empty(me.DonDeck);
        Assert.False(target.IsTapped);
        Assert.Equal(1000, target.PowerModThisTurn);
    }

    [Theory]
    [InlineData("OP06-060")]
    [InlineData("OP06-064")]
    [InlineData("OP06-066")]
    [InlineData("OP06-068")]
    public async Task GERMA没有登场目标仍可支付变身费用(string number)
    {
        var state = TestScene.New("OP06-042").MyCharacter(number).MyActiveDon(1).MyDeckTop("OP01-016").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var don = me.CostArea[0];
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(don.Id.ToString()));
        Assert.Empty(me.Characters);
        Assert.Contains(source, me.Trash);
        Assert.Contains(don, me.DonDeck);
        Assert.Single(me.Hand);
    }

    [Fact]
    public async Task 丽久可以支付费用后选择零张且取消费用时不自弃()
    {
        var state = TestScene.New("OP06-042").MyCharacter("OP06-068").MyHandAdd("OP06-069").MyActiveDon(1).Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChooseEmpty());
        Assert.Contains(source, me.Characters);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(me.CostArea[0].Id.ToString()).QueueChooseEmpty());
        Assert.Contains(source, me.Trash);
        Assert.Single(me.Hand);
        Assert.Empty(me.Characters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task 米霍克可以自选零至三张休息咚(int count)
    {
        var state = TestScene.New("OP14-020").OppCharacter("EB01-002").Build();
        var me = state.Players[0];
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Rest }));
        var prompts = new MockPromptService().QueueChoose(me.Leader.Id.ToString())
            .QueueChoose(me.CostArea.Skip(1).Take(count).Select(don => don.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(count, me.ActiveDonCount);
        Assert.Equal(DonState.Rest, me.CostArea[0].State);
        Assert.Contains(0, state.NoPlayCharacterThisTurn);
        Assert.Contains($"OP14-020-act:{me.Leader.Id}", me.TurnOnceUsed);
        Assert.Equal(0, prompts.ChooseHistory[1].min);
        Assert.Equal(3, prompts.ChooseHistory[1].max);
    }

    [Fact]
    public void 居鲁士的生命数据为四()
    {
        _ = TestScene.New();
        Assert.Equal(4, CardDatabase.Get("EB01-040")!.Cost);
    }

    [Fact]
    public async Task 盖德被整卡无效后手牌不再获得反击值()
    {
        var state = TestScene.New().MyCharacter("OP17-063").MyHandAdd("OP17-063").Build();
        var me = state.Players[0];
        var hand = me.Hand[0];
        Assert.Equal(1000, HandStaticCounter.Value(state, 0, hand));
        await EffectRuntime.Resolve(state, 1, Card("OP09-098"), EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(me.Characters[0].Id.ToString()));
        Assert.True(me.Characters[0].IsEffectsNullified);
        Assert.Equal(0, HandStaticCounter.Value(state, 0, hand));
    }

    [Theory]
    [InlineData("OP16-059", EffectTrigger.EventMain)]
    [InlineData("OP08-007", EffectTrigger.OnAttackDeclare)]
    public async Task 确认卡组顶即使没有可登场目标仍展示并允许排序(string number, EffectTrigger trigger)
    {
        var state = TestScene.New().MyActiveDon(7).MyDeckTop("OP01-003", "OP14-020", "EB01-040").Build();
        var me = state.Players[0];
        var top = me.Deck.ToArray();
        var prompts = new MockPromptService().QueueChooseEmpty()
            .QueueChoose(top.Reverse().Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, Card(number), trigger, prompts);
        Assert.Equal("LookTopReveal", prompts.ChooseHistory[0].kind);
        Assert.Empty(prompts.ChooseHistory[0].choices);
        Assert.NotNull(prompts.ChooseHistory[0].extra!["choiceCards"]);
        Assert.Equal(top.Reverse(), me.Deck);
    }

    [Fact]
    public async Task 瓦帕可以将对方一费舞台放回卡组底作为费用()
    {
        var state = TestScene.New().MyDeckTop("OP06-114").Build();
        var stage = Card("OP05-117");
        state.Players[1].StageCard = stage;
        await EffectRuntime.Resolve(state, 0, Card("OP06-114"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(stage.Id.ToString()).QueueChooseEmpty());
        Assert.Null(state.Players[1].StageCard);
        Assert.Contains(stage, state.Players[1].Deck);
    }

    [Fact]
    public async Task 恰卡只需附着一咚即可分配休息咚()
    {
        var state = TestScene.New().MyCharacter("OP05-008").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = source.Id });
        me.CostArea.AddRange([new DonCard { State = DonState.Rest }, new DonCard { State = DonState.Rest }]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(me.Leader.Id.ToString()));
        Assert.Equal(2, me.CostArea.Count(don => don.AttachedToCardId == me.Leader.Id));
        Assert.Equal(1, me.CostArea.Count(don => don.AttachedToCardId == source.Id));
    }

    [Fact]
    public async Task 丽久不因对方返回咚而抽牌或消耗次数()
    {
        var state = TestScene.New("OP06-042").MyDeckTop("OP01-016", "OP01-016").Build();
        var me = state.Players[0];
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 1, ["count"] = 1 });
        Assert.Empty(me.Hand);
        Assert.Empty(me.TurnOnceUsed);
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDonReturnedToDeck, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0, ["count"] = 1 });
        Assert.Single(me.Hand);
    }

    [Fact]
    public async Task 山智事件加入手牌不向对手公开所选卡牌()
    {
        _ = TestScene.New();
        const string deck = "OP12-041\nOP14-013";
        var engine = new GameEngine("反馈私密检索", ("s0", "我方", deck), ("s1", "对方", deck), 0, 77);
        var messages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (side, payload) => { if (side == 1) messages.Enqueue(JsonSerializer.Serialize(payload)); };
        var me = engine.State.Players[0];
        me.Deck.Clear();
        var target = Card("OP14-013");
        me.Deck.Add(target);
        var resolution = EffectRuntime.Resolve(engine.State, 0, Card("OP12-079"), EffectTrigger.EventMain, engine.Prompts);
        for (var attempt = 0; attempt < 100 && engine.State.PendingPrompt is null; attempt++) await Task.Delay(10);
        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        engine.Prompts.Resolve(prompt.PromptId, new[] { target.Id.ToString() });
        await resolution;
        Assert.Contains(target, me.Hand);
        Assert.DoesNotContain(messages, message => message.Contains("RevealCards"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task 黄黑路飞正面生命被芙兰佩加入手牌时改放卡组底(int edge, bool faceUp)
    {
        var state = TestScene.New("ST13-003").MyDeckTop("OP01-016", "OP01-016").Build();
        var me = state.Players[0];
        me.LifeArea.AddRange([Card("OP14-013"), Card("OP14-010")]);
        var selected = me.LifeArea[edge];
        selected.IsLifeFaceUp = faceUp;
        await EffectRuntime.Resolve(state, 0, Card("EB01-056"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueOption(edge));
        Assert.DoesNotContain(selected, me.LifeArea);
        Assert.False(selected.IsLifeFaceUp);
        if (faceUp)
        {
            Assert.DoesNotContain(selected, me.Hand);
            Assert.Same(selected, me.Deck[^1]);
            Assert.Single(me.Hand);
        }
        else
        {
            Assert.Contains(selected, me.Hand);
            Assert.Equal(2, me.Hand.Count);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task 玻璃大炮取得时移出生命会触发奈美领袖且旧回放保持原结算(bool draft, bool legacy)
    {
        _ = TestScene.New();
        const string deck = "OP11-041\nOP14-013";
        var engine = new GameEngine("玻璃大炮反馈", ("s0", "我方", deck), ("s1", "对方", deck), 0, 77, matchKind: MatchKind.Hex);
        var state = engine.State;
        if (legacy) HexRules.SetRulesRevisionForReplay(state, HexRules.QualityAndEffectRulesRevision);
        var me = state.Players[0];
        state.CurrentTurnPlayer = 0;
        me.Hand.Clear();
        me.Deck.Clear();
        me.Deck.Add(Card("OP14-013"));
        me.LifeArea.Clear();
        var life = Card("OP14-010");
        me.LifeArea.Add(life);
        if (draft)
        {
            var round = new HexDraftRound { RoundId = "反馈选秀", PlayerIndex = 0, OwnTurnNumber = 3,
                Tier = HexTier.Gold, DeadlineUtc = DateTime.UtcNow.AddMinutes(1), LockedChoice = 20, Locked = true };
            round.Candidates.Add(20);
            state.HexState.ActiveDraft = round;
            state.HexState.ResumePoint = HexDraftResumePoint.None;
            await HexRules.ResolveDraftAsync(engine);
        }
        else await HexRules.ApplyOnAcquireAsync(engine, 0, 20);
        Assert.Contains(life, me.Hand);
        Assert.Equal(legacy ? 1 : 2, me.Hand.Count);
        Assert.Equal(!legacy, me.TurnOnceUsed.Contains("OP11-041-lifedraw"));
    }

    [Fact]
    public async Task 黑乔巴按顺序抽牌弃牌再KO费用一的角色()
    {
        var state = TestScene.New().MyDeckTop("OP01-016", "OP01-025").OppCharacter("OP01-016").OppCharacter("OP01-120").Build();
        var me = state.Players[0];
        var cards = me.Deck.ToArray();
        var target = state.Players[1].Characters[0];
        var invalid = state.Players[1].Characters[1];
        var prompts = new MockPromptService().QueueChoose(cards.Select(c => c.Id.ToString()).ToArray()).QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP16-090"), EffectTrigger.OnEnterField, prompts);
        Assert.Empty(me.Hand);
        Assert.All(cards, c => Assert.Contains(c, me.Trash));
        Assert.Contains(target, state.Players[1].Trash);
        Assert.Contains(invalid, state.Players[1].Characters);
        Assert.DoesNotContain(invalid.Id.ToString(), prompts.ChooseHistory[^1].choices);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task 犬啮红莲能将双方合法角色返回各自持有者区域(int targetSide, bool trigger)
    {
        var state = TestScene.New().MyCharacter("OP01-016").OppCharacter("OP01-016").OppCharacter("OP01-120").Build();
        var target = state.Players[targetSide].Characters[0];
        var invalid = state.Players[1].Characters[1];
        var prompts = new MockPromptService();
        if (!trigger) prompts.QueueChooseEmpty();
        prompts.QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP05-057"), trigger ? EffectTrigger.OnLifeRevealTrigger : EffectTrigger.EventMain, prompts);
        Assert.Contains(target.Id.ToString(), prompts.ChooseHistory[^1].choices);
        Assert.DoesNotContain(invalid.Id.ToString(), prompts.ChooseHistory[^1].choices);
        Assert.DoesNotContain(target, state.Players[targetSide].Characters);
        Assert.Contains(target, trigger ? state.Players[targetSide].Hand : state.Players[targetSide].Deck);
    }

    [Fact]
    public async Task 佩罗娜实际降费后龙马可以KO五费休息角色()
    {
        var state = TestScene.New("OP06-021").OppCharacter("OP01-047").Build();
        var target = state.Players[1].Characters[0];
        target.IsTapped = true;
        Assert.Equal(5, state.CurrentCostOf(target));
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueOption(1).QueueChoose(target.Id.ToString()));
        Assert.Equal(4, state.CurrentCostOf(target));
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP15-036"), EffectTrigger.OnEnterField, prompts);
        Assert.Contains(target.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task 罗西南迪加入合法罗后可按自选顺序放底并保留未确认牌()
    {
        var state = TestScene.New().MyDeckTop("OP01-047", "OP01-016", "OP01-025", "OP14-010", "OP01-120", "OP14-013").Build();
        var me = state.Players[0];
        var top = me.Deck.Take(5).ToArray();
        var tail = me.Deck[5];
        var rest = top.Skip(1).Reverse().ToArray();
        var prompts = new MockPromptService().QueueChoose(top[0].Id.ToString()).QueueChoose(rest.Select(c => c.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, Card("OP12-108"), EffectTrigger.OnEnterField, prompts);
        Assert.Contains(top[0], me.Hand);
        Assert.Equal(new[] { tail }.Concat(rest), me.Deck);
        Assert.Equal("ReorderToDeckBottom", prompts.ChooseHistory[1].kind);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public async Task 玲玲支付最后手牌后仍可选择回血且有余牌时必须弃牌(int handCount, bool heal)
    {
        var state = TestScene.New("OP17-099").MyDeckTop("OP17-100").Build();
        var me = state.Players[0];
        var cards = Enumerable.Range(0, handCount).Select(_ => Card("OP14-013")).ToArray();
        me.Hand.AddRange(cards);
        var life = me.Deck[0];
        var prompts = new MockPromptService().QueueConfirm(true).QueueConfirm(heal)
            .QueueChoose(cards[0].Id.ToString()).QueueOption(0);
        if (handCount > 1) prompts.QueueChoose(cards[1].Id.ToString());
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Empty(me.Hand);
        Assert.All(cards, card => Assert.Contains(card, me.Trash));
        Assert.Equal(heal, me.LifeArea.Contains(life));
        Assert.Equal(!heal, me.Deck.Contains(life));
        Assert.Equal(2, prompts.ConfirmHistory.Count);
    }
}
