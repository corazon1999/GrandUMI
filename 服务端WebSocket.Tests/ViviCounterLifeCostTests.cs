using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Actions;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ViviCounterLifeCostTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static GameEngine Engine(int owner, int lifeCount, bool blocked = false)
    {
        _ = TestScene.New().Build();
        var deck = "OP04-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("vivi-counter-life-cost", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 1 - owner, rngSeed: 1);
        var state = engine.State;
        state.CurrentTurnPlayer = 1 - owner;
        state.Phase = Phase.BattleCounter;
        foreach (var player in state.Players)
        {
            player.MulliganDone = true;
            player.Hand.Clear(); player.LifeArea.Clear(); player.Characters.Clear(); player.Trash.Clear(); player.CostArea.Clear();
            player.Characters.Add(Card("OP15-003"));
        }
        var me = state.Players[owner];
        me.Hand.Add(Card("OP18-020"));
        me.CostArea.Add(new DonCard { State = DonState.Active });
        me.LifeArea.AddRange(Enumerable.Range(0, lifeCount).Select(_ => Card("OP15-004")));
        if (blocked) state.NoEffectLifeToHandThisTurn.Add(owner);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1 - owner,
            AttackerCardId = state.Players[1 - owner].Leader.Id,
            DefenderPlayerIndex = owner,
            TargetIsLeader = true,
        };
        return engine;
    }

    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(1, 0, false, false)]
    [InlineData(0, 1, false, true)]
    [InlineData(1, 1, false, true)]
    [InlineData(0, 3, false, true)]
    [InlineData(1, 3, false, true)]
    [InlineData(0, 1, true, false)]
    [InlineData(1, 1, true, false)]
    public void 反击准入合法动作与客户端权威标记一致(int owner, int lifeCount, bool blocked, bool allowed)
    {
        var state = Engine(owner, lifeCount, blocked).State;
        var result = ActionValidator.CanPlayCounter(state, owner, 0, useCounterIcon: false);
        Assert.Equal(allowed, result.Ok);
        if (!allowed) Assert.Contains("生命", result.Reason);
        Assert.Equal(allowed, LegalActionService.Enumerate(state, owner).Candidates.Any(action => action.Action == "PlayCounter"));
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, owner));
        Assert.Equal(allowed, snapshot.GetProperty("my").GetProperty("handCardCanCounter")[0].GetBoolean());
        Assert.Empty(snapshot.GetProperty("opponent").GetProperty("handCardCanCounter").EnumerateArray());
        Assert.Equal(1, state.Players[owner].ActiveDonCount);
        Assert.Single(state.Players[owner].Hand);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]
    public async Task 无法支付生命时真实反击请求和直接结算都不能减力量(int owner, int lifeCount, bool blocked)
    {
        var engine = Engine(owner, lifeCount, blocked);
        var state = engine.State;
        var me = state.Players[owner];
        var source = me.Hand[0];
        Assert.False(engine.HandleAction(owner, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0, useCounterIcon = false })));
        await engine.WaitSettledAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(source, Assert.Single(me.Hand));
        Assert.Equal(1, me.ActiveDonCount);
        Assert.Empty(me.Trash);
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.EventCounter, prompts);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Equal(lifeCount, me.LifeArea.Count);
        Assert.Equal(0, state.Players[1 - owner].Leader.PowerModThisTurn);
        Assert.Equal(0, state.Players[1 - owner].Characters[0].PowerModThisTurn);
        Assert.Null(state.PendingPrompt);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    public async Task 有生命时全部加入手牌并降低对方领袖与角色本回合力量(int owner, int lifeCount)
    {
        var state = Engine(owner, lifeCount).State;
        var me = state.Players[owner];
        var lives = me.LifeArea.ToArray();
        await EffectRuntime.Resolve(state, owner, me.Hand[0], EffectTrigger.EventCounter, new MockPromptService());
        Assert.Empty(me.LifeArea);
        Assert.All(lives, life => Assert.Contains(life, me.Hand));
        Assert.Equal(-5000, state.Players[1 - owner].Leader.PowerModThisTurn);
        Assert.Equal(-5000, state.Players[1 - owner].Characters[0].PowerModThisTurn);
        Assert.Equal(0, me.Leader.PowerModThisTurn);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task 确认期间生命消失或出现入手限制不会继续结算(int owner, bool blocked)
    {
        var engine = Engine(owner, 1);
        var state = engine.State;
        Assert.True(engine.HandleAction(owner, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0, useCounterIcon = false })));
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        if (blocked) state.NoEffectLifeToHandThisTurn.Add(owner);
        else state.Players[owner].LifeArea.Clear();
        engine.Prompts.Resolve(prompt.PromptId, ["0"]);
        await engine.WaitSettledAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, state.Players[1 - owner].Leader.PowerModThisTurn);
        Assert.Equal(0, state.Players[1 - owner].Characters[0].PowerModThisTurn);
        Assert.Equal(blocked ? 1 : 0, state.Players[owner].LifeArea.Count);
        Assert.Null(state.PendingPrompt);
    }
}
