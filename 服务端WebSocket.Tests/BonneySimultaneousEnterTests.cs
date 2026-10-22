using System.Collections.Concurrent;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>五费邦妮同时登场两张角色时的排序和连续提示回归。</summary>
public sealed class BonneySimultaneousEnterTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task 两张同名奈美可任选先后且两个检索均结算(int owner, bool secondFirst)
    {
        var state = TestScene.New("OP01-001", "OP01-001").Build();
        state.CurrentTurnPlayer = owner;
        var me = state.Players[owner];
        var bonney = Card("EB05-001");
        var first = Card("OP01-016");
        var second = Card("OP01-016");
        me.Characters.Add(bonney);
        me.Hand.AddRange([first, second]);
        me.Deck.AddRange(Enumerable.Range(0, 24).Select(_ => Card("OP01-004")));
        var expected = secondFirst ? new[] { second.Id, first.Id } : new[] { first.Id, second.Id };
        var seen = new List<Guid>();
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueChoose(expected[0].ToString());
        prompts.OnChooseResponse = kind => {
            if (kind == "LookTopReveal") seen.Add(EffectRuntime.CurrentSource!.Id);
        };
        await EffectRuntime.Resolve(state, owner, bonney, EffectTrigger.OnEnterField, prompts);
        var order = Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "EffectOrder"));
        Assert.Equal(2, order.choices.Count);
        Assert.Equal(expected, seen);
        Assert.Equal(4, me.Hand.Count);
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public async Task 放弃第一个检索仍然继续第二张角色的登场效果()
    {
        var state = TestScene.New("OP01-001").Build();
        var me = state.Players[0];
        var bonney = Card("EB05-001");
        var first = Card("OP01-016");
        var second = Card("OP01-016");
        me.Characters.Add(bonney);
        me.Hand.AddRange([first, second]);
        me.Deck.AddRange(Enumerable.Range(0, 24).Select(_ => Card("OP01-004")));
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueChoose(first.Id.ToString()).QueueChooseEmpty();
        await EffectRuntime.Resolve(state, 0, bonney, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(2, prompts.ChooseHistory.Count(prompt => prompt.kind == "LookTopReveal"));
        Assert.Equal(3, me.Hand.Count);
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public async Task 同批两次领袖监听的排序选项不能重复且各抽一次()
    {
        var state = TestScene.New("OP14-041").Build();
        // 单人测试已有的特征通配入口，让女帝领袖满足邦妮的超新星条件。
        state.Players[0] = new PlayerState { SessionId = "s0", AccountName = "p0",
            Leader = new CardInstance { Info = state.Players[0].Leader.Info.WithWildcardKeywords() } };
        var me = state.Players[0];
        state.CurrentTurnPlayer = 1;
        var bonney = Card("EB05-001");
        var first = Card("OP01-016");
        var second = Card("OP01-016");
        me.Characters.Add(bonney);
        me.Hand.AddRange([first, second]);
        me.Deck.AddRange(Enumerable.Range(0, 30).Select(_ => Card("OP01-004")));
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString());
        await EffectRuntime.Resolve(state, 0, bonney, EffectTrigger.OnEnterField, prompts);
        var orders = prompts.ChooseHistory.Where(prompt => prompt.kind == "EffectOrder").ToList();
        Assert.Equal(4, orders[0].choices.Count);
        Assert.All(orders, order => Assert.Equal(order.choices.Count, order.choices.Distinct().Count()));
        Assert.Equal(6, me.Hand.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 连锁登场等待同批效果且来源提前离场不能再发动(bool removePendingSource)
    {
        var state = TestScene.New("OP01-001").Build();
        var me = state.Players[0];
        var bonney = Card("EB05-001");
        var shirahoshi = Card("OP14-047");
        var nami = Card("OP01-016");
        var camie = Card("OP06-025");
        var filler = Card("OP01-004");
        me.Characters.AddRange([bonney, filler, Card("OP01-004")]);
        me.Hand.AddRange([shirahoshi, nami, camie]);
        me.Deck.AddRange(Enumerable.Range(0, 30).Select(index => Card(index % 2 == 0 ? "OP01-004" : "OP06-033")));
        var prompts = new MockPromptService()
            .QueueChoose(shirahoshi.Id.ToString(), nami.Id.ToString())
            .QueueChoose(shirahoshi.Id.ToString())
            .QueueChoose(camie.Id.ToString())
            .QueueChoose((removePendingSource ? nami : filler).Id.ToString());
        var searches = new List<Guid>();
        prompts.OnChooseResponse = kind => {
            if (kind == "LookTopReveal") searches.Add(EffectRuntime.CurrentSource!.Id);
        };
        await EffectRuntime.Resolve(state, 0, bonney, EffectTrigger.OnEnterField, prompts);
        Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "EffectOrder"));
        Assert.Equal(removePendingSource ? new[] { camie.Id } : new[] { nami.Id, camie.Id }, searches);
        Assert.Contains(camie, me.Characters);
        Assert.Equal(removePendingSource, me.Trash.Contains(nami));
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public async Task 第一张开始检索前同批另一张角色的持续能力已经生效()
    {
        var state = TestScene.New("OP01-001").Build();
        var me = state.Players[0];
        var bonney = Card("EB05-001");
        var nami = Card("OP01-016");
        var vivi = Card("OP18-011");
        var chopper = Card("OP18-010");
        me.Characters.AddRange([bonney, chopper]);
        me.Hand.AddRange([nami, vivi]);
        me.Deck.AddRange(Enumerable.Range(0, 20).Select(_ => Card("OP01-004")));
        var prompts = new MockPromptService().QueueChoose(nami.Id.ToString(), vivi.Id.ToString());
        var powers = new List<int>();
        prompts.OnChooseResponse = kind => {
            if (kind is "EffectOrder" or "LookTopReveal") powers.Add(state.CurrentPowerOf(0, chopper));
        };
        await EffectRuntime.Resolve(state, 0, bonney, EffectTrigger.OnEnterField, prompts);
        Assert.Equal(new[] { chopper.Info.Power + 1000, chopper.Info.Power + 1000 }, powers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 只登场零张或一张不产生空的多效果排序(int selectedCount)
    {
        var state = TestScene.New("OP01-001").Build();
        var me = state.Players[0];
        var bonney = Card("EB05-001");
        var nami = Card("OP01-016");
        me.Characters.Add(bonney);
        me.Hand.Add(nami);
        me.Deck.AddRange(Enumerable.Range(0, 20).Select(_ => Card("OP01-004")));
        var prompts = new MockPromptService().QueueChoose(selectedCount == 0 ? [] : [nami.Id.ToString()]);
        await EffectRuntime.Resolve(state, 0, bonney, EffectTrigger.OnEnterField, prompts);
        Assert.DoesNotContain(prompts.ChooseHistory, prompt => prompt.kind == "EffectOrder");
        Assert.Equal(selectedCount, prompts.ChooseHistory.Count(prompt => prompt.kind == "LookTopReveal"));
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public void 回放检查点区分同时登场批次且保留普通队列的旧投影()
    {
        var state = TestScene.New().Build();
        var card = Card("OP01-016");
        state.EnqueueEnterField(0, card, "hand");
        var legacy = DeterministicReplayCheckpointProvider.BuildFullState(state)
            .GetProperty("pendingEnterFields")[0];
        Assert.Equal(new[] { "Owner", "cardId", "From", "effectSourceKind", "EffectSourceNumber", "LifeTriggerOrigin" },
            legacy.EnumerateObject().Select(property => property.Name));
        state.PendingEnterFields.Clear();
        state.EnqueueEnterField(0, card, "hand", simultaneousBatchId: "bonney-effect");
        var current = DeterministicReplayCheckpointProvider.BuildFullState(state)
            .GetProperty("pendingEnterFields")[0];
        Assert.Equal("bonney-effect", current.GetProperty("SimultaneousBatchId").GetString());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task 真实提示逐个响应且不会覆盖第二张角色的效果(int owner, bool fullField)
    {
        _ = TestScene.New().Build();
        string deck = "OP01-001\n" + string.Join('\n', Enumerable.Repeat("OP01-004", 40));
        var engine = new GameEngine("bonney-two-prompts", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 5001);
        var state = engine.State;
        state.CurrentTurnPlayer = owner;
        var me = state.Players[owner];
        me.Hand.Clear(); me.Deck.Clear();
        var bonney = Card("EB05-001");
        var first = Card("OP01-016");
        var second = Card("OP03-044");
        me.Characters.Add(bonney);
        var fillers = Enumerable.Range(0, fullField ? 3 : 0).Select(_ => Card("OP01-004")).ToArray();
        me.Characters.AddRange(fillers);
        me.Hand.AddRange([first, second]);
        me.Deck.AddRange(Enumerable.Range(0, 30).Select(_ => Card("OP01-004")));
        var snapshots = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (side, payload) => {
            if (side == owner) snapshots.Enqueue(JsonSerializer.Serialize(payload));
        };
        var task = EffectRuntime.Resolve(state, owner, bonney, EffectTrigger.OnEnterField, engine.Prompts);
        var ids = new HashSet<string>();
        var resolvedSources = new List<string>();
        var orderChoices = new List<string[]>();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                if (state.PendingPrompt is not { } prompt || !ids.Add(prompt.PromptId))
                {
                    await Task.Delay(5);
                    continue;
                }
                var source = prompt.Extra.TryGetValue("sourceNumber", out var rawSource) ? rawSource?.ToString() : null;
                if (prompt.Kind is "LookTopReveal" or "DiscardOwnChosen") resolvedSources.Add(source ?? "");
                List<string> answer;
                if (prompt.Kind == "OwnHandCharacter") answer = [first.Id.ToString(), second.Id.ToString()];
                else if (prompt.Kind == "EffectOrder")
                {
                    orderChoices.Add(prompt.ValidChoices.ToArray());
                    answer = [second.Id.ToString()];
                }
                else if (fillers.Any(card => prompt.ValidChoices.Contains(card.Id.ToString())))
                    answer = [fillers.First(card => prompt.ValidChoices.Contains(card.Id.ToString())).Id.ToString()];
                else if (prompt.Kind.StartsWith("Reorder", StringComparison.Ordinal)) answer = prompt.ValidChoices.ToList();
                else answer = prompt.ValidChoices.Take(prompt.MaxChoose).ToList();
                engine.Prompts.Resolve(prompt.PromptId, answer);
            }
            Assert.True(task.IsCompleted, "两个登场效果应在逐个响应后完整结束");
            await task;
            Assert.Single(orderChoices);
            Assert.Equal(new[] { first.Id.ToString(), second.Id.ToString() }, orderChoices[0]);
            Assert.Equal(new[] { "OP03-044", "OP01-016" }, resolvedSources);
            Assert.Contains(first, me.Characters);
            Assert.Contains(second, me.Characters);
            Assert.Equal(fullField ? 5 : 3, me.Characters.Count);
            Assert.Null(state.PendingPrompt);
            var publishedSources = new List<string?>();
            foreach (var snapshot in snapshots)
            {
                using var doc = JsonDocument.Parse(snapshot);
                publishedSources.AddRange(doc.RootElement.GetProperty("effectActivations").EnumerateArray()
                    .Where(activation => activation.GetProperty("trigger").GetString() == "OnEnterField")
                    .Select(activation => activation.GetProperty("cardNumber").GetString()));
            }
            Assert.Contains("OP01-016", publishedSources);
            Assert.Contains("OP03-044", publishedSources);
        }
        finally
        {
            if (!task.IsCompleted)
            {
                state.WinnerIndex = owner;
                engine.Prompts.CancelForGameOver();
                await task;
            }
        }
    }
}
