using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>十月三十日游戏内反馈的实际卡牌、成本和发动边界回归。</summary>
public sealed class PlayerFeedback20261030Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task 军子没有八千角色也能先弃牌且成功支付才消耗次数(int owner, bool cancel)
    {
        var state = TestScene.New("OP18-060", "OP18-060").Build();
        state.CurrentTurnPlayer = owner;
        var me = state.Players[owner];
        var cost = Card("OP18-084"); me.Hand.Add(cost);
        me.DonDeck.Add(new DonCard());
        var prompts = new MockPromptService().QueueChoose(cancel ? [] : [cost.Id.ToString()]);
        await EffectRuntime.Resolve(state, owner, me.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.Single(prompts.ChooseHistory);
        Assert.Empty(me.CostArea);
        Assert.Single(me.DonDeck);
        Assert.Equal(!cancel, me.Trash.Contains(cost));
        Assert.Equal(!cancel, me.TurnOnceUsed.Contains($"OP18-060-main:{me.Leader.Id}"));
        Assert.False(EffectRuntime.PayingCost);
        if (!cancel)
        {
            me.Hand.Add(Card("OP18-084"));
            var repeated = new MockPromptService();
            await EffectRuntime.Resolve(state, owner, me.Leader, EffectTrigger.ActivatedMain, repeated);
            Assert.Empty(repeated.ChooseHistory);
        }
    }

    [Theory]
    [InlineData(0, 7999, 2000, false)]
    [InlineData(1, 7999, 2000, false)]
    [InlineData(0, 8000, -2000, true)]
    [InlineData(1, 8000, -2000, true)]
    public async Task 军子加咚依据原本力量而非临时力量(int owner, int original, int modifier, bool addsDon)
    {
        var state = TestScene.New("OP18-060", "OP18-060").Build();
        state.CurrentTurnPlayer = owner;
        var me = state.Players[owner];
        me.Characters.Add(new CardInstance { Info = new CardInfo
        {
            Number = "测试原本力量", Name = "测试角色", Kind = CardKind.Character,
            Color = "紫", Property = "特", Power = original, Cost = 6,
        }, PowerModThisTurn = modifier });
        var cost = Card("OP18-084"); me.Hand.Add(cost); me.DonDeck.Add(new DonCard());
        await EffectRuntime.Resolve(state, owner, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(cost.Id.ToString()));
        Assert.Contains(cost, me.Trash);
        Assert.Equal(addsDon ? 1 : 0, me.CostArea.Count);
        if (addsDon) Assert.Equal(DonState.Active, Assert.Single(me.CostArea).State);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task 卡鲁变身遵守此前薇薇的禁登场限制且无该限制时可继续出牌(int owner, bool viviFirst)
    {
        _ = TestScene.New();
        var engine = new GameEngine("卡鲁反馈回归", ("s0", "p0", "OP18-001\nEB02-001"),
            ("s1", "p1", "OP18-001\nEB02-001"), firstPlayer: 0);
        var state = engine.State;
        state.Phase = Phase.Main; state.CurrentTurnPlayer = owner; state.TurnCount = 10;
        var me = state.Players[owner];
        me.Hand.Clear(); me.Characters.Clear(); me.CostArea.Clear(); me.DonDeck.Clear();
        var source = Card("OP18-009"); var karoo = Card("EB02-001"); var next = Card("OP13-012");
        me.Characters.Add(source); me.Hand.AddRange([karoo, next]);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(i => new DonCard { State = i < 5 ? DonState.Active : DonState.Rest }));
        if (viviFirst)
        {
            // 原局先由薇薇登场鸭部队，薇薇随后被送底，但本回合禁登场仍然生效。
            var vivi = Card("EB03-024"); me.Characters.Remove(source); me.Hand.Add(source); me.Characters.Add(vivi);
            await EffectRuntime.Resolve(state, owner, vivi, EffectTrigger.OnEnterField,
                new MockPromptService().QueueChoose(source.Id.ToString()));
            AtomicOps.ReturnFieldToDeckBottom(state, owner, vivi);
            Assert.Contains(owner, state.NoPlayCharacterThisTurn);
        }
        engine.HandleAction(owner, "UseEffect", JsonSerializer.SerializeToElement(new { sourceId = source.Id.ToString(), effectKey = "main" }));
        await engine.WaitSettledAsync();
        Assert.NotNull(state.PendingPrompt);
        engine.HandleAction(owner, "PromptResponse", JsonSerializer.SerializeToElement(new { promptId = state.PendingPrompt!.PromptId, chosen = new[] { "0" } }));
        await engine.WaitSettledAsync();
        Assert.Equal("OwnHandCharacter", state.PendingPrompt?.Kind);
        engine.HandleAction(owner, "PromptResponse", JsonSerializer.SerializeToElement(new { promptId = state.PendingPrompt!.PromptId, chosen = new[] { karoo.Id.ToString() } }));
        await engine.WaitSettledAsync();
        Assert.Contains(source, me.Trash);
        Assert.Equal(!viviFirst, me.Characters.Contains(karoo));
        Assert.Equal(viviFirst, me.Hand.Contains(karoo));
        Assert.Null(state.PendingPrompt);
        engine.HandleAction(owner, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = me.Hand.IndexOf(next) }));
        await engine.WaitSettledAsync();
        // 使用无登场效果的奈美验证结算结束后还能继续出牌。
        Assert.Equal(!viviFirst, me.Characters.Contains(next));
        Assert.Equal(viviFirst, me.Hand.Contains(next));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task 巴洛克保护只在对方回合可以代替离场(int owner, bool opponentTurn)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = opponentTurn ? 1 - owner : owner;
        var me = state.Players[owner]; var guard = Card("OP18-054"); var cost = Card("OP18-047");
        me.Characters.Add(guard); me.Hand.Add(cost);
        var source = Card("ST03-009"); state.Players[1 - owner].Characters.Add(source);
        var prompts = new MockPromptService().QueueChoose(guard.Id.ToString()).QueueChoose(cost.Id.ToString());
        await EffectRuntime.Resolve(state, 1 - owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(opponentTurn, me.Characters.Contains(guard));
        Assert.Equal(opponentTurn, me.Trash.Contains(cost));
        Assert.Equal(!opponentTurn, me.Hand.Contains(guard));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 斯巴达姆加生命顶后原正面牌在第二位(int owner)
    {
        var state = TestScene.New("OP18-079", "OP18-079").Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var exposed = Card("OP18-115"); exposed.IsLifeFaceUp = true;
        var oldBottom = Card("OP18-101"); var top = Card("OP18-110"); var cost = Card("OP18-101");
        me.LifeArea.AddRange([exposed, oldBottom]); me.Deck.Add(top); me.Hand.Add(cost);
        me.CostArea.AddRange([new DonCard { State = DonState.Active }, new DonCard { State = DonState.Active }]);
        await EffectRuntime.Resolve(state, owner, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(cost.Id.ToString()).QueueOption(1));
        Assert.Equal(new[] { top, exposed, oldBottom }, me.LifeArea);
        Assert.False(top.IsLifeFaceUp); Assert.True(exposed.IsLifeFaceUp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 罗宾重置只提供知属性角色而不会提供特属性奈美(int owner)
    {
        var state = TestScene.New("EB05-010", "EB05-010").Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner]; var nami = Card("OP17-023"); nami.IsTapped = true;
        var knowledge = Card("OP18-011"); knowledge.IsTapped = true;
        me.Characters.AddRange([nami, knowledge]);
        var prompts = new MockPromptService().QueueChoose(knowledge.Id.ToString());
        await EffectRuntime.Resolve(state, owner, me.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.True(nami.IsTapped); Assert.False(knowledge.IsTapped);
        Assert.DoesNotContain(nami.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
    }
}
