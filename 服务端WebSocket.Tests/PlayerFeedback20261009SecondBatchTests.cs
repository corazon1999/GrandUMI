using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>机器人反馈 #189–#194 的费用选择、舞台成本和事件类型回归。</summary>
public sealed class PlayerFeedback20261009SecondBatchTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static GameEngine Engine()
    {
        _ = TestScene.New();
        var deck = "OP12-041\n" + string.Join('\n', Enumerable.Repeat("OP14-013", 50));
        var engine = new GameEngine("第二批反馈", ("s0", "我方", deck), ("s1", "对方", deck), 0, 99);
        engine.State.OpeningStage = OpeningStage.Playing;
        engine.State.Phase = Phase.Main;
        engine.State.CurrentTurnPlayer = 0;
        foreach (var player in engine.State.Players)
        {
            player.MulliganDone = true;
            player.Hand.Clear();
            player.CostArea.Clear();
            player.Characters.Clear();
        }
        return engine;
    }

    private static async Task<PendingPrompt> Prompt(GameEngine engine, string kind)
    {
        for (var i = 0; i < 200; i++)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
            await Task.Delay(10);
        }
        throw new TimeoutException($"未收到预期的 {kind} 提示");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 马林梵多休息后在动作入口拒绝且不再归还咚(bool extraStage)
    {
        var state = TestScene.New().MyActiveDon(3).MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var stage = Card("OP16-078");
        if (extraStage) me.ExtraStageCard = stage; else me.StageCard = stage;
        Assert.True(ActionValidator.CanUseEffect(state, 0, stage.Id).Ok);
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.True(stage.IsTapped);
        Assert.Equal(2, me.TotalDonInCostArea);
        Assert.False(ActionValidator.CanUseEffect(state, 0, stage.Id).Ok);
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        if (!extraStage) Assert.False(snapshot.GetProperty("my").GetProperty("stageCanActivateEffect").GetBoolean());
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(2, me.TotalDonInCostArea);
        Assert.Empty(prompts.ChooseHistory);
        // 卡面没有“每回合1次”；合法恢复活跃后可以再次支付完整成本。
        stage.IsTapped = false;
        Assert.True(ActionValidator.CanUseEffect(state, 0, stage.Id).Ok);
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Equal(1, me.TotalDonInCostArea);
        Assert.True(stage.IsTapped);
    }

    [Fact]
    public async Task 马林梵多不能休息时不能先归还咚()
    {
        var state = TestScene.New().MyActiveDon(2).MyDeckTop("OP15-003").Build();
        var stage = Card("OP16-078");
        state.Players[0].StageCard = stage;
        AtomicOps.AddRestriction(stage, RestrictionKind.CannotBeRested, KeywordDuration.ThisTurn);
        Assert.False(ActionValidator.CanUseEffect(state, 0, stage.Id).Ok);
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(2, state.Players[0].TotalDonInCostArea);
        Assert.Single(state.Players[0].Deck);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 马林梵多取消归还咚不横置舞台不抽弃()
    {
        var state = TestScene.New().MyActiveDon(2).MyDeckTop("OP15-003").Build();
        var stage = Card("OP16-078");
        state.Players[0].StageCard = stage;
        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChooseEmpty());
        Assert.False(stage.IsTapped);
        Assert.Equal(2, state.Players[0].TotalDonInCostArea);
        Assert.Single(state.Players[0].Deck);
        Assert.Empty(state.Players[0].Hand);
    }

    [Fact]
    public async Task 浸食轮回选择额外咚前持续等待且只横置玩家选中的两张()
    {
        var engine = Engine();
        var me = engine.State.Players[0];
        var target = Card("OP15-003");
        engine.State.Players[1].Characters.Add(target);
        me.Hand.Add(Card("OP14-096"));
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Active }));
        engine.HandleAction(0, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 }));
        var confirm = await Prompt(engine, "Option");
        engine.Prompts.Resolve(confirm.PromptId, new[] { "0" });
        var cost = await Prompt(engine, "RestOwnDon");
        Assert.Equal(2, cost.MinChoose);
        Assert.Equal(2, cost.MaxChoose);
        Assert.True(cost.Extra.ContainsKey("donChoices"));
        await Task.Delay(1200);
        Assert.Same(cost, engine.State.PendingPrompt);
        Assert.Equal(3, me.ActiveDonCount);
        Assert.False(target.IsEffectsNullified);
        var selected = me.CostArea.Where(don => don.State == DonState.Active).Skip(1).ToArray();
        foreach (var invalid in new[]
        {
            new[] { selected[0].Id.ToString() },
            new[] { selected[0].Id.ToString(), selected[0].Id.ToString() },
            new[] { selected[0].Id.ToString(), Guid.NewGuid().ToString() },
        })
        {
            engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new { promptId = cost.PromptId, chosen = invalid }));
            Assert.Same(cost, engine.State.PendingPrompt);
            Assert.Equal(3, me.ActiveDonCount);
        }
        engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = cost.PromptId, chosen = selected.Select(don => don.Id.ToString()).ToArray(),
        }));
        var chooseTarget = await Prompt(engine, "OpponentCharacterCostLe5");
        Assert.Equal(1, me.ActiveDonCount);
        Assert.All(selected, don => Assert.Equal(DonState.Rest, don.State));
        engine.Prompts.Resolve(chooseTarget.PromptId, new[] { target.Id.ToString() });
        for (var i = 0; i < 200 && !target.IsEffectsNullified; i++) await Task.Delay(10);
        Assert.True(target.IsEffectsNullified);
        Assert.Contains(me.Trash, card => card.Info.Number == "OP14-096");
        Assert.Empty(me.Characters);
    }

    [Fact]
    public async Task 浸食轮回取消额外成本保留活跃咚且不无效目标()
    {
        var state = TestScene.New().MyActiveDon(3).OppCharacter("OP15-003").Build();
        await EffectRuntime.Resolve(state, 0, Card("OP14-096"), EffectTrigger.EventMain,
            new MockPromptService().QueueConfirm(false));
        Assert.Equal(3, state.Players[0].ActiveDonCount);
        Assert.False(state.Players[1].Characters[0].IsEffectsNullified);
    }

    [Fact]
    public async Task 白线盾按两费反击事件结算且不会登场角色()
    {
        var engine = Engine();
        var state = engine.State;
        var me = state.Players[0];
        var shield = Card("EB01-019");
        Assert.Equal(CardKind.Event, shield.Info.Kind);
        me.Hand.Add(shield);
        me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ => new DonCard { State = DonState.Active }));
        Assert.False(ActionValidator.CanPlayCard(state, 0, 0).Ok);
        me.Deck.Clear();
        var searched = Card("OP10-065");
        var other = new[] { Card("OP15-003"), Card("OP15-004") };
        me.Deck.Add(searched);
        me.Deck.AddRange(other);
        state.Phase = Phase.BattleCounter;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1, AttackerCardId = state.Players[1].Leader.Id,
            DefenderPlayerIndex = 0, TargetIsLeader = true,
        };
        Assert.True(ActionValidator.CanPlayCounter(state, 0, 0, false).Ok);
        engine.HandleAction(0, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0, useCounterIcon = false }));
        var buff = await Prompt(engine, "OwnLeaderOrCharacter");
        engine.Prompts.Resolve(buff.PromptId, new[] { me.Leader.Id.ToString() });
        var search = await Prompt(engine, "LookTopReveal");
        engine.Prompts.Resolve(search.PromptId, new[] { searched.Id.ToString() });
        var order = await Prompt(engine, "ReorderToDeckBottom");
        engine.Prompts.Resolve(order.PromptId, other.Reverse().Select(card => card.Id.ToString()).ToArray());
        for (var i = 0; i < 200 && me.Deck.Count != other.Length; i++) await Task.Delay(10);
        Assert.Contains(searched, me.Hand);
        Assert.Contains(shield, me.Trash);
        Assert.Empty(me.Characters);
        Assert.Equal(0, me.ActiveDonCount);
        Assert.Equal(4000, me.Leader.PowerModThisBattle);
        Assert.Equal(other.Reverse(), me.Deck);
    }
}
