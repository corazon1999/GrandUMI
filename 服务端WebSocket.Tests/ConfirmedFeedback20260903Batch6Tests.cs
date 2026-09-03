using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-03 已确认反馈第六批：权威回放对应的规则回归。</summary>
public sealed class ConfirmedFeedback20260903Batch6Tests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP14_105_五个目标但仅有两张休息咚时最多选择两个目标()
    {
        var state = TestScene.New()
            .MyCharacter("OP14-105")
            .MyCharacter("OP14-103")
            .MyCharacter("OP14-106")
            .MyCharacter("OP14-107")
            .Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        foreach (var number in new[] { "OP14-103", "OP14-106", "OP14-107" })
            me.Hand.Add(Card(number));
        for (var index = 0; index < 3; index++)
            me.CostArea.Add(new DonCard { State = DonState.Active });
        for (var index = 0; index < 2; index++)
            me.CostArea.Add(new DonCard { State = DonState.Rest });
        var targets = new[] { me.Leader.Id.ToString(), me.Characters[1].Id.ToString() };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(me.Hand.Select(card => card.Id.ToString()).ToArray())
            .QueueChoose(targets);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, prompts);

        var targetPrompt = Assert.Single(
            prompts.ChooseHistory, prompt => prompt.kind == "OwnLeaderOrCharacter");
        Assert.Equal(0, targetPrompt.min);
        Assert.Equal(2, targetPrompt.max);
        Assert.Equal(5, targetPrompt.choices.Count);
        Assert.Equal(2, me.CostArea.Count(don => don.State == DonState.Attached));
        Assert.Equal(3, me.CostArea.Count(don => don.State == DonState.Active));
        Assert.Empty(me.CostArea.Where(don => don.State == DonState.Rest));
    }

    [Fact]
    public async Task OP12_081_原本费用五即使加至当前八也不按高费通常登场触发()
    {
        var state = TestScene.New("OP12-081").OppCharacter("OP15-088").Build();
        var opponent = state.Players[1];
        var entered = Assert.Single(opponent.Characters);
        entered.CostModThisTurn = 3;
        opponent.LifeArea.Add(Card("OP15-003"));
        var prompts = new MockPromptService();

        await EffectRuntime.TriggerEvent(
            state,
            EffectTrigger.OnAllyCharEnter,
            prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = entered.Id.ToString(),
                ["owner"] = 1,
            });

        Assert.Equal(8, state.CurrentCostOf(1, entered));
        Assert.Single(opponent.LifeArea);
        Assert.Empty(opponent.Hand);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP12_081_原本费用八即使降至当前五仍按高费通常登场触发()
    {
        var state = TestScene.New("OP12-081").OppCharacter("OP16-003").Build();
        var opponent = state.Players[1];
        var entered = Assert.Single(opponent.Characters);
        entered.CostModThisTurn = -3;
        var life = Card("OP15-003");
        opponent.LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.TriggerEvent(
            state,
            EffectTrigger.OnAllyCharEnter,
            prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = entered.Id.ToString(),
                ["owner"] = 1,
            });

        Assert.Equal(5, state.CurrentCostOf(1, entered));
        Assert.Empty(opponent.LifeArea);
        Assert.Contains(life, opponent.Hand);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP10_001_开局注册光环且仅在对方回合强化己方海军角色()
    {
        var state = TestScene.New("OP10-001")
            .MyCharacter("OP10-030")
            .OppCharacter("OP10-030")
            .Build();
        var ownSmoker = Assert.Single(state.Players[0].Characters);
        var opposingSmoker = Assert.Single(state.Players[1].Characters);
        var leader = state.Players[0].Leader;

        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnGameStart, new MockPromptService());

        state.CurrentTurnPlayer = 0;
        Assert.Equal(ownSmoker.Info.Power, state.CurrentPowerOf(0, ownSmoker));

        state.CurrentTurnPlayer = 1;
        Assert.Equal(ownSmoker.Info.Power + 1000, state.CurrentPowerOf(0, ownSmoker));
        Assert.Equal(opposingSmoker.Info.Power, state.CurrentPowerOf(1, opposingSmoker));
    }

    [Fact]
    public async Task OP10_001_重复开局事件不会叠加光环且不强化非指定特征角色()
    {
        var state = TestScene.New("OP10-001")
            .MyCharacter("ST32-001")
            .Build();
        var leader = state.Players[0].Leader;
        var ineligible = Assert.Single(state.Players[0].Characters);

        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnGameStart, new MockPromptService());

        state.CurrentTurnPlayer = 1;
        Assert.Equal(ineligible.Info.Power, state.CurrentPowerOf(0, ineligible));
        Assert.Single(state.ContinuousEffects, effect => effect.SourceCardId == leader.Id.ToString());
    }
}
