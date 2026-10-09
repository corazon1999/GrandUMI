using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>QQ 机器人反馈 #184–#188 的完整结算与规则边界回归。</summary>
public sealed class PlayerFeedback20261009Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData("OP10-065")]
    [InlineData("OP10-068")]
    [InlineData("OP10-078")]
    public async Task 砂糖支付休息成本后可检索同名角色其他角色和事件(string number)
    {
        var state = TestScene.New().MyCharacter("OP10-065").MyActiveDon(1)
            .MyDeckTop(number, "OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007").Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        var picked = me.Deck[0];
        var remaining = me.Deck.Skip(1).Take(4).Reverse().ToArray();
        var untouched = me.Deck[5];
        var prompts = new MockPromptService().QueueChoose(picked.Id.ToString())
            .QueueChoose(remaining.Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.True(source.IsTapped);
        Assert.Equal(DonState.Rest, Assert.Single(me.CostArea).State);
        Assert.Contains(picked, me.Hand);
        Assert.DoesNotContain(picked, me.Deck);
        Assert.Equal(new[] { untouched }.Concat(remaining), me.Deck);
        Assert.Contains(picked.Id.ToString(), prompts.ChooseHistory[0].choices);
    }

    [Fact]
    public async Task 砂糖可以不检索并自行排列全部五张余牌()
    {
        var state = TestScene.New().MyCharacter("OP10-065").MyActiveDon(1)
            .MyDeckTop("OP10-065", "OP10-068", "OP10-078", "OP15-003", "OP15-004", "OP15-005").Build();
        var me = state.Players[0];
        var original = me.Deck.ToArray();
        var prompts = new MockPromptService().QueueChooseEmpty()
            .QueueChoose(original.Take(5).Reverse().Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, me.Characters[0], EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(me.Hand);
        Assert.Equal(new[] { original[5] }.Concat(original.Take(5).Reverse()), me.Deck);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 砂糖已休息或没有活跃咚时不能检索(bool alreadyRested)
    {
        var scene = TestScene.New().MyCharacter("OP10-065").MyDeckTop("OP10-065");
        if (alreadyRested) scene.MyActiveDon(1);
        var state = scene.Build();
        var me = state.Players[0];
        var source = Assert.Single(me.Characters);
        source.IsTapped = alreadyRested;
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(alreadyRested, source.IsTapped);
        Assert.Single(me.Deck);
        Assert.Empty(me.Hand);
        Assert.Empty(prompts.ChooseHistory);
        if (alreadyRested) Assert.Equal(DonState.Active, Assert.Single(me.CostArea).State);
    }

    [Fact]
    public async Task 罗领袖从生命登场邦妮后活跃咚并限制对方角色休息()
    {
        var state = TestScene.New("OP10-022").AttachDonToMyLeader(1)
            .MyCharacter("OP04-083").OppCharacter("OP05-051").Build();
        var me = state.Players[0];
        var returned = Assert.Single(me.Characters);
        var target = Assert.Single(state.Players[1].Characters);
        var entered = Card("EB03-017");
        me.LifeArea.Add(entered);
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueChoose(returned.Id.ToString())
            .QueueOption(1).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(returned, me.Hand);
        Assert.Contains(entered, me.Characters);
        Assert.Empty(me.LifeArea);
        Assert.Equal(DonState.Active, don.State);
        var restriction = Assert.Single(target.Restrictions);
        Assert.Equal(RestrictionKind.CannotBeRested, restriction.Kind);
        Assert.Equal(KeywordDuration.UntilNextOpponentEndPhase, restriction.Duration);
        state.CurrentTurnPlayer = 1;
        Assert.False(AtomicOps.RestCard(target));
    }

    [Theory]
    [InlineData("OP10-022", true)]
    [InlineData("OP07-019", true)]
    [InlineData("OP04-020", false)]
    public async Task 邦妮以领袖超新星特征作为登场条件(string leader, bool eligible)
    {
        var state = TestScene.New(leader).MyHandAdd("EB03-017").OppCharacter("OP05-051").Build();
        var me = state.Players[0];
        var entered = Assert.Single(me.Hand);
        var target = Assert.Single(state.Players[1].Characters);
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueOption(1).QueueChoose(target.Id.ToString());

        await AtomicOps.PlayFromHandFree(state, 0, entered);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Contains(entered, me.Characters);
        Assert.Equal(eligible ? DonState.Active : DonState.Rest, don.State);
        Assert.Equal(eligible, target.HasRestriction(RestrictionKind.CannotBeRested));
        if (!eligible) Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 邦妮允许不活跃咚仍限制当前费用不高于八的角色()
    {
        var state = TestScene.New("OP10-022").MyHandAdd("EB03-017").OppCharacter("OP05-051").Build();
        var me = state.Players[0];
        var target = Assert.Single(state.Players[1].Characters);
        target.CostModThisTurn = 1;
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueOption(0).QueueChoose(target.Id.ToString());

        await AtomicOps.PlayFromHandFree(state, 0, me.Hand[0]);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Equal(DonState.Rest, don.State);
        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));
    }

    [Theory]
    [InlineData("ready", true)]
    [InlineData("rested", false)]
    [InlineData("nullified", false)]
    [InlineData("cannotRest", false)]
    [InlineData("declined", false)]
    public async Task 巴基放底前询问可用罗宾且被保护后仍结算自身放底(string mode, bool protectedTarget)
    {
        var state = TestScene.New().MyHandAdd("OP09-051")
            .OppCharacter("OP18-031").OppCharacter("OP09-004").Build();
        var me = state.Players[0];
        var defender = state.Players[1];
        var buggy = Assert.Single(me.Hand);
        var robin = defender.Characters[0];
        var shanks = defender.Characters[1];
        robin.IsTapped = mode == "rested";
        if (mode == "nullified") AtomicOps.NullifyEffects(robin, KeywordDuration.ThisTurn);
        if (mode == "cannotRest")
            AtomicOps.AddRestriction(robin, RestrictionKind.CannotBeRested, KeywordDuration.ThisTurn);
        var prompts = new MockPromptService().QueueChoose(shanks.Id.ToString()).QueueConfirm(mode != "declined");

        await AtomicOps.PlayFromHandFree(state, 0, buggy);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Equal(protectedTarget, defender.Characters.Contains(shanks));
        Assert.Equal(!protectedTarget, defender.Deck.Contains(shanks));
        Assert.Contains(buggy, me.Deck);
        Assert.DoesNotContain(buggy, me.Characters);
        if (protectedTarget) Assert.True(robin.IsTapped);
        Assert.Equal(mode is "ready" or "declined" ? 1 : 0, prompts.ConfirmHistory.Count);
        Assert.Empty(state.PreventLeaveCardIds);
    }

    [Fact]
    public async Task 巴基没有罗宾时正常放底并在五张高费角色满足时留场()
    {
        var state = TestScene.New().MyHandAdd("OP09-051").OppCharacter("OP09-004")
            .MyCharacter("OP05-051").MyCharacter("OP05-051")
            .MyCharacter("OP05-051").MyCharacter("OP05-051").Build();
        var me = state.Players[0];
        var buggy = Assert.Single(me.Hand);
        var shanks = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService().QueueChoose(shanks.Id.ToString());

        await AtomicOps.PlayFromHandFree(state, 0, buggy);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Contains(shanks, state.Players[1].Deck);
        Assert.Contains(buggy, me.Characters);
        Assert.DoesNotContain(buggy, me.Deck);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task 罗宾在我方回合不能替代对方巴基的效果离场()
    {
        var state = TestScene.New().MyHandAdd("OP09-051")
            .OppCharacter("OP18-031").OppCharacter("OP09-004").Build();
        state.CurrentTurnPlayer = 1;
        var defender = state.Players[1];
        var robin = defender.Characters[0];
        var shanks = defender.Characters[1];
        var prompts = new MockPromptService().QueueChoose(shanks.Id.ToString());

        await AtomicOps.PlayFromHandFree(state, 0, state.Players[0].Hand[0]);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Contains(shanks, defender.Deck);
        Assert.False(robin.IsTapped);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task 霍金斯登场不发动而被KO后从顶五直接登场角色()
    {
        var state = TestScene.New().MyHandAdd("OP14-010")
            .MyDeckTop("OP14-013", "OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007").Build();
        var me = state.Players[0];
        var hawkins = me.Hand[0];
        var picked = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(picked.Id.ToString()).QueueChooseEmpty();

        await AtomicOps.PlayFromHandFree(state, 0, hawkins);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(6, me.Deck.Count);

        await AtomicOps.KOByEffectAsync(state, 0, hawkins, prompts, 1);

        Assert.Contains(hawkins, me.Trash);
        Assert.Contains(picked, me.Characters);
        Assert.DoesNotContain(picked, me.Hand);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "LookTopReveal");
    }
}
