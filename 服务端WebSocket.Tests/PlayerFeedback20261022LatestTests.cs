using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月22日最新双源反馈的成本、力量、登场限制与战斗身份回归。</summary>
public sealed class PlayerFeedback20261022LatestTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(0, "OP17-080")]
    [InlineData(1, "OP17-080")]
    [InlineData(0, "OP17-082")]
    [InlineData(1, "OP17-082")]
    [InlineData(0, "OP17-087")]
    [InlineData(1, "OP17-087")]
    [InlineData(0, "OP17-090")]
    [InlineData(1, "OP17-090")]
    [InlineData(0, "OP17-091")]
    [InlineData(1, "OP17-091")]
    [InlineData(0, "OP17-095")]
    [InlineData(1, "OP17-095")]
    public async Task 黑胡子只封锁登场效果不封锁埃鲁巴夫角色持续加力(int owner, string number)
    {
        var state = TestScene.New("OP09-081", "OP09-081").Build();
        var me = state.Players[owner];
        await EffectRuntime.Resolve(state, 1 - owner, state.Players[1 - owner].Leader,
            EffectTrigger.OnGameStart, new MockPromptService());
        state.Players[1 - owner].Hand.Add(Card("OP15-003"));
        await EffectRuntime.Resolve(state, 1 - owner, state.Players[1 - owner].Leader,
            EffectTrigger.ActivatedMain, new MockPromptService());
        var giant = Card("OP17-089"); me.Characters.Add(giant);
        await EffectRuntime.Resolve(state, owner, giant, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(16, state.CurrentCostOf(owner, giant));
        var source = Card(number); me.Hand.Add(source);
        me.Deck.AddRange(Enumerable.Range(0, 20).Select(_ => Card("OP15-003")));
        await AtomicOps.PlayFromHandFree(state, owner, source);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());
        Assert.Empty(me.Hand);
        Assert.Equal(source.Info.Power + 3000, state.CurrentPowerOf(owner, source));
        source.IsEffectsNullified = true;
        Assert.Equal(source.Info.Power, state.CurrentPowerOf(owner, source));
        source.IsEffectsNullified = false;
        Assert.Equal(source.Info.Power + 3000, state.CurrentPowerOf(owner, source));
        me.Characters.Remove(giant);
        Assert.Equal(source.Info.Power, state.CurrentPowerOf(owner, source));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task 梅迦罗必须实际从活跃转休息才能支付成本(int owner, bool rested)
    {
        var state = TestScene.New().Build();
        var source = Card("EB04-018");
        var target = Card("OP15-003");
        source.IsTapped = rested; target.IsTapped = true;
        state.Players[owner].Characters.Add(source);
        state.Players[1 - owner].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(!rested, state.Players[1 - owner].Trash.Contains(target));
        if (rested) Assert.Empty(prompts.ConfirmHistory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 六费罗休息登场的梅迦罗不能再支付休息成本(int owner)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        var law = Card("OP13-031");
        var returned = Card("OP15-003");
        var megalo = Card("EB04-018");
        var target = Card("OP15-003"); target.IsTapped = true;
        me.Characters.AddRange([law, returned]); me.Hand.Add(megalo);
        state.Players[1 - owner].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(returned.Id.ToString())
            .QueueChoose(megalo.Id.ToString()).QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, owner, law, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(megalo, me.Characters);
        Assert.True(megalo.IsTapped);
        Assert.Contains(target, state.Players[1 - owner].Characters);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 达兹条件允许登场回合攻击角色但不能攻击领袖(int owner)
    {
        var state = TestScene.New().Build(); state.TurnCount = 3; state.CurrentTurnPlayer = owner;
        var source = Card("OP14-090"); source.TurnPlayed = state.TurnCount;
        var target = Card("OP15-003"); target.CostModThisTurn = -20;
        state.Players[owner].Characters.Add(source);
        state.Players[1 - owner].Characters.Add(target);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(ActionValidator.CanAttack(state, owner, source.Id, false, target.Id).Ok);
        Assert.False(ActionValidator.CanAttack(state, owner, source.Id, true, null).Ok);
        // 额外获得真正的速攻时，仍应允许攻击领袖。
        AtomicOps.GiveKeyword(source, "速攻", KeywordDuration.ThisTurn, owner);
        Assert.True(ActionValidator.CanAttack(state, owner, source.Id, true, null).Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 三哥读取本萨姆复制后的原本力量而非卡面力量(int owner)
    {
        var state = TestScene.New().Build(); state.CurrentTurnPlayer = owner;
        var me = state.Players[owner];
        var source = Card("ST30-014"); var bentham = Card("OP16-055");
        me.Characters.AddRange([source, bentham]);
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = bentham.Id });
        me.CostArea.AddRange([new DonCard { State = DonState.Rest }, new DonCard { State = DonState.Rest }]);
        state.Players[1 - owner].Leader.OriginalPowerOverride = 6000;
        await EffectRuntime.Resolve(state, owner, bentham, EffectTrigger.OnAttackDeclare, new MockPromptService());
        Assert.Equal(6000, state.OriginalPowerOf(owner, bentham));
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(bentham.Id.ToString()).QueueOption(2));
        Assert.Equal(3, me.AttachedDonCount(bentham.Id));
    }

    [Fact]
    public async Task 三哥不把普通加攻或原本力量已改低的卡当作六千()
    {
        var state = TestScene.New().Build(); var me = state.Players[0];
        var source = Card("ST30-014");
        var buffed = Card("OP16-055"); buffed.PowerModThisTurn = 5000;
        var changed = Card("ST30-006"); changed.OriginalPowerOverride = 5000;
        me.Characters.AddRange([source, buffed, changed]);
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(1, me.RestDonCount);
    }

    [Theory]
    [InlineData("ST10-002", 0, false)]
    [InlineData("ST10-002", 4, false)]
    [InlineData("ST10-002", 5, true)]
    [InlineData("OP01-001", 5, false)]
    public async Task 路飞先自选返两咚再检查后续领袖加力条件(string leader, int opponentDon, bool buff)
    {
        var state = TestScene.New(leader).Build(); var me = state.Players[0];
        me.CostArea.AddRange([new DonCard { State = DonState.Active }, new DonCard { State = DonState.Rest }]);
        state.Players[1].CostArea.AddRange(Enumerable.Range(0, opponentDon).Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, Card("ST26-005"), EffectTrigger.OnEnterField, prompts);
        Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "ReturnOwnDon"));
        Assert.Empty(me.CostArea);
        Assert.Equal(2, me.DonDeck.Count);
        Assert.Equal(buff ? 7000 : me.Leader.Info.Power, state.OriginalPowerOf(0, me.Leader));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 路飞可放弃返咚且不提前给予加力(int owner)
    {
        var state = TestScene.New("ST10-002", "ST10-002").Build(); var me = state.Players[owner];
        me.CostArea.AddRange([new DonCard { State = DonState.Active }, new DonCard { State = DonState.Rest }]);
        state.Players[1 - owner].CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Active }));
        await EffectRuntime.Resolve(state, owner, Card("ST26-005"), EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChooseEmpty());
        Assert.Equal(2, me.CostArea.Count);
        Assert.Equal(me.Leader.Info.Power, state.OriginalPowerOf(owner, me.Leader));
    }

    [Theory]
    [InlineData(0, 29)]
    [InlineData(0, 30)]
    [InlineData(1, 29)]
    [InlineData(1, 30)]
    public async Task 想活下去的反击效果必须满足废弃区三十张(int owner, int trashCount)
    {
        var state = TestScene.New().Build(); var me = state.Players[owner];
        me.Trash.AddRange(Enumerable.Range(0, trashCount).Select(_ => Card("OP15-003")));
        var top = Card("OP15-003"); me.Deck.AddRange([top, Card("OP15-003")]);
        await EffectRuntime.Resolve(state, owner, Card("EB01-050"), EffectTrigger.EventCounter, new MockPromptService());
        Assert.Equal(trashCount >= 30 ? 1 : 0, me.LifeArea.Count);
        if (trashCount < 30) Assert.Same(top, me.Deck[0]);
    }

    [Theory]
    [InlineData(28, false)]
    [InlineData(29, true)]
    public async Task 真实反击将事件放入废弃区后再计算三十张(int beforeCount, bool heals)
    {
        _ = TestScene.New().Build();
        string deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 40));
        var engine = new GameEngine("live-counter-trash", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 131);
        var state = engine.State; state.CurrentTurnPlayer = 0; state.TurnCount = 3;
        var me = state.Players[1]; me.Hand.Clear(); me.Trash.Clear(); me.LifeArea.Clear();
        me.Hand.Add(Card("EB01-050"));
        me.Trash.AddRange(Enumerable.Range(0, beforeCount).Select(_ => Card("OP15-003")));
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        BattleEngine.StartAttack(state, state.Players[0].Leader.Id, true, null);
        state.Phase = Phase.BattleCounter;
        Assert.True(engine.HandleAction(1, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        Assert.Equal(beforeCount + 1, me.Trash.Count);
        Assert.Equal(heals ? 1 : 0, me.LifeArea.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 巴奇受到登场限制时不能先废弃已有角色(bool byCost)
    {
        var state = TestScene.New("OP09-042").Build(); var me = state.Players[0];
        var existing = Enumerable.Range(0, 5).Select(_ => Card("OP15-003")).ToArray();
        me.Characters.AddRange(existing);
        me.CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Active }));
        var discard = Card("OP15-003"); var entering = Card("ST25-003");
        me.Hand.AddRange([discard, entering]);
        if (byCost) state.NoPlayCharacterOriginalCostGteThisTurn[0] = entering.Info.Cost;
        else state.NoPlayCharacterThisTurn.Add(0);
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString())
            .QueueChoose(entering.Id.ToString()).QueueChoose(existing[4].Id.ToString());
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(existing, me.Characters);
        Assert.Contains(entering, me.Hand);
        Assert.DoesNotContain(prompts.ChooseHistory, prompt => prompt.kind is "ChooseCharToTrashForRoom" or "OverflowTrash");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 战斗参与者离场又重登仍须终止原战斗(bool attackerLeaves)
    {
        var state = TestScene.New().Build(); state.BindFieldDepartureLifecycle(); state.TurnCount = 3;
        var attacker = Card("OP15-003"); var target = Card("OP15-003"); target.IsTapped = true;
        state.Players[0].Characters.Add(attacker); state.Players[1].Characters.Add(target);
        BattleEngine.StartAttack(state, attacker.Id, false, target.Id);
        int owner = attackerLeaves ? 0 : 1; var leaving = attackerLeaves ? attacker : target;
        AtomicOps.BounceToHand(state, owner, leaving);
        await AtomicOps.PlayFromHandFree(state, owner, leaving);
        Assert.Contains(leaving, state.Players[owner].Characters);
        Assert.False(BattleEngine.AreBattleParticipantsOnField(state));
        Assert.True(DeterministicReplayCheckpointProvider.BuildFullState(state)
            .GetProperty("currentBattle").GetProperty("ParticipantLeftField").GetBoolean());
    }

    [Fact]
    public void 无关角色离场不能中断战斗且普通检查点保留旧投影()
    {
        var state = TestScene.New().Build(); state.BindFieldDepartureLifecycle(); state.TurnCount = 3;
        var attacker = Card("OP15-003"); var unrelated = Card("OP15-003");
        state.Players[0].Characters.AddRange([attacker, unrelated]);
        BattleEngine.StartAttack(state, attacker.Id, true, null);
        AtomicOps.BounceToHand(state, 0, unrelated);
        Assert.True(BattleEngine.AreBattleParticipantsOnField(state));
        Assert.False(DeterministicReplayCheckpointProvider.BuildFullState(state)
            .GetProperty("currentBattle").TryGetProperty("ParticipantLeftField", out _));
    }

    [Fact]
    public async Task 巴奇合法满场登场只腾一次且按玩家选择废弃()
    {
        var state = TestScene.New("OP09-042").Build(); var me = state.Players[0];
        var existing = Enumerable.Range(0, 5).Select(_ => Card("OP15-003")).ToArray();
        me.Characters.AddRange(existing);
        me.CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Active }));
        var discard = Card("OP15-003"); var entering = Card("ST25-003"); me.Hand.AddRange([discard, entering]);
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString())
            .QueueChoose(entering.Id.ToString()).QueueChoose(existing[3].Id.ToString());
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(5, me.Characters.Count);
        Assert.Contains(entering, me.Characters);
        Assert.Contains(existing[3], me.Trash);
        Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "OverflowTrash"));
    }
}
