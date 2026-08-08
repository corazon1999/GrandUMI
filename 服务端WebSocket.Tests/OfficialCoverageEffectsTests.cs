using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class OfficialCoverageEffectsTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static readonly string[] AddedNumbers =
    [
        "OP01-060",
        "ST19-003", "ST19-004", "ST19-005", "ST20-004", "ST20-005",
        "P-057", "P-058", "P-059", "P-060", "P-061", "P-120", "P-121", "P-122",
        "P-123", "P-124", "P-125", "P-126", "P-127", "P-128", "P-129", "P-130",
        "P-131", "P-132", "P-133", "P-134", "P-155",
    ];

    private static readonly string[] EffectNumbers =
    [
        "OP01-060",
        "ST19-003", "ST19-004", "ST19-005", "ST20-004", "ST20-005",
        "P-057", "P-058", "P-059", "P-060", "P-120", "P-121", "P-122", "P-126",
        "P-128", "P-129", "P-130", "P-132", "P-133", "P-134", "P-155",
    ];

    [Fact]
    public void 官网遗漏卡牌_全部可加载且有文本卡均已接入实现()
    {
        _ = TestScene.New().Build();

        Assert.All(AddedNumbers, number => Assert.True(CardDatabase.Exists(number), $"未加载 {number}"));
        Assert.All(EffectNumbers, number => Assert.NotNull(ScriptedEffectRegistry.TryGet(number)));
        Assert.Contains("阻挡者", CardDatabase.Get("ST19-005")!.Abilities);
        Assert.Contains("OnEnterField", CardDatabase.Get("P-133")!.EffectTags);
        Assert.Contains("OnEnterField", CardDatabase.Get("ST19-004")!.EffectTags);
    }

    [Fact]
    public async Task ST20_004_生命成本会转活大妈角色并驱动P120手牌减费()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("ST20-004");
        var target = Card("ST20-003");
        var life = Card("P-061");
        var sanji = Card("P-120");
        target.IsTapped = true;
        me.Characters.AddRange([source, target]);
        me.LifeArea.Add(life);
        me.Hand.Add(sanji);
        state.Players[1].LifeArea.Add(Card("P-123"));
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(me.LifeArea);
        Assert.Contains(life, me.Hand);
        Assert.False(target.IsTapped);
        Assert.Contains(0, state.LifeLeftThisTurn);
        LifeRevealManagerSync.DealDamageToLeaderNoPrompt(state, 1, 1);
        Assert.Equal(4, state.HandPlayCost(0, sanji));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(6, state.HandPlayCost(0, sanji));
    }

    [Fact]
    public async Task ST20_004_触发会横置对方低费角色()
    {
        var state = TestScene.New().Build();
        var source = Card("ST20-004");
        var target = Card("ST20-004");
        state.Players[0].Trash.Add(source);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task ST20_005_支付弃牌后由对方选择弃两张手牌()
    {
        var state = TestScene.New().Build();
        var source = Card("ST20-005");
        var ownCost = Card("P-061");
        var opponentA = Card("P-123");
        var opponentB = Card("P-124");
        var opponentC = Card("P-125");
        state.Players[0].Characters.Add(source);
        state.Players[0].Hand.Add(ownCost);
        state.Players[1].Hand.AddRange([opponentA, opponentB, opponentC]);
        var prompts = new MockPromptService()
            .QueueChoose(ownCost.Id.ToString())
            .QueueChoose(opponentA.Id.ToString(), opponentB.Id.ToString())
            .QueueOption(0);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(ownCost, state.Players[0].Trash);
        Assert.Single(state.Players[1].Hand);
        Assert.Equal(2, state.Players[1].Trash.Count);
    }

    [Fact]
    public async Task P_058_主要效果会在回合结束转活全部FILM角色()
    {
        var state = TestScene.New("ST11-001").Build();
        var source = Card("P-058");
        var filmA = Card("P-061");
        var filmB = Card("ST11-002");
        filmA.IsTapped = true;
        filmB.IsTapped = true;
        state.Players[0].Characters.AddRange([filmA, filmB]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, new MockPromptService());
        Assert.All(state.Players[0].Characters, card => Assert.True(card.IsTapped));

        TurnEngine.EnterEndPhase(state);

        Assert.All(state.Players[0].Characters, card => Assert.False(card.IsTapped));
    }

    [Fact]
    public async Task P_133_场上存在原本无效果角色时动态获得2000力量()
    {
        var state = TestScene.New().Build();
        var yamato = Card("P-133");
        var vanilla = Card("P-061");
        state.Players[0].Characters.AddRange([yamato, vanilla]);

        await EffectRuntime.Resolve(state, 0, yamato, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(7000, state.CurrentPowerOf(0, yamato));
        state.Players[0].Characters.Remove(vanilla);
        Assert.Equal(5000, state.CurrentPowerOf(0, yamato));
    }

    [Fact]
    public async Task ST19_004_对方回合有赋予咚时费用动态增加4()
    {
        var state = TestScene.New().Build();
        var hina = Card("ST19-004");
        state.Players[0].Characters.Add(hina);
        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = hina.Id,
        });

        await EffectRuntime.Resolve(state, 0, hina, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(4, state.CurrentCostOf(0, hina));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(8, state.CurrentCostOf(0, hina));
    }

    [Fact]
    public async Task P_130_检索后将其余牌放底并强制弃一张手牌()
    {
        var state = TestScene.New().Build();
        var source = Card("P-130");
        var navy = Card("P-125");
        var other = Card("P-061");
        var originalHand = Card("P-123");
        state.Players[0].Characters.Add(source);
        state.Players[0].Deck.AddRange([navy, other]);
        state.Players[0].Hand.Add(originalHand);
        var prompts = new MockPromptService()
            .QueueChoose(navy.Id.ToString())
            .QueueChoose(originalHand.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(navy, state.Players[0].Hand);
        Assert.Contains(originalHand, state.Players[0].Trash);
        Assert.Equal(other, state.Players[0].Deck[^1]);
    }

    [Fact]
    public async Task P_155_对方生命不多于3时可从触发登场()
    {
        var state = TestScene.New().Build();
        var source = Card("P-155");
        state.Players[0].Trash.Add(source);
        state.Players[1].LifeArea.AddRange([Card("P-061"), Card("P-123"), Card("P-124")]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Contains(source, state.Players[0].Characters);
        Assert.DoesNotContain(source, state.Players[0].Trash);
    }
}
