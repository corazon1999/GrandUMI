using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>卡组重排、领袖条件及原本力量修改型生命【触发】回归测试。</summary>
public class LifeTriggerComplexCompletionTests
{
    [Fact]
    public async Task OP06_059_WithFiveCards_ReordersAndMovesThemToDeckBottom()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var top = new[]
        {
            Card("OP15-003"), Card("OP15-004"), Card("OP15-005"),
            Card("OP15-006"), Card("OP15-007"),
        };
        var tail = Card("OP15-008");
        me.Deck.AddRange(top);
        me.Deck.Add(tail);
        var desired = top.Reverse().ToArray();
        var prompts = new MockPromptService()
            .QueueChoose(desired.Select(c => c.Id.ToString()).ToArray())
            .QueueOption(1);

        await Resolve(state, "OP06-059", prompts);

        Assert.Same(tail, me.Deck[0]);
        Assert.Equal(desired.Select(c => c.Id), me.Deck.Skip(1).Select(c => c.Id));
    }

    [Fact]
    public async Task OP06_059_WithEmptyDeck_DoesNotPromptOrMoveCards()
    {
        var state = TestScene.New().Build();
        var prompts = new MockPromptService();

        await Resolve(state, "OP06-059", prompts);

        Assert.Empty(state.Players[0].Deck);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP09_104_WithMulticolorLeader_DrawsTwo()
    {
        var state = TestScene.New(myLeaderNumber: "EB01-001")
            .MyDeckTop("OP15-003", "OP15-004")
            .Build();

        await Resolve(state, "OP09-104");

        Assert.Equal(2, state.Players[0].Hand.Count);
        Assert.Empty(state.Players[0].Deck);
    }

    [Fact]
    public async Task OP09_104_WithMonocolorLeader_DoesNotDraw()
    {
        var state = TestScene.New(myLeaderNumber: "OP01-001")
            .MyDeckTop("OP15-003", "OP15-004")
            .Build();

        await Resolve(state, "OP09-104");

        Assert.Empty(state.Players[0].Hand);
        Assert.Equal(2, state.Players[0].Deck.Count);
    }

    [Fact]
    public async Task OP09_106_WithNicoRobinLeader_DrawsThreeAndDiscardsTwo()
    {
        var state = TestScene.New(myLeaderNumber: "OP09-062")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005")
            .Build();

        await Resolve(state, "OP09-106");

        Assert.Single(state.Players[0].Hand);
        Assert.Equal(2, state.Players[0].Trash.Count);
        Assert.Empty(state.Players[0].Deck);
    }

    [Fact]
    public async Task OP09_106_WithDifferentLeader_DoesNotDrawOrDiscard()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005")
            .Build();

        await Resolve(state, "OP09-106");

        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Trash);
        Assert.Equal(3, state.Players[0].Deck.Count);
    }

    [Fact]
    public async Task ST36_003_WithDeckCard_DrawsAndSetsLeaderOriginalPowerTo7000()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003")
            .Build();

        await Resolve(state, "ST36-003");

        Assert.Single(state.Players[0].Hand);
        Assert.Equal(7000, state.Players[0].Leader.OriginalPowerOverride);
    }

    [Fact]
    public async Task ST36_003_WithEmptyDeck_StopsBeforePowerChange()
    {
        var state = TestScene.New().Build();

        await Resolve(state, "ST36-003");

        Assert.True(state.IsGameOver);
        Assert.Null(state.Players[0].Leader.OriginalPowerOverride);
    }

    private static Task Resolve(GameState state, string number, MockPromptService? prompts = null)
        => EffectRuntime.Resolve(state, 0, Card(number),
            EffectTrigger.OnLifeRevealTrigger, prompts ?? new MockPromptService());

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };
}
