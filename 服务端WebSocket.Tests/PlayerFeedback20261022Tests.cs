using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>10月21日整理的游戏内反馈：名称条件、生命成本及真实登场链回归。</summary>
public sealed class PlayerFeedback20261022Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(1, null, false)]
    [InlineData(0, "OP09-045", false)]
    [InlineData(1, "OP09-045", false)]
    [InlineData(0, "OP09-051", true)]
    [InlineData(1, "OP09-051", true)]
    [InlineData(0, "OP09-053", true)]
    [InlineData(1, "OP09-053", true)]
    public async Task 卡巴奇只在我方存在指定同名角色时免于战斗KO(int owner, string? ally, bool guarded)
    {
        var state = TestScene.New("OP09-042", "OP09-042").Build();
        var me = state.Players[owner];
        var source = Card("OP09-045");
        me.Characters.Add(source);
        if (ally is not null) me.Characters.Add(Card(ally));
        state.Players[1 - owner].Characters.Add(Card("OP09-051"));
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(guarded, state.IsKoGuarded(source, "battle"));
        Assert.False(state.IsKoGuarded(source, "effect"));
        bool removed = await BattleEngine.KOCardAsync(state, owner, source, new MockPromptService());
        Assert.Equal(!guarded, removed);
        Assert.Equal(guarded, me.Characters.Contains(source));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 卡巴奇指定角色离场后立即失去保护(int owner)
    {
        var state = TestScene.New("OP09-042", "OP09-042").Build();
        var me = state.Players[owner];
        var source = Card("OP09-045");
        var ally = Card("OP09-053");
        me.Characters.AddRange([source, ally]);
        await EffectRuntime.Resolve(state, owner, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(state.IsKoGuarded(source, "battle"));
        AtomicOps.BounceToHand(state, owner, ally);
        Assert.False(state.IsKoGuarded(source, "battle"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 卡鲁真实开局后登场乔巴在双方回合均加攻并获得速攻(int owner)
    {
        _ = TestScene.New().Build();
        const string deck = "OP18-001\nOP18-010\nOP18-003\nOP18-010\nOP18-003\nOP18-010\nOP18-003";
        var engine = new GameEngine("feedback-karoo-chopper", ("s0", "p0", deck), ("s1", "p1", deck),
            firstPlayer: 0, rngSeed: 18010, deferInitialSetupUntilStart: true);
        engine.BeginOpeningSequence();
        await engine.WaitSettledAsync();
        var state = engine.State;
        var me = state.Players[owner];
        var chopper = Card("OP18-010");
        me.Hand.Add(chopper);
        await AtomicOps.PlayFromHandFree(state, owner, chopper);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());
        foreach (int turn in new[] { 0, 1 })
        {
            state.CurrentTurnPlayer = turn;
            Assert.Equal(6000, state.CurrentPowerOf(owner, chopper));
            Assert.True(ActionValidator.HasKeyword(state, chopper, "速攻"));
        }
    }

    [Theory]
    [InlineData(0, "back", true)]
    [InlineData(1, "back", true)]
    [InlineData(0, "face", false)]
    [InlineData(1, "face", false)]
    [InlineData(0, "empty", false)]
    [InlineData(1, "empty", false)]
    public async Task 白色印章须翻正生命顶作为成本后才可KO(int owner, string lifeState, bool paid)
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = owner;
        var me = state.Players[owner];
        var opponent = state.Players[1 - owner];
        var top = Card("OP15-003");
        top.IsLifeFaceUp = lifeState == "face";
        if (lifeState != "empty") me.LifeArea.Add(top);
        var target = Card("OP15-004");
        opponent.Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, owner, Card("OP13-117"), EffectTrigger.EventMain, prompts);
        Assert.Equal(paid, opponent.Trash.Contains(target));
        Assert.Equal(!paid, opponent.Characters.Contains(target));
        if (paid) Assert.True(top.IsLifeFaceUp);
        Assert.Equal(paid ? 1 : 0, prompts.ChooseHistory.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task 一费红巴奇无命中也确认顶五并可自选余牌顺序(bool hasEvent, bool takeEvent)
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007", "OP15-008").Build();
        var me = state.Players[0];
        if (hasEvent) me.Deck[0] = Card("OP01-029");
        var top = me.Deck.Take(5).ToArray();
        var untouched = me.Deck[5];
        var rest = top.Skip(takeEvent ? 1 : 0).Reverse().ToArray();
        var prompts = new MockPromptService().QueueChoose(takeEvent ? [top[0].Id.ToString()] : [])
            .QueueChoose(rest.Select(card => card.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(state, 0, Card("OP03-008"), EffectTrigger.OnEnterField, prompts);
        var look = Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "LookTopReveal"));
        var choices = JsonSerializer.SerializeToElement(look.extra!["choiceCards"]);
        Assert.Equal(top.Select(card => card.Info.Number), choices.EnumerateArray().Select(card => card.GetProperty("number").GetString()));
        Assert.Equal(hasEvent ? 1 : 0, look.choices.Count);
        Assert.Equal(new[] { untouched }.Concat(rest), me.Deck);
        Assert.Equal(takeEvent, me.Hand.Contains(top[0]));
    }

    [Fact]
    public async Task 卡巴奇的持续保护不受黑胡子仅封锁登场时影响()
    {
        var state = TestScene.New("OP09-042", "OP09-081").MyCharacter("OP09-045").MyCharacter("OP09-053").Build();
        state.Players[1].Hand.Add(Card("OP15-003"));
        await EffectRuntime.Resolve(state, 1, state.Players[1].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 1, state.Players[1].Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(state.Players[1].Hand[0].Id.ToString()));
        var source = state.Players[0].Characters[0];
        Assert.True(state.IsTriggerNullified(source, EffectTrigger.OnEnterField));
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.True(state.IsKoGuarded(source, "battle"));
        source.IsEffectsNullified = true;
        Assert.False(state.IsKoGuarded(source, "battle"));
    }

    [Fact]
    public async Task 白色印章选择目标时双方实际快照均公开生命顶()
    {
        _ = TestScene.New().Build();
        const string deck = "OP01-001\nOP15-003\nOP15-004\nOP15-005\nOP15-006\nOP15-007\nOP15-008\nOP15-009\nOP15-010\nOP15-011\nOP15-012";
        var engine = new GameEngine("feedback-dawn-stamp", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 13117);
        var me = engine.State.Players[0];
        var top = Card("OP15-003");
        me.LifeArea.Clear(); me.LifeArea.Add(top);
        var target = Card("OP15-004");
        engine.State.Players[1].Characters.Add(target);
        var snapshots = new[] { new ConcurrentQueue<string>(), new ConcurrentQueue<string>() };
        engine.OnSendToPlayer = (side, payload) => snapshots[side].Enqueue(JsonSerializer.Serialize(payload));
        var task = EffectRuntime.Resolve(engine.State, 0, Card("OP13-117"), EffectTrigger.EventMain, engine.Prompts);
        try
        {
            for (int i = 0; i < 200 && engine.State.PendingPrompt is null && !task.IsCompleted; i++) await Task.Delay(10);
            var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
            Assert.True(top.IsLifeFaceUp);
            for (int side = 0; side < 2; side++)
            {
                string zone = side == 0 ? "my" : "opponent";
                Assert.Contains(snapshots[side], text => {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    return root.TryGetProperty(zone, out var player)
                        && player.GetProperty("lifeFaceUp").EnumerateArray().Any(card =>
                            card.GetProperty("faceUp").GetBoolean() && card.GetProperty("number").GetString() == top.Info.Number);
                });
            }
        }
        finally
        {
            if (engine.State.PendingPrompt is { } pending) engine.Prompts.Resolve(pending.PromptId, [target.Id.ToString()]);
            await task;
        }
        Assert.Contains(target, engine.State.Players[1].Trash);
    }
}
