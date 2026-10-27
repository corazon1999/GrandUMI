using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月27日游戏内反馈的持续能力与先付成本边界回归。</summary>
public sealed class PlayerFeedback20261027Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    private static GameEngine Engine(int owner)
    {
        TestScene.New();
        const string deck = "ST10-002\nOP15-003";
        var engine = new GameEngine("反馈验证", ("s0", "p0", deck), ("s1", "p1", deck), 0, 27);
        engine.State.OpeningStage = OpeningStage.Playing;
        engine.State.Phase = Phase.Main;
        engine.State.CurrentTurnPlayer = owner;
        engine.State.TurnCount = 5;
        return engine;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 真实攻击流程中大和不足十废弃也提示弃手(int owner)
    {
        var engine = Engine(1 - owner);
        var state = engine.State;
        var me = state.Players[owner];
        var yamato = Card("EB05-046");
        var discard = Card("OP15-003");
        me.Characters.Add(yamato);
        me.Hand.Add(discard);
        Assert.True(engine.HandleAction(1 - owner, "Attack", JsonSerializer.SerializeToElement(new
        {
            attackerId = state.Players[1 - owner].Leader.Id.ToString(), targetIsLeader = true,
        })));
        await engine.WaitSettledAsync();
        var confirm = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal(owner, confirm.PlayerIndex);
        Assert.Equal("Option", confirm.Kind);
        engine.Prompts.Resolve(confirm.PromptId, ["0"]);
        await engine.WaitSettledAsync();
        var choice = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal(owner, choice.PlayerIndex);
        Assert.Contains(discard.Id.ToString(), choice.ValidChoices);
        engine.Prompts.Resolve(choice.PromptId, [discard.Id.ToString()]);
        await engine.WaitSettledAsync();
        Assert.Contains(discard, me.Trash);
        Assert.Equal(me.Leader.Info.Power, state.OriginalPowerOf(owner, me.Leader));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 真实启动流程中螳螂空场也可支付舞台成本(int owner)
    {
        var engine = Engine(owner);
        var state = engine.State;
        var mantis = Card("OP06-102");
        var stage = Card("OP06-117");
        state.Players[owner].Characters.Add(mantis);
        state.Players[owner].StageCard = stage;
        Assert.True(engine.HandleAction(owner, "UseEffect", JsonSerializer.SerializeToElement(new
        {
            sourceId = mantis.Id.ToString(), effectKey = "main",
        })));
        await engine.WaitSettledAsync();
        var confirm = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal(owner, confirm.PlayerIndex);
        engine.Prompts.Resolve(confirm.PromptId, ["0"]);
        await engine.WaitSettledAsync();
        var choice = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("Stage", choice.Kind);
        Assert.Equal(owner, choice.PlayerIndex);
        engine.Prompts.Resolve(choice.PromptId, [stage.Id.ToString()]);
        await engine.WaitSettledAsync();
        Assert.Null(state.PendingPrompt);
        Assert.Null(state.Players[owner].StageCard);
        Assert.Contains(stage, state.Players[owner].Deck);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 黑胡子封登场时仍保留尼卡三张附着咚速攻(int owner)
    {
        var state = TestScene.New("OP09-081", "OP09-081").Build();
        state.TurnCount = 5;
        state.CurrentTurnPlayer = owner;
        int opponent = 1 - owner;
        var me = state.Players[owner];
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(state, opponent, state.Players[opponent].Leader,
            EffectTrigger.OnGameStart, prompts);
        state.Players[opponent].Hand.Add(Card("OP15-003"));
        await EffectRuntime.Resolve(state, opponent, state.Players[opponent].Leader,
            EffectTrigger.ActivatedMain, prompts);
        var target = Card("OP15-003");
        state.Players[opponent].Characters.Add(target);
        var luffy = Card("ST31-004");
        me.Hand.Add(luffy);
        await AtomicOps.PlayFromHandFree(state, owner, luffy);
        await EffectRuntime.DrainPendingEnterFields(state, prompts);
        Assert.Contains(luffy, me.Characters);
        Assert.Equal(target.Info.Power, state.CurrentPowerOf(opponent, target));

        // 三张咚可以分配给不同卡牌；两张不足，第三张赋予领袖后即获得速攻。
        me.CostArea.AddRange(Enumerable.Range(0, 3).Select(_ => new DonCard { State = DonState.Active }));
        AtomicOps.AttachDonFromCost(me, luffy.Id, 2, DonState.Active);
        Assert.False(ActionValidator.CanAttack(state, owner, luffy.Id, true, null).Ok);
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Active);
        Assert.True(ActionValidator.HasKeyword(state, luffy, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, owner, luffy.Id, true, null).Ok);
        luffy.IsEffectsNullified = true;
        Assert.False(ActionValidator.HasKeyword(state, luffy, "速攻"));
        Assert.False(ActionValidator.CanAttack(state, owner, luffy.Id, true, null).Ok);
        luffy.IsEffectsNullified = false;
        Assert.True(ActionValidator.CanAttack(state, owner, luffy.Id, true, null).Ok);
        AtomicOps.ReturnFieldToDeckBottom(state, owner, luffy);
        Assert.DoesNotContain(luffy, me.Characters);
        Assert.False(ActionValidator.HasKeyword(state, luffy, "速攻"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 尼卡正常登场仍减攻且重复注册不叠加持续效果(int owner)
    {
        var state = TestScene.New("ST10-002", "ST10-002").Build();
        var luffy = Card("ST31-004");
        var target = Card("OP15-003");
        state.Players[owner].Characters.Add(luffy);
        state.Players[1 - owner].Characters.Add(target);
        await EffectRuntime.Resolve(state, owner, luffy, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(target.Info.Power - 2000, state.CurrentPowerOf(1 - owner, target));
        var effect = Assert.IsAssignableFrom<IFieldStaticEffect>(ScriptedEffectRegistry.TryGet("ST31-004"));
        var context = new EffectContext { State = state, OwnerIndex = owner, Source = luffy,
            ExecutionId = "反馈测试-持续注册", Prompts = new MockPromptService() };
        await effect.RegisterFieldStatic(context);
        await effect.RegisterFieldStatic(context);
        Assert.Single(state.ContinuousEffects.Where(item => item.SourceCardId == luffy.Id.ToString()));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 8)]
    [InlineData(0, 9)]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 8)]
    [InlineData(1, 9)]
    [InlineData(1, 10)]
    public async Task 大和可先弃手再按废弃区十张判断是否变七千(int owner, int trashCount)
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1 - owner;
        var me = state.Players[owner];
        var yamato = Card("EB05-046");
        var discarded = Card("OP15-003");
        me.Characters.Add(yamato);
        me.Hand.Add(discarded);
        me.Trash.AddRange(Enumerable.Range(0, trashCount).Select(_ => Card("OP15-003")));
        await EffectRuntime.Resolve(state, owner, yamato, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(discarded.Id.ToString()));
        Assert.Empty(me.Hand);
        Assert.Contains(discarded, me.Trash);
        Assert.Equal(trashCount + 1, me.Trash.Count);
        Assert.Equal(trashCount >= 9 ? 7000 : me.Leader.Info.Power, state.OriginalPowerOf(owner, me.Leader));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 大和允许拒绝弃手且无手牌不提示(int owner)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        var yamato = Card("EB05-046");
        var hand = Card("OP15-003");
        me.Characters.Add(yamato);
        me.Hand.Add(hand);
        var declined = new MockPromptService().QueueConfirm(false);
        await EffectRuntime.Resolve(state, owner, yamato, EffectTrigger.OnOppAttackDeclare, declined);
        Assert.Single(declined.ConfirmHistory);
        Assert.Contains(hand, me.Hand);
        Assert.Empty(me.Trash);
        me.Hand.Clear();
        var empty = new MockPromptService();
        await EffectRuntime.Resolve(state, owner, yamato, EffectTrigger.OnOppAttackDeclare, empty);
        Assert.Empty(empty.ConfirmHistory);
        Assert.Equal(me.Leader.Info.Power, state.OriginalPowerOf(owner, me.Leader));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task 螳螂无可KO目标仍能把任意一方一费舞台放底(int owner, bool opponentStage)
    {
        var state = TestScene.New().Build();
        var me = state.Players[owner];
        var mantis = Card("OP06-102");
        var stage = Card("OP06-117");
        int stageOwner = opponentStage ? 1 - owner : owner;
        me.Characters.Add(mantis);
        state.Players[stageOwner].StageCard = stage;
        var tooExpensive = Card("ST31-004");
        state.Players[1 - owner].Characters.Add(tooExpensive);
        var prompts = new MockPromptService().QueueChoose(stage.Id.ToString());
        await EffectRuntime.Resolve(state, owner, mantis, EffectTrigger.ActivatedMain, prompts);
        Assert.Empty(state.Players[stageOwner].StageCards);
        Assert.Same(stage, state.Players[stageOwner].Deck.Last());
        Assert.Contains(tooExpensive, state.Players[1 - owner].Characters);
        Assert.Single(prompts.ChooseHistory);

        // 已成功支付成本即消耗本回合次数，新舞台不能在同回合被再次放底。
        var secondStage = Card("OP06-117");
        state.Players[stageOwner].StageCard = secondStage;
        await EffectRuntime.Resolve(state, owner, mantis, EffectTrigger.ActivatedMain, prompts);
        Assert.Contains(secondStage, state.Players[stageOwner].StageCards);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 螳螂正常支付后可KO减至二费的角色(int owner)
    {
        var state = TestScene.New().Build();
        var mantis = Card("OP06-102");
        var stage = Card("OP06-117");
        var target = Card("ST31-004");
        target.CostModThisTurn = -5;
        state.Players[owner].Characters.Add(mantis);
        state.Players[owner].StageCard = stage;
        state.Players[1 - owner].Characters.Add(target);
        await EffectRuntime.Resolve(state, owner, mantis, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(stage.Id.ToString()).QueueChoose(target.Id.ToString()));
        Assert.Contains(stage, state.Players[owner].Deck);
        Assert.Contains(target, state.Players[1 - owner].Trash);
        Assert.Empty(state.Players[1 - owner].Characters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 螳螂放弃舞台成本不消耗次数(int owner)
    {
        var state = TestScene.New().Build();
        var mantis = Card("OP06-102");
        var stage = Card("OP06-117");
        state.Players[owner].Characters.Add(mantis);
        state.Players[owner].StageCard = stage;
        await EffectRuntime.Resolve(state, owner, mantis, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChooseEmpty());
        Assert.Contains(stage, state.Players[owner].StageCards);
        Assert.Empty(state.Players[owner].TurnOnceUsed);
        await EffectRuntime.Resolve(state, owner, mantis, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(stage.Id.ToString()));
        Assert.Contains(stage, state.Players[owner].Deck);
    }
}
