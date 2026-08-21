using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OfficialConsistencyFixesTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP11110_可横置鱼人岛卡代替自身被KO()
    {
        var state = TestScene.New().Build();
        var samezvezda = Card("OP11-110");
        var cost = Card("OP11-023");
        state.Players[0].Characters.AddRange([samezvezda, cost]);

        await EffectRuntime.Resolve(state, 0, samezvezda, EffectTrigger.PreKO, new MockPromptService());

        Assert.True(cost.IsTapped);
        Assert.Contains(samezvezda.Id, state.PreventKOCardIds);
    }

    [Fact]
    public async Task OP13023_登场后禁止原本费用五以上角色登场()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var uta = Card("OP13-023");
        me.Characters.Add(uta);
        var highCost = CardDatabase.GetBySet("OP13")
            .First(info => info.Kind == CardKind.Character && info.Cost >= 5);
        var candidate = new CardInstance { Info = highCost };
        me.Hand.Add(candidate);

        await EffectRuntime.Resolve(state, 0, uta, EffectTrigger.OnEnterField, new MockPromptService());
        await AtomicOps.PlayFromHandFree(state, 0, candidate);

        Assert.Equal(5, state.NoPlayCharacterOriginalCostGteThisTurn[0]);
        Assert.Contains(candidate, me.Hand);
        Assert.DoesNotContain(candidate, me.Characters);
    }

    [Fact]
    public async Task OP13038_即使没有休息目标也会预约回合末活跃咚()
    {
        var state = TestScene.New().Build();
        var source = Card("OP13-038");
        var don1 = new DonCard { State = DonState.Rest };
        var don2 = new DonCard { State = DonState.Rest };
        state.Players[0].CostArea.AddRange([don1, don2]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, new MockPromptService());
        Assert.Contains(state.EndOfTurnTasks, task => task.Kind == "RefreshOwnDon" && task.Count == 2);

        TurnEngine.EnterEndPhase(state);
        Assert.Equal(DonState.Active, don1.State);
        Assert.Equal(DonState.Active, don2.State);
    }

    [Fact]
    public async Task OP13119_生命三张以下动态获得速攻()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var ace = Card("OP13-119");
        me.Characters.Add(ace);
        me.LifeArea.AddRange([Card("OP15-003"), Card("OP15-004"), Card("OP15-005")]);

        await EffectRuntime.Resolve(state, 0, ace, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(ActionValidator.HasKeyword(state, ace, "速攻"));
        me.LifeArea.Add(Card("OP15-006"));
        Assert.False(ActionValidator.HasKeyword(state, ace, "速攻"));
    }

    [Fact]
    public async Task OP14079_只阻止我方效果令对方角色离场()
    {
        var state = TestScene.New("OP14-079").Build();
        var source = Card("ST03-009");
        var target = Card("OP15-003");
        state.Players[0].Characters.Add(source);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains(target, state.Players[1].Characters);
        Assert.DoesNotContain(target, state.Players[1].Hand);
    }

    [Fact]
    public async Task OP15024_仅免疫对方领袖或角色效果的休息()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        var usopp = Card("OP15-024");
        var rayleigh = Card("OP13-066");
        state.Players[0].Characters.Add(usopp);
        state.Players[1].Characters.Add(rayleigh);

        await EffectRuntime.Resolve(state, 1, rayleigh, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(usopp.IsTapped);

        var eventSource = Card("OP13-038");
        await EffectRuntime.Resolve(state, 1, eventSource, EffectTrigger.EventMain, new MockPromptService());
        Assert.True(usopp.IsTapped);
    }

    [Fact]
    public async Task OP15093_同时赋予速攻角色与斩属性并在回合末清除()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP15-093");
        var luffy = Card("OP15-119");
        me.Characters.AddRange([source, luffy]);
        for (var i = 0; i < 14; i++) me.Trash.Add(Card("OP15-095"));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(ActionValidator.HasKeyword(state, luffy, "速攻"));
        Assert.True(luffy.HasProperty("斩"));
        TurnEngine.EnterEndPhase(state);
        Assert.False(luffy.HasProperty("斩"));
    }

    [Fact]
    public async Task ST06016_生命触发后本回合我方角色不会被KO()
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var source = Card("ST06-016");
        var character = Card("OP15-003");
        state.Players[0].Characters.Add(character);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, new MockPromptService());
        AtomicOps.KO(state, 0, character);

        Assert.Contains(character, state.Players[0].Characters);
        Assert.True(state.IsKoGuarded(character, "effect"));
    }

    [Fact]
    public async Task ST09015_低生命时将对方低费角色正面置入生命()
    {
        var state = TestScene.New().Build();
        state.Players[0].LifeArea.AddRange([Card("OP15-003"), Card("OP15-004")]);
        var targetInfo = CardDatabase.GetBySet("OP15")
            .First(info => info.Kind == CardKind.Character && info.Cost <= 3);
        var target = new CardInstance { Info = targetInfo };
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(state, 0, Card("ST09-015"), EffectTrigger.EventCounter, new MockPromptService());

        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Same(target, state.Players[1].LifeArea[0]);
        Assert.True(target.IsLifeFaceUp);
    }

    [Fact]
    public async Task ST13004_登场后按规则加生命并取一张放卡组顶()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var oldLife = Card("OP15-003");
        var deckTop = Card("OP15-004");
        me.LifeArea.Add(oldLife);
        me.Deck.Add(deckTop);

        await EffectRuntime.Resolve(state, 0, Card("ST13-004"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Single(me.LifeArea);
        Assert.Same(deckTop, me.Deck[0]);
        Assert.Same(oldLife, me.LifeArea[0]);
    }

    [Fact]
    public async Task ST20001_启动成本会把生命顶翻为正面()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("ST20-001");
        var life = Card("OP15-003");
        var don = new DonCard { State = DonState.Rest };
        me.Characters.Add(source);
        me.LifeArea.Add(life);
        me.CostArea.Add(don);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(life.IsLifeFaceUp);
        Assert.Equal(DonState.Attached, don.State);
    }

    [Fact]
    public async Task ST28004_必须退回两张赋予咚才获得速攻和力量()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("ST28-004");
        me.Characters.Add(source);
        var don1 = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        var don2 = new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id };
        me.CostArea.AddRange([don1, don2]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Equal(DonState.Rest, don1.State);
        Assert.Equal(DonState.Rest, don2.State);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Equal(1000, source.PowerModThisTurn);
    }
}
