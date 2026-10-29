using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>最新游戏内反馈的离场次数、休息咚计数与原局条件回归。</summary>
public sealed class PlayerFeedback20261029Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Victim() => new() { Info = new CardInfo
    {
        Number = "测试本家", Name = "离场次数测试角色", Kind = CardKind.Character,
        Cost = 3, Power = 4000, Color = "紫", Property = "打", Keywords = ["堂吉诃德海盗团"],
    } };

    private static async Task Bounce(GameState state, int owner, CardInstance victim, MockPromptService prompts)
    {
        var source = Card("ST03-009");
        state.Players[1 - owner].Characters.Add(source);
        await EffectRuntime.Resolve(state, 1 - owner, source, EffectTrigger.OnEnterField,
            prompts.QueueChoose(victim.Id.ToString()));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task 维尔高KO与非KO离场共用每回合一次(int owner, bool firstKo)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        var vergo = Card("OP14-061");
        var victim = Victim();
        me.Characters.AddRange([vergo, victim]);
        me.CostArea.AddRange(Enumerable.Range(0, 3).Select(_ => new DonCard { State = DonState.Rest }));
        if (firstKo)
            Assert.False(await AtomicOps.KOByEffectAsync(state, owner, victim, new MockPromptService(), 1 - owner));
        else
            await Bounce(state, owner, victim, new MockPromptService());
        Assert.Contains(victim, me.Characters);
        Assert.Equal(2, me.CostArea.Count);
        var second = new MockPromptService();
        if (firstKo)
        {
            await Bounce(state, owner, victim, second);
            Assert.Contains(victim, me.Hand);
        }
        else
        {
            Assert.True(await AtomicOps.KOByEffectAsync(state, owner, victim, second, 1 - owner));
            Assert.Contains(victim, me.Trash);
        }
        Assert.Equal(2, me.CostArea.Count);
        Assert.Single(me.DonDeck);
        Assert.Empty(second.ConfirmHistory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 维尔高放弃不消耗次数且每个回合结束恢复(int owner)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        me.Characters.Add(Card("OP14-061"));
        me.CostArea.AddRange(Enumerable.Range(0, 3).Select(_ => new DonCard { State = DonState.Active }));
        var declined = Victim(); me.Characters.Add(declined);
        await Bounce(state, owner, declined, new MockPromptService().QueueConfirm(false));
        Assert.Contains(declined, me.Hand);
        Assert.Empty(me.TurnOnceUsed);
        var victim = Victim(); me.Characters.Add(victim);
        await Bounce(state, owner, victim, new MockPromptService());
        Assert.Contains(victim, me.Characters);
        Assert.Equal(2, me.CostArea.Count);
        TurnEngine.EnterEndPhase(state);
        state.CurrentTurnPlayer = 1 - state.CurrentTurnPlayer;
        state.TurnCount++;
        state.Phase = Phase.Main;
        await Bounce(state, owner, victim, new MockPromptService());
        Assert.Contains(victim, me.Characters);
        Assert.Single(me.CostArea);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 两张维尔高各有一次保护且攻击时成本独立(int owner)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        var first = Card("OP14-061");
        var second = Card("OP14-061");
        var victim = Victim();
        me.Characters.AddRange([first, second, victim]);
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Active }));
        await Bounce(state, owner, victim, new MockPromptService());
        await Bounce(state, owner, victim, new MockPromptService());
        Assert.Contains(victim, me.Characters);
        Assert.Equal(2, me.CostArea.Count);
        var target = state.Players[1 - owner].Characters[0];
        await EffectRuntime.Resolve(state, owner, first, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(me.CostArea[0].Id.ToString()).QueueChoose(target.Id.ToString()));
        Assert.Equal(target.Info.Power - 2000, state.CurrentPowerOf(1 - owner, target));
        Assert.Single(me.CostArea);
        await Bounce(state, owner, victim, new MockPromptService());
        Assert.Contains(victim, me.Hand);
        Assert.Single(me.CostArea);
    }

    [Theory]
    [InlineData(0, 6, 0, false)]
    [InlineData(0, 7, 0, true)]
    [InlineData(0, 8, 0, true)]
    [InlineData(0, 5, 2, true)]
    [InlineData(1, 6, 0, false)]
    [InlineData(1, 7, 0, true)]
    [InlineData(1, 8, 0, true)]
    [InlineData(1, 5, 2, true)]
    public async Task 猫蝮蛇将休息咚与其他休息卡合计且排除附着咚(int owner, int restedDon, int restedCards, bool rush)
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = owner; state.TurnCount = 5;
        var source = Card("OP08-028"); source.TurnPlayed = 5;
        state.Players[owner].Characters.Add(source);
        var opponent = state.Players[1 - owner];
        opponent.CostArea.AddRange(Enumerable.Range(0, restedDon).Select(_ => new DonCard { State = DonState.Rest }));
        opponent.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = opponent.Leader.Id });
        opponent.CostArea.Add(new DonCard { State = DonState.Active });
        if (restedCards > 0)
        {
            opponent.Leader.IsTapped = true;
            var character = Card("OP15-003"); character.IsTapped = true;
            opponent.Characters.Add(character);
        }
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(rush, ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Equal(rush, ActionValidator.CanAttack(state, owner, source.Id, true, null).Ok);
        TurnEngine.EnterEndPhase(state);
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 0)]
    public async Task 艾斯领袖下纽哥特仍须在登场时生命不多于二(int life, int tax)
    {
        var state = TestScene.New("OP13-002").Build();
        var me = state.Players[0];
        me.LifeArea.AddRange(Enumerable.Range(0, life).Select(_ => Card("OP15-003")));
        var source = Card("OP08-043"); me.Characters.Add(source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(tax, state.AttackTaxDiscard[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 万雷在原反馈九咚场面可从真实出牌入口发动(int owner)
    {
        TestScene.New();
        var engine = new GameEngine("万雷反馈调查", ("s0", "p0", "OP12-061\nOP15-078"),
            ("s1", "p1", "OP14-041\nOP15-078"), 0, 29);
        var state = engine.State;
        state.CurrentTurnPlayer = owner; state.TurnCount = 12;
        state.OpeningStage = OpeningStage.Playing; state.Phase = Phase.Main;
        var me = state.Players[owner];
        me.Hand.Clear(); me.Deck.Clear();
        var source = Card("OP15-078"); me.Hand.Add(source);
        var drawn = Card("OP15-003"); me.Deck.Add(drawn); me.Deck.Add(Card("OP15-003"));
        me.CostArea.AddRange(Enumerable.Range(0, 9).Select(index =>
            new DonCard { State = index < 2 ? DonState.Active : DonState.Rest }));
        var target = Card("OP16-119"); target.PowerModThisTurn = 4000 - target.Info.Power;
        state.Players[1 - owner].Characters.Add(target);
        Assert.True(engine.HandleAction(owner, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        for (int step = 0; step < 6 && state.PendingPrompt is { } prompt; step++)
        {
            Assert.Equal(owner, prompt.PlayerIndex);
            var choices = prompt.Kind == "ReturnOwnDon"
                ? me.CostArea.Where(don => don.State == DonState.Rest).Take(2).Select(don => don.Id.ToString()).ToArray()
                : prompt.Kind == "Option" ? new[] { "0" } : new[] { target.Id.ToString() };
            engine.Prompts.Resolve(prompt.PromptId, choices);
            await engine.WaitSettledAsync();
        }
        Assert.Null(state.PendingPrompt);
        Assert.Contains(source, me.Trash);
        Assert.Contains(drawn, me.Hand);
        Assert.Equal(7, me.CostArea.Count);
        Assert.True(target.IsTapped);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public async Task 戈普有命中无命中或放弃领取均能自选剩余牌顺序(int owner, int mode)
    {
        var state = TestScene.New("OP13-001", "OP13-001").Build();
        var me = state.Players[owner];
        var eligible = Card(mode == 0 ? "OP01-016" : "OP13-031");
        var top = new[] { Card("OP13-016"), eligible, Card("OP01-055"), Card("OP13-016") };
        var untouched = Card("OP15-003"); me.Deck.AddRange(top); me.Deck.Add(untouched);
        bool take = mode == 1;
        var rest = top.Where(card => !take || card != eligible).ToArray();
        var prompts = new MockPromptService().QueueChoose(take ? new[] { eligible.Id.ToString() } : [])
            .QueueChoose(rest.Reverse().Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, owner, Card("OP13-016"), EffectTrigger.OnEnterField, prompts);
        Assert.Equal(take, me.Hand.Contains(eligible));
        Assert.Equal(new[] { untouched }.Concat(rest.Reverse()), me.Deck);
        Assert.Equal("LookTopReveal", prompts.ChooseHistory[0].kind);
        Assert.Equal("ReorderToDeckBottom", prompts.ChooseHistory[1].kind);
        Assert.Equal(mode == 0 ? 0 : 1, prompts.ChooseHistory[0].choices.Count);
    }
}
