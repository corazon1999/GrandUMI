using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月12日两渠道反馈的区域移动、领袖门槛和原本力量规则回归。</summary>
public sealed class PlayerFeedback20261012Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 全周日登场后能同时从墓地登场四费和一费原巴洛克角色()
    {
        var state = TestScene.New("OP18-041").MyHandAdd("OP14-084").Build();
        var me = state.Players[0];
        var source = me.Hand[0];
        var four = Card("OP16-056");
        var one = Card("OP09-056");
        me.Trash.AddRange([four, one]);
        var prompts = new MockPromptService().QueueChoose(four.Id.ToString()).QueueChoose(one.Id.ToString());

        await AtomicOps.PlayFromHandFree(state, 0, source);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Equal(new[] { source, four, one }, me.Characters);
        Assert.Empty(me.Trash);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.Empty(state.PendingEnterFields);
    }

    [Theory]
    [InlineData("巴洛克工作室", true)]
    [InlineData("原巴洛克工作室", true)]
    [InlineData("草帽一伙", false)]
    public async Task 全周日领袖门槛按特征包含匹配且不向非本家开放(string keyword, bool eligible)
    {
        var state = TestScene.New().Build();
        state.Players[0] = new PlayerState
        {
            SessionId = "s0", AccountName = "p0",
            Leader = new CardInstance { Info = new CardInfo
            {
                Number = "TEST-BAROQUE-LEADER", Name = "回归领袖", Kind = CardKind.Leader,
                Color = "蓝/黑", Property = "特", Power = 5000, Cost = 4, Keywords = [keyword],
            }},
        };
        var me = state.Players[0];
        var target = Card("OP16-056");
        me.Trash.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-084"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(eligible, me.Characters.Contains(target));
        Assert.Equal(!eligible, me.Trash.Contains(target));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task 全周日第二段只能登场费用恰为一的角色(int cost, bool eligible)
    {
        var state = TestScene.New("OP18-041").Build();
        var me = state.Players[0];
        var target = new CardInstance { Info = new CardInfo
        {
            Number = "TEST-BAROQUE-ONE", Name = "回归角色", Kind = CardKind.Character,
            Color = "黑", Property = "打", Power = 1000, Cost = cost, Keywords = ["巴洛克工作室"],
        }};
        me.Trash.Add(target);
        var prompts = new MockPromptService().QueueChooseEmpty().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-084"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(eligible, me.Characters.Contains(target));
        Assert.Equal(!eligible, me.Trash.Contains(target));
    }

    [Theory]
    [InlineData("OP14-107", true)]
    [InlineData("OP14-112", true)]
    [InlineData("OP14-116", true)]
    [InlineData("OP07-058", true)]
    [InlineData("OP15-003", false)]
    public async Task 玛格丽特可以检索两种特征的角色事件和舞台并保留余牌排序弃手链(string targetNumber, bool eligible)
    {
        var state = TestScene.New().MyDeckTop(targetNumber, "OP15-004", "OP15-005", "OP15-006", "OP15-007", "OP15-008")
            .MyHandAdd("OP15-009").Build();
        var me = state.Players[0];
        var target = me.Deck[0];
        var discarded = me.Hand[0];
        var remaining = me.Deck.Take(5).Where(card => !eligible || card != target).Reverse().ToArray();
        var unlooked = me.Deck[5];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString())
            .QueueChoose(remaining.Select(card => card.Id.ToString()).ToArray()).QueueChoose(discarded.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-113"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(eligible, me.Hand.Contains(target));
        Assert.Contains(discarded, me.Trash);
        Assert.Equal(new[] { unlooked }.Concat(remaining), me.Deck);
        Assert.Equal(eligible, prompts.ChooseHistory[0].choices.Contains(target.Id.ToString()));
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "ReorderToDeckBottom");
    }

    [Fact]
    public void P116罗宾规则颜色与本地黑色卡面一致()
    {
        _ = TestScene.New();
        var info = Card("P-116").Info;
        Assert.Equal("黑", info.Color);
        Assert.Equal(new[] { "黑" }, info.ColorList);
        Assert.True(info.SharesColorWith(Card("ST38-001").Info));
        Assert.False(info.SharesColorWith(Card("OP15-058").Info));
    }

    [Fact]
    public void 服务端路飞卡组规则接受逗号分隔的动物阿拉巴斯坦卡()
    {
        _ = TestScene.New();
        var leader = Card("ST37-001").Info;
        var scissors = Card("OP15-013").Info;
        Assert.True(scissors.HasKeyword("动物"));
        Assert.True(scissors.HasKeyword("阿拉巴斯坦王国"));
        var other = new[] { "OP13", "OP15", "OP18", "EB01", "EB02", "EB03", "EB04" }
            .SelectMany(CardDatabase.GetBySet)
            .Where(card => card.Kind != CardKind.Leader && card.Number != scissors.Number
                && card.Subscript != 1 && card.HasKeyword("阿拉巴斯坦王国") && card.SharesColorWith(leader))
            .SelectMany(card => Enumerable.Repeat(card.Number, 4)).Take(46).ToArray();
        Assert.Equal(46, other.Length);
        var deck = string.Join('\n', new[] { leader.Number }.Concat(Enumerable.Repeat(scissors.Number, 4)).Concat(other));
        var result = DeckValidator.Validate(deck, DeckValidator.FormatStandardRanked);
        Assert.True(result.Ok, result.Reason);
    }

    [Theory]
    [InlineData("OP07-098")]
    [InlineData("OP07-100")]
    [InlineData("OP07-101")]
    [InlineData("OP07-105")]
    [InlineData("OP07-110")]
    [InlineData("OP07-111")]
    [InlineData("EB03-058")]
    public async Task 艾格赫德科学家只有贝加班克领袖时才从生命触发登场(string number)
    {
        foreach (bool validLeader in new[] { false, true })
        {
            var state = TestScene.New(validLeader ? "OP07-097" : "OP15-001").Build();
            var me = state.Players[0];
            var source = Card(number);
            me.Trash.Add(source);
            await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger,
                new MockPromptService().QueueConfirm(false));
            Assert.Equal(validLeader, me.Characters.Contains(source));
            Assert.Equal(!validLeader, me.Trash.Contains(source));
        }
    }

    [Theory]
    [InlineData("OP07-097", true)]
    [InlineData("OP15-001", false)]
    public async Task 实际生命伤害入口不会绕过科学家的领袖条件(string leader, bool eligible)
    {
        _ = TestScene.New();
        string deck = leader + "\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("feedback-life-condition", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 1);
        var me = engine.State.Players[0];
        me.LifeArea.Clear();
        me.Hand.Clear();
        me.Characters.Clear();
        var source = Card("OP07-098");
        me.LifeArea.Add(source);
        var resolution = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var pending = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal("LifeTrigger", pending.Kind);
        engine.Prompts.Resolve(pending.PromptId, new[] { "trigger" });
        await resolution.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(eligible, me.Characters.Contains(source));
        Assert.Equal(!eligible, me.Trash.Contains(source));
        Assert.DoesNotContain(source, me.Hand);
        Assert.Null(engine.State.PendingPrompt);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task 布玲保护随大妈八千原本力量光环和双方回合动态变化(int turn, bool guarded)
    {
        var state = TestScene.New().MyHandAdd("OP17-112").MyCharacter("OP17-103")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005").Build();
        var me = state.Players[0];
        var linlin = me.Hand[0];
        var target = me.Characters[0];
        Assert.Equal(4000, target.Info.Power);
        Assert.True(target.Info.HasKeyword("大妈海盗团"));
        Assert.NotEmpty(target.Info.Trigger);
        await AtomicOps.PlayFromHandFree(state, 0, linlin);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, Card("EB05-059"), EffectTrigger.EventMain, new MockPromptService());
        state.CurrentTurnPlayer = turn;
        Assert.Equal(turn == 0 ? 8000 : 4000, state.OriginalPowerOf(0, target));
        Assert.Equal(guarded, state.IsKoGuarded(target, "battle"));
        Assert.Equal(!guarded, await BattleEngine.KOCardAsync(state, 0, target, new MockPromptService()));
        Assert.Equal(guarded, me.Characters.Contains(target));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task 女帝领袖KO条件读取离场前被大妈改写的原本力量(bool changedToZero, bool triggers)
    {
        var state = TestScene.New("OP14-041").MyCharacter("OP14-112").AttachDonToMyLeader(1).Build();
        var me = state.Players[0];
        var target = me.Characters[0];
        var life = Card("OP15-003");
        state.Players[1].LifeArea.Add(life);
        target.PowerModThisTurn = -10000;
        if (changedToZero) target.OriginalPowerOverride = 0;

        await BattleEngine.KOCardAsync(state, 0, target, new MockPromptService().QueueConfirm(true));

        Assert.Contains(target, me.Trash);
        Assert.Equal(triggers, state.Players[1].Hand.Contains(life));
        Assert.Equal(!triggers, state.Players[1].LifeArea.Contains(life));
    }

    [Fact]
    public async Task ST34大妈完整成本链将原本力量改为零且回合末恢复()
    {
        var state = TestScene.New(oppLeaderNumber: "OP14-041").MyHandAdd("ST34-004")
            .MyHandAdd("OP15-003").MyActiveDon(4).OppCharacter("OP14-112").Build();
        var me = state.Players[0];
        var source = me.Hand[0];
        var discarded = me.Hand[1];
        var target = state.Players[1].Characters[0];
        var don = me.CostArea.ToArray();
        var prompts = new MockPromptService().QueueChoose(discarded.Id.ToString())
            .QueueChoose(don.Select(card => card.Id.ToString()).ToArray()).QueueChoose(target.Id.ToString());

        await AtomicOps.PlayFromHandFree(state, 0, source);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);

        Assert.Contains(discarded, me.Trash);
        Assert.Empty(me.CostArea);
        Assert.Equal(4, me.DonDeck.Count);
        Assert.Equal(0, state.OriginalPowerOf(1, target));
        Assert.Equal(0, state.CurrentPowerOf(1, target));
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(10000, state.OriginalPowerOf(1, target));
        Assert.Equal(10000, state.CurrentPowerOf(1, target));
    }

    [Theory]
    [InlineData("OP15-058")]
    [InlineData("OP05-098")]
    public async Task 放电通过真实零费反击出牌及目标响应入口给艾尼路领袖加两千(string leader)
    {
        _ = TestScene.New();
        string deck = leader + "\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 20));
        var engine = new GameEngine("feedback-discharge-counter", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 1);
        var state = engine.State;
        var me = state.Players[0];
        me.Hand.Clear();
        me.CostArea.Clear();
        var source = Card("OP15-074");
        me.Hand.Add(source);
        state.CurrentTurnPlayer = 1;
        state.Phase = Phase.BattleCounter;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id, TargetIsLeader = true,
        };
        Assert.True(ActionValidator.CanPlayCounter(state, 0, 0, false).Ok);
        Assert.False(ActionValidator.CanPlayCounter(state, 0, 0, true).Ok);

        Assert.True(engine.HandleAction(0, "PlayCounter", JsonSerializer.SerializeToElement(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        var pending = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("OwnLeaderOrCharacter", pending.Kind);
        Assert.Contains(me.Leader.Id.ToString(), pending.ValidChoices);
        Assert.True(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = pending.PromptId, chosen = new[] { me.Leader.Id.ToString() },
        })));
        await engine.WaitSettledAsync();

        Assert.Empty(me.Hand);
        Assert.Contains(source, me.Trash);
        Assert.Empty(me.CostArea);
        Assert.Equal(2000, me.Leader.PowerModThisBattle);
        Assert.Equal(7000, state.CurrentPowerOf(0, me.Leader));
        BattleEngine.EndBattle(state);
        Assert.Equal(5000, state.CurrentPowerOf(0, me.Leader));
    }
}
