using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class ST25EffectTests
{
    public static TheoryData<string, int> 条件被动角色 => new()
    {
        { "ST25-001", 0 }, { "ST25-001", 1 },
        { "ST25-002", 0 }, { "ST25-002", 1 },
        { "ST25-005", 0 }, { "ST25-005", 1 },
    };

    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance 费用角色(int originalCost) => new()
    {
        Info = new CardInfo
        {
            Number = $"测试费用{originalCost}", Name = "测试角色", Kind = CardKind.Character,
            Cost = originalCost, Power = 5000, Color = "蓝", Property = "打",
        },
    };

    private static void 断言条件被动(GameState state, int owner, CardInstance source, bool active)
    {
        Assert.Equal(source.Info.Cost + (active ? 1 : 0), state.CurrentCostOf(owner, source));
        Assert.Equal(active && source.Info.Number != "ST25-001",
            ActionValidator.HasKeyword(state, source, "阻挡者"));
    }

    [Theory]
    [MemberData(nameof(条件被动角色))]
    public async Task 登场时被选择性无效仍注册持续费用和阻挡者(string number, int owner)
    {
        var state = TestScene.New().Build();
        var source = Card(number);
        state.Players[owner].Characters.AddRange([费用角色(5), 费用角色(6)]);
        state.Players[owner].Hand.Add(source);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = state.Players[1 - owner].Leader.Id.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false },
            NullifyOnlyTrigger = EffectTrigger.OnEnterField,
            Predicate = (_, _, card) => card.Id == source.Id,
        });

        await AtomicOps.PlayFromHandFree(state, owner, source);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());

        断言条件被动(state, owner, source, true);
    }

    [Theory]
    [MemberData(nameof(条件被动角色))]
    public async Task 整卡无效期间登场的持续能力在无效解除后恢复且不重复叠加(string number, int owner)
    {
        var state = TestScene.New().Build();
        var source = Card(number);
        state.Players[owner].Characters.AddRange([source, 费用角色(5), 费用角色(6)]);
        source.IsEffectsNullified = true;

        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        断言条件被动(state, owner, source, false);

        source.IsEffectsNullified = false;
        断言条件被动(state, owner, source, true);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        断言条件被动(state, owner, source, true);

        source.IsEffectsNullified = true;
        断言条件被动(state, owner, source, false);
        source.IsEffectsNullified = false;
        断言条件被动(state, owner, source, true);
    }

    [Theory]
    [MemberData(nameof(条件被动角色))]
    public async Task 条件按原本费用动态计算且不统计对方角色或手牌(string number, int owner)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        var source = Card(number);
        var first = 费用角色(5);
        var second = 费用角色(6);
        first.CostModThisTurn = -5;
        second.CostModPersistent = -6;
        me.Characters.AddRange([source, first]);
        me.Hand.Add(second);
        state.Players[1 - owner].Characters.AddRange([费用角色(5), 费用角色(6)]);

        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        断言条件被动(state, owner, source, false);

        me.Hand.Remove(second);
        me.Characters.Add(second);
        断言条件被动(state, owner, source, true);

        AtomicOps.BounceToHand(state, owner, second);
        断言条件被动(state, owner, source, false);
        var increased = 费用角色(4);
        increased.CostModThisTurn = 10;
        me.Characters.Add(increased);
        断言条件被动(state, owner, source, false);

        me.Characters.Remove(increased);
        me.Characters.Add(费用角色(5));
        断言条件被动(state, owner, source, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 毛奇在登场时无效环境满足条件后能够实际宣言阻挡(int owner)
    {
        var state = TestScene.New().Build();
        var source = Card("ST25-005");
        var me = state.Players[owner];
        me.Characters.AddRange([source, 费用角色(5)]);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = state.Players[1 - owner].Leader.Id.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false },
            NullifyOnlyTrigger = EffectTrigger.OnEnterField,
            Predicate = (_, _, _) => true,
        });
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        state.CurrentTurnPlayer = 1 - owner;
        BattleEngine.StartAttack(state, state.Players[1 - owner].Leader.Id, true, null);
        state.Phase = Phase.BattleBlock;
        Assert.False(ActionValidator.CanDeclareBlocker(state, owner, source.Id).Ok);

        me.Characters.Add(费用角色(5));
        Assert.Equal(5, state.CurrentCostOf(owner, source));
        Assert.True(ActionValidator.CanDeclareBlocker(state, owner, source.Id).Ok);
        BattleEngine.DeclareBlocker(state, source.Id);

        Assert.True(source.IsTapped);
        Assert.Equal(source.Id, state.CurrentBattle!.TargetCardId);
        Assert.Equal(Phase.BattleCounter, state.Phase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 卡巴奇对方回合力量加成不受登场时无效及费用条件影响(int owner)
    {
        var state = TestScene.New().Build();
        var source = Card("ST25-002");
        state.Players[owner].Characters.Add(source);
        source.IsEffectsNullified = true;
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        source.IsEffectsNullified = false;

        state.CurrentTurnPlayer = owner;
        Assert.Equal(1000, state.CurrentPowerOf(owner, source));
        state.CurrentTurnPlayer = 1 - owner;
        Assert.Equal(6000, state.CurrentPowerOf(owner, source));
        断言条件被动(state, owner, source, false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 提奇封锁登场效果后从手牌支付费用打出毛奇仍获得持续能力(int owner)
    {
        _ = TestScene.New();
        var engine = new GameEngine("毛奇实战回归", ("s0", "p0", "OP09-081\nST25-005"),
            ("s1", "p1", "OP09-081\nST25-005"), firstPlayer: 0);
        var state = engine.State;
        state.Phase = Phase.Main;
        state.CurrentTurnPlayer = owner;
        state.TurnCount = 10;
        var me = state.Players[owner];
        me.Hand.Clear();
        me.Characters.Clear();
        me.CostArea.Clear();
        var source = Card("ST25-005");
        me.Hand.Add(source);
        me.Characters.AddRange([费用角色(5), 费用角色(6)]);
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Active }));
        var opponent = state.Players[1 - owner];
        var discard = Card("ST25-004");
        opponent.Hand.Clear();
        opponent.Hand.Add(discard);
        await EffectRuntime.Resolve(state, 1 - owner, opponent.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 1 - owner, opponent.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(discard.Id.ToString()));

        engine.HandleAction(owner, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 }));
        await engine.WaitSettledAsync();

        Assert.Contains(source, me.Characters);
        Assert.Empty(me.Hand);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        Assert.Null(state.PendingPrompt);
        断言条件被动(state, owner, source, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 爱比达独立注册持续能力后仍正常抽三丢二(int owner)
    {
        var state = TestScene.New("OP09-042", "OP09-042").Build();
        var me = state.Players[owner];
        var source = Card("ST25-001");
        var discardA = Card("ST25-004");
        var discardB = Card("ST25-005");
        me.Characters.AddRange([source, 费用角色(5), 费用角色(6)]);
        me.Hand.AddRange([discardA, discardB]);
        me.Deck.AddRange([Card("ST25-002"), Card("ST25-004"), Card("ST25-005")]);

        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(discardA.Id.ToString(), discardB.Id.ToString()));

        Assert.Equal(3, me.Hand.Count);
        Assert.Equal(new[] { discardA, discardB }, me.Trash);
        Assert.Empty(me.Deck);
        断言条件被动(state, owner, source, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 毛奇持续能力调整后仍保留巴奇领袖的KO抽牌(int owner)
    {
        var state = TestScene.New("OP09-042", "OP09-042").Build();
        var source = Card("ST25-005");
        var me = state.Players[owner];
        me.Characters.Add(source);
        me.Deck.Add(Card("ST25-004"));
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());

        await BattleEngine.KOCardAsync(state, owner, source, new MockPromptService());

        Assert.Contains(source, me.Trash);
        Assert.Single(me.Hand);
        Assert.Empty(me.Deck);
    }
}
