using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OP18UpdatedEffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };
    private static CardInstance Custom(string number, int power, int cost, CardKind kind = CardKind.Character,
        params string[] keywords) => new()
    {
        Info = new CardInfo { Number = number, Name = number, Color = "红", Kind = kind,
            Property = "知", Power = power, Cost = cost, Keywords = keywords },
    };
    private static void FillDeck(PlayerState player, int count = 4)
        => player.Deck.AddRange(Enumerable.Range(0, count).Select(_ => Card("OP18-112")));

    [Fact]
    public async Task 卡鲁仅加成我方双特征角色且不限我方回合()
    {
        var state = TestScene.New("OP18-001", "OP18-022").Build();
        var both = Card("OP18-003");
        var onlyAnimal = Card("OP18-025");
        var enemy = Card("OP18-003");
        state.Players[0].Characters.AddRange([both, onlyAnimal]);
        state.Players[1].Characters.Add(enemy);
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        state.CurrentTurnPlayer = 1;
        Assert.Equal(7000, state.CurrentPowerOf(0, both));
        Assert.True(ActionValidator.HasKeyword(state, both, "速攻"));
        Assert.False(ActionValidator.HasKeyword(state, onlyAnimal, "速攻"));
        Assert.False(ActionValidator.HasKeyword(state, enemy, "速攻"));
        Assert.Equal(6000, state.CurrentPowerOf(1, enemy));
    }

    [Fact]
    public void 大猿王在合法窗口可阻挡且休息后不可再阻挡()
    {
        var state = TestScene.New().Build();
        var defender = state.Players[0];
        var blocker = Card("OP18-112");
        defender.Characters.Add(blocker);
        state.CurrentTurnPlayer = 1;
        state.Phase = Phase.BattleBlock;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = true,
        };

        Assert.True(ActionValidator.HasKeyword(state, blocker, "阻挡者"));
        Assert.True(ActionValidator.CanDeclareBlocker(state, 0, blocker.Id).Ok);
        BattleEngine.DeclareBlocker(state, blocker.Id);
        Assert.True(blocker.IsTapped);
        Assert.Equal(blocker.Id, state.CurrentBattle.TargetCardId);

        state.Phase = Phase.BattleBlock;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = true,
        };
        var rejected = ActionValidator.CanDeclareBlocker(state, 0, blocker.Id);
        Assert.False(rejected.Ok);
        Assert.Contains("活跃状态", rejected.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 同一次KO由玩家选择先抽牌还是先登场(bool drawFirst)
    {
        var state = TestScene.New("OP18-001", "OP18-022").Build();
        var me = state.Players[0];
        var victim = Card("OP18-016");
        var target = Card("OP18-003");
        me.Characters.Add(victim);
        if (drawFirst) me.Deck.Add(target); else me.Hand.Add(target);
        FillDeck(me);
        var prompts = new MockPromptService()
            .QueueChoose((drawFirst ? me.Leader : victim).Id.ToString())
            .QueueChoose(target.Id.ToString());
        Assert.True(await BattleEngine.KOCardAsync(state, 0, victim, prompts));
        Assert.Contains(target, me.Characters);
        Assert.Contains(victim, me.Trash);
        Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "EffectOrder"));
        Assert.Contains($"OP18-001-ko:{me.Leader.Id}", me.TurnOnceUsed);
        var another = Card("OP18-003");
        me.Characters.Add(another);
        int handCount = me.Hand.Count;
        await AtomicOps.KOByEffectAsync(state, 0, another, new MockPromptService(), 1);
        Assert.Equal(handCount, me.Hand.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 效果KO及延迟KO同样允许领袖联动先于自身效果(bool deferred)
    {
        var state = TestScene.New("OP18-001", "OP18-022").Build();
        var me = state.Players[0];
        var victim = Card("OP18-016");
        var target = Card("OP18-003");
        me.Characters.Add(victim);
        me.Deck.Add(target);
        FillDeck(me);
        var prompts = new MockPromptService().QueueChoose(me.Leader.Id.ToString()).QueueChoose(target.Id.ToString());
        Assert.True(await AtomicOps.KOByEffectAsync(state, 0, victim, prompts, 1, deferOnKO: deferred));
        if (deferred) await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Contains(target, me.Characters);
        Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "EffectOrder"));
        Assert.Empty(state.PendingKOEffects);
        Assert.Empty(state.PendingWatchers);
    }

    [Theory]
    [InlineData(2999, 10000, false)]
    [InlineData(3000, -2000, true)]
    public async Task 全周日以原本力量判定且由对方选择弃牌(int power, int modifier, bool triggers)
    {
        var state = TestScene.New("OP18-041", "OP18-022").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var victim = Custom("TEST-BAROQUE", power, 2, CardKind.Character, "巴洛克工作室");
        victim.PowerModThisTurn = modifier;
        me.Characters.Add(victim);
        FillDeck(me);
        var kept = Card("OP18-003");
        var discarded = Card("OP18-112");
        opponent.Hand.AddRange([kept, discarded]);
        var prompts = new MockPromptService().QueueChoose(discarded.Id.ToString());
        await BattleEngine.KOCardAsync(state, 0, victim, prompts);
        Assert.Equal(triggers ? 1 : 0, me.Hand.Count);
        Assert.Equal(triggers, opponent.Trash.Contains(discarded));
        Assert.Contains(kept, opponent.Hand);
    }

    [Fact]
    public async Task 路飞KO时只允许符合力量及特征的手牌角色()
    {
        var state = TestScene.New("OP18-022").Build();
        var me = state.Players[0];
        var source = Card("OP18-016");
        var low = Card("OP18-003");
        var tooStrong = Card("OP18-016");
        var wrongFeature = Card("OP18-112");
        me.Hand.AddRange([low, tooStrong, wrongFeature]);
        var prompts = new MockPromptService().QueueChoose(low.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);
        Assert.Contains(low, me.Characters);
        Assert.Contains(tooStrong, me.Hand);
        Assert.Contains(wrongFeature, me.Hand);
        Assert.Single(prompts.ChooseHistory[0].choices);
        Assert.True(ActionValidator.HasKeyword(state, source, "流放"));
    }

    [Theory]
    [InlineData(2, true, false)]
    [InlineData(3, false, false)]
    [InlineData(3, true, true)]
    public async Task 路飞重置要求三咚且跳过标记出现在重连快照并只消费一次(int donCount, bool accept, bool activated)
    {
        var state = TestScene.New("OP18-022").Build();
        var me = state.Players[0];
        me.Leader.IsTapped = true;
        me.CostArea.AddRange(Enumerable.Range(0, donCount).Select(_ => new DonCard
        { State = DonState.Attached, AttachedToCardId = me.Leader.Id }));
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueConfirm(accept));
        Assert.Equal(!activated, me.Leader.IsTapped);
        Assert.Equal(activated, me.Leader.CannotActivateNextReset);
        if (!activated) return;
        me.Leader.IsTapped = true;
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, new MockPromptService());
        Assert.True(me.Leader.IsTapped);
        var snapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.True(snapshot.GetProperty("players")[0].GetProperty("leader").GetProperty("cannotActivateNextReset").GetBoolean());
        TurnEngine.EnterResetPhase(state);
        Assert.True(me.Leader.IsTapped);
        Assert.False(me.Leader.CannotActivateNextReset);
        TurnEngine.EnterResetPhase(state);
        Assert.False(me.Leader.IsTapped);
    }

    [Fact]
    public async Task 路飞二次攻击与跨回合跳过重置可由动作日志确定性重建()
    {
        const string roomId = "op18-022-replay";
        const int seed = 18022;
        string deck = "OP18-022\n" + string.Join('\n', Enumerable.Repeat("OP18-003", 20));
        var live = new GameEngine(
            roomId,
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: seed);
        var tape = new List<MatchReplay.ActionEntry>();

        async Task Apply(int playerIndex, string action, object data)
        {
            var payload = JsonSerializer.SerializeToElement(data);
            Assert.True(live.HandleAction(playerIndex, action, payload), $"实时动作被拒绝：{action}");
            tape.Add(new MatchReplay.ActionEntry(playerIndex, action, payload.Clone()));
            await live.WaitSettledAsync();
        }

        await Apply(0, "Mulligan", new { redraw = false });
        await Apply(1, "Mulligan", new { redraw = false });
        await Apply(0, "EndTurn", new { });
        await Apply(1, "EndTurn", new { });
        Assert.Equal(3, live.State.Players[0].ActiveDonCount);

        await Apply(0, "AttachDon", new { targetId = "leader", count = 3 });
        string leaderId = live.State.Players[0].Leader.Id.ToString();
        await Apply(0, "Attack", new { attackerId = leaderId, targetIsLeader = true });
        var confirm = Assert.IsType<PendingPrompt>(live.State.PendingPrompt);
        Assert.Equal("Option", confirm.Kind);
        await Apply(0, "PromptResponse", new { promptId = confirm.PromptId, chosen = new[] { "0" } });
        await Apply(1, "PassCounter", new { });

        Assert.False(live.State.Players[0].Leader.IsTapped);
        Assert.True(live.State.Players[0].Leader.CannotActivateNextReset);
        await Apply(0, "Attack", new { attackerId = leaderId, targetIsLeader = true });
        Assert.Null(live.State.PendingPrompt);
        await Apply(1, "PassCounter", new { });
        Assert.True(live.State.Players[0].Leader.IsTapped);

        await Apply(0, "EndTurn", new { });
        await Apply(1, "EndTurn", new { });
        Assert.True(live.State.Players[0].Leader.IsTapped);
        Assert.False(live.State.Players[0].Leader.CannotActivateNextReset);

        var rebuilt = await MatchReplay.RebuildAsync(
            roomId,
            seed,
            0,
            ("alice", deck),
            ("bob", deck),
            tape);

        Assert.Equal(
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(live.State)),
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(rebuilt.State)));
        Assert.True(rebuilt.State.Players[0].Leader.IsTapped);
        Assert.False(rebuilt.State.Players[0].Leader.CannotActivateNextReset);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task 昆平仅在自身休息时于我方回合结束重置领袖(bool rested, bool activates)
    {
        var state = TestScene.New("OP18-022").Build();
        var source = Card("OP18-025");
        source.IsTapped = rested;
        state.Players[0].Characters.Add(source);
        state.Players[0].Leader.IsTapped = true;
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnMyTurnEnd, new MockPromptService());
        Assert.Equal(!activates, state.Players[0].Leader.IsTapped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 奇姆尼完整支付两张角色休息成本或取消不支付(bool cancel)
    {
        var state = TestScene.New("OP18-022").Build();
        var me = state.Players[0];
        var source = Card("OP18-028");
        var gonbe = Card("OP18-025");
        me.Characters.AddRange([source, gonbe]);
        me.Leader.IsTapped = true;
        var prompts = new MockPromptService();
        if (cancel) prompts.QueueChooseEmpty(); else prompts.QueueChoose(gonbe.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(!cancel, source.IsTapped);
        Assert.Equal(!cancel, gonbe.IsTapped);
        Assert.Equal(cancel, me.Leader.IsTapped);
    }

    [Fact]
    public async Task 宾斯凯瑟琳娜抽弃且额外咚加成随附着及整卡无效变化()
    {
        var state = TestScene.New("OP18-022").Build();
        var me = state.Players[0];
        var source = Card("OP18-044");
        me.Characters.Add(source);
        var oldHand = Card("OP18-003");
        me.Hand.Add(oldHand);
        FillDeck(me);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(oldHand.Id.ToString()));
        Assert.Contains(oldHand, me.Trash);
        Assert.Single(me.Hand);
        Assert.Equal(4000, state.CurrentPowerOf(0, source));
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = source.Id });
        Assert.Equal(6000, state.CurrentPowerOf(0, source));
        source.IsEffectsNullified = true;
        Assert.Equal(5000, state.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task 不吉利二人组不能把自身作为成本且对方自己选择弃牌()
    {
        var state = TestScene.New("OP18-022").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("OP18-056");
        var other = Card("OP18-044");
        me.Characters.AddRange([source, other]);
        var discard = Card("OP18-112");
        opponent.Hand.Add(discard);
        var prompts = new MockPromptService().QueueChoose(other.Id.ToString()).QueueChoose(discard.Id.ToString());
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(source, me.Characters);
        Assert.Same(other, me.Deck.Last());
        Assert.DoesNotContain(source.Id.ToString(), prompts.ChooseHistory[0].choices);
        Assert.Contains(discard, opponent.Trash);
    }

    [Fact]
    public async Task 赞巴KO舞台支付成本但不会误触角色KO监听()
    {
        var state = TestScene.New("OP18-001").Build();
        var me = state.Players[0];
        FillDeck(me);
        var source = Card("OP18-066");
        me.Characters.Add(source);
        var stage = Custom("TEST-STAGE", 0, 5, CardKind.Stage, "阿拉巴斯坦王国");
        me.StageCard = stage;
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(stage.Id.ToString()));
        Assert.Null(me.StageCard);
        Assert.Contains(stage, me.Trash);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.Empty(me.Hand);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("capacity")]
    public async Task 潜水艇登场先抽牌且可选追加休息咚(string mode)
    {
        var state = TestScene.New("OP18-022").Build();
        var me = state.Players[0];
        var source = Card("OP18-076");
        me.StageCard = source;
        FillDeck(me);
        var drawn = me.Deck[0];
        var don = new DonCard { State = DonState.Active };
        me.DonDeck.Add(don);
        if (mode == "capacity")
            me.CostArea.AddRange(Enumerable.Range(0, TurnEngine.MaxDonInCostArea)
                .Select(_ => new DonCard { State = DonState.Rest }));
        var prompts = new MockPromptService().QueueOption(mode == "accept" ? 1 : 0);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(drawn, me.Hand);
        Assert.Equal(mode == "accept", me.CostArea.Contains(don));
        Assert.Equal(mode != "accept", me.DonDeck.Contains(don));
        if (mode == "accept") Assert.Equal(DonState.Rest, don.State);
        Assert.Equal(mode == "capacity" ? 0 : 1, prompts.OptionHistory.Count);
        Assert.False(source.IsTapped);
    }

    [Theory]
    [InlineData(0, 6000)]
    [InlineData(1, 6000)]
    public async Task 潜水艇角色不受力量门槛限制但领袖受门槛限制(int attachedDon, int expectedLeader)
    {
        var state = TestScene.New("OP18-022").Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var source = Card("OP18-076");
        me.StageCard = source;
        var high = Card("OP18-016");
        var low = Card("OP18-025");
        var unchosen = Card("OP18-003");
        me.Characters.AddRange([high, low, unchosen]);
        if (attachedDon == 1) me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(high.Id.ToString(), low.Id.ToString()));
        Assert.True(source.IsTapped);
        Assert.Equal(8000, state.CurrentPowerOf(0, high));
        Assert.Equal(1000, state.CurrentPowerOf(0, low));
        Assert.Equal(6000, state.CurrentPowerOf(0, unchosen));
        Assert.Equal(expectedLeader, state.CurrentPowerOf(0, me.Leader));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("skip-life")]
    [InlineData("cancel")]
    [InlineData("stale")]
    public async Task 斯巴达姆复合成本成功或零修改(string mode)
    {
        var state = TestScene.New("OP18-079").Build();
        var me = state.Players[0];
        var cp = Card("OP18-079");
        me.Hand.Add(cp);
        FillDeck(me);
        var top = me.Deck[0];
        var first = new DonCard { State = DonState.Active };
        var second = new DonCard { State = DonState.Active };
        me.CostArea.AddRange([first, second]);
        var prompts = new MockPromptService();
        if (mode == "cancel") prompts.QueueChooseEmpty(); else prompts.QueueChoose(cp.Id.ToString());
        if (mode == "success") prompts.QueueOption(1);
        if (mode == "stale") prompts.OnChooseResponse = _ => second.State = DonState.Rest;
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);
        bool paid = mode is "success" or "skip-life";
        Assert.Equal(paid, me.Leader.IsTapped);
        Assert.Equal(paid ? DonState.Rest : DonState.Active, first.State);
        Assert.Equal(paid, me.Trash.Contains(cp));
        Assert.Equal(!paid, me.Hand.Contains(cp));
        Assert.Equal(mode == "success", me.LifeArea.Contains(top));
        Assert.Equal(mode != "success", me.Deck.Contains(top));
    }

    [Fact]
    public async Task 戈尔德巴古持续加费用并在KO时只选择低费用对方角色()
    {
        var state = TestScene.New("OP18-022", "OP18-022").Build();
        var source = Card("OP18-086");
        state.Players[0].Characters.Add(source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(15, state.CurrentCostOf(source));
        source.IsEffectsNullified = true;
        Assert.Equal(3, state.CurrentCostOf(source));
        source.IsEffectsNullified = false;
        var low = Custom("TEST-LOW", 2000, 4);
        var high = Custom("TEST-HIGH", 2000, 5);
        state.Players[1].Characters.AddRange([low, high]);
        var prompts = new MockPromptService().QueueChoose(low.Id.ToString());
        await BattleEngine.KOCardAsync(state, 0, source, prompts);
        Assert.Contains(low, state.Players[1].Trash);
        Assert.Contains(high, state.Players[1].Characters);
        Assert.Single(prompts.ChooseHistory[0].choices);
        Assert.DoesNotContain(state.ContinuousEffects, effect => effect.SourceCardId == source.Id.ToString());
    }
}
