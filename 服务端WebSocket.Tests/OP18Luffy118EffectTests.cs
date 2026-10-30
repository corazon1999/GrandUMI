using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18Luffy118EffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Character(string feature = "七水之城") => new() { Info = new CardInfo
        { Number = "TEST-OP18-118", Name = "测试角色", Color = "绿", Kind = CardKind.Character,
            Power = 12000, Cost = 12, Property = "打", Keywords = [feature] } };
    private static (GameState state, CardInstance source) Scene(int owner = 0)
    {
        var state = TestScene.New("OP18-021", "OP18-021").Build();
        state.CurrentTurnPlayer = owner; state.TurnCount = 5;
        var source = Card("OP18-118"); state.Players[owner].Characters.Add(source);
        return (state, source);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    public async Task 登场可选零至三张休息咚且不动已活跃或赋予中的咚(int owner, int count)
    {
        var (state, source) = Scene(owner); var me = state.Players[owner];
        var rest = Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Rest }).ToList();
        me.CostArea.AddRange(rest);
        var active = new DonCard { State = DonState.Active };
        var attached = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.AddRange([active, attached]);
        var target = Character(); state.Players[1 - owner].Characters.Add(target);
        var prompts = new MockPromptService().QueueOption(count).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(count, rest.Count(don => don.State == DonState.Active));
        Assert.Equal(4 - count, rest.Count(don => don.State == DonState.Rest));
        Assert.Equal(DonState.Active, active.State); Assert.Equal(DonState.Attached, attached.State);
        Assert.Equal(source.Id, attached.AttachedToCardId);
        Assert.Equal(new[] { "0 张", "1 张", "2 张", "3 张" }, Assert.Single(prompts.OptionHistory).options);
        Assert.Equal(owner, prompts.OptionHistory[0].playerIdx);
        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));
        Assert.False(AtomicOps.CanRestCard(state, target, 1 - owner));
        Assert.False(target.IsTapped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 没有休息咚仍施加无限费用封休且期限跨过己方回合末(int owner)
    {
        var (state, source) = Scene(owner); var opponent = state.Players[1 - owner];
        var target = Character(); opponent.Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(prompts.OptionHistory);
        var restriction = Assert.Single(target.Restrictions);
        Assert.Equal(KeywordDuration.UntilNextOpponentEndPhase, restriction.Duration);
        Assert.Equal(owner, restriction.AppliedBySide);

        // 已经结算的封休不依赖路飞继续在场，也不能在施加者自己的回合结束时提前清除。
        AtomicOps.TrashFieldCard(state, owner, source);
        TurnEngine.EnterEndPhase(state);
        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));
        state.CurrentTurnPlayer = 1 - owner; state.Phase = Phase.Main; state.TurnCount++;
        Assert.False(ActionValidator.CanAttack(state, 1 - owner, target.Id, true, null).Ok);
        Assert.Throws<InvalidOperationException>(() => BattleEngine.StartAttack(state, target.Id, true, null));
        Assert.False(AtomicOps.RestCard(state, target, 1 - owner));
        TurnEngine.EnterEndPhase(state);
        Assert.False(target.HasRestriction(RestrictionKind.CannotBeRested));
        state.Phase = Phase.Main;
        Assert.True(ActionValidator.CanAttack(state, 1 - owner, target.Id, true, null).Ok);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    public async Task 登场封休可放弃且拒绝重复或离场目标(string response)
    {
        var (state, source) = Scene(); var target = Character(); state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService();
        if (response == "cancel") prompts.QueueChooseEmpty();
        else if (response == "duplicate") prompts.QueueChoose(target.Id.ToString(), target.Id.ToString());
        else
        {
            prompts.QueueChoose(target.Id.ToString());
            prompts.OnChooseResponse = _ => state.Players[1].Characters.Remove(target);
        }
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(target.Restrictions);
        Assert.DoesNotContain(state.Players[1].Leader.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
    }

    [Fact]
    public async Task 咚选择数量过期不部分恢复且之后仍封休()
    {
        var (state, source) = Scene(); var me = state.Players[0];
        for (int i = 0; i < 3; i++) me.CostArea.Add(new DonCard { State = DonState.Rest });
        var target = Character(); state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueOption(3).QueueChoose(target.Id.ToString());
        prompts.OnOptionResponse = () => me.CostArea.RemoveAt(0);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        Assert.True(target.HasRestriction(RestrictionKind.CannotBeRested));
    }

    [Theory]
    [InlineData(0, "leader")]
    [InlineData(0, "self")]
    [InlineData(0, "stage")]
    [InlineData(1, "ally")]
    [InlineData(1, "extraStage")]
    public async Task 己方回合末可恢复七水领袖自身其他角色及两个舞台槽(int owner, string kind)
    {
        var (state, source) = Scene(owner); var me = state.Players[owner];
        var ally = Character(); var wrong = Character("海军"); me.Characters.AddRange([ally, wrong]);
        me.StageCard = Card("OP03-075"); me.ExtraStageCard = Card("OP03-075");
        var own = new[] { me.Leader, source, ally, wrong, me.StageCard, me.ExtraStageCard };
        foreach (var card in own) card.IsTapped = true;
        var hand = Character(); hand.IsTapped = true; me.Hand.Add(hand);
        var enemy = Character(); enemy.IsTapped = true; state.Players[1 - owner].Characters.Add(enemy);
        var target = kind switch { "leader" => me.Leader, "self" => source, "stage" => me.StageCard,
            "extraStage" => me.ExtraStageCard, _ => ally };
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnMyTurnEnd, prompts);

        Assert.False(target.IsTapped);
        Assert.All(own.Where(card => card != target), card => Assert.True(card.IsTapped));
        Assert.True(hand.IsTapped); Assert.True(enemy.IsTapped);
        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(0, choice.min); Assert.Equal(1, choice.max); Assert.Equal(5, choice.choices.Count);
        Assert.DoesNotContain(wrong.Id.ToString(), choice.choices);
        Assert.DoesNotContain(hand.Id.ToString(), choice.choices);
        Assert.DoesNotContain(enemy.Id.ToString(), choice.choices);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    public async Task 回合末取消重复或过期响应不恢复卡牌(string response)
    {
        var (state, source) = Scene(); var me = state.Players[0];
        var target = Card("OP03-075"); target.IsTapped = true; me.StageCard = target;
        var prompts = new MockPromptService();
        if (response == "cancel") prompts.QueueChooseEmpty();
        else if (response == "duplicate") prompts.QueueChoose(target.Id.ToString(), target.Id.ToString());
        else
        {
            prompts.QueueChoose(target.Id.ToString());
            prompts.OnChooseResponse = _ => { me.StageCard = null; target.IsTapped = true; };
        }
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, prompts);
        Assert.True(target.IsTapped);
        Assert.Single(prompts.ChooseHistory);
    }

    [Theory]
    [InlineData("opponentTurn")]
    [InlineData("offField")]
    [InlineData("nullified")]
    public async Task 对方回合末或来源离场无效时不触发恢复(string reason)
    {
        var (state, source) = Scene(); var me = state.Players[0]; me.Leader.IsTapped = true;
        if (reason == "opponentTurn") state.CurrentTurnPlayer = 1;
        else if (reason == "offField") me.Characters.Remove(source);
        else source.IsEffectsNullified = true;
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, prompts);
        Assert.True(me.Leader.IsTapped); Assert.Empty(prompts.ChooseHistory);
    }
}
