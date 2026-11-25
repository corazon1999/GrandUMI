using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LatestFeedbackInteractions20261125Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    public void 阿龙减费计入对方休息咚(int restDon, bool reduced)
    {
        var s = TestScene.New("OP03-022").MyHandAdd("OP11-023").Build();
        s.Players[1].CostArea.AddRange(Enumerable.Range(0, restDon).Select(_ => new DonCard { State = DonState.Rest }));
        var card = s.Players[0].Hand[0];
        Assert.Equal(card.Info.Cost - (reduced ? 3 : 0), s.HandPlayCost(0, card));
        s.Players[0].LifeArea.AddRange(Enumerable.Range(0, 4).Select(_ => Card("OP15-003")));
        Assert.Equal(card.Info.Cost, s.HandPlayCost(0, card));
    }

    [Fact]
    public async Task 萨波没有登场效果且可获山智速攻并保留静态力量()
    {
        var s = TestScene.New("PRB01-001").MyCharacter("P-044").Build();
        var me = s.Players[0]; var card = me.Characters[0];
        Assert.DoesNotContain("OnEnterField", card.Info.EffectTags);
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.OnEnterField, new MockPromptService());
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.True(ActionValidator.HasKeyword(s, card, "速攻"));
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = card.Id });
        Assert.Equal(7000, s.CurrentPowerOf(0, card));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 龙胆加生命后受到伤害且允许选择不加(bool add)
    {
        var s = TestScene.New().Build(); s.CurrentTurnPlayer = 1;
        var me = s.Players[0]; var oldLife = Card("OP15-003"); var top = Card("OP15-003");
        me.LifeArea.Add(oldLife); me.Deck.Add(top);
        await EffectRuntime.Resolve(s, 0, Card("OP14-115"), EffectTrigger.OnKO,
            new MockPromptService().QueueConfirm(add));
        Assert.Equal(add ? 1 : 0, me.LifeCount);
        Assert.Contains(add ? top : oldLife, me.Hand);
        Assert.False(s.IsGameOver);
    }

    [Fact]
    public async Task 龙胆效果伤害允许生命触发且不继承战斗流放()
    {
        var s = TestScene.New().OppCharacter("OP01-070").Build(); s.CurrentTurnPlayer = 1;
        var me = s.Players[0]; var top = Card("EB02-030");
        Assert.Contains("抽", top.Info.Trigger);
        me.Deck.AddRange([top, Card("OP15-003"), Card("OP15-003")]);
        var attacker = s.Players[1].Characters[0];
        AtomicOps.GiveKeyword(attacker, "流放", KeywordDuration.ThisTurn);
        s.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = attacker.Id, TargetIsLeader = false, TargetCardId = Guid.NewGuid() };
        await EffectRuntime.Resolve(s, 0, Card("OP14-115"), EffectTrigger.OnKO,
            new MockPromptService().QueueLifeTrigger(true));
        Assert.Contains(top, me.Trash);
        Assert.NotEmpty(me.Hand);
        Assert.DoesNotContain(top, me.Hand);
    }

    [Fact]
    public async Task 龙胆零生命且不补生命时正常败北()
    {
        var s = TestScene.New().Build(); s.CurrentTurnPlayer = 1;
        await EffectRuntime.Resolve(s, 0, Card("OP14-115"), EffectTrigger.OnKO, new MockPromptService());
        Assert.True(s.IsGameOver);
        Assert.Equal(1, s.WinnerIndex);
    }

    [Theory]
    [InlineData("Character", 1, true)]
    [InlineData("Event", 1, false)]
    [InlineData("Leader", 1, false)]
    [InlineData("Character", 0, false)]
    public async Task 佐罗只替代对方角色效果造成的横置(string kind, int acting, bool protect)
    {
        var s = TestScene.New().MyCharacter("PRB02-006").MyCharacter("OP15-003").Build();
        s.CurrentTurnPlayer = 1;
        var self = s.Players[0].Characters[0]; self.IsTapped = true;
        await EffectRuntime.Resolve(s, 0, self, EffectTrigger.OnCharRested, new MockPromptService(),
            new() { ["restedCardId"] = self.Id.ToString(), ["reason"] = "effect", ["actingSide"] = acting, ["sourceKind"] = kind });
        Assert.Equal(!protect, self.IsTapped);
        Assert.Equal(protect, s.Players[0].Characters[1].IsTapped);
    }

    [Fact]
    public async Task 舞台从手牌重登不残留休息或无效状态()
    {
        var s = TestScene.New().Build(); var stage = Card("OP18-078");
        stage.IsTapped = true; stage.IsEffectsNullified = true;
        s.Players[0].Hand.Add(stage);
        await AtomicOps.PlayFromHandFree(s, 0, stage);
        Assert.Same(stage, s.Players[0].StageCard);
        Assert.False(stage.IsTapped);
        Assert.False(stage.IsEffectsNullified);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task 同时登场的腾位候选不包含新角色(int existingCount)
    {
        var s = TestScene.New().Build(); var me = s.Players[0];
        var existing = Enumerable.Range(0, existingCount).Select(_ => Card("OP15-003")).ToList();
        me.Characters.AddRange(existing);
        var entering = new[] { Card("OP15-003"), Card("OP15-003") }; me.Hand.AddRange(entering);
        var prompts = new MockPromptService();
        await AtomicOps.PlayFromHandSimultaneously(new EffectContext { State = s, OwnerIndex = 0,
            Source = me.Leader, ExecutionId = "并发登场测试", Prompts = prompts }, entering);
        Assert.Equal(5, me.Characters.Count);
        Assert.All(entering, c => Assert.Contains(c, me.Characters));
        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(existingCount - 3, choice.min);
        Assert.Equal(existing.Select(c => c.Id.ToString()), choice.choices);
        Assert.Equal(existingCount - 3, me.Trash.Count);
    }

    [Fact]
    public async Task 山智自身被选中离场并牺牲后仍保护同批草帽()
    {
        var s = TestScene.New().MyCharacter("OP18-026").MyCharacter("OP18-030").Build();
        var me = s.Players[0]; var sanji = me.Characters[0]; var other = me.Characters[1];
        Assert.True(other.Info.HasKeyword("草帽一伙"));
        var eventCard = Card("OP18-051"); s.Players[1].Hand.Add(eventCard);
        s.Players[1].CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new MockPromptService().QueueChoose(sanji.Id.ToString()).QueueChoose(other.Id.ToString());
        await EffectRuntime.Resolve(s, 1, eventCard, EffectTrigger.OnEnterField, prompts);
        Assert.Contains(sanji, me.Trash);
        Assert.Contains(other, me.Characters);
        Assert.DoesNotContain(other, me.Deck);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 路飞已经结算的力量不会被后续无效清除且准时结束(int owner)
    {
        var s = TestScene.New("EB02-010", "EB02-010").Build(); s.CurrentTurnPlayer = owner;
        var me = s.Players[owner]; me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ => new DonCard { State = DonState.Rest }));
        await EffectRuntime.Resolve(s, owner, me.Leader, EffectTrigger.ActivatedMain, new MockPromptService());
        me.Leader.IsEffectsNullified = true;
        Assert.Equal(6000, s.CurrentPowerOf(owner, me.Leader));
        TurnEngine.EnterEndPhase(s);
        Assert.Equal(6000, s.CurrentPowerOf(owner, me.Leader));
        s.CurrentTurnPlayer = 1 - owner; TurnEngine.EnterEndPhase(s);
        Assert.Equal(5000, s.CurrentPowerOf(owner, me.Leader));
    }

    [Fact]
    public async Task 罗咚多于对方时既不补咚也不加力量()
    {
        var s = TestScene.New("OP12-061").MyCharacter("OP12-073").Build();
        var me = s.Players[0]; me.CostArea.Add(new DonCard { State = DonState.Active });
        await EffectRuntime.Resolve(s, 0, me.Characters[0], EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(me.Leader.Info.Power, s.CurrentPowerOf(0, me.Leader));
        Assert.Single(me.CostArea);
    }

    [Fact]
    public async Task 鸟笼等待三张山智保护时不阻塞调用线程()
    {
        var s = TestScene.New("OP04-019").Build(); var cage = Card("OP05-040");
        s.Players[0].StageCard = cage;
        s.Players[0].CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        s.Players[1].Characters.AddRange(Enumerable.Range(0, 3).Select(_ => Card("OP12-070")));
        foreach (var c in s.Players[1].Characters) c.IsTapped = true;
        s.Players[1].CostArea.AddRange(Enumerable.Range(0, 3).Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new DeferredConfirmPrompts();
        Task? resolution = null;
        var invoked = Task.Run(() => { resolution = EffectRuntime.Resolve(s, 0, cage, EffectTrigger.OnMyTurnEnd, prompts); });
        try
        {
            await prompts.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await invoked.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(resolution); Assert.False(resolution.IsCompleted);
        }
        finally { prompts.Answer.TrySetResult(true); }
        await resolution!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, s.Players[1].Characters.Count);
        Assert.Empty(s.Players[1].CostArea);
        Assert.Null(s.Players[0].StageCard);
        Assert.Contains(cage, s.Players[0].Trash);
    }

    private sealed class DeferredConfirmPrompts : IPromptService
    {
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MockPromptService inner = new();
        public Task<bool> ConfirmOptional(int playerIdx, string text) { Asked.TrySetResult(); return Answer.Task; }
        public Task<List<string>> ChooseCards(int playerIdx, string kind, string text, IReadOnlyList<string> validChoices,
            int min, int max, Dictionary<string, object?>? extra = null) => inner.ChooseCards(playerIdx, kind, text, validChoices, min, max, extra);
        public Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options, Dictionary<string, object?>? extra = null)
            => inner.ChooseOption(playerIdx, text, options, extra);
        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger) => Task.FromResult(false);
    }
}
