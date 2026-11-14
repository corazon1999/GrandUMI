using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-11-10 最新 F 反馈与QQ群收集记录对应的效果回归。</summary>
public sealed class LatestFeedback20261110RegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP09_061_对手返咚不会触发我方领袖效果或消耗次数()
    {
        var state = TestScene.New("OP09-061").Build();
        state.CurrentTurnPlayer = 0;
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Leader, EffectTrigger.OnDonReturnedToDeck, prompts,
            new Dictionary<string, object?> { ["owner"] = 1, ["count"] = 2 });

        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task OP15_119_被黑胡子封锁登场时仍获得速攻()
    {
        var state = TestScene.New("OP09-081", "ST30-001").Build();
        var blackbeard = state.Players[0].Leader;
        var discard = Card("OP15-003");
        state.Players[0].Hand.Add(discard);

        var luffy = Card("OP15-119");
        state.Players[1].Characters.Add(luffy);
        state.Players[1].CostArea.AddRange(
            Enumerable.Range(0, 6).Select(_ => new DonCard { State = DonState.Active }));

        await EffectRuntime.Resolve(
            state, 0, blackbeard, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
        await EffectRuntime.Resolve(
            state, 1, luffy, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(ActionValidator.HasKeyword(state, luffy, "速攻"));
    }

    [Fact]
    public async Task OP15_119_缺少对手事件归属时不误触发()
    {
        var state = TestScene.New("OP09-081", "ST30-001").Build();
        var luffy = Card("OP15-119");
        state.Players[1].Characters.Add(luffy);
        state.Players[1].LifeArea.Add(Card("OP15-003"));

        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(
            state, 1, luffy, EffectTrigger.OnOppEventPlayed, prompts);

        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Single(state.Players[1].LifeArea);
    }

    [Fact]
    public async Task P_084_被黑胡子封锁登场时仍限制双方三四费角色攻击()
    {
        var state = TestScene.New("OP09-042", "OP09-081")
            .MyCharacter("P-084")
            .OppCharacter("OP09-086")
            .Build();
        var blackbeard = state.Players[1].Leader;
        var discard = Card("OP15-003");
        state.Players[1].Hand.Add(discard);
        var burgess = Assert.Single(state.Players[1].Characters);

        await EffectRuntime.Resolve(
            state, 1, blackbeard, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Characters[0], EffectTrigger.OnEnterField,
            new MockPromptService());

        state.CurrentTurnPlayer = 1;
        state.TurnCount = 3;
        Assert.True(state.HasContinuousRestriction(burgess, RestrictionKind.CannotAttack));
        Assert.False(ActionValidator.CanAttack(state, 1, burgess.Id, true, null).Ok);
    }

    [Fact]
    public async Task OP15_088_被黑胡子封锁登场时仍保留自身加费()
    {
        var state = TestScene.New("OP09-081", "ST30-001").Build();
        var blackbeard = state.Players[0].Leader;
        var discard = Card("OP15-003");
        state.Players[0].Hand.Add(discard);
        var cosmicDock = Card("OP15-088");
        state.Players[1].Characters.Add(cosmicDock);

        await EffectRuntime.Resolve(
            state, 0, blackbeard, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
        await EffectRuntime.Resolve(
            state, 1, cosmicDock, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(cosmicDock.Info.Cost + 6, state.CurrentCostOf(1, cosmicDock));
    }

    [Fact]
    public async Task OP09_004_被黑胡子封锁登场时仍降低对方角色力量()
    {
        var state = TestScene.New("OP09-081", "ST30-001")
            .MyCharacter("OP15-003")
            .Build();
        var blackbeard = state.Players[0].Leader;
        var discard = Card("OP15-004");
        state.Players[0].Hand.Add(discard);
        var shanks = Card("OP09-004");
        state.Players[1].Characters.Add(shanks);
        var target = Assert.Single(state.Players[0].Characters);

        await EffectRuntime.Resolve(
            state, 0, blackbeard, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
        await EffectRuntime.Resolve(
            state, 1, shanks, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(target.Info.Power - 1_000, state.CurrentPowerOf(0, target));
    }
}
