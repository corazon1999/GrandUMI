using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Scripted;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ST37ST38LeaderTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static GameState Scene(string leaderNumber)
    {
        var state = TestScene.New(leaderNumber).Build();
        foreach (var player in state.Players)
            player.Deck.AddRange(Enumerable.Range(0, 20).Select(_ => Card("OP15-004")));
        return state;
    }

    [Theory]
    [InlineData("ST37-001", "红/蓝", 3)]
    [InlineData("ST38-001", "蓝/黑", 4)]
    public void 新预组领航资料与中文卡面一致(string number, string color, int life)
    {
        _ = TestScene.New();
        var info = CardDatabase.Get(number)!;
        Assert.Equal(CardKind.Leader, info.Kind);
        Assert.Equal(color, info.Color);
        Assert.Equal(5000, info.Power);
        Assert.Equal(life, info.Cost);
        Assert.Equal(5, info.Subscript);
        Assert.Equal(CardPlayability.Playable, info.Playability);
    }

    [Fact]
    public async Task 路飞每次攻击抽两张并仅在回合末弃至七张()
    {
        var state = Scene("ST37-001");
        var me = state.Players[0];
        me.Hand.AddRange(Enumerable.Range(0, 7).Select(_ => Card("OP15-004")));
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);
        Assert.Equal(11, me.Hand.Count);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(2, state.EndOfTurnTasks.Count);
        var discarded = me.Hand.TakeLast(4).Select(card => card.Id).ToArray();
        prompts.QueueChoose(discarded.Select(id => id.ToString()).ToArray());

        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, prompts);

        Assert.Equal(7, me.Hand.Count);
        Assert.Equal(discarded, me.Trash.Select(card => card.Id));
        Assert.Single(prompts.ChooseHistory);
        Assert.True(me.HandDiscardedByEffectThisTurn);
        Assert.Empty(state.EndOfTurnTasks);
        Assert.Empty(state.PendingWatchers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task 路飞回合末手牌不多于七张时不要求弃牌(int initialHandCount)
    {
        var state = Scene("ST37-001");
        var me = state.Players[0];
        me.Hand.AddRange(Enumerable.Range(0, initialHandCount).Select(_ => Card("OP15-004")));
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);

        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, prompts);

        Assert.Equal(initialHandCount + 2, me.Hand.Count);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Empty(me.Trash);
    }

    [Fact]
    public async Task 路飞未攻击时不会自动限制手牌()
    {
        var state = Scene("ST37-001");
        state.Players[0].Hand.AddRange(Enumerable.Range(0, 9).Select(_ => Card("OP15-004")));

        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, new MockPromptService());

        Assert.Equal(9, state.Players[0].Hand.Count);
    }

    [Fact]
    public async Task 路飞发动后领航效果无效仍执行延迟弃牌且不能取消强制选择()
    {
        var state = Scene("ST37-001");
        var me = state.Players[0];
        me.Hand.AddRange(Enumerable.Range(0, 8).Select(_ => Card("OP15-004")));
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, new MockPromptService());
        me.Leader.IsEffectsNullified = true;
        var id = me.Hand[0].Id.ToString();
        var prompts = new MockPromptService().QueueChoose(id, id);

        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, prompts);

        Assert.Equal(7, me.Hand.Count);
        Assert.Equal(3, me.Trash.Count);
        Assert.True(me.HandDiscardedByEffectThisTurn);
    }

    [Fact]
    public async Task 路飞效果在另一侧玩家也正确抽牌及弃牌()
    {
        var state = TestScene.New(oppLeaderNumber: "ST37-001").Build();
        state.CurrentTurnPlayer = 1;
        var player = state.Players[1];
        player.Deck.AddRange(Enumerable.Range(0, 10).Select(_ => Card("OP15-004")));
        player.Hand.AddRange(Enumerable.Range(0, 8).Select(_ => Card("OP15-004")));
        await EffectRuntime.Resolve(state, 1, player.Leader, EffectTrigger.OnAttackDeclare, new MockPromptService());

        await TurnEngine.ResolvePromptedEndPhaseTasksAsync(state, new MockPromptService());

        Assert.Equal(7, player.Hand.Count);
        Assert.Equal(3, player.Trash.Count);
        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Trash);
    }

    [Fact]
    public async Task 克洛克达尔攻击先支付所选手牌再抽一张()
    {
        var state = Scene("ST38-001");
        var me = state.Players[0];
        var discarded = Card("OP15-007");
        var kept = Card("OP15-004");
        var drawn = me.Deck[0];
        me.Hand.AddRange([kept, discarded]);
        var prompts = new MockPromptService().QueueChoose(discarded.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(new[] { kept.Id, drawn.Id }, me.Hand.Select(card => card.Id));
        Assert.Contains(discarded, me.Trash);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 克洛克达尔攻击取消发动或选牌不支付也不抽牌(bool decline)
    {
        var state = Scene("ST38-001");
        state.Players[0].Hand.Add(Card("OP15-004"));
        var prompts = decline ? new MockPromptService().QueueConfirm(false) : new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Single(state.Players[0].Hand);
        Assert.Equal(20, state.Players[0].Deck.Count);
        Assert.Empty(state.Players[0].Trash);
    }

    [Fact]
    public async Task 克洛克达尔攻击没有手牌时不弹空成本选择()
    {
        var state = Scene("ST38-001");
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Empty(state.Players[0].Hand);
    }

    [Fact]
    public async Task 克洛克达尔主要按原本费用和包含特征筛选并每回合仅成功一次()
    {
        var state = Scene("ST38-001");
        var me = state.Players[0];
        var valid = Card("OP16-045");
        valid.CostModThisTurn = -3;
        var lowOriginal = Card("OP16-054");
        lowOriginal.CostModThisTurn = 5;
        var otherTrait = Card("OP15-004");
        var secondValid = Card("P-082");
        me.Characters.AddRange([valid, lowOriginal, otherTrait, secondValid]);
        var prompts = new MockPromptService().QueueChoose(valid.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(valid.Id.ToString(), choice.choices);
        Assert.Contains(secondValid.Id.ToString(), choice.choices);
        Assert.DoesNotContain(lowOriginal.Id.ToString(), choice.choices);
        Assert.DoesNotContain(otherTrait.Id.ToString(), choice.choices);
        Assert.Contains(valid, me.Trash);
        Assert.Single(me.Hand);
        Assert.Contains(me.Leader.Id, me.OncePerTurnEffectUsedCardIds);
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Contains(secondValid, me.Characters);
        Assert.Single(me.Hand);

        TurnEngine.EnterEndPhase(state);
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(secondValid.Id.ToString()));
        Assert.Contains(secondValid, me.Trash);
        Assert.Equal(2, me.Hand.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 克洛克达尔主要取消发动或选牌不消耗次数(bool decline)
    {
        var state = Scene("ST38-001");
        var me = state.Players[0];
        var cost = Card("P-082");
        me.Characters.Add(cost);
        var prompts = decline ? new MockPromptService().QueueConfirm(false) : new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(cost, me.Characters);
        Assert.Empty(me.Hand);
        Assert.Empty(me.TurnOnceUsed);
        Assert.Empty(me.OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public async Task 克洛克达尔主要失效响应不会KO或消耗次数()
    {
        var state = Scene("ST38-001");
        var me = state.Players[0];
        var cost = Card("P-082");
        me.Characters.Add(cost);
        var prompts = new MockPromptService().QueueChoose(cost.Id.ToString());
        prompts.OnChooseResponse = _ => me.Characters.Remove(cost);

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(me.Hand);
        Assert.Empty(me.Trash);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task 克洛克达尔主要KO被保护阻止时不抽牌或消耗次数()
    {
        var state = Scene("ST38-001");
        var me = state.Players[0];
        var cost = Card("OP16-045");
        me.Characters.Add(cost);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = me.Leader.Id.ToString(),
            Scope = new ContinuousScope(),
            KoGuard = "effect",
            Predicate = (_, _, card) => card.Id == cost.Id,
        });

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(cost.Id.ToString()));

        Assert.Contains(cost, me.Characters);
        Assert.Empty(me.Hand);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task 克洛克达尔主要先抽牌再结算成本角色的KO时效果()
    {
        var state = Scene("ST38-001");
        var me = state.Players[0];
        var cost = Card("P-145");
        me.Characters.Add(cost);
        state.Players[1].Hand.AddRange(Enumerable.Range(0, 6).Select(_ => Card("OP15-004")));
        var prompts = new MockPromptService().QueueChoose(cost.Id.ToString());
        bool sawKoEffectAfterDraw = false;
        prompts.OnChooseResponse = kind =>
        {
            if (kind != "OwnCharacterKOCost") sawKoEffectAfterDraw = me.Hand.Count == 1;
        };

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.True(sawKoEffectAfterDraw);
        Assert.Equal(4, state.Players[1].Hand.Count);
        Assert.Empty(state.PendingKOEffects);
    }

    [Theory]
    [InlineData("ST37-001", DeckValidator.FormatStandardRanked)]
    [InlineData("ST37-001", DeckValidator.FormatPublicUnrestricted)]
    [InlineData("ST38-001", DeckValidator.FormatStandardRanked)]
    [InlineData("ST38-001", DeckValidator.FormatPublicUnrestricted)]
    public void 新预组领航可进入标准和狂野排位(string number, string format)
    {
        _ = TestScene.New();
        var result = DeckValidator.Validate(BuildDeck(number), format);
        Assert.True(result.Ok, result.Reason);
    }

    [Theory]
    [InlineData("ST37-001", "ranked")]
    [InlineData("ST37-001", "rankedWild")]
    [InlineData("ST38-001", "ranked")]
    [InlineData("ST38-001", "rankedWild")]
    public void 实际排位队列入口接受新预组领航(string number, string queue)
    {
        _ = TestScene.New();
        var method = typeof(WebSocketBridge).GetMethod("DeckFormatForQueue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var format = (string)method.Invoke(null, [queue])!;
        var result = DeckValidator.Validate(BuildDeck(number), format);
        Assert.True(result.Ok, result.Reason);
    }

    [Theory]
    [InlineData(DeckValidator.FormatStandardRanked)]
    [InlineData(DeckValidator.FormatPublicUnrestricted)]
    [InlineData(DeckValidator.FormatUnrestricted)]
    public void 路飞阿拉巴斯坦限制在所有对战格式生效(string format)
    {
        _ = TestScene.New();
        var lines = BuildDeck("ST37-001").Split('\n');
        lines[^1] = "OP15-007";

        var result = DeckValidator.Validate(string.Join('\n', lines), format);

        Assert.False(result.Ok);
        Assert.Contains("阿拉巴斯坦王国", result.Reason ?? "");
        Assert.Contains("OP15-007", result.Reason ?? "");
    }

    [Fact]
    public void 克洛克达尔没有合格成本时主要不可用且已登记每回合一次标识()
    {
        var state = Scene("ST38-001");
        Assert.NotNull(new ST38_001_Crocodile().GetActivatedMainUnavailableReason(state, 0, state.Players[0].Leader));
        Assert.True(OncePerTurnEffectCatalog.Contains("ST38-001", state));
    }

    private static string BuildDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = new[] { "EB01", "EB02", "EB03", "EB04", "OP13", "OP15", "OP18" }
            .SelectMany(CardDatabase.GetBySet)
            .Where(card => card.Kind != CardKind.Leader && card.Subscript != 1 && card.SharesColorWith(leader))
            .Where(card => leaderNumber != "ST37-001" || card.HasKeyword("阿拉巴斯坦王国"));
        var main = pool.SelectMany(card => Enumerable.Repeat(card.Number, 4)).Take(50).ToArray();
        Assert.Equal(50, main.Length);
        return string.Join('\n', new[] { leaderNumber }.Concat(main));
    }
}
