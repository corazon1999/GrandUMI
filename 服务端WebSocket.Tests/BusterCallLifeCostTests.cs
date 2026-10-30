using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Actions;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class BusterCallLifeCostTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static GameState Scene(int owner, int lifeCount)
    {
        var state = TestScene.New("OP18-079", "OP18-079").Build();
        state.CurrentTurnPlayer = owner;
        foreach (var player in state.Players) player.MulliganDone = true;
        var me = state.Players[owner];
        me.Hand.Add(Card("OP18-117"));
        me.LifeArea.AddRange(Enumerable.Range(0, lifeCount).Select(_ => Card("OP15-003")));
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        me.Characters.Add(Card("OP18-098"));
        state.Players[1 - owner].Characters.Add(Card("OP18-104"));
        return state;
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 3, true)]
    [InlineData(1, 3, true)]
    public void 出牌校验合法动作与客户端快照都要求至少一张生命(int owner, int lifeCount, bool allowed)
    {
        var state = Scene(owner, lifeCount);
        var result = ActionValidator.CanPlayCard(state, owner, 0);
        Assert.Equal(allowed, result.Ok);
        if (!allowed) Assert.Contains("生命", result.Reason);
        var actions = LegalActionService.Enumerate(state, owner);
        Assert.Equal(allowed, actions.Candidates.Any(action => action.Action == "PlayCard"));
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, owner));
        Assert.Equal(allowed, snapshot.GetProperty("my").GetProperty("handCardCanPlay")[0].GetBoolean());
        Assert.Equal(10, state.Players[owner].ActiveDonCount);
        Assert.Single(state.Players[owner].Hand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 空生命直接结算也不会询问成本清场或登场(int owner)
    {
        var state = Scene(owner, 0);
        var me = state.Players[owner];
        var recruit = Card("OP18-102"); me.Deck.Add(recruit);
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, owner, me.Hand[0], EffectTrigger.EventMain, prompts);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
        Assert.All(state.Players, player => Assert.Single(player.Characters));
        Assert.Contains(recruit, me.Deck);
        Assert.Empty(me.Trash);
        Assert.Equal(10, me.ActiveDonCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    public async Task 有生命时支付全部生命后仍正常清场(int owner, int lifeCount)
    {
        var state = Scene(owner, lifeCount);
        var me = state.Players[owner];
        var lives = me.LifeArea.ToArray();
        var prompts = new MockPromptService().QueueChooseEmpty();
        await EffectRuntime.Resolve(state, owner, me.Hand[0], EffectTrigger.EventMain, prompts);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Empty(me.LifeArea);
        Assert.All(lives, life => Assert.Contains(life, me.Trash));
        Assert.All(state.Players, player => Assert.Empty(player.Characters));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 真实引擎拒绝空生命发动且不扣咚不弃事件不改变场面(int owner)
    {
        var engine = Engine(owner);
        var state = engine.State;
        var me = state.Players[owner];
        var source = me.Hand[0];
        Assert.False(engine.HandleAction(owner, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        await engine.WaitSettledAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(source, Assert.Single(me.Hand));
        Assert.Equal(10, me.ActiveDonCount);
        Assert.All(state.Players, player => { Assert.Single(player.Characters); Assert.Empty(player.Trash); });
        Assert.Null(state.PendingPrompt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 费用确认期间生命已消失时不会继续清场(int owner)
    {
        var engine = Engine(owner);
        var state = engine.State;
        var me = state.Players[owner];
        me.LifeArea.Add(Card("OP15-003"));
        Assert.True(engine.HandleAction(owner, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Contains("屠魔令", prompt.PromptText);
        me.LifeArea.Clear();
        engine.Prompts.Resolve(prompt.PromptId, ["0"]);
        await engine.WaitSettledAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(state.Players, player => Assert.Single(player.Characters));
        Assert.Null(state.PendingPrompt);
    }

    private static GameEngine Engine(int owner)
    {
        _ = TestScene.New().Build();
        var deck = "OP18-079\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("buster-call-empty-life", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: owner, rngSeed: 1);
        var state = engine.State;
        state.CurrentTurnPlayer = owner;
        state.Phase = Phase.Main;
        foreach (var player in state.Players)
        {
            player.MulliganDone = true;
            player.Hand.Clear(); player.LifeArea.Clear(); player.Characters.Clear(); player.Trash.Clear(); player.CostArea.Clear();
            player.Characters.Add(Card("OP18-098"));
        }
        var me = state.Players[owner];
        var source = Card("OP18-117"); me.Hand.Add(source);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        return engine;
    }
}
